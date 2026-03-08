namespace Boxcars.Data;

public static class PlayerColorOptions
{
    public static readonly string[] Colors = ["blue", "red", "green", "cyan", "orange", "purple"];

    public static bool IsSupported(string? color)
    {
        return Colors.Contains(color?.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeOrDefault(string? color, string fallback = "blue")
    {
        var match = Colors.FirstOrDefault(candidate => string.Equals(candidate, color?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? fallback;
    }

    public static string ToLabel(string color)
    {
        return string.IsNullOrWhiteSpace(color)
            ? string.Empty
            : char.ToUpperInvariant(color[0]) + color[1..].ToLowerInvariant();
    }
}