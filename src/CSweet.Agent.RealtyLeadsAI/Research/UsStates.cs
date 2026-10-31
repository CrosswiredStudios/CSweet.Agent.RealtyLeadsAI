using System.Text.RegularExpressions;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>
/// Fixed table of US state and district names. Lets a request that names a state in words ("New York") ground the
/// two-letter abbreviation the model derives from it, without trusting the model's geography.
/// </summary>
public static class UsStates
{
    private static readonly IReadOnlyDictionary<string, string> NamesByAbbreviation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia", ["FL"] = "Florida",
        ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana",
        ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine",
        ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
        ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire",
        ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota",
        ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island",
        ["SC"] = "South Carolina", ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin",
        ["WY"] = "Wyoming",
    };

    /// <summary>Abbreviations that are also ordinary English words; these count only when written in capitals.</summary>
    private static readonly HashSet<string> WordLikeAbbreviations = new(StringComparer.Ordinal)
        { "AL", "AR", "CO", "DE", "HI", "ID", "IN", "LA", "MA", "ME", "MI", "MO", "MS", "MT", "NE", "OH", "OK", "OR", "PA", "SO", "WA" };

    public static bool IsKnownAbbreviation(string? abbreviation) =>
        abbreviation is { Length: 2 } && NamesByAbbreviation.ContainsKey(abbreviation);

    public static string? NameOf(string? abbreviation) =>
        IsKnownAbbreviation(abbreviation) ? NamesByAbbreviation[abbreviation!] : null;

    /// <summary>True when the request names the state by abbreviation or by its full name as a whole word.</summary>
    public static bool IsNamedIn(string source, string? abbreviation)
    {
        if (!IsKnownAbbreviation(abbreviation)) return false;
        var code = abbreviation!.ToUpperInvariant();
        var name = NamesByAbbreviation[code];
        return WholeWord(source, code, ignoreCase: !WordLikeAbbreviations.Contains(code)) || WholeWord(source, name, ignoreCase: true);
    }

    /// <summary>The one state the text names, or null when it names none or more than one.</summary>
    public static string? FindSingleNamed(string source)
    {
        var found = NamesByAbbreviation.Keys.Where(code => IsNamedIn(source, code)).ToList();
        // "West Virginia" also matches "Virginia"; keep the longer name when one is contained in another.
        found.RemoveAll(code => found.Any(other => other != code &&
            NamesByAbbreviation[other].Contains(NamesByAbbreviation[code], StringComparison.OrdinalIgnoreCase) &&
            WholeWord(source, NamesByAbbreviation[other], ignoreCase: true)));
        return found.Count == 1 ? found[0] : null;
    }

    private static bool WholeWord(string source, string value, bool ignoreCase) =>
        Regex.IsMatch(source, @"(?<![\p{L}\p{N}])" + Regex.Escape(value).Replace("\\ ", @"\s+") + @"(?![\p{L}\p{N}])",
            (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
}
