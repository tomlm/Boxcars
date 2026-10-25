using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Boxcars.Auth;

/// <summary>
/// Stores externally-fetched user profile photos in a private blob container. The app serves them from
/// <see cref="PhotoPathPrefix"/> (see <see cref="AuthEndpoints"/>), so <c>ApplicationUser.ThumbnailUrl</c> is an
/// ordinary image URL even though the storage account doesn't allow public access.
/// </summary>
public sealed class UserPhotoStorage : IDisposable
{
    public const string PhotoPathPrefix = "/user-photo";

    private const string ContainerName = "user-photos";

    private const string UploadedImagePrefix = "upload-";

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public void Dispose() => _initLock.Dispose();

    public UserPhotoStorage(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    /// <summary>Uploads the photo and returns the app URL it's served from, or null if it couldn't be stored.</summary>
    public async Task<string?> UploadAsync(
        string userId,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        // Hashed so photo URLs don't expose the user's email address.
        var blobName = $"{HashUserId(userId)}.{GetExtension(contentType)}";

        // Best-effort: a storage failure must not fail sign-in.
        return await TryUploadBlobAsync(blobName, bytes, contentType, cancellationToken);
    }

    /// <summary>
    /// Uploads a profile image picked in the app (already resized by <see cref="ProfileImageResizer"/>) and returns the
    /// app URL it's served from, or null if it couldn't be stored. Each upload gets its own blob name so sign-in's
    /// photo refresh (which reuses the hashed name) can't overwrite it, and browsers don't keep showing a cached
    /// earlier image.
    /// </summary>
    public async Task<string?> UploadUserImageAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var blobName = $"{UploadedImagePrefix}{Guid.NewGuid():N}.webp";
        return await TryUploadBlobAsync(blobName, bytes, ProfileImageResizer.ContentType, cancellationToken);
    }

    /// <summary>
    /// Deletes an image uploaded with <see cref="UploadUserImageAsync"/>. Other URLs (sign-in photos, external links)
    /// are ignored. Pass only a URL read from the user's saved record.
    /// </summary>
    public async Task DeleteUserImageAsync(string? photoUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(photoUrl)
            || !photoUrl.StartsWith($"{PhotoPathPrefix}/{UploadedImagePrefix}", StringComparison.Ordinal))
        {
            return;
        }

        var blobName = photoUrl[(PhotoPathPrefix.Length + 1)..];
        if (blobName.Contains('/', StringComparison.Ordinal))
        {
            return;
        }

        // Best-effort: a leftover blob is harmless.
        try
        {
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
        }
    }

    /// <summary>Opens a stored photo for streaming, or returns null when there's no photo with that name.</summary>
    public async Task<(Stream Content, string ContentType)?> OpenAsync(string blobName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var response = await _container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return (response.Value.Content, response.Value.Details.ContentType ?? "image/jpeg");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<string?> TryUploadBlobAsync(
        string blobName,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureContainerAsync(cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            await _container.GetBlobClient(blobName).UploadAsync(
                stream,
                new BlobHttpHeaders { ContentType = contentType },
                cancellationToken: cancellationToken);
            return $"{PhotoPathPrefix}/{blobName}";
        }
        catch
        {
            return null;
        }
    }

    private static string GetExtension(string contentType)
    {
        return contentType switch
        {
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => "jpg",
        };
    }

    private static string HashUserId(string userId)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
    }

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
