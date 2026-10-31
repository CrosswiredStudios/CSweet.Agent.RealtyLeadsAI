using CSweet.Agent.SDK;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>Result of one bounded research run. Leads are provider records exactly as returned, de-duplicated by ID.</summary>
public sealed record ResearchResult(ResearchBrief Brief, IReadOnlyList<InheritanceLead> Leads, int DuplicatesRemoved);

/// <summary>
/// The only code path that calls the granted provider capabilities. It receives a validated brief, runs exactly one
/// search (or one lookup), and never widens, retries with different filters or follows up with a second query.
/// </summary>
public sealed class LeadResearchService
{
    public const string ProviderSearch = "realtyleads.inheritance.search.v1";
    public const string ProviderLookup = "realtyleads.inheritance.lookup.v1";

    public static bool IsValidSearch(LeadSearch? input) =>
        input is not null && input.Limit is >= 1 and <= ResearchBrief.MaximumBriefResults &&
        (input.MinScore is not { } score || double.IsFinite(score) && score is >= 0 and <= 100) &&
        new[] { input.State, input.County, input.City, input.Status }.All(x => x is null || x.Length is > 0 and <= 128 && !x.Any(char.IsControl));

    public async Task<ResearchResult> SearchAsync(ResearchBrief brief, AgentRuntimeContext context, CancellationToken ct)
    {
        var leads = await SearchAsync(brief.ToSearch(), context, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = leads.Where(l => !string.IsNullOrWhiteSpace(l.InheritanceLeadId) && seen.Add(l.InheritanceLeadId)).ToArray();
        return new(brief, distinct.Take(brief.Limit).ToArray(), leads.Length - distinct.Length);
    }

    /// <summary>Raw typed search for callers with their own grants; the provider decides what a null state means.</summary>
    public async Task<InheritanceLead[]> SearchAsync(LeadSearch query, AgentRuntimeContext context, CancellationToken ct)
    {
        if (!IsValidSearch(query)) throw new ArgumentException("The lead query does not match the reviewed search schema.");
        return await context.Platform.InvokeAsync<LeadSearch, InheritanceLead[]>(ProviderSearch, query, ct) ?? [];
    }

    public Task<InheritanceLead?> LookupAsync(string id, AgentRuntimeContext context, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "D", out _)) throw new ArgumentException("Provide a lead ID in UUID format.");
        return context.Platform.InvokeAsync<LeadLookup, InheritanceLead?>(ProviderLookup, new(id), ct);
    }
}
