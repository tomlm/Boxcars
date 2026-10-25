using Boxcars.Auth;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Boxcars.Engine.Tests.Unit;

public class ProfileImageResizerTests
{
    [Theory]
    [InlineData(1024, 512, 256, 128)]
    [InlineData(300, 900, 85, 256)]
    [InlineData(2000, 2000, 256, 256)]
    public async Task ResizeAsync_LargeImage_FitsWithinMaxDimensionKeepingAspectRatio(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var source = await CreatePngAsync(width, height);

        var resized = await ProfileImageResizer.ResizeAsync(source, CancellationToken.None);

        Assert.NotNull(resized);
        using var result = Image.Load(resized);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        Assert.Equal("image/webp", result.Metadata.DecodedImageFormat?.DefaultMimeType);
    }

    [Fact]
    public async Task ResizeAsync_SmallImage_IsNotEnlarged()
    {
        using var source = await CreatePngAsync(100, 50);

        var resized = await ProfileImageResizer.ResizeAsync(source, CancellationToken.None);

        Assert.NotNull(resized);
        using var result = Image.Load(resized);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
    }

    [Fact]
    public async Task ResizeAsync_NotAnImage_ReturnsNull()
    {
        using var source = new MemoryStream("not an image"u8.ToArray());

        var resized = await ProfileImageResizer.ResizeAsync(source, CancellationToken.None);

        Assert.Null(resized);
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 40, 120));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
