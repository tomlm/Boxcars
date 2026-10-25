using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Boxcars.Auth;

/// <summary>
/// Shrinks an uploaded profile image to fit within <see cref="MaxDimension"/> pixels on each side, keeping its aspect
/// ratio, and re-encodes it as WebP. Smaller images aren't enlarged.
/// </summary>
public static class ProfileImageResizer
{
    public const int MaxDimension = 256;

    public const string ContentType = "image/webp";

    public const string AcceptedContentTypes = "image/png,image/jpeg,image/gif,image/webp";

    public static bool IsAcceptedContentType(string? contentType)
    {
        return contentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
    }

    /// <summary>Returns the resized image bytes, or null if the stream isn't a readable image. The stream must be seekable.</summary>
    public static async Task<byte[]?> ResizeAsync(Stream source, CancellationToken cancellationToken)
    {
        try
        {
            var startPosition = source.Position;
            var info = await Image.IdentifyAsync(source, cancellationToken);
            source.Position = startPosition;

            var tooLarge = info.Width > MaxDimension || info.Height > MaxDimension;

            // TargetSize lets the decoder scale down while decoding, so a huge photo isn't fully expanded in memory.
            // It also scales small images up, so it's only used when the image needs shrinking.
            var decoderOptions = tooLarge
                ? new DecoderOptions { TargetSize = new Size(MaxDimension, MaxDimension) }
                : new DecoderOptions();
            using var image = await Image.LoadAsync(decoderOptions, source, cancellationToken);

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = 85 }, cancellationToken);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }
    }
}
