namespace Boxcars.Data.Maps;

public static class ColorUtilities
{
    /// <summary>
    /// Returns black or white depending on the perceived luminance of the given hex color,
    /// ensuring readable contrast.
    /// </summary>
    public static string GetContrastColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor) || hexColor.Length < 7 || hexColor[0] != '#')
        {
            return "#FFFFFF";
        }

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);
        var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return luminance > 0.5 ? "#000000" : "#FFFFFF";
    }
}
