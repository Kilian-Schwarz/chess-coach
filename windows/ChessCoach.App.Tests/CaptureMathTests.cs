using System.Drawing;
using ChessCoach.App.Platform;

namespace ChessCoach.App.Tests;

public class CaptureMathTests
{
    [Fact]
    public void RegionInsideWindowMapsToLocalCoordinates()
    {
        var crop = CaptureMath.WindowCrop(new Rectangle(300, 200, 400, 400), new Rectangle(100, 50, 1000, 800), out var clipped);
        Assert.Equal(new Rectangle(200, 150, 400, 400), crop);
        Assert.Equal(new Rectangle(300, 200, 400, 400), clipped);
    }

    [Fact]
    public void RegionBeyondWindowIsClipped()
    {
        var crop = CaptureMath.WindowCrop(new Rectangle(50, 0, 300, 300), new Rectangle(100, 50, 1000, 800), out var clipped);
        Assert.Equal(new Rectangle(0, 0, 250, 250), crop);
        Assert.Equal(new Rectangle(100, 50, 250, 250), clipped);
    }

    [Fact]
    public void WindowOnSecondMonitorWithNegativeOrigin()
    {
        var crop = CaptureMath.WindowCrop(new Rectangle(-1500, 100, 300, 300), new Rectangle(-1920, 0, 1920, 1080), out var clipped);
        Assert.Equal(new Rectangle(420, 100, 300, 300), crop);
        Assert.Equal(new Rectangle(-1500, 100, 300, 300), clipped);
    }

    [Fact]
    public void RegionOutsideWindowIsEmpty()
    {
        var crop = CaptureMath.WindowCrop(new Rectangle(2000, 2000, 100, 100), new Rectangle(0, 0, 1000, 800), out _);
        Assert.True(crop.IsEmpty);
    }

    [Fact]
    public void UniformImagesAreDetected()
    {
        var black = new byte[16 * 4];
        for (int i = 3; i < black.Length; i += 4) black[i] = 255;
        Assert.True(CaptureMath.IsUniform(black, 16));
    }

    [Fact]
    public void SinglePixelDifferenceIsNotUniform()
    {
        var img = new byte[16 * 4];
        img[15 * 4 + 1] = 7;
        Assert.False(CaptureMath.IsUniform(img, 16));
    }
}
