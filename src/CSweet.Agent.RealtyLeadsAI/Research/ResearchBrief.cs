using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>Typed provider search arguments. Mirrors the reviewed realtyleads.inheritance.search.v1 input schema.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadSearch(string? State = null, string? County = null, string? City = null, string? Status = null, double? MinScore = null, int Limit = 50);

/// <summary>Typed provider lookup arguments. Mirrors the reviewed realtyleads.inheritance.lookup.v1 input schema.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadLookup(string Id);

/// <summary>
/// The reviewed provider fields: the eleven identity/location values plus three optional obituary-derived values
/// (plugin 0.2.1+; absent until the provider publishes them). Values are provider records, never verified facts.
/// </summary>
public sealed record InheritanceLead(string InheritanceLeadId, string? PropertyId, string? Address, string? OwnerName, string? Status, double? Score,
    string? DeceasedId, string? DeceasedName, string? CityName, string? CountyName, string? StateAbbreviation,
    string? DateOfDeath = null, int? TotalHeirs = null, int? ContactableHeirs = null);

/// <summary>Bounded model interpretation of a human request. Internal: tests inject it through the planner seam.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResearchIntent(string Action, LeadSearch? Search = null, string? Id = null);

/// <summary>
/// A research assignment in a fixed, model-free grammar that managers and humans can write and the analyst parses
/// deterministically. One line, starting with <c>research-brief.v1</c>, followed by <c>key=value</c> pairs:
/// <code>research-brief.v1 state=TX county="Travis" minScore=70 limit=10 purpose="Q4 acquisitions"</code>
/// Keys: state (required, two letters), county, city, status, minScore (0-100), limit (1-100), purpose.
/// </summary>
public sealed record ResearchBrief(string State, string? County, string? City, string? Status, double? MinScore, int Limit, string? Purpose)
{
    public const string Prefix = "research-brief.v1";
    public const int MaximumChatResults = 10;
    public const int MaximumBriefResults = 100;
    private static readonly Regex Pair = new("""(?<key>[A-Za-z]+)=(?:"(?<quoted>[^"\r\n]{0,128})"|(?<bare>[^\s"]{1,128}))""", RegexOptions.CultureInvariant);

    public LeadSearch ToSearch() => new(State, County, City, Status, MinScore, Limit);

    /// <summary>The canonical one-line form managers send. Quoted values never contain quotes, control characters or newlines.</summary>
    public string ToInstruction()
    {
        var builder = new StringBuilder(Prefix).Append(" state=").Append(State);
        Append(builder, "county", County); Append(builder, "city", City); Append(builder, "status", Status);
        if (MinScore is { } score) builder.Append(" minScore=").Append(score.ToString(CultureInfo.InvariantCulture));
        builder.Append(" limit=").Append(Limit.ToString(CultureInfo.InvariantCulture));
        Append(builder, "purpose", Purpose);
        return builder.ToString();
        static void Append(StringBuilder b, string key, string? value)
        { if (!string.IsNullOrWhiteSpace(value)) b.Append(' ').Append(key).Append("=\"").Append(value).Append('"'); }
    }

    /// <summary>Returns true when <paramref name="text"/> is a brief line that validates; invalid briefs explain why in <paramref name="error"/>.</summary>
    public static bool TryParse(string? text, out ResearchBrief? brief, out string? error)
    {
        brief = null; error = null;
        if (text is null) return false;
        var line = text.Trim();
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var rest = line[Prefix.Length..];
        if (rest.Length > 1024 || rest.Any(char.IsControl)) { error = "The research brief is too long or contains control characters."; return true; }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var consumed = 0;
        foreach (Match m in Pair.Matches(rest))
        {
            if (!string.IsNullOrWhiteSpace(rest[consumed..m.Index])) { error = "The research brief contains text that is not a key=value pair."; return true; }
            consumed = m.Index + m.Length;
            var value = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value;
            if (!values.TryAdd(m.Groups["key"].Value, value)) { error = $"The research brief repeats '{m.Groups["key"].Value}'."; return true; }
        }
        if (!string.IsNullOrWhiteSpace(rest[consumed..])) { error = "The research brief contains text that is not a key=value pair."; return true; }
        var unknown = values.Keys.FirstOrDefault(k => k is not ("state" or "county" or "city" or "status" or "minScore" or "limit" or "purpose"));
        if (unknown is not null) { error = $"The research brief uses an unknown key '{unknown}'."; return true; }
        if (!values.TryGetValue("state", out var state) || state.Length != 2 || !state.All(char.IsAsciiLetter))
        { error = "The research brief needs state=XX with a two-letter state abbreviation."; return true; }
        double? minScore = null;
        if (values.TryGetValue("minScore", out var scoreText))
        {
            if (!double.TryParse(scoreText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var score) || !double.IsFinite(score) || score is < 0 or > 100)
            { error = "minScore must be a number from 0 to 100."; return true; }
            minScore = score;
        }
        var limit = MaximumChatResults;
        if (values.TryGetValue("limit", out var limitText))
        {
            if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaximumBriefResults)
            { error = $"limit must be a whole number from 1 to {MaximumBriefResults}."; return true; }
        }
        brief = new(state.ToUpperInvariant(), Clean(values, "county"), Clean(values, "city"), Clean(values, "status"), minScore, limit, Clean(values, "purpose"));
        return true;
        static string? Clean(Dictionary<string, string> v, string key) => v.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }

    /// <summary>Builds a brief from a validated interpreted search; returns null when the search is not grounded enough to run.</summary>
    public static ResearchBrief? FromSearch(LeadSearch? search, string? purpose = null)
    {
        if (search?.State is not { Length: 2 } state || !state.All(char.IsAsciiLetter) || search.Limit is < 1 or > MaximumBriefResults) return null;
        return new(state.ToUpperInvariant(), search.County, search.City, search.Status, search.MinScore, search.Limit, purpose);
    }
}
