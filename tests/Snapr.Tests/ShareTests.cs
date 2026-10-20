using Snapr.Core.Output;
using Snapr.Core.Settings;

namespace Snapr.Tests;

public class ShareTests
{
    [Theory]
    [InlineData(null, "Choose the share folder.")]
    [InlineData("  ", "Choose the share folder.")]
    [InlineData("snapr", @"Use a full folder path, such as C:\Users\you\OneDrive\Snapr.")]
    [InlineData(@"C:\Share", null)]
    [InlineData(@"\\server\share", null)]
    public void Folder_rules(string? folder, string? error) => Assert.Equal(error, ShareRules.FolderError(folder));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("https://files.example.com/snapr/", null)]
    [InlineData("http://intranet/snapr", null)]
    [InlineData("files.example.com", "The link prefix must start with http:// or https://.")]
    [InlineData("ftp://x/", "The link prefix must start with http:// or https://.")]
    public void Prefix_rules(string? prefix, string? error) => Assert.Equal(error, ShareRules.PrefixError(prefix));

    [Theory]
    [InlineData(null, "Enter the upload URL.")]
    [InlineData("", "Enter the upload URL.")]
    [InlineData("example.com/upload", "Enter a full URL, such as https://example.com/upload.")]
    [InlineData("ftp://example.com/upload", "Enter a full URL, such as https://example.com/upload.")]
    [InlineData("https://example.com/upload", null)]
    [InlineData("http://localhost:8080/up", null)]
    [InlineData("http://127.0.0.1/up", null)]
    [InlineData("http://[::1]/up", null)]
    [InlineData("http://example.com/upload", "Use https:// (http:// is only allowed for localhost).")]
    public void Upload_url_rules(string? url, string? error) => Assert.Equal(error, ShareRules.UploadUrlError(url));

    [Fact]
    public void Json_path_needed_only_for_json_mode()
    {
        Assert.Null(ShareRules.JsonPathError(UploadLinkMode.WholeBody, null));
        Assert.Equal("Enter the JSON field that holds the link, such as data.link.", ShareRules.JsonPathError(UploadLinkMode.JsonField, " "));
        Assert.Null(ShareRules.JsonPathError(UploadLinkMode.JsonField, "data.link"));
        Assert.Equal(200L * 1024 * 1024, ShareRules.MaxUploadBytes);
    }

    [Theory]
    [InlineData(null, "a b.png", @"C:\S\a b.png", @"C:\S\a b.png")]
    [InlineData("https://x.com/s", "a b.png", @"C:\S\a b.png", "https://x.com/s/a%20b.png")]
    [InlineData("https://x.com/s/", "#1.png", @"C:\S\#1.png", "https://x.com/s/%231.png")]
    [InlineData("https://x.com/", "café.png", @"C:\S\café.png", "https://x.com/caf%C3%A9.png")]
    public void Links(string? prefix, string name, string path, string expected) => Assert.Equal(expected, ShareLinks.Build(prefix, name, path));

    [Theory]
    [InlineData("  https://i.example/a.png \n", UploadLinkMode.WholeBody, null, "https://i.example/a.png")]
    [InlineData("<html>error</html>", UploadLinkMode.WholeBody, null, null)]
    [InlineData("""{"data":{"link":"https://i.example/b.png"}}""", UploadLinkMode.JsonField, "data.link", "https://i.example/b.png")]
    [InlineData("""{"files":[{"url":"https://i.example/c.png"}]}""", UploadLinkMode.JsonField, "files.0.url", "https://i.example/c.png")]
    [InlineData("""{"Data":{"Link":"https://i.example/d.png"}}""", UploadLinkMode.JsonField, "data.link", "https://i.example/d.png")]
    [InlineData("""{"data":{}}""", UploadLinkMode.JsonField, "data.link", null)]
    [InlineData("""{"data":{"link":42}}""", UploadLinkMode.JsonField, "data.link", null)]
    [InlineData("""{"data":{"link":"ftp://x/y"}}""", UploadLinkMode.JsonField, "data.link", null)]
    [InlineData("""{"data":{"link":" https://i.example/e.png "}}""", UploadLinkMode.JsonField, "data.link", "https://i.example/e.png")]
    [InlineData("not json", UploadLinkMode.JsonField, "data.link", null)]
    [InlineData("""{"files":[]}""", UploadLinkMode.JsonField, "files.0.url", null)]
    public void Response_links(string body, UploadLinkMode mode, string? path, string? expected) => Assert.Equal(expected, UploadResponse.Link(body, mode, path));
}
