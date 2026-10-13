using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public sealed class RecorderTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-rec").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Failed_ffmpeg_start_releases_audio_and_leaves_no_temp_folder()
    {
        var notAnExe = Path.Combine(_dir, "ffmpeg.exe");
        File.WriteAllText(notAnExe, "not a program");
        using var recorder = new RecorderService(new(notAnExe, new Rct(0, 0, 100, 100), SystemAudio: true, Microphone: false, null,
            Path.Combine(_dir, "out.mp4")));

        Assert.Throws<RecorderException>(recorder.Start);
        Assert.False(Directory.Exists(recorder.WorkDirectory));
    }
}
