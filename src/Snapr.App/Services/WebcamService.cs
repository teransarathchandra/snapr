using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapr.Core;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Snapr.App.Services;

public sealed class WebcamException(string message) : Exception(message);

/// <summary>Reads a camera into a WriteableBitmap on the UI thread; drops frames while a copy is pending.</summary>
public sealed class WebcamService : IAsyncDisposable
{
    readonly MediaCapture _capture;
    readonly MediaFrameReader _reader;
    readonly Dispatcher _dispatcher;
    byte[]? _buffer;
    int _pending, _disposed;
    bool _loggedError;

    /// <summary>Null until the first frame arrives. Touch on the UI thread only.</summary>
    public WriteableBitmap? Frame { get; private set; }

    /// <summary>Raised on the UI thread each time <see cref="Frame"/> is created or replaced.</summary>
    public event Action? FrameChanged;

    WebcamService(MediaCapture capture, MediaFrameReader reader, Dispatcher dispatcher)
    {
        _capture = capture;
        _reader = reader;
        _dispatcher = dispatcher;
    }

    public static async Task<IReadOnlyList<(string Id, string Name)>> ListAsync()
    {
        try
        {
            var all = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return all.Select(d => (d.Id, d.Name)).ToList();
        }
        catch (Exception ex)
        {
            Log.Write("Webcam: listing cameras failed", ex);
            return [];
        }
    }

    /// <summary>Call on the UI thread.</summary>
    public static async Task<WebcamService> OpenAsync(string? deviceId)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var cameras = await ListAsync();
        var id = cameras.FirstOrDefault(c => c.Id == deviceId).Id ?? cameras.FirstOrDefault().Id
            ?? throw new WebcamException("No camera was found.");
        MediaCapture? capture = null;
        WebcamService? service = null;
        MediaFrameReader? reader = null;
        void Cleanup()
        {
            if (reader is not null && service is not null) reader.FrameArrived -= service.OnFrame;
            reader?.Dispose();
            capture?.Dispose();
        }
        try
        {
            capture = await InitAsync(id, MediaCaptureSharingMode.SharedReadOnly)
                ?? await InitAsync(id, MediaCaptureSharingMode.ExclusiveControl)
                ?? throw new WebcamException("The camera is in use or unavailable (could not initialise).");
            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new WebcamException("The camera is in use or unavailable (no colour stream).");
            var format = PickFormat(source.SupportedFormats);
            if (format is not null)
            {
                try { await source.SetFormatAsync(format); }
                catch (Exception ex) { Log.Write("Webcam: setting the format failed", ex); }
            }
            reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            service = new WebcamService(capture, reader, dispatcher);
            reader.FrameArrived += service.OnFrame;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new WebcamException($"The camera is in use or unavailable ({status}).");
            }
            return service;
        }
        catch (WebcamException)
        {
            Cleanup();
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            Cleanup();
            throw new WebcamException("Windows blocked camera access. Turn on Settings → Privacy & security → Camera → Let desktop apps access your camera.");
        }
        catch (Exception ex)
        {
            Cleanup();
            throw new WebcamException($"The camera is in use or unavailable ({ex.Message.Trim()}).");
        }
    }

    /// <summary>Null when shared mode failed (so the caller may retry exclusive); access-denied and exclusive failures throw.</summary>
    static async Task<MediaCapture?> InitAsync(string id, MediaCaptureSharingMode mode)
    {
        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = mode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            return capture;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException && mode == MediaCaptureSharingMode.SharedReadOnly)
        {
            Log.Write("Webcam: shared read-only mode failed, retrying exclusive", ex);
            capture.Dispose();
            return null;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    static MediaFrameFormat? PickFormat(IReadOnlyList<MediaFrameFormat> formats)
    {
        static double Fps(MediaFrameFormat f) => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator;
        static long Distance(MediaFrameFormat f) => Math.Abs((long)f.VideoFormat.Width * f.VideoFormat.Height - 640L * 480);
        var preferred = formats.Where(f => Fps(f) is >= 15 and <= 30).ToList();
        return (preferred.Count > 0 ? preferred : formats).OrderBy(Distance).FirstOrDefault();
    }

    void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
        var queued = false;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bmp = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bmp is null) return;
            using var converted = bmp.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bmp.BitmapAlphaMode == BitmapAlphaMode.Premultiplied
                ? null : SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var src = converted ?? bmp;
            int w = src.PixelWidth, h = src.PixelHeight, stride;
            using (var locked = src.LockBuffer(BitmapBufferAccessMode.Read))
                stride = locked.GetPlaneDescription(0).Stride;
            if (stride < w * 4) stride = w * 4;
            if (_buffer is null || _buffer.Length != stride * h) _buffer = new byte[stride * h];
            src.CopyToBuffer(_buffer.AsBuffer());
            var bytes = _buffer;
            _dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (Volatile.Read(ref _disposed) != 0) return;
                    var replaced = false;
                    if (Frame is null || Frame.PixelWidth != w || Frame.PixelHeight != h)
                    {
                        Frame = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                        replaced = true;
                    }
                    Frame.WritePixels(new Int32Rect(0, 0, w, h), bytes, stride, 0);
                    if (replaced) FrameChanged?.Invoke();
                }
                catch (Exception ex) { LogOnce(ex); }
                finally { Volatile.Write(ref _pending, 0); }
            });
            queued = true;
        }
        catch (Exception ex)
        {
            LogOnce(ex);
        }
        finally
        {
            // Only the queued callback clears the flag on success; every other path (including a failed BeginInvoke) clears it here.
            if (!queued) Volatile.Write(ref _pending, 0);
        }
    }

    void LogOnce(Exception ex)
    {
        if (_loggedError) return;
        _loggedError = true;
        Log.Write("Webcam: frame failed", ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            _reader.FrameArrived -= OnFrame;
            await _reader.StopAsync();
        }
        catch (Exception ex) { Log.Write("Webcam: stopping failed", ex); }
        try
        {
            _reader.Dispose();
            _capture.Dispose();
        }
        catch (Exception ex) { Log.Write("Webcam: disposing failed", ex); }
    }
}
