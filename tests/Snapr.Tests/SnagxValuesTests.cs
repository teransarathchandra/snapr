using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.Tests;

public class SnagxValuesTests
{
    [Fact]
    public void Point_parses_invariant_x_comma_y() => Assert.Equal(new Pt(81.5, 23), SnagxValues.Point("81.500000,23.000000"));

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("x,y")]
    [InlineData("NaN,0")]
    [InlineData("1e999,0")]
    [InlineData(null)]
    public void Point_rejects_anything_else(string? text) => Assert.Null(SnagxValues.Point(text));

    [Fact]
    public void Colour_keeps_argb() => Assert.Equal("#FFFF594B", SnagxValues.Color("#FFFF594B"));

    [Fact]
    public void Colour_widens_rgb_to_opaque_argb() => Assert.Equal("#FFFF594B", SnagxValues.Color("#ff594b"));

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#FFFF594B\n")]
    public void Colour_rejects_anything_else(string text) => Assert.Null(SnagxValues.Color(text));

    [Fact]
    public void Rtf_paragraphs_become_lines_and_tables_are_skipped() =>
        Assert.Equal("Hello\nWorld", RtfText.ToPlain(@"{\rtf1\ansi{\fonttbl\f0\fswiss Helvetica;}{\colortbl;\red255\green0\blue0;}\f0\fs24 Hello\par World}"));

    [Fact]
    public void Rtf_escaped_braces_and_backslash_are_kept() =>
        Assert.Equal(@"a {b} c\d", RtfText.ToPlain(@"{\rtf1 a \{b\} c\\d}"));

    [Fact]
    public void Rtf_hex_escape_is_decoded() => Assert.Equal("café", RtfText.ToPlain(@"{\rtf1 caf\'e9}"));

    [Fact]
    public void Rtf_unicode_escape_skips_its_fallback() => Assert.Equal("5 €", RtfText.ToPlain(@"{\rtf1 5 \u8364?}"));

    [Fact]
    public void Rtf_cocoa_line_breaks_and_ignorable_groups() =>
        Assert.Equal("One\nTwo", RtfText.ToPlain("{\\rtf1\\cocoartf2709 {\\*\\expandedcolortbl;;}\\f0 One\\\nTwo}"));

    [Fact]
    public void Rtf_empty_gives_empty() => Assert.Equal("", RtfText.ToPlain(""));
}
