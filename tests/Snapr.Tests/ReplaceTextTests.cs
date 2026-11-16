using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class ReplaceTextTests
{
    const uint White = 0xFFFFFFFF, Black = 0xFF000000, Grey = 0xFF808080, Dark = 0xFF202020;

    // 30×20 white; black block x 10..19, y 8..11 (40 px); anti-aliased grey column at x 9, y 8..11.
    static Frame Sample(uint bg = White, uint ink = Black)
    {
        var px = Enumerable.Repeat(bg, 30 * 20).ToArray();
        for (var y = 8; y <= 11; y++)
        {
            for (var x = 10; x <= 19; x++) px[y * 30 + x] = ink;
            px[y * 30 + 9] = Grey;
        }
        return new Frame(30, 20, px);
    }

    static readonly TextLine Line = new([new TextWord("ab", new Rct(8, 6, 6, 8)), new TextWord("cd", new Rct(14, 6, 8, 8))]);

    [Fact]
    public void Line_box_text_and_offset()
    {
        Assert.Equal(new Rct(8, 6, 14, 8), ReplaceText.LineBox(Line));
        Assert.Equal("ab cd", ReplaceText.LineText(Line));
        Assert.Equal(new Rct(18, 26, 14, 8), ReplaceText.LineBox(ReplaceText.Offset(Line, 10, 20)));
    }

    [Fact]
    public void Ink_colour_box_and_count()
    {
        var f = Sample();
        var line = ReplaceText.LineBox(Line);
        var bg = ReplaceText.Background(f, line);
        Assert.Equal(White, bg);
        var ink = ReplaceText.Ink(f, line, bg);
        Assert.Equal(Black, ink);                                   // greys don't win the top 10 %
        Assert.Equal(new Rct(10, 8, 10, 4), ReplaceText.InkBox(f, line, bg, ink));   // grey is under half the distance
        Assert.Equal(40, ReplaceText.InkCount(f, line, bg, ink));
    }

    [Fact]
    public void Light_text_on_dark()
    {
        var f = Sample(Dark, White);
        var line = ReplaceText.LineBox(Line);
        var bg = ReplaceText.Background(f, line);
        Assert.Equal(Dark, bg);
        Assert.Equal(White, ReplaceText.Ink(f, line, bg));
    }

    [Fact]
    public void Blank_box_falls_back_to_contrast()
    {
        var dark = new Frame(30, 20, Enumerable.Repeat(Dark, 600).ToArray());
        var line = ReplaceText.LineBox(Line);
        Assert.Equal(White, ReplaceText.Ink(dark, line, Dark));
        Assert.Equal(line, ReplaceText.InkBox(dark, line, Dark, White));
        Assert.Equal(0, ReplaceText.InkCount(dark, line, Dark, White));
        var light = new Frame(30, 20, Enumerable.Repeat(White, 600).ToArray());
        Assert.Equal(Black, ReplaceText.Ink(light, line, White));
    }

    [Fact]
    public void Cover_is_clamped_to_the_image()
    {
        Assert.Equal(new Rct(6, 4, 18, 12), ReplaceText.CoverRect(new Rct(8, 6, 14, 8), 30, 20));
        Assert.Equal(new Rct(0, 0, 7, 7), ReplaceText.CoverRect(new Rct(0, 0, 5, 5), 30, 20));
        Assert.Equal(new Rct(25, 15, 5, 5), ReplaceText.CoverRect(new Rct(27, 17, 5, 5), 30, 20));
        var corner = new Frame(30, 20, Enumerable.Repeat(White, 600).ToArray());
        Assert.Equal(White, ReplaceText.Background(corner, new Rct(27, 17, 5, 5)));
    }

    [Fact]
    public void Objects_share_a_group_cover_first()
    {
        var text = new TextAnnotation(new Pt(10, 8), "ab cd", "Segoe UI", 12, false, "#FF000000");
        var (cover, t) = ReplaceText.Objects(new Rct(6, 4, 18, 12), White, text);
        Assert.Equal(new ShapeAnnotation(ShapeKind.Rectangle, new Rct(6, 4, 18, 12), "#FFFFFFFF", 0, "#FFFFFFFF") with { Id = cover.Id, GroupId = cover.GroupId }, cover);
        Assert.NotNull(cover.GroupId);
        Assert.Equal(cover.GroupId, t.GroupId);

        var s = DocumentState.Empty.AddReplace(cover, t, "new words");
        Assert.Equal(new[] { cover.Id, t.Id }, s.Annotations.Select(a => a.Id));
        Assert.Equal("new words", ((TextAnnotation)s.Annotations[1]).Text);

        var erased = DocumentState.Empty.AddReplace(cover, t, "  ");
        Assert.Equal(new[] { cover.Id }, erased.Annotations.Select(a => a.Id));
    }

    [Fact]
    public void Font_choices()
    {
        Assert.Equal(new[] { "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Times New Roman", "Georgia", "Consolas" }, TextFonts.All);
        Assert.Same(TextFonts.All, TextFonts.For("Arial"));
        Assert.Equal("Comic Sans MS", TextFonts.For("Comic Sans MS")[0]);
        Assert.Equal(9, TextFonts.For("Comic Sans MS").Count);
    }
}
