using System.Globalization;
using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class MapBackgroundResolver
{
    private readonly IWebHostEnvironment _environment;

    public MapBackgroundResolver(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<BackgroundResolutionResult> ResolveAsync(
        MapDefinition mapDefinition,
        string? uploadedBackgroundDataUrl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(uploadedBackgroundDataUrl))
        {
            return BackgroundResolutionResult.Success(uploadedBackgroundDataUrl);
        }

        if (string.IsNullOrWhiteSpace(mapDefinition.BackgroundKey))
        {
            return BackgroundResolutionResult.Failure("Background asset is required but no map background key was provided.");
        }

        var candidates = BuildCandidatePaths(mapDefinition.BackgroundKey);
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            await using var stream = File.OpenRead(candidate);
            await using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var contentType = GetContentType(candidate);
            var base64 = Convert.ToBase64String(bytes);
            return BackgroundResolutionResult.Success($"data:{contentType};base64,{base64}");
        }

        return BackgroundResolutionResult.Failure(
            $"Background image for key '{mapDefinition.BackgroundKey}' was not found. Upload a background image or place one in wwwroot/maps.");
    }

    private string[] BuildCandidatePaths(string backgroundKey)
    {
        var key = backgroundKey.Trim();
        var webRoot = _environment.WebRootPath;
        var mapRoot = Path.Combine(webRoot, "maps");

        return new[]
        {
            Path.Combine(mapRoot, $"{key}BGND2.JPG"),
            Path.Combine(mapRoot, $"{key}BGND2.jpg"),
            Path.Combine(mapRoot, $"{key}BGND.JPG"),
            Path.Combine(mapRoot, $"{key}BGND.jpg"),
            Path.Combine(mapRoot, $"{key}.JPG"),
            Path.Combine(mapRoot, $"{key}.jpg"),
            Path.Combine(webRoot, $"{key}BGND2.JPG"),
            Path.Combine(webRoot, $"{key}BGND2.jpg"),
            Path.Combine(webRoot, $"{key}.JPG"),
            Path.Combine(webRoot, $"{key}.jpg")
        };
    }

    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLower(CultureInfo.InvariantCulture);
        return extension switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }
}

public sealed record BackgroundResolutionResult(bool Succeeded, string? DataUrl, string? Error)
{
    public static BackgroundResolutionResult Success(string dataUrl) => new(true, dataUrl, null);

    public static BackgroundResolutionResult Failure(string error) => new(false, null, error);
}
