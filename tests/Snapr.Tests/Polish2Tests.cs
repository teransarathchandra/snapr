using Snapr.Core;
using Snapr.Core.Output;
using Snapr.Core.Recording;

namespace Snapr.Tests;

public sealed class Polish2Tests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-polish2").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(null, false, MicAttempt.Default)]
    [InlineData("id", false, MicAttempt.Chosen)]
    [InlineData("id", true, MicAttempt.Default)]
    [InlineData(null, true, MicAttempt.None)]
    public void Mic_choice(string? id, bool failed, MicAttempt expected) => Assert.Equal(expected, MicChoice.Next(id, failed));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Authorization", null)]
    [InlineData("X-Api-Key", null)]
    [InlineData("X API Key", "Use a header name without spaces, such as Authorization or X-Api-Key.")]
    [InlineData("X:Key", "Use a header name without spaces, such as Authorization or X-Api-Key.")]
    [InlineData("Ключ", "Use a header name without spaces, such as Authorization or X-Api-Key.")]
    public void Header_names(string? name, string? error) => Assert.Equal(error, ShareRules.HeaderNameError(name));

    [Fact]
    public void Tag_cut_keeps_whole_characters()
    {
        var emoji = new string('a', 39) + "😀b";              // the emoji's two UTF-16 units straddle 40
        Assert.Equal(new string('a', 39), TagRules.Clean([emoji])[0]);
        var accent = new string('a', 39) + "éx";        // e + combining acute straddles 40
        Assert.Equal(new string('a', 39), TagRules.Clean([accent])[0]);
        Assert.Equal(new string('a', 40), TagRules.Clean([new string('a', 45)])[0]);
    }

    [Fact]
    public void Fit_shortens_with_an_ellipsis()
    {
        double Measure(string s) => s.Length * 10;
        Assert.Equal("Short", TemplateTexts.Fit("Short", 100, Measure));
        Assert.Equal("Hello wo…", TemplateTexts.Fit("Hello world, long", 90, Measure));
        Assert.Equal("Hello…", TemplateTexts.Fit("Hello  world", 70, Measure));   // trailing spaces trimmed before …
        Assert.Equal("…", TemplateTexts.Fit("Hello", 5, Measure));
    }

    [Fact]
    public void Share_temp_cleans_only_old_snapr_folders()
    {
        var now = DateTime.UtcNow;
        string Make(string name, TimeSpan age)
        {
            var p = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
            File.WriteAllText(Path.Combine(p, "a.png"), "x");
            Directory.SetLastWriteTimeUtc(p, now - age);
            return p;
        }
        var old = Make("snapr-share-old", TimeSpan.FromMinutes(30));
        var fresh = Make("snapr-share-new", TimeSpan.FromMinutes(2));
        var other = Make("other-old", TimeSpan.FromHours(5));
        Assert.Equal(1, ShareTemp.Clean(_dir, now, TimeSpan.FromMinutes(10)));
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(other));
    }
}
