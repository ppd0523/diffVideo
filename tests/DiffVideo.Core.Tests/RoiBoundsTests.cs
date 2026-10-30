using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class RoiBoundsTests
{
    [Theory]
    [InlineData(100, 80, 600, 400, 1920, 1080, 100, 80, 600, 400)]
    [InlineData(100, 80, 1920, 1080, 1920, 1080, 100, 80, 1820, 1000)]
    [InlineData(-1, -100, -1, 0, 1920, 1080, 0, 0, 2, 2)]
    [InlineData(1920, 1080, 900, 500, 1920, 1080, 1918, 1078, 2, 2)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 1920, 1080, 1918, 1078, 2, 2)]
    [InlineData(100, 200, 1080, 1920, 1080, 1920, 100, 200, 980, 1720)] // portrait frame
    [InlineData(100, 100, 400, 500, 2, 2, 0, 0, 2, 2)] // two-pixel source keeps the whole frame
    public void CropAlwaysFitsSource(int x, int y, int width, int height, int frameWidth, int frameHeight, int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new(expectedX, expectedY, expectedWidth, expectedHeight), RoiBounds.Clamp(new(x, y, width, height), frameWidth, frameHeight));
    }
}
