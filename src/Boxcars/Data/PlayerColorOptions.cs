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
        "darkgreen",
        "white",
        "lavender",
        "black",
        "hotpink"
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

    /// <summary>The color's hex value, for drawing it.</summary>
    public static string GetHex(string? color)
    {
        return NormalizeToken(color) switch
        {
            "darkblue" => "#0b3d91",
            "blue" => "#2563eb",
            "cyan" => "#06b6d4",
            "red" => "#dc2626",
            "darkred" => "#7f1d1d",
            "orange" => "#ea580c",
            "yellow" => "#facc15",
            "purple" => "#9333ea",
            "green" => "#16a34a",
            "darkgreen" => "#166534",
            "white" => "#f8fafc",
            "lavender" => "#c4b5fd",
            "black" => "#111827",
            "hotpink" => "#ff1493",
            _ => "#888"
        };
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
            "white" => "White",
            "lavender" => "Lavender",
            "black" => "Black",
            "hotpink" => "Pink",
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