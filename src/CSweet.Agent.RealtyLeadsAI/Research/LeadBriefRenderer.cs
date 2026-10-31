using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Renders provider records as escaped literal data. Provider text never enters a model prompt and is never
/// interpreted as markdown, links or instructions. Missing values are marked, never guessed.
/// </summary>
public static class LeadBriefRenderer
{
    public const string DocumentType = "realtyleads.lead-brief.v1";
    public const string Disclaimer = "These are provider records, not verified ownership or legal conclusions.";
    /// <summary>Heading of the machine-readable block managers parse. The JSON carries exactly the eleven reviewed fields.</summary>
    public const string DataHeading = "## Data (realtyleads.lead-brief.v1)";
    private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web);
    private const int MaximumCell = 256;

    /// <summary>Escapes one provider value for safe inclusion in markdown table cells and lists.</summary>
    public static string Cell(string? value) => string.IsNullOrWhiteSpace(value) ? "Not supplied" :
        WebUtility.HtmlEncode(new string(value.Take(MaximumCell).Where(c => !char.IsControl(c)).ToArray()))
            .Replace("|", "&#124;").Replace("`", "&#96;").Replace("[", "&#91;").Replace("]", "&#93;")
            .Replace("*", "&#42;").Replace("_", "&#95;").Replace("\\", "&#92;");

    private static string Score(double? score) => score is { } s ? s.ToString(CultureInfo.InvariantCulture) : "Not supplied";

    /// <summary>Compact seven-column chat summary used for conversational replies.</summary>
    public static string ChatTable(IReadOnlyList<InheritanceLead> leads)
    {
        var b = new StringBuilder($"RealtyLeadsAI returned {leads.Count} lead(s). {Disclaimer}\n\n");
        b.Append("| Lead ID | Address | City | County | State | Status | Score |\n|---|---|---|---|---|---|---|\n");
        foreach (var l in leads)
            b.Append($"| {Cell(l.InheritanceLeadId)} | {Cell(l.Address)} | {Cell(l.CityName)} | {Cell(l.CountyName)} | {Cell(l.StateAbbreviation)} | {Cell(l.Status)} | {Cell(Score(l.Score))} |\n");
        b.Append("\nAsk to look up a returned lead ID for its current record. No outreach or changes were made.");
        return b.ToString();
    }

    /// <summary>Full lead brief with the criteria that were actually used and all eleven reviewed fields per lead.</summary>
    public static string Brief(ResearchResult result, DateTimeOffset generatedAt)
    {
        var brief = result.Brief;
        var b = new StringBuilder();
        b.Append("# Lead brief: ").Append(Cell(Scope(brief))).Append('\n').Append('\n');
        if (brief.Purpose is not null) b.Append("Purpose: ").Append(Cell(brief.Purpose)).Append("\n\n");
        b.Append("## Criteria used\n\n");
        b.Append("| Filter | Value |\n|---|---|\n");
        b.Append($"| State | {Cell(brief.State)} |\n| County | {Cell(brief.County)} |\n| City | {Cell(brief.City)} |\n| Status | {Cell(brief.Status)} |\n");
        b.Append($"| Minimum score | {Cell(Score(brief.MinScore))} |\n| Limit | {brief.Limit.ToString(CultureInfo.InvariantCulture)} |\n\n");
        b.Append($"Generated {generatedAt:yyyy-MM-dd HH:mm} UTC. Provider returned {result.Leads.Count} unique lead(s)");
        if (result.DuplicatesRemoved > 0) b.Append($" ({result.DuplicatesRemoved.ToString(CultureInfo.InvariantCulture)} duplicate(s) removed)");
        b.Append(". ").Append(Disclaimer).Append(" Scores are provider-supplied. No outreach or record changes were made.\n\n");
        b.Append("## Leads\n\n");
        if (result.Leads.Count == 0) b.Append("No subscribed leads matched these criteria. The search was not broadened.\n");
        var n = 0;
        foreach (var l in result.Leads)
        {
            b.Append($"### {++n}. {Cell(l.Address)}\n\n");
            b.Append($"- Lead ID: {Cell(l.InheritanceLeadId)}\n- Property ID: {Cell(l.PropertyId)}\n- Owner name: {Cell(l.OwnerName)}\n");
            b.Append($"- Status: {Cell(l.Status)}\n- Score: {Cell(Score(l.Score))}\n- Deceased ID: {Cell(l.DeceasedId)}\n- Deceased name: {Cell(l.DeceasedName)}\n");
            b.Append($"- City: {Cell(l.CityName)}\n- County: {Cell(l.CountyName)}\n- State: {Cell(l.StateAbbreviation)}\n\n");
        }
        b.Append(DataHeading).Append("\n\n```json\n").Append(JsonSerializer.Serialize(new { criteria = brief.ToSearch(), leads = result.Leads }, DataJson)).Append("\n```\n");
        return b.ToString();
    }

    /// <summary>Reads the leads back out of a brief's data block. Returns null when the block is absent or malformed.</summary>
    public static IReadOnlyList<InheritanceLead>? ParseData(string? content)
    {
        if (content is null) return null;
        var start = content.IndexOf(DataHeading, StringComparison.Ordinal);
        if (start < 0) return null;
        var open = content.IndexOf("```json\n", start, StringComparison.Ordinal);
        var close = open < 0 ? -1 : content.IndexOf("\n```", open + 8, StringComparison.Ordinal);
        if (open < 0 || close < 0) return null;
        try { return JsonSerializer.Deserialize<BriefData>(content[(open + 8)..close], DataJson)?.Leads; }
        catch (JsonException) { return null; }
    }
    private sealed record BriefData(LeadSearch Criteria, InheritanceLead[] Leads);

    public static string Scope(ResearchBrief brief)
    {
        var parts = new List<string>();
        if (brief.City is not null) parts.Add(brief.City);
        if (brief.County is not null) parts.Add(brief.County + " County");
        parts.Add(brief.State);
        return string.Join(", ", parts);
    }

    /// <summary>Single-record view for lookups: the same eleven fields, nothing more.</summary>
    public static string Record(InheritanceLead l) =>
        $"Current provider record for lead {Cell(l.InheritanceLeadId)}. {Disclaimer}\n\n" +
        $"- Address: {Cell(l.Address)}\n- Property ID: {Cell(l.PropertyId)}\n- Owner name: {Cell(l.OwnerName)}\n- Status: {Cell(l.Status)}\n- Score: {Cell(Score(l.Score))}\n" +
        $"- Deceased ID: {Cell(l.DeceasedId)}\n- Deceased name: {Cell(l.DeceasedName)}\n- City: {Cell(l.CityName)}\n- County: {Cell(l.CountyName)}\n- State: {Cell(l.StateAbbreviation)}\n\n" +
        "No outreach or changes were made.";
}
