using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Snapr.App.Platform;
using Snapr.Core;
using Snapr.Core.Recording;

namespace Snapr.App.Services;

public sealed class RecorderException(string message) : Exception(message);

public sealed class RecorderService : IDisposable
{
    public sealed record Options(string FfmpegPath, Rct ScreenRect, bool SystemAudio, bool Microphone, string? MicDeviceId, string OutputPath);

    readonly Options _options;
    readonly List<string> _segments = new();
    readonly Queue<string> _stderr = new();
    readonly object _audioLock = new();
    Process? _ffmpeg;
    volatile bool _expectExit;
    volatile bool _paused;
    // Audio is written only while a video segment is actually grabbing frames, so the tracks line up
    // at the start and after every resume (ffmpeg takes a few hundred ms to start each segment).
    volatile bool _videoRunning;
    bool _completed;
    WasapiLoopbackCapture? _loop;
    WaveFileWriter? _loopWriter;
    WasapiOut? _silence;
    WasapiCapture? _mic;
    WaveFileWriter? _micWriter;

    public RecorderService(Options options)
    {
        _options = options;
        WorkDirectory = Path.Combine(Path.GetTempPath(), "Snapr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkDirectory);
    }

    public string WorkDirectory { get; }
    public bool IsPaused => _paused;
    public bool MicUnavailable { get; private set; }

    /// <summary>ffmpeg exited without being asked to; the argument is its last output lines. Raised on a thread-pool thread.</summary>
    public event Action<string>? Failed;

    string LoopPath => Path.Combine(WorkDirectory, "system.wav");
    string MicPath => Path.Combine(WorkDirectory, "mic.wav");

    public void Start()
    {
        try
        {
            StartAudio();
            StartSegment();
        }
        catch
        {
            // Nothing was recorded: release the microphone/loopback now and leave no temp folder behind.
            StopAudio();
            TryDeleteWorkDirectory();
            throw;
        }
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        _videoRunning = false;
        StopSegment();
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        StartSegment();
    }

    public async Task<string> StopAsync()
    {
        if (!_paused) StopSegment();
        StopAudio();

        var segments = _segments.Where(s => File.Exists(s) && new FileInfo(s).Length > 0).ToList();
        if (segments.Count == 0) throw new RecorderException($"ffmpeg did not record any video.\n\n{LastErrors()}");
        var video = segments[0];
        if (segments.Count > 1)
        {
            var list = Path.Combine(WorkDirectory, "segments.txt");
            await File.WriteAllTextAsync(list, FfmpegArgs.ConcatList(segments));
            video = Path.Combine(WorkDirectory, "video.mp4");
            await RunAsync(FfmpegArgs.Concat(list, video));
        }

        var audio = new[] { LoopPath, MicPath }.Where(p => File.Exists(p) && new FileInfo(p).Length > 44).ToList();
        // Mux inside our temp folder and move the result ourselves: Controlled Folder Access judges each process,
        // and Snapr may be allowed into Videos while ffmpeg.exe is not.
        var muxed = Path.Combine(WorkDirectory, "final.mp4");
        await RunAsync(FfmpegArgs.Mux(video, audio, muxed));
        Directory.CreateDirectory(Path.GetDirectoryName(_options.OutputPath)!);
        File.Move(muxed, _options.OutputPath, overwrite: true);
        _completed = true;
        return _options.OutputPath;
    }

    public string LastErrors()
    {
        lock (_stderr) return string.Join(Environment.NewLine, _stderr);
    }

    void StartSegment()
    {
        var segment = Path.Combine(WorkDirectory, $"segment{_segments.Count:D3}.mp4");
        _segments.Add(segment);
        var r = _options.ScreenRect;
        var process = Create(FfmpegArgs.Grab((int)r.X, (int)r.Y, (int)r.W, (int)r.H, segment), redirectInput: true);
        process.ErrorDataReceived += (_, e) =>
        {
            if (!_paused && e.Data?.Contains("Press [q]", StringComparison.Ordinal) == true) _videoRunning = true;
        };
        process.Exited += (_, _) =>
        {
            if (ReferenceEquals(process, _ffmpeg) && !_expectExit) Failed?.Invoke(LastErrors());
        };
        _expectExit = false;
        _ffmpeg = process;
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _ffmpeg = null;
            throw new RecorderException($"Couldn't start ffmpeg at {_options.FfmpegPath}.\n\n{ex.Message}");
        }
        TieToApp(process);
        process.BeginErrorReadLine();
    }

    void StopSegment()
    {
        if (_ffmpeg is not { } process) return;
        _expectExit = true;
        _videoRunning = false;
        try
        {
            process.StandardInput.Write('q');   // ffmpeg's own "stop and finalise the file" key
            process.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        if (!process.WaitForExit(5000))
        {
            Log.Write("ffmpeg did not stop on 'q'; killing it");
            process.Kill();
        }
        process.Dispose();
        _ffmpeg = null;
    }

    async Task RunAsync(IReadOnlyList<string> args)
    {
        using var process = Create(args, redirectInput: false);
        process.Start();
        TieToApp(process);
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new RecorderException($"ffmpeg failed (exit code {process.ExitCode}).\n\n{LastErrors()}");
    }

    /// <summary>ffmpeg must die with Snapr: a crashed Snapr must not leave the screen being recorded invisibly.</summary>
    static void TieToApp(Process process)
    {
        try { ChildProcessJob.App.Assign(process); }
        catch (System.ComponentModel.Win32Exception ex) { Log.Write("Couldn't tie ffmpeg to Snapr's lifetime", ex); }
    }

    Process Create(IReadOnlyList<string> args, bool redirectInput)
    {
        var info = new ProcessStartInfo(_options.FfmpegPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = redirectInput,
        };
        info.ArgumentList.Add("-hide_banner");
        foreach (var a in args) info.ArgumentList.Add(a);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderr)
            {
                _stderr.Enqueue(e.Data);
                while (_stderr.Count > 20) _stderr.Dequeue();
            }
        };
        return process;
    }

    void StartAudio()
    {
        if (_options.SystemAudio)
        {
            try
            {
                _loop = new WasapiLoopbackCapture();
                _loopWriter = new WaveFileWriter(LoopPath, _loop.WaveFormat);
                _loop.DataAvailable += (_, e) => { lock (_audioLock) if (_videoRunning) _loopWriter?.Write(e.Buffer, 0, e.BytesRecorded); };
                // Loopback only delivers data while something is playing; playing silence keeps the track continuous.
                _silence = new WasapiOut(AudioClientShareMode.Shared, 100);
                _silence.Init(new SilenceProvider(_loop.WaveFormat));
                _silence.Play();
                _loop.StartRecording();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                Log.Write("System audio unavailable; recording without it", ex);
                DisposeLoop();
            }
        }
        if (_options.Microphone)
        {
            try
            {
                using var devices = new MMDeviceEnumerator();
                var device = _options.MicDeviceId is { } id ? devices.GetDevice(id) : devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _mic = new WasapiCapture(device);
                _micWriter = new WaveFileWriter(MicPath, _mic.WaveFormat);
                _mic.DataAvailable += (_, e) => { lock (_audioLock) if (_videoRunning) _micWriter?.Write(e.Buffer, 0, e.BytesRecorded); };
                _mic.StartRecording();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Write("Microphone unavailable; recording without it", ex);
                MicUnavailable = true;
                DisposeMic();
            }
        }
    }

    void StopAudio()
    {
        _loop?.StopRecording();
        _mic?.StopRecording();
        _silence?.Stop();
        DisposeLoop();
        DisposeMic();
    }

    void DisposeLoop()
    {
        lock (_audioLock)
        {
            _loopWriter?.Dispose();   // finalises the WAV header
            _loopWriter = null;
        }
        _silence?.Dispose();
        _silence = null;
        _loop?.Dispose();
        _loop = null;
    }

    void DisposeMic()
    {
        lock (_audioLock)
        {
            _micWriter?.Dispose();
            _micWriter = null;
        }
        _mic?.Dispose();
        _mic = null;
    }

    /// <summary>Stops everything. Temporary files are deleted only after a successful StopAsync, so a failed recording can be recovered.</summary>
    public void Dispose()
    {
        if (_ffmpeg is not null) StopSegment();
        StopAudio();
        if (_completed) TryDeleteWorkDirectory();
    }

    void TryDeleteWorkDirectory()
    {
        try { Directory.Delete(WorkDirectory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"Could not delete {WorkDirectory}", ex); }
    }
}
