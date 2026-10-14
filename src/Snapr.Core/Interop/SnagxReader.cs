using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Snapr.Core.Interop;

/// <summary>Why something in a .snagx was approximated or skipped. Shown to the user after a count.</summary>
public static class ImportReasons
{
    public const string Rotation = "objects imported without their rotation";
    public const string Opacity = "objects imported fully opaque";
    public const string Dash = "dashed outlines imported as solid lines";
    public const string TextFormatting = "texts imported without formatting";
    public const string Zoom = "magnifier zooms limited to 150–400%";
    public const string ExtraPages = "extra pages not imported (only the first page)";
    public const string Effects = "Snagit effects not imported";
    public const string Unsupported = "unsupported or damaged objects skipped";
}

public sealed record ImportReport(IReadOnlyDictionary<string, int> Counts)
{
    public bool IsEmpty => Counts.Count == 0;
    public string Summary() => string.Join("; ", Counts.Select(kv => $"{kv.Value} {kv.Key}"));
}

/// <summary>The first page of a .snagx, converted to Snapr. BaseRect is in pixels; W/H of 0 mean the picture's natural size.</summary>
public sealed record SnagxPage(int Width, int Height, string BackgroundColor, byte[] BasePng, Rct BaseRect,
    IReadOnlyList<Annotation> Annotations, DateTimeOffset? CapturedAt, ImportReport Report);

/// <summary>
/// Reads Snagit .snagx files (a ZIP of JSON + PNG). The file is untrusted: entries are read in memory with size limits,
/// unknown fields are ignored, and one damaged object is skipped instead of failing the import.
/// </summary>
public static class SnagxReader
{
    public const long MaxEntryBytes = 200L * 1024 * 1024;
    public const int MaxObjects = 10_000;
    public const int MaxPagePixels = 32_000;
    public const long MaxTotalPixels = 100_000_000;

    public static SnagxPage Read(Stream stream, long maxEntryBytes = MaxEntryBytes)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException ex) { throw new InvalidDataException("This is not a Snagit file (it is not a ZIP archive).", ex); }

        using (zip)
        {
            var report = new Dictionary<string, int>();
            void Count(string reason) => report[reason] = report.GetValueOrDefault(reason) + 1;

            using var index = ReadJson(zip, "index.json", maxEntryBytes);
            var pages = index.RootElement.ValueKind == JsonValueKind.Object && index.RootElement.TryGetProperty("Pages", out var p)
                && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                    : [];
            if (pages.Count == 0) throw new InvalidDataException("The file has no pages.");
            if (pages.Count > 1) Count(ImportReasons.ExtraPages);

            using var pageDoc = ReadJson(zip, pages[0], maxEntryBytes);
            var root = pageDoc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{pages[0]} is not a page.");

            var dpi = Num(root, "DPI") ?? 72;
            var s = dpi > 0 ? dpi / 72 : 1;
            var canvasW = Num(root, "CaptureCanvasWidth") ?? 0;
            var canvasH = Num(root, "CaptureCanvasHeight") ?? 0;
            if (canvasW <= 0 || canvasH <= 0) throw new InvalidDataException("The page has no canvas size.");
            if (!ValidPixels(canvasW * s) || !ValidPixels(canvasH * s)) throw new InvalidDataException("The page size is not valid.");

            if (canvasW * s * canvasH * s > MaxTotalPixels) throw new InvalidDataException("The page is too large.");

            var imageName = Str(root, "CaptureBackgroundImage") ?? Path.ChangeExtension(pages[0], ".png");
            var basePng = ReadBytes(zip, imageName, maxEntryBytes);
            if (!WithinPixelLimit(basePng)) throw new InvalidDataException("The page is too large.");
            var location = SnagxValues.Point(Str(root, "CaptureBackgroundLocation")) ?? new Pt(0, 0);
            var size = SnagxValues.Point(Str(root, "BackgroundSize"));
            var baseRect = new Rct(location.X * s, location.Y * s, (size?.X ?? 0) * s, (size?.Y ?? 0) * s);

            if (root.TryGetProperty("Effects", out var fx) && fx.ValueKind == JsonValueKind.Array && fx.GetArrayLength() > 0)
                Count(ImportReasons.Effects);

            var annotations = new List<Annotation>();
            if (root.TryGetProperty("CaptureObjects", out var objects) && objects.ValueKind == JsonValueKind.Array)
            {
                if (objects.GetArrayLength() > MaxObjects) throw new InvalidDataException($"The page has more than {MaxObjects} objects.");
                foreach (var o in objects.EnumerateArray())
                {
                    try
                    {
                        if (o.ValueKind == JsonValueKind.Object && Map(o, s, Count) is { } a) annotations.Add(a);
                        else Count(ImportReasons.Unsupported);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException
                                                   or ArgumentException or OverflowException or IndexOutOfRangeException)
                    {
                        Count(ImportReasons.Unsupported);
                    }
                }
            }

            return new SnagxPage((int)Math.Round(canvasW * s), (int)Math.Round(canvasH * s),
                SnagxValues.Color(Str(root, "CaptureBackgroundColor")) ?? "#00000000", basePng, baseRect,
                annotations, CapturedAt(zip, maxEntryBytes), new ImportReport(report));
        }
    }

    // ---------- objects ----------

    static Annotation? Map(JsonElement o, double s, Action<string> count)
    {
        var mode = Str(o, "ToolMode");
        var points = Points(o, "PointsArray").Select(p => new Pt(p.X * s, p.Y * s)).ToList();
        Rct Box() => points.Count >= 2 ? Rct.FromPoints(points[0], points[1]) : throw new FormatException("The object needs two points.");
        var fg = SnagxValues.Color(Str(o, "ForegroundColor")) ?? "#FFFF3B30";
        var bgRaw = SnagxValues.Color(Str(o, "BackgroundColor"));
        var bg = bgRaw is not null && SnagxValues.Alpha(bgRaw) > 0 ? bgRaw : null;
        var stroke = Math.Clamp(Finite(Num(o, "StrokeWidth"), 3) * s, 0, 500);
        var fontSize = Math.Clamp(Finite(Num(o, "FontSize"), 20) * s, 1, 2000);

        Annotation? a = mode switch
        {
            "Arrow" => Arrow(o, points, s, fg, stroke),
            "Shape" => Shape(o, Box(), fg, bg, stroke),
            "Text" => new TextAnnotation(new Pt(Box().X, Box().Y), Text(o, count), Str(o, "FontFamily") ?? "Segoe UI", fontSize,
                Bool(o, "TextSelectionBold"), fg, bg),
            "Callout" => Callout(o, Box(), s, fg, bg, fontSize, count),
            "Step" => Step(o, Box(), fg, bg),
            "Highlight" => new HighlightAnnotation(Box(), ColorText.WithAlpha(fg, 0x66)),
            "Pen" => points.Count > 0 ? new PenAnnotation(points, fg, stroke) : throw new FormatException("A pen needs points."),
            "Magnify" => Magnify(o, Box(), s, count),
            "Image" => Image(o, Box()),
            _ => null,
        };
        if (a is null) return null;   // the caller counts it as unsupported
        if (Math.Abs(Num(o, "RotationAngle") ?? 0) > 0.01) count(ImportReasons.Rotation);
        if ((Num(o, "Opacity") ?? 100) < 100 && mode != "Highlight") count(ImportReasons.Opacity);
        if (Str(o, "DashType") is { } dash && dash != "Solid") count(ImportReasons.Dash);
        return a with { Shadow = Bool(o, "DropShadowEnabled") };
    }

    static double Finite(double? v, double fallback) => v is { } d && double.IsFinite(d) ? d : fallback;

    static bool WithinPixelLimit(ReadOnlySpan<byte> png) =>
        PngInfo.Size(png) is var (w, h) && (long)w * h <= MaxTotalPixels;

    static ArrowAnnotation Arrow(JsonElement o, List<Pt> points, double s, string fg, double stroke)
    {
        Pt Scaled(string name) => SnagxValues.Point(Str(o, name)) is { } p ? new Pt(p.X * s, p.Y * s) : throw new FormatException("An arrow needs two points.");
        var start = points.Count >= 2 ? points[0] : Scaled("StartPoint");
        var end = points.Count >= 2 ? points[1] : Scaled("EndPoint");
        return new ArrowAnnotation(start, end, fg, stroke, Head(Str(o, "ArrowEnd")), Head(Str(o, "ArrowStart")));
    }

    static ArrowHead Head(string? style) =>
        style is null ? ArrowHead.None
        : style.Contains("Arrow", StringComparison.OrdinalIgnoreCase) ? ArrowHead.Triangle
        : style.Contains("Line", StringComparison.OrdinalIgnoreCase) ? ArrowHead.Line
        : ArrowHead.None;

    static ShapeAnnotation Shape(JsonElement o, Rct box, string fg, string? bg, double stroke)
    {
        var shape = Str(o, "ToolShape");
        var radius = shape == "RoundedRectangle" ? (Num(o, "CornerRadiusRatio") is { } r ? r * Math.Min(box.W, box.H) / 2 : 8) : 0;
        return new ShapeAnnotation(shape == "Ellipse" ? ShapeKind.Ellipse : ShapeKind.Rectangle, box, fg, stroke, bg, radius);
    }

    static string Text(JsonElement o, Action<string> count)
    {
        if (Str(o, "PlainText") is { Length: > 0 } plain) return plain;
        if (Str(o, "RTFEncodedText") is not { Length: > 0 } rtf64) return "";
        count(ImportReasons.TextFormatting);
        return RtfText.ToPlain(Encoding.Latin1.GetString(Convert.FromBase64String(rtf64)));
    }

    static CalloutAnnotation Callout(JsonElement o, Rct box, double s, string fg, string? bg, double fontSize, Action<string> count)
    {
        var tails = Points(o, "CalloutTails");
        var tail = tails.Count > 0 ? new Pt(tails[0].X * s, tails[0].Y * s) : new Pt(box.Center.X, box.Bottom + 24);
        var stroke = SnagxValues.Alpha(fg) > 0 ? fg : SnagxValues.Color(Str(o, "TailColor")) ?? "#FF111111";
        var textColor = SnagxValues.Color(Str(o, "TextSelectionColor")) ?? "#FF111111";
        return new CalloutAnnotation(box, Text(o, count), fontSize, bg ?? "#FFFFFFFF", stroke, textColor, tail);
    }

    static StepAnnotation Step(JsonElement o, Rct box, string fg, string? bg)
    {
        var label = Str(o, "PlainText")?.Trim() ?? "";
        var (number, style) = int.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (n, StepStyle.Numbers)
            : label.Length is > 0 and <= 3 && label.All(char.IsAsciiLetter) ? (FromLetters(label), StepStyle.Letters)
            : (1, StepStyle.Numbers);
        return new StepAnnotation(box.Center, Math.Clamp(number, 1, 9999), Math.Max(box.W, box.H), fg, bg ?? "#FFFFFFFF", style);
    }

    /// <summary>"A" → 1, "Z" → 26, "AA" → 27 (the inverse of StepLabels for letters).</summary>
    static int FromLetters(string label) => label.ToUpperInvariant().Aggregate(0, (n, c) => n * 26 + (c - 'A' + 1));

    static MagnifyAnnotation Magnify(JsonElement o, Rct box, double s, Action<string> count)
    {
        var scale = (Num(o, "MagnifyScale") ?? 200) / 100;
        var zoom = Math.Clamp(scale, 1.5, 4);
        if (Math.Abs(zoom - scale) > 0.001) count(ImportReasons.Zoom);
        var target = SnagxValues.Point(Str(o, "MagnifyOffset")) is { } d ? new Pt(box.Center.X + d.X * s, box.Center.Y + d.Y * s) : box.Center;
        return new MagnifyAnnotation(box, target, zoom, Str(o, "ToolShape") == "Ellipse" ? MagnifyShape.Circle : MagnifyShape.Rectangle);
    }

    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static ImageAnnotation Image(JsonElement o, Rct box)
    {
        var data = Str(o, "Image") ?? throw new FormatException("The image object has no picture.");
        var bytes = Convert.FromBase64String(data);
        if (!bytes.AsSpan().StartsWith(PngSignature)) throw new FormatException("The image object is not a PNG.");
        if (!WithinPixelLimit(bytes)) throw new FormatException("The image object is too large.");
        return new ImageAnnotation(box, data);
    }

    // ---------- JSON and ZIP helpers ----------

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static List<Pt> Points(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? SnagxValues.Point(x.GetString()) : null)
                .Where(p => p is not null).Select(p => p!.Value).ToList()
            : [];

    static bool ValidPixels(double v) => double.IsFinite(v) && Math.Round(v) is >= 1 and <= MaxPagePixels;

    static DateTimeOffset? CapturedAt(ZipArchive zip, long maxEntryBytes)
    {
        if (zip.GetEntry("metadata.json") is null) return null;
        try
        {
            using var meta = ReadJson(zip, "metadata.json", maxEntryBytes);
            return meta.RootElement.ValueKind == JsonValueKind.Object
                && DateTime.TryParseExact(Str(meta.RootElement, "CaptureDate"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var when)
                    ? new DateTimeOffset(when)
                    : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException) { return null; }
    }

    static JsonDocument ReadJson(ZipArchive zip, string name, long max)
    {
        var bytes = ReadBytes(zip, name, max);
        var json = bytes.AsMemory();
        if (json.Span.StartsWith((byte[])[0xEF, 0xBB, 0xBF])) json = json[3..];   // .snagx JSON starts with a UTF-8 BOM
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new InvalidDataException($"{name} is not valid JSON.", ex); }
    }

    static byte[] ReadBytes(ZipArchive zip, string name, long max)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} is missing from the file.");
        if (entry.Length > max) throw new InvalidDataException($"{name} is too large.");
        using var source = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            total += read;
            if (total > max) throw new InvalidDataException($"{name} is too large.");   // the declared size can lie
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}
