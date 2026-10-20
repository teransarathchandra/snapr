using Snapr.Core.Video;

namespace Snapr.Tests;

public class FfmpegVideoTests
{
    const string WithAudio = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\v\rec 1.mp4':
          Duration: 00:01:02.50, start: 0.000000, bitrate: 1234 kb/s
          Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1920x1080, 1100 kb/s, 30 fps
          Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s
        At least one output file must be specified
        """;

    const string Silent = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'x.mp4':
          Duration: 00:00:03.03, start: 0.000000, bitrate: 50 kb/s
          Stream #0:0[0x1](und): Video: h264 (High), yuv420p, 320x240 [SAR 1:1 DAR 4:3], 45 kb/s, 30 fps
        """;

    [Fact]
    public void Probe_reads_duration_size_and_audio()
    {
        Assert.Equal(new VideoInfo(62.5, 1920, 1080, true), FfmpegProbe.Parse(WithAudio));
        Assert.Equal(new VideoInfo(3.03, 320, 240, false), FfmpegProbe.Parse(Silent));
    }

    [Fact]
    public void Probe_of_garbage_is_null() => Assert.Null(FfmpegProbe.Parse("x.mp4: Invalid data found when processing input"));

    [Theory]
    [InlineData("out_time_us=1500000", 1.5)]
    [InlineData("out_time_ms=2500000", 2.5)]       // ffmpeg writes microseconds here too
    [InlineData("out_time=00:01:02.250000", 62.25)]
    public void Progress_lines_give_seconds(string line, double seconds) => Assert.Equal(seconds, FfmpegProgress.Parse(line)!.Value, 6);

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("frame=12")]
    [InlineData("progress=continue")]
    [InlineData("out_time_us=-9223372036854775807")]   // ffmpeg writes this before the first frame
    [InlineData("out_time=-00:00:00.000001")]
    public void Other_lines_give_null(string line) => Assert.Null(FfmpegProgress.Parse(line));

    [Fact]
    public void Mp4_with_audio_trims_and_concats_both_streams()
    {
        var args = VideoExportArgs.Mp4("in.mp4", [new(0, 1.5), new(2.25, 4)], true, "out.part");
        Assert.Equal(
        [
            "-y", "-i", "in.mp4", "-filter_complex",
            "[0:v]trim=start=0:end=1.5,setpts=PTS-STARTPTS[v0];[0:a]atrim=start=0:end=1.5,asetpts=PTS-STARTPTS[a0];" +
            "[0:v]trim=start=2.25:end=4,setpts=PTS-STARTPTS[v1];[0:a]atrim=start=2.25:end=4,asetpts=PTS-STARTPTS[a1];" +
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[cv][a];[cv]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]",
            "-map", "[v]", "-map", "[a]",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "160k",
            "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", "-f", "mp4", "out.part",
        ], args);
    }

    [Fact]
    public void Mp4_without_audio_has_no_audio_filters()
    {
        var args = VideoExportArgs.Mp4("in.mp4", [new(1, 2)], false, "out.part");
        Assert.Equal("[0:v]trim=start=1:end=2,setpts=PTS-STARTPTS[v0];[v0]concat=n=1:v=1:a=0[cv];[cv]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]", args[args.ToList().IndexOf("-filter_complex") + 1]);
        Assert.DoesNotContain("[a]", args);
        Assert.DoesNotContain("aac", args);
    }

    [Fact]
    public void Mp4_rounds_odd_sizes_to_even()
    {
        var args = VideoExportArgs.Mp4("in.mp4", [new(0, 1)], false, "out.part");
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", args[args.ToList().IndexOf("-filter_complex") + 1]);
    }

    [Fact]
    public void Gif_scales_and_uses_a_palette()
    {
        var args = VideoExportArgs.Gif("in.mp4", [new(0, 2)], 640, 10, "out.part");
        Assert.Equal(
        [
            "-y", "-i", "in.mp4", "-filter_complex",
            "[0:v]trim=start=0:end=2,setpts=PTS-STARTPTS[v0];[v0]concat=n=1:v=1:a=0[c];" +
            "[c]fps=10,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5[g]",
            "-map", "[g]", "-loop", "0", "-progress", "pipe:1", "-nostats", "-f", "gif", "out.part",
        ], args);
    }

    [Fact]
    public void Gif_without_width_does_not_scale()
    {
        var args = VideoExportArgs.Gif("in.mp4", [new(0, 2)], null, 15, "out.part");
        Assert.Contains("[c]fps=15,split[s0][s1];", args[4]);
    }

    [Fact]
    public void Paths_are_passed_as_single_arguments()
    {
        const string input = @"C:\My Videos\it's ünïcode.mp4";
        var args = VideoExportArgs.Mp4(input, [new(0, 1)], false, @"C:\out dir\a b.part");
        Assert.Contains(input, args);
        Assert.Equal(@"C:\out dir\a b.part", args[^1]);
    }

    static string Filter(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-filter_complex") + 1];

    [Fact]
    public void Mp4_with_one_overlay_and_one_range()
    {
        var args = VideoExportArgs.Mp4("in.mp4", [new TimeRange(0, 3)], false, "out.mp4", [new VideoOverlayLayer("a.png", 1, 2)]);
        Assert.Equal(["-y", "-i", "in.mp4", "-i", "a.png", "-filter_complex"], args.Take(6));
        Assert.Equal("[0:v][1:v]overlay=0:0:enable='gte(t,1)*lt(t,2)'[o1];[o1]trim=start=0:end=3,setpts=PTS-STARTPTS[v0];[v0]concat=n=1:v=1:a=0[cv];[cv]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]", Filter(args));
    }

    [Fact]
    public void Mp4_with_two_overlays_two_ranges_and_audio()
    {
        var args = VideoExportArgs.Mp4("in.mp4", [new TimeRange(0, 1), new TimeRange(2, 3)], true, "out.mp4",
            [new VideoOverlayLayer("a.png", 0.5, 2.5), new VideoOverlayLayer("b.png", 1, 1.25)]);
        Assert.Equal(["-y", "-i", "in.mp4", "-i", "a.png", "-i", "b.png", "-filter_complex"], args.Take(8));
        Assert.Equal("[0:v][1:v]overlay=0:0:enable='gte(t,0.5)*lt(t,2.5)'[o1];[o1][2:v]overlay=0:0:enable='gte(t,1)*lt(t,1.25)'[o2];[o2]split=2[r0][r1];"
            + "[r0]trim=start=0:end=1,setpts=PTS-STARTPTS[v0];[0:a]atrim=start=0:end=1,asetpts=PTS-STARTPTS[a0];"
            + "[r1]trim=start=2:end=3,setpts=PTS-STARTPTS[v1];[0:a]atrim=start=2:end=3,asetpts=PTS-STARTPTS[a1];"
            + "[v0][a0][v1][a1]concat=n=2:v=1:a=1[cv][a];[cv]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]", Filter(args));
    }

    [Fact]
    public void Gif_with_overlay_and_two_ranges()
    {
        var args = VideoExportArgs.Gif("in.mp4", [new TimeRange(0, 1), new TimeRange(2, 3)], 640, 10, "out.gif", [new VideoOverlayLayer("a.png", 1, 2)]);
        Assert.Equal(["-y", "-i", "in.mp4", "-i", "a.png", "-filter_complex"], args.Take(6));
        Assert.Equal("[0:v][1:v]overlay=0:0:enable='gte(t,1)*lt(t,2)'[o1];[o1]split=2[r0][r1];"
            + "[r0]trim=start=0:end=1,setpts=PTS-STARTPTS[v0];[r1]trim=start=2:end=3,setpts=PTS-STARTPTS[v1];"
            + "[v0][v1]concat=n=2:v=1:a=0[c];[c]fps=10,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5[g]", Filter(args));
    }

    [Fact]
    public void Empty_overlay_list_matches_no_overlays()
    {
        Assert.Equal(VideoExportArgs.Mp4("in.mp4", [new TimeRange(0, 3)], true, "o.mp4"), VideoExportArgs.Mp4("in.mp4", [new TimeRange(0, 3)], true, "o.mp4", []));
        Assert.Equal(VideoExportArgs.Gif("in.mp4", [new TimeRange(0, 3)], null, 15, "o.gif"), VideoExportArgs.Gif("in.mp4", [new TimeRange(0, 3)], null, 15, "o.gif", []));
    }
}
