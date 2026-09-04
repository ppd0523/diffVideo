using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class RoiBoundsTests
{
    [Theory]
    [InlineData(100, 80, 600, 400, 100, 80, 600, 400)]
    [InlineData(100, 80, 1920, 1080, 100, 80, 1820, 1000)]
    [InlineData(-1, -100, -1, 0, 0, 0, 2, 2)]
    [InlineData(1920, 1080, 900, 500, 1918, 1078, 2, 2)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 1918, 1078, 2, 2)]
    public void CropAlwaysFitsSource(int x, int y, int width, int height, int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new(expectedX, expectedY, expectedWidth, expectedHeight), RoiBounds.Clamp(new(x, y, width, height), 1920, 1080));
    }

    [Fact]
    public void CropUsesDisplayOrientedPortraitBounds()
    {
        Assert.Equal(new(100, 200, 980, 1720), RoiBounds.Clamp(new(100, 200, 1080, 1920), 1080, 1920));
    }

    [Fact]
    public void TwoPixelSourceKeepsWholeFrame()
    {
        Assert.Equal(new(0, 0, 2, 2), RoiBounds.Clamp(new(100, 100, 400, 500), 2, 2));
    }
}
