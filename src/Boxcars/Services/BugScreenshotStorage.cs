using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Boxcars.Services;

/// <summary>
/// Stores bug report screenshots in a private blob container. The app serves them from
/// <see cref="ScreenshotPathPrefix"/> without sign-in so the public GitHub issue can show them; each has a random
/// name, so only the issue links to it.
/// </summary>
public sealed partial class BugScreenshotStorage : IDisposable
{
    public const string ScreenshotPathPrefix = "/bug-screenshot";

    /// <summary>The largest screenshot accepted from the browser.</summary>
    public const long MaximumScreenshotBytes = 8 * 1024 * 1024;

    private const string ContainerName = "bug-screenshots";

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public BugScreenshotStorage(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public void Dispose() => _initLock.Dispose();

    /// <summary>Uploads a JPEG screenshot and returns the app path it's served from, or null if it couldn't be stored.</summary>
    public async Task<string?> UploadAsync(byte[] jpegBytes, CancellationToken cancellationToken)
    {
        if (jpegBytes.Length == 0 || jpegBytes.Length > MaximumScreenshotBytes)
        {
            return null;
        }

        var blobName = $"{Guid.NewGuid():N}.jpg";

        // Best-effort: the bug is still filed without its screenshot.
        try
        {
            await EnsureContainerAsync(cancellationToken);
            using var stream = new MemoryStream(jpegBytes, writable: false);
            await _container.GetBlobClient(blobName).UploadAsync(
                stream,
                new BlobHttpHeaders { ContentType = "image/jpeg" },
                cancellationToken: cancellationToken);
            return $"{ScreenshotPathPrefix}/{blobName}";
        }
        catch (RequestFailedException)
        {
            return null;
        }
    }

    /// <summary>Opens a stored screenshot for streaming, or returns null when there's none with that name.</summary>
    public async Task<Stream?> OpenAsync(string blobName, CancellationToken cancellationToken)
    {
        if (!ScreenshotName().IsMatch(blobName))
        {
            return null;
        }

        try
        {
            var response = await _container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.jpg$")]
    private static partial Regex ScreenshotName();

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
