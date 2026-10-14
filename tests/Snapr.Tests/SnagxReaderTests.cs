using System.IO.Compression;
using System.Text;
using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.Tests;

/// <summary>Synthetic .snagx files built in memory (never Snagit's own sample files).</summary>
public class SnagxReaderTests
{
    const string P = "{11111111-2222-3333-4444-555555555555}";
    /// <summary>PNG signature + an IHDR chunk header declaring w x h; the rest is not a decodable picture.</summary>
    static byte[] PngHeader(uint w, uint h) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h,
    ];

    static readonly byte[] TinyPng = PngHeader(10, 10);
    static readonly string TinyPng64 = Convert.ToBase64String(TinyPng);

    static byte[] U(string s) => Encoding.UTF8.GetBytes(s);
    static byte[] WithBom(string s) => [0xEF, 0xBB, 0xBF, .. U(s)];   // real .snagx JSON starts with a UTF-8 BOM

    static string Page(string objects, int dpi = 72, string effects = "[]") => $$"""
        {"CaptureCanvasWidth":600,"CaptureCanvasHeight":400,"DPI":{{dpi}},"CaptureBackgroundColor":"#FF101010",
         "CaptureBackgroundImage":"{{P}}.png","CaptureBackgroundLocation":"10,20","BackgroundSize":"500,300",
         "Effects":{{effects}},"CaptureObjects":[{{objects}}]}
        """;

    static MemoryStream Zip(params (string Name, byte[] Data)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        ms.Position = 0;
        return ms;
    }

    static MemoryStream Snagx(string page, int pages = 1, byte[]? basePng = null, string meta = """{"CaptureDate":"2021-02-01 19:50:26","Tags":[]}""")
    {
        var list = string.Join(",", Enumerable.Repeat($"\"{P}.json\"", pages));
        return Zip(("index.json", WithBom($$"""{"Pages":[{{list}}],"Version":"1.0"}""")),
            ("metadata.json", WithBom(meta)),
            ($"{P}.json", WithBom(page)),
            ($"{P}.png", basePng ?? TinyPng));
    }

    static SnagxPage Read(string objects, int dpi = 72) => SnagxReader.Read(Snagx(Page(objects, dpi)));

    [Fact]
    public void Canvas_and_background_scale_with_dpi()
    {
        var p = Read("", dpi: 144);
        Assert.Equal((1200, 800), (p.Width, p.Height));
        Assert.Equal(new Rct(20, 40, 1000, 600), p.BaseRect);
        Assert.Equal("#FF101010", p.BackgroundColor);
        Assert.Equal(TinyPng, p.BasePng);
        Assert.True(p.Report.IsEmpty);
    }

    [Fact]
    public void Arrow_maps_points_colour_width_and_heads()
    {
        var a = Assert.IsType<ArrowAnnotation>(Assert.Single(Read("""
            {"ToolMode":"Arrow","PointsArray":["10,20","110,70"],"ForegroundColor":"#FFFF594B","StrokeWidth":10,
             "ArrowStart":"Round","ArrowEnd":"TaperArrow","Opacity":100,"RotationAngle":0,"DashType":"Solid","DropShadowEnabled":true}
            """).Annotations));
        Assert.Equal((new Pt(10, 20), new Pt(110, 70), "#FFFF594B", 10.0), (a.Start, a.End, a.Color, a.Width));
        Assert.Equal((ArrowHead.Triangle, ArrowHead.None, true), (a.Head, a.Tail, a.Shadow));
    }

    [Fact]
    public void Shapes_map_kind_fill_and_rounded_corners()
    {
        var p = Read("""
            {"ToolMode":"Shape","ToolShape":"Ellipse","PointsArray":["50,50","10,10"],"ForegroundColor":"#FF4B9CE3","BackgroundColor":"#00000000","StrokeWidth":4},
            {"ToolMode":"Shape","ToolShape":"RoundedRectangle","PointsArray":["0,0","40,20"],"ForegroundColor":"#00000000","BackgroundColor":"#FFFFFFFF","StrokeWidth":4,"CornerRadiusRatio":0.5}
            """);
        var ellipse = Assert.IsType<ShapeAnnotation>(p.Annotations[0]);
        Assert.Equal((ShapeKind.Ellipse, new Rct(10, 10, 40, 40), "#FF4B9CE3", (string?)null), (ellipse.Kind, ellipse.Bounds, ellipse.Stroke, ellipse.Fill));
        var rounded = Assert.IsType<ShapeAnnotation>(p.Annotations[1]);
        Assert.Equal((ShapeKind.Rectangle, 5.0, "#FFFFFFFF"), (rounded.Kind, rounded.CornerRadius, rounded.Fill));
    }

    [Fact]
    public void Text_uses_plain_text_or_rtf()
    {
        var rtf = Convert.ToBase64String(Encoding.ASCII.GetBytes(@"{\rtf1{\fonttbl\f0 Arial;}\f0 Hi\par there}"));
        var p = Read($$"""
            {"ToolMode":"Text","PointsArray":["30,40","200,80"],"PlainText":"Hello","FontFamily":"Arial","FontSize":20,"TextSelectionBold":true,"ForegroundColor":"#FFFFFFFF","BackgroundColor":"#FFFF5B53"},
            {"ToolMode":"Text","PointsArray":["0,0","10,10"],"RTFEncodedText":"{{rtf}}","FontSize":12,"ForegroundColor":"#FF000000","BackgroundColor":"#00000000"}
            """);
        var plain = Assert.IsType<TextAnnotation>(p.Annotations[0]);
        Assert.Equal((new Pt(30, 40), "Hello", "Arial", 20.0, true, "#FFFF5B53"), (plain.Position, plain.Text, plain.FontFamily, plain.FontSize, plain.Bold, plain.Background));
        Assert.Equal("Hi\nthere", Assert.IsType<TextAnnotation>(p.Annotations[1]).Text);
        Assert.Equal(1, p.Report.Counts[ImportReasons.TextFormatting]);
    }

    [Fact]
    public void Callout_maps_box_tail_and_colours()
    {
        var c = Assert.IsType<CalloutAnnotation>(Assert.Single(Read("""
            {"ToolMode":"Callout","PointsArray":["387,305","534,391"],"CalloutTails":["336,274"],"ForegroundColor":"#00000000",
             "BackgroundColor":"#FFFF594B","TailColor":"#FFFF594B","PlainText":"Note","FontSize":32}
            """).Annotations));
        Assert.Equal((new Rct(387, 305, 147, 86), new Pt(336, 274)), (c.Bounds, c.TailPoint));
        Assert.Equal(("#FFFF594B", "#FFFF594B", "#FF111111", "Note", 32.0), (c.Fill, c.Stroke, c.TextColor, c.Text, c.FontSize));
    }

    [Fact]
    public void Steps_map_numbers_and_letters()
    {
        var p = Read("""
            {"ToolMode":"Step","PointsArray":["618,226","656,188"],"ForegroundColor":"#FF55A6E7","BackgroundColor":"#FFE6EFF7","PlainText":"3"},
            {"ToolMode":"Step","PointsArray":["618,226","656,188"],"ForegroundColor":"#FF55A6E7","BackgroundColor":"#FFE6EFF7","PlainText":"C"}
            """);
        var n = Assert.IsType<StepAnnotation>(p.Annotations[0]);
        Assert.Equal((new Pt(637, 207), 38.0, 3, StepStyle.Numbers, "#FF55A6E7", "#FFE6EFF7"), (n.Center, n.Size, n.Number, n.Style, n.Fill, n.TextColor));
        var l = Assert.IsType<StepAnnotation>(p.Annotations[1]);
        Assert.Equal((3, StepStyle.Letters), (l.Number, l.Style));
    }

    [Fact]
    public void Highlight_pen_magnify_and_image_map()
    {
        var p = Read($$"""
            {"ToolMode":"Highlight","PointsArray":["325,312","375,364"],"ForegroundColor":"#FFFFE895","Opacity":0},
            {"ToolMode":"Pen","PointsArray":["1,2","3,4","5,6"],"ForegroundColor":"#FFE0EBF5","StrokeWidth":7},
            {"ToolMode":"Magnify","ToolShape":"Ellipse","PointsArray":["78,53","108,23"],"MagnifyScale":130},
            {"ToolMode":"Image","PointsArray":["552,20","613,80"],"Image":"{{TinyPng64}}"}
            """);
        Assert.Equal("#66FFE895", Assert.IsType<HighlightAnnotation>(p.Annotations[0]).Color);
        Assert.Equal([new Pt(1, 2), new Pt(3, 4), new Pt(5, 6)], Assert.IsType<PenAnnotation>(p.Annotations[1]).Points);
        var m = Assert.IsType<MagnifyAnnotation>(p.Annotations[2]);
        Assert.Equal((new Rct(78, 23, 30, 30), new Pt(93, 38), 1.5, MagnifyShape.Circle), (m.SourceBounds, m.TargetCenter, m.Zoom, m.Shape));
        var i = Assert.IsType<ImageAnnotation>(p.Annotations[3]);
        Assert.Equal((new Rct(552, 20, 61, 60), TinyPng64), (i.Bounds, i.Png));
        Assert.Equal([ImportReasons.Zoom], p.Report.Counts.Keys);
    }

    [Fact]
    public void Approximations_are_counted()
    {
        var objects = """
            {"ToolMode":"Arrow","PointsArray":["0,0","10,10"],"RotationAngle":45},
            {"ToolMode":"Shape","PointsArray":["0,0","10,10"],"Opacity":50},
            {"ToolMode":"Shape","PointsArray":["0,0","10,10"],"DashType":"Dash"},
            {"ToolMode":"Blur2","PointsArray":["0,0","1,1"]},
            {"ToolMode":"Image","PointsArray":["0,0","1,1"],"Image":"bm90IGEgcG5n"}
            """;
        var p = SnagxReader.Read(Snagx(Page(objects, effects: """[{"Type":"Border"}]"""), pages: 2));
        Assert.Equal(3, p.Annotations.Count);
        var c = p.Report.Counts;
        Assert.Equal((1, 1, 1, 2, 1, 1), (c[ImportReasons.Rotation], c[ImportReasons.Opacity], c[ImportReasons.Dash],
            c[ImportReasons.Unsupported], c[ImportReasons.Effects], c[ImportReasons.ExtraPages]));
    }

    [Fact]
    public void One_bad_object_does_not_stop_the_page()
    {
        var p = Read("""
            {"ToolMode":"Arrow","PointsArray":["x"]},
            {"ToolMode":"Arrow","PointsArray":["0,0","10,10"]}
            """);
        Assert.IsType<ArrowAnnotation>(Assert.Single(p.Annotations));
        Assert.Equal(1, p.Report.Counts[ImportReasons.Unsupported]);
    }

    [Fact]
    public void Not_a_zip_throws() => Assert.Throws<InvalidDataException>(() => SnagxReader.Read(new MemoryStream(U("hello"))));

    [Fact]
    public void Missing_page_image_throws() => Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Zip(
        ("index.json", U($$"""{"Pages":["{{P}}.json"]}""")), ($"{P}.json", U(Page(""))))));

    [Fact]
    public void Too_many_objects_throws() =>
        Assert.Throws<InvalidDataException>(() => Read(string.Join(",", Enumerable.Repeat("{}", SnagxReader.MaxObjects + 1))));

    [Fact]
    public void Oversized_entry_throws() =>
        Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Snagx(Page("")), maxEntryBytes: 50));

    [Fact]
    public void Infinite_dpi_throws() =>
        Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Snagx(
            """{"CaptureCanvasWidth":600,"CaptureCanvasHeight":400,"DPI":1e999,"CaptureObjects":[]}""")));

    [Fact]
    public void Huge_canvas_throws() =>
        Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Snagx(
            """{"CaptureCanvasWidth":2000000000,"CaptureCanvasHeight":400,"DPI":72,"CaptureObjects":[]}""")));

    [Fact]
    public void Capture_date_is_read_from_metadata() =>
        Assert.Equal(new DateTime(2021, 2, 1, 19, 50, 26), Read("").CapturedAt!.Value.DateTime);

    [Fact]
    public void Huge_canvas_area_throws() =>
        Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Snagx(
            """{"CaptureCanvasWidth":20000,"CaptureCanvasHeight":20000,"DPI":72,"CaptureBackgroundImage":"{11111111-2222-3333-4444-555555555555}.png","CaptureObjects":[]}""")));

    [Fact]
    public void Base_png_declaring_huge_size_throws() =>
        Assert.Throws<InvalidDataException>(() => SnagxReader.Read(Snagx(Page(""), basePng: PngHeader(60_000, 60_000))));

    [Fact]
    public void Image_object_declaring_huge_size_is_skipped()
    {
        var huge = Convert.ToBase64String(PngHeader(60_000, 60_000));
        var p = Read($$"""{"ToolMode":"Image","PointsArray":["552,20","613,80"],"Image":"{{huge}}"}""");
        Assert.Empty(p.Annotations);
        Assert.Equal(1, p.Report.Counts[ImportReasons.Unsupported]);
    }

    [Fact]
    public void Out_of_range_capture_date_is_ignored()
    {
        var p = SnagxReader.Read(Snagx(Page(""), meta: """{"CaptureDate":"0001-01-01 00:00:00"}"""));
        Assert.Equal(600, p.Width);
    }

    [Fact]
    public void Bad_numbers_are_clamped()
    {
        var p = Read("""
            {"ToolMode":"Text","PointsArray":["1,1","2,2"],"PlainText":"x","FontSize":0},
            {"ToolMode":"Pen","PointsArray":["1,2","3,4"],"StrokeWidth":1e9}
            """);
        Assert.Equal(1, Assert.IsType<TextAnnotation>(p.Annotations[0]).FontSize);
        Assert.Equal(500, Assert.IsType<PenAnnotation>(p.Annotations[1]).Width);
    }
}
