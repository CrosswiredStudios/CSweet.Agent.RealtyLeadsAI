using System.Text.Json;
using System.Text.RegularExpressions;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Lead Research Analyst. An individual contributor that turns a bounded research brief into a lead brief using the
/// two granted RealtyLeadsAI read capabilities. It has no key, transport, outreach or write authority.
/// Inputs: a manager's delegation carrying a <c>research-brief.v1</c> line, or a human chat request.
/// Output: an escaped literal result in the conversation and, when granted, a <c>realtyleads.lead-brief.v1</c> artifact.
/// </summary>
public sealed partial class RealtyLeadsAgent : CSweetAgentBase
{
    public const string Search = "realtyleads.research.search.v1";
    public const string Lookup = "realtyleads.research.lookup.v1";
    public const string ProviderSearch = LeadResearchService.ProviderSearch;
    public const string ProviderLookup = LeadResearchService.ProviderLookup;
    public const int MaximumMessageLength = 4000;
    internal const string Clarification = "Which state and city or county should I search? Please include the two-letter state abbreviation and any score or status preference. I can search inheritance leads or look up a lead ID; I cannot contact leads or change records.";
    internal const string CredentialGuidance = "Enter credentials only in native RealtyLeadsAI account settings, then explicitly grant this agent read access. Never send a key in chat.";
    internal const string Unavailable = "RealtyLeadsAI or the selected model is unavailable. Check the native account connection, read grant and approved model configuration. No results are available from this turn.";

    private static readonly Regex CredentialWords = new("api[ -]?key|secret|password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LongToken = new("[A-Za-z0-9_+.-]{32,}", RegexOptions.CultureInvariant);

    private readonly IResearchPlanner _planner;
    private readonly LeadResearchService _research = new();
    private readonly LeadBriefPublisher _publisher = new();

    public override string AgentId => "com.crosswiredstudios.agent.realtyleads";
    public override string Version => "0.2.0";

    public RealtyLeadsAgent() : this(new ModelResearchPlanner()) { }
    public RealtyLeadsAgent(IResearchPlanner planner) => _planner = planner;
    /// <summary>Test seam: a synthetic planner in place of the model.</summary>
    internal RealtyLeadsAgent(Func<string, CancellationToken, Task<ResearchIntent>> planner) : this(new DelegatePlanner(planner)) { }

    private sealed class DelegatePlanner(Func<string, CancellationToken, Task<ResearchIntent>> planner) : IResearchPlanner
    {
        public Task<ResearchIntent> InterpretAsync(string text, CommunicationMessageReceivedEvent incoming, AgentRuntimeContext context, CancellationToken ct) => planner(text, ct);
    }

    // ---- Typed capabilities other agents may call through their own grants ----

    protected override async Task<AgentWorkResult> ExecuteCapabilityCoreAsync(AgentCapabilityRequest request, AgentRuntimeContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (request.Capability == Search)
            {
                var input = DeserializePayload<LeadSearch>(request.Arguments);
                if (!LeadResearchService.IsValidSearch(input)) return AgentWorkResult.Failure("Provide a bounded lead query and a limit from 1 to 100.");
                return AgentWorkResult.Success(await _research.SearchAsync(input!, context, ct));
            }
            if (request.Capability == Lookup)
            {
                var input = DeserializePayload<LeadLookup>(request.Arguments);
                if (input is null || !Guid.TryParseExact(input.Id, "D", out _)) return AgentWorkResult.Failure("Provide a lead ID in UUID format.");
                return AgentWorkResult.Success(await _research.LookupAsync(input.Id, context, ct));
            }
            return AgentWorkResult.Failure("The requested research capability is not supported.");
        }
        catch (JsonException) { return AgentWorkResult.Failure("The lead query does not match the expected fields."); }
        catch (ArgumentException) { return AgentWorkResult.Failure("Provide a bounded lead query and a limit from 1 to 100."); }
        catch (PlatformCapabilityException) { return AgentWorkResult.Failure("RealtyLeadsAI access is unavailable. Connect and grant access in C-Sweet account settings."); }
    }

    // ---- Chat and delegated-instruction intake ----

    public override async Task HandleEventAsync(AgentEventEnvelope message, AgentRuntimeContext context, CancellationToken ct)
    {
        if (message.EventType != CommunicationEvents.MessageReceived) { await base.HandleEventAsync(message, context, ct); return; }
        var incoming = DeserializePayload<CommunicationMessageReceivedEvent>(message.Data);
        if (incoming is null || !Guid.TryParse(incoming.ConversationId, out _) || incoming.TurnId == Guid.Empty) return;
        await RespondAsync(incoming, delegation: null, context, ct);
    }

    /// <summary>A manager's accepted instruction arrives as a real turn. The brief in it is parsed without a model.</summary>
    public override Task HandleDelegationExecutionAsync(DelegationSnapshot delegation, CommunicationMessageReceivedEvent incoming,
        AgentEventEnvelope envelope, AgentRuntimeContext context, CancellationToken ct) => RespondAsync(incoming, delegation, context, ct);

    private async Task RespondAsync(CommunicationMessageReceivedEvent incoming, DelegationSnapshot? delegation, AgentRuntimeContext context, CancellationToken ct)
    {
        await using var stream = context.CreateTurnStream(incoming.ConversationId, incoming.TurnId, incoming.Attempt, "Confidential");
        var text = delegation?.Instruction ?? incoming.Context?.GetValueOrDefault("currentUserMessage") ?? incoming.Message;
        string reply;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumMessageLength) reply = Clarification;
        else if (LooksLikeCredential(text)) reply = CredentialGuidance;
        else
        {
            try { reply = await ResearchAsync(text, incoming, delegation, context, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (PlatformCapabilityException) { reply = Unavailable; }
            catch (JsonException) { reply = Clarification; }
            catch (InvalidOperationException) { reply = "I could not interpret this request. Please specify a state abbreviation and city or county, or a lead ID."; }
        }
        await stream.CommitAsync(reply, ct);
    }

    internal static bool LooksLikeCredential(string text) =>
        CredentialWords.IsMatch(text) || LongToken.Matches(text).Any(m => !Guid.TryParseExact(m.Value, "D", out _));

    private async Task<string> ResearchAsync(string text, CommunicationMessageReceivedEvent incoming, DelegationSnapshot? delegation,
        AgentRuntimeContext context, CancellationToken ct)
    {
        ResearchBrief? brief; string? lookupId = null;
        if (ResearchBrief.TryParse(text, out brief, out var briefError))
        {
            // Structured assignment: never interpreted by a model. Invalid briefs are answered with the exact defect.
            if (brief is null) return $"{briefError} Send a line such as: {ResearchBrief.Prefix} state=TX county=\"Travis\" minScore=70 limit=10";
        }
        else
        {
            var intent = await _planner.InterpretAsync(text, incoming, context, ct);
            lookupId = Grounding.GroundedLookup(intent, text);
            if (lookupId is null)
            {
                brief = Grounding.GroundedSearch(intent, text);
                if (brief is null) return Clarification;
            }
        }
        if (lookupId is not null)
        {
            var lead = await _research.LookupAsync(lookupId, context, ct);
            return lead is null ? "No matching subscribed lead was returned." : LeadBriefRenderer.Record(lead);
        }
        var result = await _research.SearchAsync(brief!, context, ct);
        var conversation = Guid.TryParse(incoming.ConversationId, out var chat) ? chat : (Guid?)null;
        var key = delegation is { } d ? $"lead-brief:delegation:{d.Id:N}" : $"lead-brief:message:{incoming.MessageId:N}";
        var published = incoming.MessageId == Guid.Empty && delegation is null ? null
            : await _publisher.PublishAsync(result, key, conversation, delegation?.WorkItemId, context, ct);
        // The marker leads so it survives the platform's truncation of recorded responses.
        var saved = published is null ? "" : $"Lead brief saved as an artifact with all reviewed fields {published.Marker}\n\n";
        if (result.Leads.Count == 0)
            return saved + "No subscribed leads matched these filters. You can broaden the geography or relax the score/status filter; I have not run another search.";
        var table = result.Leads.Count > ResearchBrief.MaximumChatResults
            ? LeadBriefRenderer.ChatTable(result.Leads.Take(ResearchBrief.MaximumChatResults).ToArray()) + $"\n\nShowing {ResearchBrief.MaximumChatResults} of {result.Leads.Count} leads here; the full set is in the lead brief."
            : LeadBriefRenderer.ChatTable(result.Leads);
        return saved + table;
    }

    // ---- Delegation protocol: deterministic, no model, no outgoing delegation ----

    /// <summary>The analyst never delegates; every chat message is handled directly.</summary>
    public override Task<DelegationIntent> ClassifyDelegationIntentAsync(CommunicationMessageReceivedEvent incoming, AgentRuntimeContext context, CancellationToken token) =>
        Task.FromResult(new DelegationIntent("Other", null, null, null, null));

    /// <summary>Accepts only valid research briefs; anything else gets a precise clarification without a model call.</summary>
    public override Task<DelegationResponse> HandleDelegationReceivedAsync(DelegationSnapshot delegation, AgentRuntimeContext context, CancellationToken token)
    {
        if (LooksLikeCredential(delegation.Instruction ?? "")) return Task.FromResult<DelegationResponse>(new DelegationResponse.Decline(CredentialGuidance));
        if (ResearchBrief.TryParse(delegation.Instruction, out var brief, out var error))
            return Task.FromResult<DelegationResponse>(brief is not null
                ? new DelegationResponse.Acceptance($"I will run one bounded RealtyLeadsAI search for {LeadBriefRenderer.Scope(brief)} (limit {brief.Limit}) and deliver a lead brief with the reviewed fields. No outreach or record changes.")
                : new DelegationResponse.Clarification($"{error} Please resend as: {ResearchBrief.Prefix} state=XX [county=\"...\"] [city=\"...\"] [status=\"...\"] [minScore=N] [limit=N]"));
        return Task.FromResult<DelegationResponse>(new DelegationResponse.Clarification(
            $"Please send the assignment as a research brief line so I can run it exactly: {ResearchBrief.Prefix} state=XX [county=\"...\"] [city=\"...\"] [status=\"...\"] [minScore=N] [limit=N] [purpose=\"...\"]"));
    }

    /// <summary>Delivery is proven by the published brief marker in the recorded execution evidence, not by a model opinion.</summary>
    public override Task<DelegationResponse.FinalOutcome?> ReviewDelegationOutcomeAsync(DelegationSnapshot delegation, AgentRuntimeContext context, CancellationToken token)
    {
        var content = delegation.ExecutionEvidence?.Content;
        if (PublishedBrief.ParseMarker(content) is { } artifact)
            return Task.FromResult<DelegationResponse.FinalOutcome?>(new($"Lead brief delivered {PublishedBrief.MarkerPrefix}{artifact:D}]. Results are provider records for review; no outreach or changes were made.", true));
        if (content is not null && (content.StartsWith(Clarification, StringComparison.Ordinal) || content.StartsWith(CredentialGuidance, StringComparison.Ordinal)))
            return Task.FromResult<DelegationResponse.FinalOutcome?>(new("The assignment could not be run as written; see the analyst's clarification.", false));
        return Task.FromResult<DelegationResponse.FinalOutcome?>(null);
    }

    /// <summary>The analyst binds no separate work item; the delegation's own commitment tracks it.</summary>
    public override Task<Guid?> ResolveDelegationWorkAsync(DelegationSnapshot delegation, string response, AgentRuntimeContext context, CancellationToken token) =>
        Task.FromResult(delegation.WorkItemId);

    public override Task<string> SummarizeDelegationUpdateAsync(DelegationSnapshot delegation, TrackingUpdate update, AgentRuntimeContext context, CancellationToken token) =>
        Task.FromResult(update.Summary);
}
