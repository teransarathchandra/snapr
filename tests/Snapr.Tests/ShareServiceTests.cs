using System.Net;
using System.Net.Sockets;
using System.Text;
using Snapr.App.Services;
using Snapr.Core.Output;
using Snapr.Core.Settings;

namespace Snapr.Tests;

public sealed class ShareServiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "snapr-sharetest-" + Guid.NewGuid());
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    public ShareServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    string MakeFile(string name = "pic.png")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, Png);
        return p;
    }

    [Fact]
    public async Task CopyToFolder_NumbersDuplicatesAndBuildsLinks()
    {
        var file = MakeFile("my pic.png");
        var folder = Path.Combine(_dir, "share");
        var svc = new ShareService();
        var first = await svc.CopyToFolderAsync(file, new ShareSettings(folder));
        var second = await svc.CopyToFolderAsync(file, new ShareSettings(folder, "https://x/s/"));
        Assert.Equal(Path.Combine(folder, "my pic.png"), first);
        Assert.Equal("https://x/s/my%20pic%20%282%29.png", second);
        Assert.True(File.Exists(Path.Combine(folder, "my pic (2).png")));
    }

    [Fact]
    public void SecretStore_RoundTripsAndRejectsGarbage()
    {
        var p = SecretStore.Protect("Bearer abc");
        Assert.DoesNotContain("abc", p);
        Assert.Equal("Bearer abc", SecretStore.Unprotect(p));
        Assert.Null(SecretStore.Unprotect("not base64!"));
        Assert.Null(SecretStore.Unprotect(null));
    }

    sealed class Server : IDisposable
    {
        public HttpListener Listener = new();
        public string Url;
        public Task<(System.Collections.Specialized.NameValueCollection Headers, byte[] Body)>? Served;

        public Server()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Url = $"http://localhost:{port}/";
            Listener.Prefixes.Add(Url);
            Listener.Start();
        }

        /// <summary>Serves one request; a null body never answers.</summary>
        public void ServeOnce(int status, string? body, string? location = null) => Served = Task.Run(async () =>
        {
            var ctx = await Listener.GetContextAsync();
            using var ms = new MemoryStream();
            await ctx.Request.InputStream.CopyToAsync(ms);
            var seen = (ctx.Request.Headers, ms.ToArray());
            if (body is null) { await Task.Delay(3000); return seen; }
            ctx.Response.StatusCode = status;
            if (location is not null) ctx.Response.RedirectLocation = location;
            var b = Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(b);
            ctx.Response.Close();
            return seen;
        });

        public void Dispose() => Listener.Close();
    }

    static Server? TryServer()
    {
        try { return new Server(); }
        catch (HttpListenerException) { return null; } // no URL ACL: skip
    }

    [Fact]
    public async Task Upload_WholeBody_SendsMultipartAndHeader()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, "https://i.example/x.png");
        var u = new UploadSettings(s.Url, "file", "X-Token", SecretStore.Protect("t1"));
        var link = await new ShareService().UploadAsync(MakeFile(), u, default);
        Assert.Equal("https://i.example/x.png", link);
        var (headers, body) = await s.Served!;
        Assert.Equal("t1", headers["X-Token"]);
        var text = Encoding.Latin1.GetString(body);
        Assert.Contains("name=\"file\"; filename=\"pic.png\"", text);
        Assert.True(body.AsSpan().IndexOf(Png) >= 0);
    }

    [Fact]
    public async Task Upload_JsonField_ReturnsLink()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, """{"data":{"link":"https://i.example/y.png"}}""");
        var u = new UploadSettings(s.Url, LinkMode: UploadLinkMode.JsonField, JsonPath: "data.link");
        Assert.Equal("https://i.example/y.png", await new ShareService().UploadAsync(MakeFile(), u, default));
    }

    [Fact]
    public async Task Upload_ServerError_Throws()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(500, "boom");
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), new UploadSettings(s.Url), default));
        Assert.StartsWith("The upload failed: 500", ex.Message);
        Assert.EndsWith("boom", ex.Message);
    }

    [Fact]
    public async Task Upload_HtmlBody_IsNotALink()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, "<html>");
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), new UploadSettings(s.Url), default));
        Assert.Equal("The upload worked but the response wasn't a link.", ex.Message);
    }

    [Fact]
    public async Task Upload_MissingJsonField_Throws()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, """{"other":1}""");
        var u = new UploadSettings(s.Url, LinkMode: UploadLinkMode.JsonField, JsonPath: "data.link");
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), u, default));
        Assert.Equal("The upload worked but the response had no link at data.link.", ex.Message);
    }

    [Fact]
    public async Task Upload_Timeout_Throws()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, null);
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService(TimeSpan.FromMilliseconds(300)).UploadAsync(MakeFile(), new UploadSettings(s.Url), default));
        Assert.StartsWith("The upload timed out after", ex.Message);
    }

    [Fact]
    public async Task Upload_TooLarge_Throws()
    {
        var p = Path.Combine(_dir, "big.mp4");
        using (var f = File.Create(p)) f.SetLength(ShareRules.MaxUploadBytes + 1);
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(p, new UploadSettings("http://localhost:1/"), default));
        Assert.Equal("The file is larger than 200 MB, so it wasn't uploaded.", ex.Message);
    }

    [Fact]
    public async Task Upload_Redirect_IsNotFollowed()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(302, "", "http://localhost:1/elsewhere");
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), new UploadSettings(s.Url), default));
        Assert.Equal("The upload failed: 302 Found.", ex.Message);
    }

    [Fact]
    public async Task Upload_CallerCancel_RethrowsCancellation()
    {
        using var s = TryServer();
        if (s is null) return;
        s.ServeOnce(200, null);
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ShareService().UploadAsync(MakeFile(), new UploadSettings(s.Url), cts.Token));
    }

    [Fact]
    public async Task Upload_UnreadableHeader_Throws()
    {
        var u = new UploadSettings("http://localhost:1/", HeaderName: "X-Token", HeaderValueProtected: "garbage!");
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), u, default));
        Assert.Equal("The saved upload header couldn't be read. Enter it again in Settings.", ex.Message);
    }

    [Fact]
    public async Task Upload_NothingListening_ReportsNetworkError()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var ex = await Assert.ThrowsAsync<ShareException>(() => new ShareService().UploadAsync(MakeFile(), new UploadSettings($"http://localhost:{port}/"), default));
        Assert.StartsWith("The upload failed:", ex.Message);
    }

    [Fact]
    public async Task Upload_MissingFileAndBadUrl_AreShareExceptions()
    {
        var svc = new ShareService();
        var e1 = await Assert.ThrowsAsync<ShareException>(() => svc.UploadAsync(Path.Combine(_dir, "nope.png"), new UploadSettings("http://localhost:1/"), default));
        Assert.StartsWith("The upload failed:", e1.Message);
        var e2 = await Assert.ThrowsAsync<ShareException>(() => svc.UploadAsync(MakeFile(), new UploadSettings("not a url?token=SECRET"), default));
        Assert.StartsWith("The upload failed:", e2.Message);
        Assert.DoesNotContain("SECRET", e2.Message);
    }
}
