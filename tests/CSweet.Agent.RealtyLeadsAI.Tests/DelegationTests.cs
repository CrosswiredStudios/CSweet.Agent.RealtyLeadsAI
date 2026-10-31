using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.RealtyLeadsAI.Tests;

/// <summary>The analyst's side of the manager → analyst delegation protocol. No model is used anywhere in these paths.</summary>
public sealed class DelegationTests
{
    private static readonly Guid Manager = Guid.NewGuid(), Analyst = Guid.NewGuid(), Chat = Guid.NewGuid(), Source = Guid.NewGuid();
    private const string Brief = "research-brief.v1 state=TX county=\"Travis\" minScore=70 limit=3 purpose=\"Q4 acquisitions\"";
    private static AgentIdentity Identity() => new(Analyst.ToString("D"), "Analyst", null, null, null, [], null, Manager.ToString("D"), "Manager");
    private static DelegationSnapshot Snapshot(string instruction = Brief, string status = "AwaitingAcknowledgement") => new(Guid.NewGuid(), 1, Manager, Manager,
        Analyst, "Acquisitions Manager", "Lead Research Analyst", instruction, "A lead brief for Travis County, TX", status, true, Chat,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, [], []);
    private static InheritanceLead Lead(string id, string address) => new(id, null, address, "Fixture owner", "Verified", 88, null, "Fixture person", "Austin", "Travis", "TX");
    /// <summary>A planner that must never be consulted for structured assignments.</summary>
    private static RealtyLeadsAgent AgentWithForbiddenPlanner() => new((_, _) => throw new Xunit.Sdk.XunitException("The model planner must not run for a research brief."));

    [Fact]
    public async Task ValidBriefIsAcceptedWithoutAModel()
    {
        var d = Snapshot(); DelegationResponseRequest? response = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read, (_, _) => Task.FromResult<IReadOnlyList<DelegationSnapshot>>([d]))
            .RegisterCapability<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, (r, _) => { response = r; return Task.FromResult(d with { Status = "Active" }); });
        await runtime.DeliverEventAsync(AgentWithForbiddenPlanner(), DelegationCapabilities.Received, new DelegationWake(d.Id), identity: Identity());
        Assert.Equal("Accepted", response!.Disposition); Assert.Contains("Travis County, TX", response.Summary); Assert.Contains("limit 3", response.Summary);
        Assert.Null(response.WorkItemId);
    }

    [Theory]
    [InlineData("Please find some leads in Texas", "Clarification", "research-brief.v1 state=XX")]
    [InlineData("research-brief.v1 county=\"Travis\"", "Clarification", "state=XX")]
    [InlineData("research-brief.v1 state=TX limit=500", "Clarification", "limit")]
    [InlineData("Use api key synthetic-key-0000000000000000000000000000 and search TX", "Declined", "native RealtyLeadsAI account settings")]
    public async Task UnusableAssignmentsAreClarifiedOrDeclinedPrecisely(string instruction, string disposition, string expected)
    {
        var d = Snapshot(instruction); DelegationResponseRequest? response = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read, (_, _) => Task.FromResult<IReadOnlyList<DelegationSnapshot>>([d]))
            .RegisterCapability<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, (r, _) => { response = r; return Task.FromResult(d); });
        await runtime.DeliverEventAsync(AgentWithForbiddenPlanner(), DelegationCapabilities.Received, new DelegationWake(d.Id), identity: Identity());
        Assert.Equal(disposition, response!.Disposition); Assert.Contains(expected, response.Summary);
        Assert.DoesNotContain("synthetic-key", response.Summary);
    }

    [Fact]
    public async Task ExecutionRunsOneSearchPublishesTheBriefAndReportsTheMarker()
    {
        var d = Snapshot(status: "Active") with { ExecutionMessageId = Source, WorkItemId = Guid.NewGuid() };
        var searches = new List<LeadSearch>(); CreateArtifactDocument? artifact = null; var responses = new List<DelegationResponseRequest>();
        var artifactId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, object>(DelegationCapabilities.Create, (_, _) => throw new Xunit.Sdk.XunitException("The analyst must not delegate."))
            .RegisterCapability<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read,
                (r, _) => Task.FromResult<IReadOnlyList<DelegationSnapshot>>(r.DelegationId == d.Id || r.ExecutionMessageId == Source ? [d] : []))
            .RegisterCapability<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, (r, _) => { responses.Add(r); return Task.FromResult(d); })
            .RegisterCapability<LeadSearch, InheritanceLead[]>(RealtyLeadsAgent.ProviderSearch, (q, _) =>
            {
                searches.Add(q);
                return Task.FromResult(new[] { Lead("00000000-0000-0000-0000-000000000001", "1 Fixture St"), Lead("00000000-0000-0000-0000-000000000001", "1 Fixture St"),
                    Lead("00000000-0000-0000-0000-000000000002", "2 Fixture | Ave [x]"), Lead("00000000-0000-0000-0000-000000000003", "3 Fixture Rd"), Lead("00000000-0000-0000-0000-000000000004", "4 Too Many") });
            })
            .RegisterCapability<CreateArtifactDocument, ArtifactDocument>(PlatformCapabilities.ArtifactCreate, (a, _) =>
            {
                artifact = a;
                return Task.FromResult(new ArtifactDocument(artifactId, a.Title, a.DocumentType, "Draft", Guid.NewGuid(), null, null, a.OriginConversationId, a.OriginWorkItemId, []));
            });
        var incoming = new CommunicationMessageReceivedEvent(Guid.NewGuid(), Chat.ToString(), Manager.ToString(), Brief, null, Guid.NewGuid(), 0, Source);
        await runtime.DeliverEventAsync(AgentWithForbiddenPlanner(), CommunicationEvents.MessageReceived, incoming, identity: Identity());

        var query = Assert.Single(searches);
        Assert.Equal(new LeadSearch("TX", "Travis", null, null, 70, 3), query);

        Assert.NotNull(artifact);
        Assert.Equal(LeadBriefRenderer.DocumentType, artifact!.DocumentType);
        Assert.Equal($"lead-brief:delegation:{d.Id:N}", artifact.IdempotencyKey);
        Assert.Equal(d.WorkItemId, artifact.OriginWorkItemId); Assert.Equal(Chat, artifact.OriginConversationId);
        Assert.Contains("| State | TX |", artifact.Content); Assert.Contains("| County | Travis |", artifact.Content); Assert.Contains("| Minimum score | 70 |", artifact.Content);
        Assert.Contains("3 unique lead(s)", artifact.Content); Assert.Contains("1 duplicate(s) removed", artifact.Content);
        Assert.Contains("Owner name: Fixture owner", artifact.Content); Assert.Contains("Deceased name: Fixture person", artifact.Content);
        Assert.Contains("2 Fixture &#124; Ave &#91;x&#93;", artifact.Content); Assert.DoesNotContain("4 Too Many", artifact.Content);

        var reply = Assert.Single(runtime.Progress).GetProperty("delta").GetString()!;
        Assert.Contains("RealtyLeadsAI returned 3 lead(s)", reply); Assert.Contains($"[lead-brief:{artifactId:D}]", reply);
        var progress = Assert.Single(responses, r => r.Disposition == "Progress");
        Assert.Contains($"[lead-brief:{artifactId:D}]", progress.Summary);
    }

    [Fact]
    public async Task ExecutionWithoutArtifactGrantStillAnswersAndSaysNothingWasSaved()
    {
        var d = Snapshot(status: "Active") with { ExecutionMessageId = Source };
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, object>(DelegationCapabilities.Create, (_, _) => Task.FromResult<object>(new { }))
            .RegisterCapability<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read,
                (r, _) => Task.FromResult<IReadOnlyList<DelegationSnapshot>>(r.DelegationId == d.Id || r.ExecutionMessageId == Source ? [d] : []))
            .RegisterCapability<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, (_, _) => Task.FromResult(d))
            .RegisterCapability<LeadSearch, InheritanceLead[]>(RealtyLeadsAgent.ProviderSearch, (_, _) => Task.FromResult(new[] { Lead("00000000-0000-0000-0000-000000000001", "1 Fixture St") }));
        var incoming = new CommunicationMessageReceivedEvent(Guid.NewGuid(), Chat.ToString(), Manager.ToString(), Brief, null, Guid.NewGuid(), 0, Source);
        await runtime.DeliverEventAsync(AgentWithForbiddenPlanner(), CommunicationEvents.MessageReceived, incoming, identity: Identity());
        var reply = Assert.Single(runtime.Progress).GetProperty("delta").GetString()!;
        Assert.Contains("returned 1 lead(s)", reply); Assert.DoesNotContain("[lead-brief:", reply);
    }

    [Fact]
    public async Task OutcomeIsFinalOnlyWhenTheRecordedEvidenceCarriesTheBriefMarker()
    {
        var agent = AgentWithForbiddenPlanner(); var context = new AgentTestRuntime().CreateContext(identity: Identity());
        var id = Guid.NewGuid();
        var delivered = Snapshot(status: "Active") with { ExecutionEvidence = new(Guid.NewGuid(), Guid.NewGuid(), "Committed", $"RealtyLeadsAI returned 2 lead(s).\n\nLead brief saved. [lead-brief:{id:D}]") };
        var outcome = await agent.ReviewDelegationOutcomeAsync(delivered, context, CancellationToken.None);
        Assert.NotNull(outcome); Assert.True(outcome!.Succeeded); Assert.Contains(id.ToString("D"), outcome.Result);

        var clarified = Snapshot(status: "Active") with { ExecutionEvidence = new(Guid.NewGuid(), Guid.NewGuid(), "Committed", RealtyLeadsAgent.Clarification) };
        var failed = await agent.ReviewDelegationOutcomeAsync(clarified, context, CancellationToken.None);
        Assert.NotNull(failed); Assert.False(failed!.Succeeded);

        Assert.Null(await agent.ReviewDelegationOutcomeAsync(Snapshot(status: "Active"), context, CancellationToken.None));
        Assert.Null(await agent.ReviewDelegationOutcomeAsync(Snapshot(status: "Active") with { ExecutionEvidence = new(Guid.NewGuid(), Guid.NewGuid(), "Committed", "[lead-brief:not-a-guid]") }, context, CancellationToken.None));
    }

    [Fact]
    public async Task AnalystNeverClassifiesChatAsDelegationAndBindsNoExtraWork()
    {
        var agent = AgentWithForbiddenPlanner(); var context = new AgentTestRuntime().CreateContext(identity: Identity());
        var intent = await agent.ClassifyDelegationIntentAsync(new(Guid.NewGuid(), Chat.ToString(), "u", "Ask Gabriel to do it", null, Guid.NewGuid()), context, CancellationToken.None);
        Assert.Equal("Other", intent.Kind);
        var d = Snapshot(status: "Active") with { WorkItemId = Guid.NewGuid() };
        Assert.Equal(d.WorkItemId, await agent.ResolveDelegationWorkAsync(d, "anything", context, CancellationToken.None));
        Assert.Equal("shared", await agent.SummarizeDelegationUpdateAsync(d, new(Guid.NewGuid(), Guid.NewGuid(), d.Id, 1, "Progress", "shared", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), context, CancellationToken.None));
    }

    [Fact]
    public async Task ChatBriefFromAHumanPublishesWithAMessageScopedKey()
    {
        var message = Guid.NewGuid(); CreateArtifactDocument? artifact = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<LeadSearch, InheritanceLead[]>(RealtyLeadsAgent.ProviderSearch, (_, _) => Task.FromResult(Array.Empty<InheritanceLead>()))
            .RegisterCapability<CreateArtifactDocument, ArtifactDocument>(PlatformCapabilities.ArtifactCreate, (a, _) =>
            { artifact = a; return Task.FromResult(new ArtifactDocument(Guid.NewGuid(), a.Title, a.DocumentType, "Draft", null, null, null, null, null, [])); });
        await runtime.DeliverEventAsync(AgentWithForbiddenPlanner(), CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(Guid.NewGuid(), Chat.ToString(), "ceo", "research-brief.v1 state=TX city=\"Austin\"", null, Guid.NewGuid(), 0, message), identity: Identity());
        Assert.Equal($"lead-brief:message:{message:N}", artifact!.IdempotencyKey);
        Assert.Contains("No subscribed leads matched these criteria", artifact.Content);
        var reply = Assert.Single(runtime.Progress).GetProperty("delta").GetString()!;
        Assert.Contains("No subscribed leads matched these filters", reply); Assert.Contains("[lead-brief:", reply);
    }
}
