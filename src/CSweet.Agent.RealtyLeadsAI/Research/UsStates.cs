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

    public static bool IsKnownAbbreviation(string? abbreviation) =>
        abbreviation is { Length: 2 } && NamesByAbbreviation.ContainsKey(abbreviation);

    /// <summary>True when the request names the state by abbreviation or by its full name as a whole word.</summary>
    public static bool IsNamedIn(string source, string? abbreviation)
    {
        if (!IsKnownAbbreviation(abbreviation)) return false;
        var name = NamesByAbbreviation[abbreviation!];
        return WholeWord(source, abbreviation!) || WholeWord(source, name);
    }

    private static bool WholeWord(string source, string value) =>
        Regex.IsMatch(source, @"(?<![\p{L}\p{N}])" + Regex.Escape(value).Replace("\\ ", @"\s+") + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
