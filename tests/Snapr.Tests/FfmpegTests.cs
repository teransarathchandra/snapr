using Snapr.Core.Recording;

namespace Snapr.Tests;

public sealed class FfmpegTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-ffmpeg").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Grab_rounds_size_down_to_even_and_keeps_negative_offsets()
    {
        var a = FfmpegArgs.Grab(-1920, 15, 801, 601, "out.mp4");
        Assert.Equal("-1920", a[a.ToList().IndexOf("-offset_x") + 1]);
        Assert.Equal("15", a[a.ToList().IndexOf("-offset_y") + 1]);
        Assert.Equal("800x600", a[a.ToList().IndexOf("-video_size") + 1]);
        Assert.Equal("out.mp4", a[^1]);
        Assert.Contains("yuv420p", a);
        Assert.Contains("+frag_keyframe+empty_moov", a);
        Assert.Equal("30", a[a.ToList().IndexOf("-g") + 1]);   // a fragment per second, so a killed recording keeps all but the last second
        Assert.Equal("1", a[a.ToList().IndexOf("-flush_packets") + 1]);   // small fragments must reach the disk, not sit in the write buffer
    }

    [Fact]
    public void Grab_rejects_tiny_areas() =>
        Assert.Throws<ArgumentException>(() => FfmpegArgs.Grab(0, 0, 1, 50, "o.mp4"));

    [Fact]
    public void Mux_without_audio_copies_video()
    {
        var a = FfmpegArgs.Mux("v.mp4", [], "o.mp4");
        Assert.Equal(["-y", "-i", "v.mp4", "-c", "copy", "o.mp4"], a);
    }

    [Fact]
    public void Mux_with_one_audio_maps_it()
    {
        var a = FfmpegArgs.Mux("v.mp4", ["s.wav"], "o.mp4");
        Assert.Contains("1:a", a);
        Assert.DoesNotContain("-filter_complex", a);
    }

    [Fact]
    public void Mux_with_two_audios_mixes_and_trims()
    {
        var a = FfmpegArgs.Mux("v.mp4", ["s.wav", "m.wav"], "o.mp4", audioTrimSeconds: 0.4567);
        Assert.Equal("[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[a]", a[a.ToList().IndexOf("-filter_complex") + 1]);
        Assert.Equal(2, a.Count(x => x == "-ss"));
        Assert.Contains("0.457", a);
    }

    [Fact]
    public void Concat_list_escapes_quotes_and_uses_forward_slashes() =>
        Assert.Equal("file 'C:/t/it'\\''s.mp4'\nfile 'C:/t/b.mp4'\n", FfmpegArgs.ConcatList([@"C:\t\it's.mp4", @"C:\t\b.mp4"]));

    [Fact]
    public void Locator_prefers_configured_path_then_PATH()
    {
        var configured = Path.Combine(_dir, "custom.exe");
        File.WriteAllText(configured, "");
        var onPath = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        File.WriteAllText(Path.Combine(onPath, "ffmpeg.exe"), "");

        Assert.Equal(configured, FfmpegLocator.Find(configured, onPath));
        Assert.Equal(Path.Combine(onPath, "ffmpeg.exe"), FfmpegLocator.Find(Path.Combine(_dir, "gone.exe"), $"C:\\nowhere;{onPath}"));
        Assert.Null(FfmpegLocator.Find(null, "C:\\nowhere"));
    }
}
