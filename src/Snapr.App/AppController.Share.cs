using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.App;

public sealed partial class AppController
{
    readonly ShareService _share = new();
    readonly HashSet<string> _sharing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one Share entry point. key identifies what is shared (a second share of it is ignored while one runs);
    /// prepareFile returns the file to send; deleteAfter removes that file's temp folder when the share is done.
    /// </summary>
    public async Task ShareAsync(Window owner, ShareTarget target, string key, Func<string> prepareFile, bool deleteAfter)
    {
        if (!ShareSetup.IsReady(_settings.Share, target))
        {
            ShowSettings("Sharing");
            return;
        }
        key = Path.GetFullPath(key);
        if (!_sharing.Add(key)) return;
        string? file = null;
        var windowsOwnsTemp = false;
        try
        {
            file = prepareFile();
            switch (target)
            {
                case ShareTarget.Folder:
                    var copied = await _share.CopyToFolderAsync(file, _settings.Share);
                    CopyLink(owner, copied);
                    Notify(string.IsNullOrWhiteSpace(_settings.Share.UrlPrefix) ? "Path copied" : "Link copied", Uri.UnescapeDataString(Path.GetFileName(copied)));
                    break;
                case ShareTarget.Upload:
                    var link = await _share.UploadAsync(file, _settings.Share.Upload!, CancellationToken.None);
                    CopyLink(owner, link);
                    Notify("Uploaded, link copied", link);
                    break;
                default:
                    await ShareService.ShowWindowsShareAsync(owner, file);
                    if (deleteAfter)
                    {
                        windowsOwnsTemp = true;
                        DeleteTempWhenDone(owner, file);
                    }
                    break;
            }
        }
        catch (ShareException ex)
        {
            MessageBox.Show(owner, ex.Message, "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Share failed", ex);
            MessageBox.Show(owner, "Snapr couldn't prepare the file to share.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _sharing.Remove(key);
            if (deleteAfter && !windowsOwnsTemp && file is not null) DeleteTemp(file);
        }
    }

    static void CopyLink(Window owner, string text)
    {
        if (!ClipboardImage.SetText(text))
            MessageBox.Show(owner, "Another app is holding the clipboard. The link is:\n\n" + text, "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Renders the capture into a new %TEMP%\snapr-share-{guid} folder as {stem}.png and returns its path.</summary>
    internal static string WriteShareTemp(BitmapSource image, string? stem)
    {
        var folder = Path.Combine(Path.GetTempPath(), "snapr-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var name = string.Concat((string.IsNullOrWhiteSpace(stem) ? "Snapr capture" : stem).Split(Path.GetInvalidFileNameChars())) + ".png";
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, ImageIo.EncodePng(image));
            return path;
        }
        catch
        {
            DeleteTemp(Path.Combine(folder, "x"));
            throw;
        }
    }

    static void DeleteTemp(string file)
    {
        try { Directory.Delete(Path.GetDirectoryName(file)!, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write("Couldn't delete the share temp folder", ex); }
    }

    /// <summary>The target app may still be reading the file: delete when the owner closes or after 10 minutes, whichever is first.</summary>
    static void DeleteTempWhenDone(Window owner, string file)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        EventHandler? closed = null;
        void Done()
        {
            timer.Stop();
            owner.Closed -= closed;
            DeleteTemp(file);
        }
        closed = (_, _) => Done();
        timer.Tick += (_, _) => Done();
        owner.Closed += closed;
        timer.Start();
    }
}
