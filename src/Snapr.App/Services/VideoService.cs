using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Platform;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.App.Services;

public sealed class VideoExportException(string message) : Exception(message);

/// <summary>Runs ffmpeg for the video editor (spec parity 8 §4).</summary>
public static class VideoService
{
    public static async Task<VideoInfo?> ProbeAsync(string ffmpeg, string file)
    {
        using var p = Start(ffmpeg, ["-i", file], stdout: false);
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();   // exits 1 ("At least one output file must be specified"); that is expected
        return FfmpegProbe.Parse(stderr);
    }

    /// <summary>One frame at <paramref name="seconds"/> as a picture, or null when ffmpeg can't read it.</summary>
    public static async Task<BitmapSource?> GrabFrameAsync(string ffmpeg, string file, double seconds)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"snapr-frame-{Guid.NewGuid():N}.png");
        try
        {
            var exit = await RunAsync(["-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", file,
                "-frames:v", "1", "-f", "image2", "-c:v", "png", temp]);
            // -ss at or past the end exits 0 without writing a file; take the last frame instead
            if (exit == 0 && !File.Exists(temp))
                exit = await RunAsync(["-hide_banner", "-y", "-sseof", "-0.5", "-i", file, "-update", "1", "-f", "image2", "-c:v", "png", temp]);
            if (exit != 0 || !File.Exists(temp)) return null;
            var image = ImageIo.DecodePng(File.ReadAllBytes(temp));
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or FileFormatException)
        {
            Log.Write($"Couldn't grab a frame of {file}", ex);
            return null;
        }
        finally { TryDelete(temp); }

        async Task<int> RunAsync(string[] args)
        {
            using var p = Start(ffmpeg, args, stdout: false);
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
    }

    /// <summary>Runs an export into <c>output + ".part"</c> and moves it to <paramref name="output"/> on success. On cancel or
    /// failure the .part file is deleted and an existing <paramref name="output"/> is left untouched.</summary>
    public static async Task ExportAsync(string ffmpeg, Func<string, IReadOnlyList<string>> argsForPart, string output,
        double expectedSeconds, IProgress<double>? progress, CancellationToken ct)
    {
        var part = output + ".part";
        var errors = new Queue<string>();
        using var p = Start(ffmpeg, argsForPart(part), stdout: true);
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errors)
            {
                errors.Enqueue(e.Data);
                while (errors.Count > 20) errors.Dequeue();
            }
        };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && FfmpegProgress.Parse(e.Data) is { } t && expectedSeconds > 0)
                progress?.Report(Math.Clamp(t / expectedSeconds, 0, 1));
        };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch (InvalidOperationException) { }
            TryDelete(part);
            throw;
        }
        if (p.ExitCode != 0)
        {
            TryDelete(part);
            string last;
            lock (errors) last = string.Join(Environment.NewLine, errors);
            throw new VideoExportException($"ffmpeg failed (exit code {p.ExitCode}).{Environment.NewLine}{Environment.NewLine}{last}");
        }
        try
        {
            File.Move(part, output, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Couldn't replace {output}", ex);
            TryDelete(part);
            throw new VideoExportException($"Couldn't replace {Path.GetFileName(output)}: it may be open in another program. Close it and save again, or choose another name.");
        }
        progress?.Report(1);
    }

    static Process Start(string ffmpeg, IReadOnlyList<string> args, bool stdout)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = stdout,
        };
        info.ArgumentList.Add("-hide_banner");
        foreach (var a in args) info.ArgumentList.Add(a);
        var p = new Process { StartInfo = info };
        p.Start();
        try { ChildProcessJob.App.Assign(p); }   // ffmpeg must not outlive Snapr
        catch (System.ComponentModel.Win32Exception ex) { Log.Write("Couldn't tie ffmpeg to Snapr's lifetime", ex); }
        return p;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"Couldn't delete {path}", ex); }
    }
}
