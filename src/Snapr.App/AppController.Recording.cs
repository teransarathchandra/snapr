using System.Diagnostics;
using System.IO;
using System.Windows;
using Snapr.App.Capture;
using Snapr.App.Dialogs;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Recording;

namespace Snapr.App;

public sealed partial class AppController
{
    RecorderService? _recorder;
    RecordingBar? _bar;
    WebcamService? _camera;
    WebcamBubble? _bubble;

    /// <summary>Starts a recording, or stops the current one (same hotkey).</summary>
    public async Task ToggleRecordingAsync()
    {
        if (_recorder is not null)
        {
            await StopRecordingAsync(failure: null);
            return;
        }
        if (_busy) return;
        if (FfmpegLocator.Find(_settings.FfmpegPath, Environment.GetEnvironmentVariable("PATH")) is not { } ffmpeg)
        {
            Notify("ffmpeg needed", "Snapr records video with ffmpeg. Set the path to ffmpeg.exe in Settings.");
            ShowSettings();
            return;
        }
        _busy = true;
        try
        {
            var grab = CaptureService.GrabVirtualScreen(includeCursor: false);
            var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
            if (await OverlaySession.RunAsync(grab, windows, OverlayMode.Video) is not { } result) return;
            var r = result.Selection.Bounds;
            var screen = new Rct(r.X + grab.OriginX, r.Y + grab.OriginY, r.W, r.H);
            if (screen.W < 16 || screen.H < 16)
            {
                Notify("Area too small", "Select at least 16 × 16 pixels to record.");
                return;
            }
            await CountdownWindow.RunAsync(3);
            if (_settings.Webcam.Enabled) await StartWebcamAsync(screen);
            var recorder = new RecorderService(new RecorderService.Options(ffmpeg, screen, _settings.RecordSystemAudio,
                _settings.RecordMicrophone, _settings.MicDeviceId, CapturePaths.NewVideoPath(CapturePaths.VideosRoot, DateTime.Now)));
            recorder.Failed += errors => Application.Current.Dispatcher.BeginInvoke(() => Fire(() => StopRecordingAsync(errors)));
            recorder.Start();
            _recorder = recorder;
            _bar = new RecordingBar(this, screen, recorder.MicUnavailable, hasCamera: _bubble is not null);
            _bar.Show();
            _tray.SetRecording(true, false);
        }
        finally
        {
            if (_recorder is null) StopWebcam();
            _busy = false;
        }
    }

    async Task StartWebcamAsync(Rct screen)
    {
        const string title = "Snapr couldn't open the camera";
        var open = WebcamService.OpenAsync(_settings.Webcam.DeviceId);
        if (await Task.WhenAny(open, Task.Delay(5000)) != open)
        {
            Notify(title, "The camera took too long to start.");
            _ = open.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) _ = t.Result.DisposeAsync();
                else Log.Write("Webcam: late open failed", t.Exception);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        WebcamService camera;
        try
        {
            camera = await open;
        }
        catch (WebcamException ex)
        {
            Notify(title, ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Log.Write("Webcam: opening failed", ex);
            Notify(title, ex.Message);
            return;
        }
        _camera = camera;
        try
        {
            var bubble = new WebcamBubble(camera, screen, _settings.Webcam);
            if (bubble.Placed)
            {
                _bubble = bubble;
                bubble.Show();
            }
            else StopWebcam();
        }
        catch (Exception ex)   // a camera problem never stops the recording (spec parity 18 §1)
        {
            Log.Write("Webcam: bubble failed", ex);
            StopWebcam();
            Notify(title, ex.Message);
        }
    }

    public void ToggleWebcam() => _bubble?.Toggle();

    void StopWebcam()
    {
        _bubble?.Close();
        _bubble = null;
        if (_camera is not { } camera) return;
        _camera = null;
        _ = Task.Run(async () =>
        {
            try { await camera.DisposeAsync(); }
            catch (Exception ex) { Log.Write("Webcam: closing failed", ex); }
        });
    }

    public void TogglePause()
    {
        if (_recorder is null) return;
        if (_recorder.IsPaused) _recorder.Resume();
        else _recorder.Pause();
        _bar?.SetPaused(_recorder.IsPaused);
        _tray.SetRecording(true, _recorder.IsPaused);
    }

    /// <summary>Start/pause/resume hotkey: starts a recording when none is running, otherwise pauses or resumes it.</summary>
    public async Task PauseResumeRecordingAsync()
    {
        if (_recorder is null) await ToggleRecordingAsync();
        else TogglePause();
    }

    public void RefreshRecents() => _editor?.RefreshRecents();

    /// <param name="failure">ffmpeg's last output if it stopped on its own; null for a normal stop.</param>
    async Task StopRecordingAsync(string? failure)
    {
        if (_recorder is not { } recorder) return;
        _recorder = null;
        _bar?.Close();
        _bar = null;
        StopWebcam();
        _tray.SetRecording(false, false);
        try
        {
            var path = await recorder.StopAsync();
            recorder.Dispose();
            _editor?.RefreshRecents();
            if (failure is not null)
            {
                MessageBox.Show($"ffmpeg stopped unexpectedly. Snapr saved what was recorded:\n{path}\n\nLast ffmpeg output:\n{failure}",
                    "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Notify("Video saved", Path.GetFileName(path));
            ShowVideoEditor(path);
        }
        catch (RecorderException ex)
        {
            Log.Write("Recording could not be saved", ex);
            recorder.Dispose();
            MessageBox.Show($"The recording could not be saved.\n\n{ex.Message}\n\nThe recorded pieces were kept in:\n{recorder.WorkDirectory}",
                "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Returns false if the user cancelled exit to keep unsaved edits.</summary>
    async Task<bool> ShutdownFlowsAsync()
    {
        if (_recorder is not null) await StopRecordingAsync(failure: null);
        StopWebcam();
        return _editor?.Shutdown() ?? true;
    }
}
