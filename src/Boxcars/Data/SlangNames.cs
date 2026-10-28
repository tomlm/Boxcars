using System.Text.RegularExpressions;

namespace Boxcars.Data;

/// <summary>
/// Slang mode: the nicknames shown for some cities, railroads and the Superchief when a player turns it on. Display
/// only; the game, its history and everyone else's screen keep the real names.
/// </summary>
public static class SlangNames
{
    /// <summary>The name of the cascading value through which the game board tells its components whether slang mode is on.</summary>
    public const string CascadingValueName = "SlangMode";

    // Railroads are listed by their full names (with and without a trailing "RR") and short names.
    private static readonly IReadOnlyDictionary<string, string> Nicknames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Chicago, Milwaukee, St. Paul, and Pacific"] = "Constipation",
        ["CMStP&P"] = "Constipation",
        ["Norfolk and Western"] = "Norfucker",
        ["N&W"] = "Norfucker",
        ["Denver and Rio Grande Western"] = "Dirgwa",
        ["D&RGW"] = "Dirgwa",
        ["Chicago, Rock Island, and Pacific"] = "CRIP",
        ["CRI&P"] = "CRIP",
        ["St. Louis and San Francisco"] = "Frisco",
        ["SLSF"] = "Frisco",
        ["Texas and Pacific"] = "Toilet Paper",
        ["T&P"] = "Toilet Paper",
        ["New York, New Haven, and Hartford RR"] = "Nynaha",
        ["New York, New Haven, and Hartford"] = "Nynaha",
        ["NYNH&H"] = "Nynaha",
        ["Boston and Maine RR"] = "Bowel Movement",
        ["Boston and Maine"] = "Bowel Movement",
        ["B&M"] = "Bowel Movement",

        ["Butte"] = "Butt",
        ["Seattle"] = "Skadattle",
        ["Portland OR"] = "Portlandia",
        ["Spokane"] = "Spokavegas",
        ["Pocatello"] = "Sweet Home Pocatello",
        ["Salt Lake City"] = "Salt Lake Shitty",
        ["Omaha"] = "OMAHA!!!",
        ["Denver"] = "Danvey",
        ["Sacramento"] = "Sacto",
        ["Las Vegas"] = "Vegas",
        ["Tucumcari"] = "Tucumscary",
        ["Milwaukee"] = "Milwalkay",
        ["Detroit"] = "Det troit",
        ["Buffalo"] = "Butthole",
        ["Norfolk"] = "Norfuck",
        ["Louisville"] = "Louessville",
        ["Atlanta"] = "Hotlanta",
        ["Mobile"] = "Going Mobile",
        ["Kansas City"] = "KanSassCity",
        ["Chattanooga"] = "Chataboogy",
        ["San Francisco/Oakland"] = "Frisco",

        ["Superchief"] = "Pooperchief"
    };

    // Longest names first, so a railroad named after a city ("Denver and Rio Grande Western") is renamed as a whole.
    // A name only matches on its own: not inside a longer word, and not inside a short name like "NYNH&H".
    private static readonly Regex NamePattern = new(
        @"(?<![\w&])(?:"
            + string.Join("|", Nicknames.Keys.OrderByDescending(name => name.Length).Select(Regex.Escape))
            + @")(?![\w&])",
        RegexOptions.CultureInvariant);

    /// <summary>The text with every city, railroad and Superchief that has a nickname called by it.</summary>
    public static string Apply(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? text ?? string.Empty
            : NamePattern.Replace(text, match => Nicknames[match.Value]);
    }

    /// <summary>The text as a player shows it: slang when <paramref name="slangMode"/> is on, otherwise unchanged.</summary>
    public static string Apply(string? text, bool slangMode)
    {
        return slangMode ? Apply(text) : text ?? string.Empty;
    }
}
