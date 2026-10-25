using Boxcars.Auth;
using SkiaSharp;

namespace Boxcars.Engine.Tests.Unit;

public class ProfileImageResizerTests
{
    [Theory]
    [InlineData(1024, 512, 256, 128)]
    [InlineData(300, 900, 85, 256)]
    [InlineData(2000, 2000, 256, 256)]
    public void Resize_LargeImage_FitsWithinMaxDimensionKeepingAspectRatio(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var source = new MemoryStream(CreateImage(width, height, SKEncodedImageFormat.Png));

        var resized = ProfileImageResizer.Resize(source);

        using var result = DecodeWebp(resized);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Fact]
    public void Resize_LargeJpeg_FitsWithinMaxDimension()
    {
        using var source = new MemoryStream(CreateImage(3000, 1500, SKEncodedImageFormat.Jpeg));

        var resized = ProfileImageResizer.Resize(source);

        using var result = DecodeWebp(resized);
        Assert.Equal(256, result.Width);
        Assert.Equal(128, result.Height);
    }

    [Fact]
    public void Resize_SmallImage_IsNotEnlarged()
    {
        using var source = new MemoryStream(CreateImage(100, 50, SKEncodedImageFormat.Png));

        var resized = ProfileImageResizer.Resize(source);

        using var result = DecodeWebp(resized);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
    }

    // The source is red on its left half and blue on its right.
    [Theory]
    [InlineData(6, true)]  // turned 90° clockwise: left side ends up on top
    [InlineData(8, false)] // turned 90° counter-clockwise: left side ends up at the bottom
    public void Resize_RotatedJpeg_IsTurnedUpright(ushort orientation, bool redOnTop)
    {
        var jpeg = WithExifOrientation(CreateImage(400, 200, SKEncodedImageFormat.Jpeg), orientation);
        using var source = new MemoryStream(jpeg);

        var resized = ProfileImageResizer.Resize(source);

        using var result = DecodeWebp(resized);
        Assert.Equal(128, result.Width);
        Assert.Equal(256, result.Height);
        var top = result.GetPixel(64, 32);
        var bottom = result.GetPixel(64, 224);
        Assert.True(redOnTop ? IsRed(top) && IsBlue(bottom) : IsBlue(top) && IsRed(bottom));
    }

    [Fact]
    public void Resize_NotAnImage_ReturnsNull()
    {
        using var source = new MemoryStream("not an image"u8.ToArray());

        Assert.Null(ProfileImageResizer.Resize(source));
    }

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap DecodeWebp(byte[]? bytes)
    {
        Assert.NotNull(bytes);
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        return SKBitmap.Decode(bytes);
    }

    // Inserts a minimal EXIF segment holding just the orientation tag right after the JPEG's start marker.
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00,               // little-endian TIFF header
            0x08, 0x00, 0x00, 0x00,                         // first IFD at offset 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // orientation tag, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00                          // no next IFD
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    private static bool IsRed(SKColor color) => color.Red > 200 && color.Blue < 60;

    private static bool IsBlue(SKColor color) => color.Blue > 200 && color.Red < 60;
}
