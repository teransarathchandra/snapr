using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Snapr.Core;
using Snapr.Core.Output;
using Snapr.Core.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace Snapr.App.Services;

public sealed class ShareException(string message) : Exception(message);

/// <summary>Sends a file to the share folder, a custom upload endpoint or the Windows Share dialog.</summary>
public sealed class ShareService
{
    static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 1 << 20,
    };
    readonly TimeSpan _timeout;

    public ShareService() : this(TimeSpan.FromSeconds(60)) { }

    // Public because Snapr.App has no InternalsVisibleTo for the tests.
    public ShareService(TimeSpan timeout) => _timeout = timeout;

    public Task<string> CopyToFolderAsync(string file, ShareSettings s) => Task.Run(() =>
    {
        try
        {
            var folder = s.Folder!.Trim();
            Directory.CreateDirectory(folder);
            var name = FileNames.Unique(Path.GetFileNameWithoutExtension(file), Path.GetExtension(file).TrimStart('.'),
                n => File.Exists(Path.Combine(folder, n)));
            var full = Path.Combine(folder, name);
            File.Copy(file, full);
            return ShareLinks.Build(s.UrlPrefix, name, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ShareException($"Snapr couldn't copy to the share folder: {ex.Message}");
        }
    });

    public async Task<string> UploadAsync(string file, UploadSettings u, CancellationToken ct)
    {
        long size;
        try { size = new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ShareException($"The upload failed: {ex.Message}");
        }
        if (size > ShareRules.MaxUploadBytes) throw new ShareException("The file is larger than 200 MB, so it wasn't uploaded.");

        string? header = null;
        if (!string.IsNullOrWhiteSpace(u.HeaderName) && !string.IsNullOrEmpty(u.HeaderValueProtected))
        {
            header = SecretStore.Unprotect(u.HeaderValueProtected)
                ?? throw new ShareException("The saved upload header couldn't be read. Enter it again in Settings.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            await using var stream = File.OpenRead(file);
            using var content = new MultipartFormDataContent();
            var part = new StreamContent(stream);
            part.Headers.ContentType = new(MimeOf(file));
            var fileName = Path.GetFileName(file);
            var disp = new ContentDispositionHeaderValue("form-data") { Name = $"\"{u.FieldName}\"", FileName = $"\"{fileName.Replace("\"", "_")}\"" };
            if (fileName.Any(c => c > 127)) disp.FileNameStar = fileName;
            part.Headers.ContentDisposition = disp;
            content.Add(part);
            using var req = new HttpRequestMessage(HttpMethod.Post, u.Url) { Content = content };
            if (header is not null) req.Headers.TryAddWithoutValidation(u.HeaderName!, header);

            using var resp = await Http.SendAsync(req, timeout.Token);
            var body = await resp.Content.ReadAsStringAsync(timeout.Token);
            Log.Write($"Upload to {new Uri(u.Url).Host}: {(int)resp.StatusCode}, {size} bytes");
            if (!resp.IsSuccessStatusCode)
            {
                var excerpt = body.Length > 200 ? body[..200] : body;
                excerpt = excerpt.Trim();
                throw new ShareException($"The upload failed: {(int)resp.StatusCode} {resp.ReasonPhrase}." + (excerpt.Length > 0 ? " " + excerpt : ""));
            }
            return UploadResponse.Link(body, u.LinkMode, u.JsonPath)
                ?? throw new ShareException(u.LinkMode == UploadLinkMode.WholeBody
                    ? "The upload worked but the response wasn't a link."
                    : $"The upload worked but the response had no link at {u.JsonPath}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ShareException($"The upload timed out after {_timeout.TotalSeconds:0.#} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new ShareException($"The upload failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException or InvalidOperationException or NotSupportedException)
        {
            // Messages here can carry the URL (e.g. UriFormatException), so keep only the type.
            Log.Write($"Upload failed: {ex.GetType().Name}");
            throw new ShareException(ex is IOException or UnauthorizedAccessException
                ? $"The upload failed: {ex.Message}" : "The upload failed: the upload address isn't valid.");
        }
    }

    static string MimeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream",
    };

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    public static Task ShowWindowsShareAsync(Window owner, string file)
    {
        DataTransferManager? dtm = null;
        TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
        try
        {
            var hwnd = new WindowInteropHelper(owner).Handle;
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var iid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");
            var p = interop.GetForWindow(hwnd, ref iid);
            try { dtm = DataTransferManager.FromAbi(p); }
            finally { Marshal.Release(p); }
            handler = async (sender, args) =>
            {
                sender.DataRequested -= handler;
                var d = args.Request.GetDeferral();
                try
                {
                    args.Request.Data.Properties.Title = Path.GetFileName(file);
                    var item = await StorageFile.GetFileFromPathAsync(file);
                    args.Request.Data.SetStorageItems([item]);
                }
                catch (Exception ex)
                {
                    Log.Write("Windows Share data request failed", ex);
                }
                finally { d.Complete(); }
            };
            dtm.DataRequested += handler;
            interop.ShowShareUIForWindow(hwnd);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (dtm is not null && handler is not null) dtm.DataRequested -= handler;
            Log.Write("Windows Share failed", ex);
            throw new ShareException("Windows couldn't open the Share dialog.");
        }
    }
}
