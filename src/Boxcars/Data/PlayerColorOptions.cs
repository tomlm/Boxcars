namespace Boxcars.Data;

public static class PlayerColorOptions
{
    public static readonly string[] Colors =
    [
        "darkblue",
        "blue",
        "cyan",
        "red",
        "darkred",
        "orange",
        "yellow",
        "purple",
        "green",
        "darkgreen"
    ];

    public static bool IsSupported(string? color)
    {
        return Colors.Contains(NormalizeToken(color), StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeOrDefault(string? color, string fallback = "blue")
    {
        var normalizedFallback = Colors.FirstOrDefault(candidate => string.Equals(candidate, NormalizeToken(fallback), StringComparison.OrdinalIgnoreCase))
            ?? "blue";
        var normalizedToken = NormalizeToken(color);
        var match = Colors.FirstOrDefault(candidate => string.Equals(candidate, normalizedToken, StringComparison.OrdinalIgnoreCase));
        return match ?? normalizedFallback;
    }

    public static string ToLabel(string color)
    {
        return NormalizeToken(color) switch
        {
            "darkblue" => "Dark Blue",
            "blue" => "Blue",
            "cyan" => "Cyan",
            "red" => "Red",
            "darkred" => "Dark Red",
            "orange" => "Orange",
            "yellow" => "Yellow",
            "purple" => "Purple",
            "green" => "Green",
            "darkgreen" => "Dark Green",
            _ => string.Empty
        };
    }

    private static string NormalizeToken(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return string.Empty;
        }

        return color
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}