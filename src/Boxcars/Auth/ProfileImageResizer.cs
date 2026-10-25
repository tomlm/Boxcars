using SkiaSharp;

namespace Boxcars.Auth;

/// <summary>
/// Shrinks an uploaded profile image to fit within <see cref="MaxDimension"/> pixels on each side, keeping its aspect
/// ratio, and re-encodes it as WebP. Smaller images aren't enlarged. Camera rotation (EXIF orientation) is applied so
/// phone photos come out upright.
/// </summary>
public static class ProfileImageResizer
{
    public const int MaxDimension = 256;

    public const string ContentType = "image/webp";

    public const string AcceptedContentTypes = "image/png,image/jpeg,image/gif,image/webp";

    private const int WebpQuality = 85;

    public static bool IsAcceptedContentType(string? contentType)
    {
        return contentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
    }

    /// <summary>Returns the resized image bytes, or null if the stream isn't a readable image.</summary>
    public static byte[]? Resize(Stream source)
    {
        using var codec = SKCodec.Create(source);
        if (codec is null)
        {
            return null;
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        var scale = Math.Min(1f, (float)MaxDimension / Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        // JPEGs can decode straight at a reduced size, so a huge photo isn't fully expanded in memory. Fall back to
        // full size if the codec would decode smaller than the target.
        var decodeSize = codec.GetScaledDimensions(scale);
        if (decodeSize.Width < targetWidth || decodeSize.Height < targetHeight)
        {
            decodeSize = new SKSizeI(width, height);
        }

        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        using var resized = decoded.Width == targetWidth && decoded.Height == targetHeight
            ? decoded.Copy()
            : decoded.Resize(
                new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
        {
            return null;
        }

        using var upright = ApplyOrigin(resized, codec.EncodedOrigin);
        using var image = SKImage.FromBitmap(upright);
        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return data?.ToArray();
    }

    /// <summary>Redraws the bitmap so the way the camera was held (its EXIF orientation) is baked into the pixels.</summary>
    private static SKBitmap ApplyOrigin(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return bitmap.Copy();
        }

        var w = bitmap.Width;
        var h = bitmap.Height;
        var swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(new SKImageInfo(swapsSides ? h : w, swapsSides ? w : h, bitmap.ColorType, bitmap.AlphaType));

        using var canvas = new SKCanvas(output);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // upside down
                canvas.Translate(w, h);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // upside down, mirrored
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // turned 90° clockwise
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.Translate(h, w);
                canvas.Scale(1, -1);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom: // turned 90° counter-clockwise
                canvas.Translate(0, w);
                canvas.RotateDegrees(270);
                break;
        }

        // Quarter turns and flips map pixels one-to-one, so no smoothing is needed.
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return output;
    }
}
