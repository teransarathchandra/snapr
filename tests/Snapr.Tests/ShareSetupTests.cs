using Snapr.App.Services;
using Snapr.Core.Settings;
using Xunit;

namespace Snapr.Tests;

public class ShareSetupTests
{
    [Fact]
    public void IsReady_checks_the_target_settings()
    {
        var empty = new ShareSettings();
        Assert.False(ShareSetup.IsReady(empty, ShareTarget.Folder));
        Assert.False(ShareSetup.IsReady(empty, ShareTarget.Upload));
        Assert.True(ShareSetup.IsReady(empty, ShareTarget.Windows));

        var set = new ShareSettings(@"C:\Share", null, new UploadSettings("https://example.com/up"));
        Assert.True(ShareSetup.IsReady(set, ShareTarget.Folder));
        Assert.True(ShareSetup.IsReady(set, ShareTarget.Upload));
        Assert.False(ShareSetup.IsReady(new ShareSettings("relative", null, new UploadSettings("ftp://x")), ShareTarget.Folder));
        Assert.False(ShareSetup.IsReady(new ShareSettings(null, null, new UploadSettings("http://example.com/up")), ShareTarget.Upload));
    }

    [Fact]
    public void Validate_only_checks_the_parts_that_are_entered()
    {
        Assert.Null(ShareSetup.Validate("", "not a url", "", UploadLinkMode.JsonField, ""));
        Assert.Equal("The link prefix must start with http:// or https://.", ShareSetup.Validate(@"C:\Share", "ftp://x", "", UploadLinkMode.WholeBody, null));
        Assert.NotNull(ShareSetup.Validate("relative", null, "", UploadLinkMode.WholeBody, null));
        Assert.NotNull(ShareSetup.Validate("", null, "example.com", UploadLinkMode.WholeBody, null));
        Assert.StartsWith("Enter the JSON field", ShareSetup.Validate("", null, "https://example.com/up", UploadLinkMode.JsonField, " ")!);
        Assert.Null(ShareSetup.Validate(@"C:\Share", "https://x.test/s", "https://example.com/up", UploadLinkMode.JsonField, "data.link"));
    }
}
