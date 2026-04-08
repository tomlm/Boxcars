using System.Net.Http.Headers;

namespace Boxcars.Auth;

/// <summary>
/// Best-effort fetch of a Microsoft Graph user photo. Returns the raw image
/// bytes and content type so the caller can persist them (e.g. to blob storage).
/// </summary>
internal static class GraphPhotoFetcher
{
    private const string PhotoEndpoint = "https://graph.microsoft.com/v1.0/me/photos/96x96/$value";

    public static async Task<(byte[] Bytes, string ContentType)?> TryFetchAsync(
        string? accessToken,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, PhotoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length == 0 ? null : (bytes, contentType);
        }
        catch
        {
            return null;
        }
    }
}
