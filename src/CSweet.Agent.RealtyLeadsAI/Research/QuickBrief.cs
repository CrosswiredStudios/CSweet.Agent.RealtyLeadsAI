using System.Globalization;
using System.Text.RegularExpressions;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Deterministic interpretation of the simplest and most common request: leads for one US state, with at most a
/// result count. No model is involved, so the answer does not depend on the model's judgement, surrounding platform
/// instructions or recalled memory. Anything richer (a city, a county, a score, a status, a lead ID) goes to the planner.
/// </summary>
public static class QuickBrief
{
    public const int DefaultLimit = 5;
    private static readonly Regex Intent = new(@"\b(leads?|lead information|inheritance|research|search|find|look(ing)? for|show|list|pull|get)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OtherFilters = new(@"\b(county|counties|city|cities|town|score|status|above|over|under|below|at least|minimum|min|uuid|id)\b|\d{4,}|[0-9a-f]{8}-",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Negation = new(@"\b(not|don't|do not|never|except|without|instead|stop|cancel|thanks|thank you|great|got it|email|call|contact|send|mail)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Count = new(@"\b(\d{1,3})\s+(leads?|results?|records?|properties)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"[\p{L}']+", RegexOptions.CultureInvariant);

    /// <summary>A state-only brief when the message is unambiguous; otherwise null and the planner decides.</summary>
    public static ResearchBrief? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 400 || text.Contains('\n')) return null;
        if (!Intent.IsMatch(text) || OtherFilters.IsMatch(text) || Negation.IsMatch(text)) return null;
        var state = UsStates.FindSingleNamed(text);
        if (state is null) return null;
        // Any other capitalised word (not sentence-initial, not "I", not part of the state name) may be a city or a
        // person; leave those to the grounded planner rather than guess a state-wide search.
        var stateWords = UsStates.NameOf(state)!.Split(' ').Append(state).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words = Word.Matches(text).Select(m => m.Value).ToArray();
        for (var i = 1; i < words.Length; i++)
            if (char.IsUpper(words[i][0]) && words[i] != "I" && !stateWords.Contains(words[i])) return null;
        var digits = Regex.Matches(text, @"\d+").Count;
        var limit = DefaultLimit;
        if (Count.Match(text) is { Success: true } count)
        {
            limit = int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture);
            if (limit < 1 || limit > ResearchBrief.MaximumChatResults) return null;
        }
        else if (digits > 0) return null;
        return new ResearchBrief(state, null, null, null, null, limit, null);
    }
}
