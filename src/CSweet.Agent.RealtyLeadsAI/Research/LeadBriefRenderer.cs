using System.Globalization;
using System.Net;
using System.Text;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Renders provider records as escaped literal data. Provider text never enters a model prompt and is never
/// interpreted as markdown, links or instructions. Missing values are marked, never guessed.
/// </summary>
public static class LeadBriefRenderer
{
    public const string DocumentType = "realtyleads.lead-brief.v1";
    public const string Disclaimer = "These are provider records, not verified ownership or legal conclusions.";
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
        return b.ToString();
    }

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
