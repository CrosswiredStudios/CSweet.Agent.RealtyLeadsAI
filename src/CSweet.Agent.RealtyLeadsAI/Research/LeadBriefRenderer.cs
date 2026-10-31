using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Renders provider records as escaped literal data. Provider text never enters a model prompt and is never
/// interpreted as markdown, links or instructions. Missing values are marked, never guessed. The only links the
/// renderer emits are built by the agent from validated UUIDs, never from provider text.
/// </summary>
public static class LeadBriefRenderer
{
    public const string DocumentType = "realtyleads.lead-brief.v1";
    public const string Disclaimer = "These are provider records, not verified ownership or legal conclusions.";
    public const string Footer = "_Provider records, not verified ownership or legal conclusions. Nothing was sent or changed._";
    /// <summary>Heading of the machine-readable block managers parse. The JSON carries the reviewed fields.</summary>
    public const string DataHeading = "## Data (realtyleads.lead-brief.v1)";
    /// <summary>Public lead page on RealtyLeads; only a validated UUID is ever appended.</summary>
    public const string LeadUrlPrefix = "https://realtyleads.ai/leads/";
    private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web);
    private const int MaximumCell = 256;
    private const string Missing = "Not supplied";

    /// <summary>Escapes one provider value for safe inclusion in markdown table cells and lists.</summary>
    public static string Cell(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : Escape(value);

    private static string Escape(string value) =>
        WebUtility.HtmlEncode(new string(value.Take(MaximumCell).Where(c => !char.IsControl(c)).ToArray()))
            .Replace("|", "&#124;").Replace("`", "&#96;").Replace("[", "&#91;").Replace("]", "&#93;")
            .Replace("*", "&#42;").Replace("_", "&#95;").Replace("\\", "&#92;");

    private static string Score(double? score) => score is { } s ? s.ToString(CultureInfo.InvariantCulture) : Missing;

    // ---- Human-readable values -------------------------------------------------------------------------------

    /// <summary>Validated lead link, or null when the ID is not a canonical UUID (the link is then simply omitted).</summary>
    public static string? LeadLink(string? leadId, string text = "Open ↗") =>
        Guid.TryParseExact(leadId, "D", out var id) ? $"[{text}]({LeadUrlPrefix}{id:D} \"Lead {id:D}\")" : null;

    /// <summary>
    /// Street line for reading: drops a trailing ".0" on the house number, title-cases shouted provider text, and
    /// keeps the five-digit ZIP. City and state are dropped when the caller already shows them.
    /// <c>2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822-3962</c> → <c>2180 Mangrum Ave · 95822</c>.
    /// </summary>
    public static string ShortAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Missing;
        var parts = address.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return Missing;
        var zip = parts.LastOrDefault(p => Regex.IsMatch(p, @"^\d{5}(-\d{4})?$"));
        var street = Regex.Replace(parts[0], @"^(\d+)\.0\b", "$1");
        street = TitleCase(street);
        return Escape(zip is null ? street : $"{street} · {zip[..5]}");
    }

    private static string TitleCase(string value)
    {
        if (value.Any(char.IsLower)) return value; // already mixed case: leave the provider's spelling alone
        var words = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w is "n" or "s" or "e" or "w" or "ne" or "nw" or "se" or "sw") { words[i] = w.ToUpperInvariant(); continue; } // directions
            if (Regex.IsMatch(w, @"^\d+(st|nd|rd|th)$") || char.IsDigit(w[0])) continue;                    // 63rd, 95822
            words[i] = char.ToUpperInvariant(w[0]) + w[1..];
        }
        return string.Join(' ', words);
    }

    /// <summary>"Aug 14, 2026" from an ISO date or date-time; null when absent or unparseable.</summary>
    public static string? DeathDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : null;

    /// <summary>"3 · 2 reachable", "1 · none reachable", or "—" when the provider gave no count.</summary>
    public static string Heirs(int? total, int? contactable)
    {
        if (total is not { } t || t <= 0) return "—";
        var c = Math.Clamp(contactable ?? 0, 0, t);
        return c == 0 ? $"{t} · none reachable" : $"{t} · {c} reachable";
    }

    private static string DeceasedCell(InheritanceLead l)
    {
        var name = Cell(l.DeceasedName);
        return DeathDate(l.DateOfDeath) is { } d ? $"{name} · d. {d}" : name;
    }

    /// <summary>Human name of the searched place: "Sacramento, CA", "Travis County, TX", "New York".</summary>
    public static string Place(ResearchBrief brief)
    {
        var parts = new List<string>();
        if (brief.City is not null) parts.Add(brief.City);
        if (brief.County is not null) parts.Add(brief.County + " County");
        if (parts.Count == 0) return UsStates.NameOf(brief.State) ?? brief.State;
        parts.Add(brief.State);
        return string.Join(", ", parts);
    }

    // ---- Chat reply ------------------------------------------------------------------------------------------

    /// <summary>
    /// The chat reply for a search: one header line with everything the rows share, a table of what differs, and the
    /// footer. At most <paramref name="maximumRows"/> rows are shown, highest score first; the brief holds the rest.
    /// <paramref name="briefMarker"/> is the published brief's marker (null when nothing was saved).
    /// </summary>
    public static string ChatReply(ResearchResult result, string? briefMarker, int maximumRows = ResearchBrief.MaximumChatResults)
    {
        var all = result.Leads;
        var shown = all.OrderByDescending(l => l.Score ?? double.MinValue).Take(maximumRows).ToArray();
        var b = new StringBuilder();
        if (briefMarker is not null) b.Append(BriefDefinition(briefMarker)).Append("\n\n");
        b.Append(Header(result, all, shown.Length, briefMarker));
        if (all.Count == 0)
        {
            b.Append("\n\nNothing in the subscription matches these filters. I did not broaden the search — widen the area or relax the score/status filter and ask again.");
            return b.Append("\n\n").Append(Footer).ToString();
        }
        var placeVaries = Varies(all, l => $"{l.CityName}|{l.CountyName}|{l.StateAbbreviation}");
        b.Append("\n\n| Address |").Append(placeVaries ? " City · County |" : "").Append(" Owner | Deceased | Heirs | Score | |\n");
        b.Append("|---|").Append(placeVaries ? "---|" : "").Append("---|---|---|---:|---|\n");
        foreach (var l in shown)
        {
            b.Append("| ").Append(ShortAddress(l.Address)).Append(" | ");
            if (placeVaries) b.Append(Cell(l.CityName)).Append(" · ").Append(Cell(l.CountyName)).Append(" | ");
            b.Append(Cell(l.OwnerName)).Append(" | ").Append(DeceasedCell(l)).Append(" | ").Append(Heirs(l.TotalHeirs, l.ContactableHeirs))
             .Append(" | **").Append(Escape(Score(l.Score))).Append("** | ").Append(LeadLink(l.InheritanceLeadId) ?? "—").Append(" |\n");
        }
        return b.Append('\n').Append(Footer).ToString();
    }

    private static string Header(ResearchResult result, IReadOnlyList<InheritanceLead> all, int shown, string? briefMarker)
    {
        var brief = result.Brief;
        var b = new StringBuilder("**");
        b.Append(all.Count == 0 ? "No" : all.Count.ToString(CultureInfo.InvariantCulture)).Append(" inheritance lead").Append(all.Count == 1 ? "" : "s");
        b.Append(" · ").Append(Escape(Place(brief)));
        if (brief.MinScore is { } min) b.Append(" · score ≥ ").Append(min.ToString(CultureInfo.InvariantCulture));
        if (brief.Status is not null) b.Append(" · status ").Append(Escape(brief.Status));
        b.Append("**");
        var facts = new List<string>();
        if (all.Count > 0)
        {
            if (brief.City is null && brief.County is null)
            {
                var counties = all.Select(l => l.CountyName).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (counties > 1) facts.Add($"{counties} counties");
            }
            var statuses = all.Select(l => l.Status).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (brief.Status is null && statuses.Length == 1) facts.Add("all status " + Escape(statuses[0]!));
            var scores = all.Where(l => l.Score is not null).Select(l => l.Score!.Value).ToArray();
            if (scores.Length > 0)
            {
                var lo = scores.Min().ToString(CultureInfo.InvariantCulture); var hi = scores.Max().ToString(CultureInfo.InvariantCulture);
                facts.Add(lo == hi ? "score " + lo : $"scores {lo}–{hi}");
            }
            if (shown < all.Count) facts.Add($"showing the top {shown} by score");
        }
        if (briefMarker is not null) facts.Add(all.Count > shown ? $"[Full brief (all {all.Count})][{briefMarker.Trim('[', ']')}]" : $"[Full brief][{briefMarker.Trim('[', ']')}]");
        if (facts.Count > 0) b.Append(" — ").Append(string.Join(" · ", facts));
        return b.ToString();
    }

    private static bool Varies<T>(IReadOnlyList<InheritanceLead> leads, Func<InheritanceLead, T> key) =>
        leads.Select(key).Distinct().Count() > 1;

    /// <summary>
    /// The brief marker as a markdown link reference definition: still the first thing in the reply (so delegation
    /// review finds it even in a truncated transcript), invisible when rendered, and the target of the "Full brief"
    /// link. The relative URL resolves to the organization's Documents page from a conversation route.
    /// </summary>
    public static string BriefDefinition(string marker)
    {
        var id = PublishedBrief.ParseMarker(marker) ?? Guid.Empty;
        return $"{marker}: ../documents?artifact={id:D}";
    }

    // ---- Brief document --------------------------------------------------------------------------------------

    /// <summary>Full lead brief: the criteria actually used, a readable table of every lead, per-lead detail, and the data block.</summary>
    public static string Brief(ResearchResult result, DateTimeOffset generatedAt)
    {
        var brief = result.Brief;
        var b = new StringBuilder();
        b.Append("# Lead brief: ").Append(Escape(Place(brief))).Append('\n').Append('\n');
        if (brief.Purpose is not null) b.Append("Purpose: ").Append(Cell(brief.Purpose)).Append("\n\n");
        b.Append($"Generated {generatedAt:yyyy-MM-dd HH:mm} UTC. Provider returned {result.Leads.Count} unique lead(s)");
        if (result.DuplicatesRemoved > 0) b.Append($" ({result.DuplicatesRemoved.ToString(CultureInfo.InvariantCulture)} duplicate(s) removed)");
        b.Append(". ").Append(Disclaimer).Append(" Scores are provider-supplied. No outreach or record changes were made.\n\n");
        b.Append("## Criteria used\n\n| Filter | Value |\n|---|---|\n");
        b.Append($"| State | {Cell(brief.State)} |\n| County | {Cell(brief.County)} |\n| City | {Cell(brief.City)} |\n| Status | {Cell(brief.Status)} |\n");
        b.Append($"| Minimum score | {Cell(Score(brief.MinScore))} |\n| Limit | {brief.Limit.ToString(CultureInfo.InvariantCulture)} |\n\n");
        b.Append("## Leads\n\n");
        if (result.Leads.Count == 0) b.Append("No subscribed leads matched these criteria. The search was not broadened.\n\n");
        else
        {
            b.Append("| # | Address | City | County | State | Owner | Deceased | Heirs | Status | Score | |\n|---:|---|---|---|---|---|---|---|---|---:|---|\n");
            var n = 0;
            foreach (var l in result.Leads.OrderByDescending(l => l.Score ?? double.MinValue))
                b.Append($"| {++n} | {ShortAddress(l.Address)} | {Cell(l.CityName)} | {Cell(l.CountyName)} | {Cell(l.StateAbbreviation)} | {Cell(l.OwnerName)} | {DeceasedCell(l)} | {Heirs(l.TotalHeirs, l.ContactableHeirs)} | {Cell(l.Status)} | {Escape(Score(l.Score))} | {LeadLink(l.InheritanceLeadId) ?? "—"} |\n");
            b.Append("\n## Details\n\n");
            n = 0;
            foreach (var l in result.Leads)
            {
                b.Append($"### {++n}. {Cell(l.Address)}\n\n");
                b.Append($"- Lead ID: {Cell(l.InheritanceLeadId)}").Append(LeadLink(l.InheritanceLeadId, "open in RealtyLeads") is { } link ? $" — {link}\n" : "\n");
                b.Append($"- Property ID: {Cell(l.PropertyId)}\n- Owner name: {Cell(l.OwnerName)}\n- Status: {Cell(l.Status)}\n- Score: {Cell(Score(l.Score))}\n");
                b.Append($"- Deceased: {Cell(l.DeceasedName)} (ID {Cell(l.DeceasedId)})\n- Date of death: {(DeathDate(l.DateOfDeath) ?? Missing)}\n");
                b.Append($"- Heirs: {(l.TotalHeirs is { } t ? t.ToString(CultureInfo.InvariantCulture) : Missing)}; contactable: {(l.ContactableHeirs is { } c ? c.ToString(CultureInfo.InvariantCulture) : Missing)}\n");
                b.Append($"- City: {Cell(l.CityName)}\n- County: {Cell(l.CountyName)}\n- State: {Cell(l.StateAbbreviation)}\n\n");
            }
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

    /// <summary>Criteria text for titles and acceptances: "Sacramento, CA", "Travis County, TX".</summary>
    public static string Scope(ResearchBrief brief)
    {
        var parts = new List<string>();
        if (brief.City is not null) parts.Add(brief.City);
        if (brief.County is not null) parts.Add(brief.County + " County");
        parts.Add(brief.State);
        return string.Join(", ", parts);
    }

    /// <summary>Single-record view for lookups: the reviewed fields, nothing more, with the lead link.</summary>
    public static string Record(InheritanceLead l)
    {
        var b = new StringBuilder("**").Append(ShortAddress(l.Address)).Append("** · ").Append(Cell(l.CityName)).Append(", ").Append(Cell(l.CountyName)).Append(" County, ").Append(Cell(l.StateAbbreviation));
        if (LeadLink(l.InheritanceLeadId, "Open in RealtyLeads ↗") is { } link) b.Append(" · ").Append(link);
        b.Append("\n\n");
        b.Append($"- Owner: {Cell(l.OwnerName)}\n- Deceased: {DeceasedCell(l)}\n- Heirs: {Heirs(l.TotalHeirs, l.ContactableHeirs)}\n- Status: {Cell(l.Status)} · score {Escape(Score(l.Score))}\n");
        b.Append($"- Lead ID: {Cell(l.InheritanceLeadId)} · property {Cell(l.PropertyId)} · deceased ID {Cell(l.DeceasedId)}\n\n");
        return b.Append(Footer).ToString();
    }
}
