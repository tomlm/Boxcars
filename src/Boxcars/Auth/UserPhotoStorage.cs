using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Boxcars.Auth;

/// <summary>
/// Stores externally-fetched user profile photos in a public blob container so
/// they can be referenced as ordinary image URLs from <c>ApplicationUser.ThumbnailUrl</c>.
/// </summary>
public sealed class UserPhotoStorage : IDisposable
{
    private const string ContainerName = "user-photos";

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public void Dispose() => _initLock.Dispose();

    public UserPhotoStorage(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

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

        await EnsureContainerAsync(cancellationToken);

        var extension = contentType switch
        {
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => "jpg",
        };
        var blobName = $"{userId}.{extension}";
        var blob = _container.GetBlobClient(blobName);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            await blob.UploadAsync(
                stream,
                new BlobHttpHeaders { ContentType = contentType },
                cancellationToken: cancellationToken);
            return blob.Uri.ToString();
        }
        catch
        {
            return null;
        }
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

            await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
