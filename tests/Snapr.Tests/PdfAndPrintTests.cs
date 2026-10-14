using System.IO.Compression;
using System.Text;
using Snapr.Core;
using Snapr.Core.Output;

namespace Snapr.Tests;

public class PdfAndPrintTests
{
    static byte[] Rgb(int w, int h) => Enumerable.Range(0, w * h * 3).Select(i => (byte)(i * 7)).ToArray();
    static string Ascii(byte[] b) => Encoding.Latin1.GetString(b);

    /// <summary>The bytes of the image XObject's stream, inflated.</summary>
    internal static byte[] ImageStream(byte[] pdf)
    {
        var text = Ascii(pdf);
        var obj = text.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        var lengthAt = text.IndexOf("/Length ", obj, StringComparison.Ordinal) + "/Length ".Length;
        var length = int.Parse(text[lengthAt..text.IndexOf(' ', lengthAt)].Trim('>', ' '));
        var start = text.IndexOf("stream\n", obj, StringComparison.Ordinal) + "stream\n".Length;
        using var z = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Pdf_has_header_and_end_marker()
    {
        var text = Ascii(PdfWriter.SingleImagePage(4, 2, Rgb(4, 2)));
        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void Xref_offsets_point_at_their_objects()
    {
        var text = Ascii(PdfWriter.SingleImagePage(4, 2, Rgb(4, 2)));
        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref\n0 6\n", text[startxref..]);
        var entries = text[(startxref + "xref\n0 6\n".Length)..].Split('\n').Skip(1).Take(5).ToArray();
        for (var i = 0; i < 5; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[int.Parse(entries[i][..10])..]);
    }

    [Fact]
    public void Image_stream_inflates_to_the_pixels()
    {
        var rgb = Rgb(4, 2);
        Assert.Equal(rgb, ImageStream(PdfWriter.SingleImagePage(4, 2, rgb)));
    }

    [Fact]
    public void Page_is_the_image_at_96_dpi() =>
        Assert.Contains("/MediaBox [0 0 75 37.5]", Ascii(PdfWriter.SingleImagePage(100, 50, Rgb(100, 50))));

    [Fact]
    public void Wrong_pixel_count_throws() =>
        Assert.Throws<ArgumentException>(() => PdfWriter.SingleImagePage(4, 2, new byte[5]));

    [Fact]
    public void Fit_shrinks_a_large_image_and_centres_it() =>
        Assert.Equal(new Rct(0, 150, 600, 300), PrintLayout.Place(2000, 1000, 600, 600, fit: true));

    [Fact]
    public void Fit_never_enlarges_a_small_image() =>
        Assert.Equal(new Rct(250, 275, 100, 50), PrintLayout.Place(100, 50, 600, 600, fit: true));

    [Fact]
    public void Actual_size_keeps_the_scale_and_centres() =>
        Assert.Equal(new Rct(-200, 100, 1000, 400), PrintLayout.Place(1000, 400, 600, 600, fit: false));

    [Fact]
    public void Wide_images_want_landscape()
    {
        Assert.True(PrintLayout.WantsLandscape(800, 600));
        Assert.False(PrintLayout.WantsLandscape(600, 800));
    }
}
