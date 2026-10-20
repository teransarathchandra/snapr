using Snapr.App.Services;

namespace Snapr.Tests;

public class WebcamServiceTests
{
    [Fact]
    public async Task Listing_cameras_never_throws()
    {
        var cameras = await WebcamService.ListAsync();
        Assert.NotNull(cameras);
        Assert.All(cameras, c => Assert.False(string.IsNullOrEmpty(c.Id)));
    }
}
