using System.Diagnostics;
using Snapr.App.Services;
using Snapr.Core.Recording;
using Snapr.Core.Video;

namespace Snapr.Tests;

/// <summary>Runs real ffmpeg; every test returns early (passes) when ffmpeg isn't installed, like the OCR tests.</summary>
public sealed class VideoServiceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-video").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static string? Ffmpeg => FfmpegLocator.Find(null, Environment.GetEnvironmentVariable("PATH"));

    string MakeClip(string ffmpeg, string name, int seconds, bool audio)
    {
        var path = Path.Combine(_dir, name);
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in (string[])["-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size=320x240:rate=30"]) info.ArgumentList.Add(a);
        if (audio) foreach (var a in (string[])["-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}"]) info.ArgumentList.Add(a);
        foreach (var a in (string[])["-c:v", "libx264", "-pix_fmt", "yuv420p", "-shortest", path]) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return path;
    }

    [Fact]
    public async Task Probe_reads_a_real_clip()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var info = await VideoService.ProbeAsync(ffmpeg, MakeClip(ffmpeg, "a.mp4", 3, audio: true));
        Assert.NotNull(info);
        Assert.InRange(info!.Duration, 2.9, 3.1);
        Assert.Equal((320, 240, true), (info.Width, info.Height, info.HasAudio));
    }

    [Fact]
    public async Task Export_with_a_removal_has_the_kept_length_and_audio()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var clip = MakeClip(ffmpeg, "b.mp4", 3, audio: true);
        var cut = VideoCut.For(3).Remove(new(1, 2));
        var output = Path.Combine(_dir, "b edited.mp4");
        var reports = new List<double>();
        await VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Mp4(clip, cut.Kept, true, part), output, cut.KeptDuration,
            new Progress<double>(reports.Add), CancellationToken.None);
        var info = await VideoService.ProbeAsync(ffmpeg, output);
        Assert.InRange(info!.Duration, 1.9, 2.1);
        Assert.True(info.HasAudio);
        Assert.False(File.Exists(output + ".part"));
    }

    [Fact]
    public async Task Move_onto_a_locked_output_fails_cleanly()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var clip = MakeClip(ffmpeg, "d.mp4", 2, audio: false);
        var output = Path.Combine(_dir, "d edited.mp4");
        File.WriteAllText(output, "original");
        using (new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = await Assert.ThrowsAsync<VideoExportException>(() =>
                VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Mp4(clip, VideoCut.For(2).Kept, false, part), output, 2, null, CancellationToken.None));
            Assert.Contains("Close it", ex.Message);
        }
        Assert.False(File.Exists(output + ".part"));
        Assert.Equal("original", File.ReadAllText(output));
    }

    [Fact]
    public async Task Export_of_silent_clip_works()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var clip = MakeClip(ffmpeg, "c.mp4", 2, audio: false);
        var output = Path.Combine(_dir, "c edited.mp4");
        await VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Mp4(clip, VideoCut.For(2).WithTrim(0.5, 2).Kept, false, part),
            output, 1.5, null, CancellationToken.None);
        Assert.False((await VideoService.ProbeAsync(ffmpeg, output))!.HasAudio);
    }

    [Fact]
    public async Task Gif_export_writes_a_gif()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var clip = MakeClip(ffmpeg, "d.mp4", 2, audio: true);
        var output = Path.Combine(_dir, "d.gif");
        await VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Gif(clip, VideoCut.For(2).Kept, 160, 10, part), output, 2, null, CancellationToken.None);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 6));
    }

    [Fact]
    public async Task Cancel_removes_the_part_file_and_writes_nothing()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var clip = MakeClip(ffmpeg, "e.mp4", 20, audio: false);
        var output = Path.Combine(_dir, "e edited.mp4");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Gif(clip, VideoCut.For(20).Kept, null, 15, part), output, 20, null, cts.Token));
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".part"));
    }

    [Fact]
    public async Task Failed_export_keeps_an_existing_output()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var output = Path.Combine(_dir, "f.mp4");
        File.WriteAllText(output, "keep me");
        var ex = await Assert.ThrowsAsync<VideoExportException>(() =>
            VideoService.ExportAsync(ffmpeg, part => VideoExportArgs.Mp4(Path.Combine(_dir, "missing.mp4"), [new(0, 1)], false, part),
                output, 1, null, CancellationToken.None));
        Assert.Contains("missing.mp4", ex.Message);
        Assert.Equal("keep me", File.ReadAllText(output));
        Assert.False(File.Exists(output + ".part"));
    }

    [Fact]
    public async Task Probe_of_a_non_video_is_null()
    {
        if (Ffmpeg is not { } ffmpeg) return;
        var notVideo = Path.Combine(_dir, "x.mp4");
        File.WriteAllText(notVideo, "nope");
        Assert.Null(await VideoService.ProbeAsync(ffmpeg, notVideo));
    }
}
