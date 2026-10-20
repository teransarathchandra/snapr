using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.App.Rendering;

/// <summary>The one renderer used by both the editor canvas and export, so they cannot differ.</summary>
public static class DocumentRenderer
{
    static readonly Brush ShadowBrush = ImageIo.Brush("#59000000");
    const double ShadowOffset = 4;

    public static void DrawScene(DrawingContext dc, BitmapSource baseImage, DocumentState state)
    {
        dc.DrawImage(baseImage, new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
        var spots = state.Annotations.OfType<SpotlightAnnotation>().ToArray();
        if (spots.Length > 0) DrawSpotlights(dc, baseImage, spots);
        foreach (var a in state.Annotations) DrawAnnotation(dc, baseImage, a);
        PrunePictures(state);
    }

    // Evict only pictures of objects no longer in the document, and only once the cache has grown well past it.
    static void PrunePictures(DocumentState state)
    {
        var live = state.Annotations.OfType<ImageAnnotation>().Select(i => i.Id).ToHashSet();
        if (Pictures.Count <= live.Count + 64) return;
        foreach (var key in Pictures.Keys)
            if (!live.Contains(key.Item1)) Pictures.TryRemove(key, out _);
    }

    public static BitmapSource Render(BitmapSource baseImage, DocumentState state, bool applyEffects)
    {
        var view = state.Crop ?? new Rct(0, 0, baseImage.PixelWidth, baseImage.PixelHeight);
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, view.W, view.H)));
            dc.PushTransform(new TranslateTransform(-view.X, -view.Y));
            DrawScene(dc, baseImage, state);
            dc.Pop();
            dc.Pop();
        }
        var image = ImageIo.Rasterize(v, (int)view.W, (int)view.H);
        return applyEffects ? EffectsRenderer.Apply(image, state.Effects) : image;
    }

    public static void DrawAnnotation(DrawingContext dc, BitmapSource baseImage, Annotation a)
    {
        var group = new DrawingGroup();
        using (var gc = group.Open()) DrawBody(gc, baseImage, a);
        if (a.Shadow && !group.Bounds.IsEmpty)
        {
            // Silhouette of the annotation, offset down-right, used as an opacity mask over a dark fill.
            dc.PushTransform(new TranslateTransform(ShadowOffset, ShadowOffset));
            dc.PushOpacityMask(new DrawingBrush(group));
            dc.DrawRectangle(ShadowBrush, null, group.Bounds);
            dc.Pop();
            dc.Pop();
        }
        dc.DrawDrawing(group);
    }

    static void DrawBody(DrawingContext dc, BitmapSource src, Annotation a)
    {
        switch (a)
        {
            case ArrowAnnotation x: DrawArrow(dc, x); break;
            case ShapeAnnotation x: DrawShape(dc, x); break;
            case TextAnnotation x: DrawText(dc, x); break;
            case CalloutAnnotation x: DrawCallout(dc, x); break;
            case StepAnnotation x: DrawStep(dc, x); break;
            case HighlightAnnotation x: dc.DrawRectangle(ImageIo.Brush(x.Color), null, R(x.Bounds)); break;
            case PenAnnotation x: DrawPolyline(dc, x.Points, Pen(x.Color, x.Width)); break;
            case BlurAnnotation x: DrawBlur(dc, src, x); break;
            case MagnifyAnnotation x: DrawMagnify(dc, src, x); break;
            case ImageAnnotation x: DrawImageObject(dc, x); break;
            case StampAnnotation x: DrawStamp(dc, x); break;
        }
    }

    // One blurred backdrop per base image instance; weak keys, so old bases (before an image edit) can be collected.
    static readonly ConditionalWeakTable<BitmapSource, BitmapSource> Backdrops = new();

    /// <summary>The base shrunk by 13 and materialized; drawn scaled up smoothly it is the blurred spotlight backdrop (spec parity 11 §5).</summary>
    public static BitmapSource SpotlightBackdrop(BitmapSource src) => Backdrops.GetValue(src, s =>
    {
        const double f = 13;
        int sw = Math.Max(1, (int)(s.PixelWidth / f)), sh = Math.Max(1, (int)(s.PixelHeight / f));
        var small = new WriteableBitmap(new TransformedBitmap(s, new ScaleTransform((double)sw / s.PixelWidth, (double)sh / s.PixelHeight)));
        small.Freeze();
        return small;
    });

    /// <summary>One backdrop for all spotlights (spec parity 7 §3.2): outside their union the base is blurred if any asks
    /// for it, then darkened by the largest Dim.</summary>
    static void DrawSpotlights(DrawingContext dc, BitmapSource src, IReadOnlyList<SpotlightAnnotation> all)
    {
        var spots = all.Where(s => s.Bounds.W > 0 && s.Bounds.H > 0).ToArray(); // a cut can collapse one to nothing
        if (spots.Length == 0) return;
        var holes = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var s in spots)
            holes.Children.Add(s.Shape == SpotlightShape.Ellipse ? new EllipseGeometry(R(s.Bounds)) : new RectangleGeometry(R(s.Bounds)));
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, src.PixelWidth, src.PixelHeight)), holes);
        outside.Freeze();
        if (spots.Any(s => s.Blur))
        {
            dc.PushClip(outside);
            var g = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(g, BitmapScalingMode.HighQuality);
            using (var gc = g.Open()) gc.DrawImage(SpotlightBackdrop(src), new Rect(0, 0, src.PixelWidth, src.PixelHeight));
            dc.DrawDrawing(g);
            dc.Pop();
        }
        var dim = spots.Max(s => Math.Clamp(s.Dim, 0, 90));
        if (dim == 0) return;
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(dim * 2.55), 0, 0, 0));
        brush.Freeze();
        dc.DrawGeometry(brush, null, outside);
    }

    static Rect R(Rct r) => new(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H));
    static Point P(Pt p) => new(p.X, p.Y);

    static Pen Pen(string color, double width)
    {
        var pen = new Pen(ImageIo.Brush(color), width)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>"Segoe UI" for an empty family or one that is a font location (UNC path, URI, folder) - WPF would fetch it, leaking credentials.</summary>
    public static string SafeFontFamily(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['#', '/', '\\', ':']) >= 0 ? "Segoe UI" : name;

    static FormattedText Text(string text, string family, double size, bool bold, string color, double maxWidth)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(SafeFontFamily(family)), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            double.IsFinite(size) ? Math.Clamp(size, 1, 2000) : 1, ImageIo.Brush(color), 1.0);
        if (!double.IsNaN(maxWidth)) ft.MaxTextWidth = maxWidth;
        return ft;
    }

    static void DrawArrow(DrawingContext dc, ArrowAnnotation x)
    {
        var pen = Pen(x.Color, x.Width);
        var brush = ImageIo.Brush(x.Color);
        Point s = P(x.Start), e = P(x.End);
        var head = Math.Max(10, x.Width * 3.5);
        // Shorten the shaft so the round line cap does not poke through the arrowhead tip.
        var shaftEnd = x.Head == ArrowHead.Triangle ? Back(e, s, head * 0.8) : e;
        var shaftStart = x.Tail == ArrowHead.Triangle ? Back(s, e, head * 0.8) : s;
        dc.DrawLine(pen, shaftStart, shaftEnd);
        DrawHead(dc, x.Head, s, e, head, brush, pen);
        DrawHead(dc, x.Tail, e, s, head, brush, pen);
    }

    static Point Back(Point tip, Point from, double by)
    {
        var d = tip - from;
        if (d.Length <= by) return tip;
        d.Normalize();
        return tip - d * by;
    }

    static void DrawHead(DrawingContext dc, ArrowHead kind, Point from, Point tip, double size, Brush brush, Pen pen)
    {
        if (kind == ArrowHead.None) return;
        var d = tip - from;
        if (d.Length < 0.001) return;
        d.Normalize();
        var n = new Vector(-d.Y, d.X);
        var b = tip - d * size;
        Point left = b + n * size * 0.5, right = b - n * size * 0.5;
        if (kind == ArrowHead.Line)
        {
            dc.DrawLine(pen, tip, left);
            dc.DrawLine(pen, tip, right);
            return;
        }
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(tip, true, true);
            c.LineTo(left, true, true);
            c.LineTo(right, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(brush, null, g);
    }

    static void DrawShape(DrawingContext dc, ShapeAnnotation x)
    {
        var pen = Pen(x.Stroke, x.StrokeWidth);
        var fill = x.Fill is null ? null : ImageIo.Brush(x.Fill);
        var r = R(x.Bounds);
        if (x.Kind == ShapeKind.Ellipse)
            dc.DrawEllipse(fill, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
        else
            dc.DrawRoundedRectangle(fill, pen, r, x.CornerRadius, x.CornerRadius);
    }

    static void DrawText(DrawingContext dc, TextAnnotation x)
    {
        var ft = Text(x.Text, x.FontFamily, x.FontSize, x.Bold, x.Color, double.NaN);
        if (x.Background is not null && x.Text.Length > 0)
            dc.DrawRoundedRectangle(ImageIo.Brush(x.Background), null,
                new Rect(x.Position.X - 6, x.Position.Y - 4, ft.WidthIncludingTrailingWhitespace + 12, ft.Height + 8), 4, 4);
        dc.DrawText(ft, P(x.Position));
    }

    static void DrawCallout(DrawingContext dc, CalloutAnnotation x)
    {
        var r = R(x.Bounds);
        var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var tail = P(x.TailPoint);
        Geometry shape = new RectangleGeometry(r, 8, 8);
        var dir = tail - center;
        if (!r.Contains(tail) && dir.Length > 0.001)
        {
            dir.Normalize();
            var n = new Vector(-dir.Y, dir.X);
            var half = Math.Min(r.Width, r.Height) * 0.2;
            var t = new StreamGeometry();
            using (var c = t.Open())
            {
                c.BeginFigure(center + n * half, true, true);
                c.LineTo(tail, true, true);
                c.LineTo(center - n * half, true, true);
            }
            shape = new CombinedGeometry(GeometryCombineMode.Union, shape, t);
        }
        dc.DrawGeometry(ImageIo.Brush(x.Fill), Pen(x.Stroke, 2), shape);
        dc.DrawText(Text(x.Text, "Segoe UI", x.FontSize, false, x.TextColor, Math.Max(1, r.Width - 16)), new Point(r.X + 8, r.Y + 6));
    }

    static void DrawStep(DrawingContext dc, StepAnnotation x)
    {
        dc.DrawEllipse(ImageIo.Brush(x.Fill), null, P(x.Center), x.Size / 2, x.Size / 2);
        var label = StepLabels.Format(x.Number, x.Style);
        var ft = Text(label, "Segoe UI", x.Size * (label.Length > 2 ? 0.4 : 0.55), true, x.TextColor, double.NaN);
        var scale = StepLabels.FitScale(x.Size, ft.Width);
        if (scale < 1) ft = Text(label, "Segoe UI", x.Size * (label.Length > 2 ? 0.4 : 0.55) * scale, true, x.TextColor, double.NaN);
        dc.DrawText(ft, new Point(x.Center.X - ft.Width / 2, x.Center.Y - ft.Height / 2));
    }

    static void DrawPolyline(DrawingContext dc, IReadOnlyList<Pt> points, Pen pen)
    {
        if (points.Count == 0) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(points[0]), false, false);
            c.PolyLineTo(points.Skip(1).Select(P).DefaultIfEmpty(P(points[0])).ToList(), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    /// <summary>Whole-pixel part of r inside the image, or empty.</summary>
    static Int32Rect Clip(Rct r, BitmapSource s)
    {
        var c = r.Intersect(new Rct(0, 0, s.PixelWidth, s.PixelHeight));
        if (c.IsEmpty) return Int32Rect.Empty;
        int x = (int)Math.Floor(c.X), y = (int)Math.Floor(c.Y);
        int w = Math.Min(s.PixelWidth - x, (int)Math.Ceiling(c.Right) - x), h = Math.Min(s.PixelHeight - y, (int)Math.Ceiling(c.Bottom) - y);
        return w <= 0 || h <= 0 ? Int32Rect.Empty : new Int32Rect(x, y, w, h);
    }

    static void DrawBlur(DrawingContext dc, BitmapSource src, BlurAnnotation x)
    {
        var r = Clip(x.Bounds, src);
        if (r.IsEmpty) return;
        var crop = new CroppedBitmap(src, r);
        var target = new Rect(r.X, r.Y, r.Width, r.Height);
        var strength = Math.Clamp(x.Strength, 1, 20);
        if (x.Mode == BlurMode.Pixelate)
        {
            dc.DrawImage(Pixelate(crop, strength * 2), target);
            return;
        }
        // Blur: shrink then let WPF scale it back up smoothly.
        var f = strength + 1.0;
        int sw = Math.Max(1, (int)(r.Width / f)), sh = Math.Max(1, (int)(r.Height / f));
        var small = new TransformedBitmap(crop, new ScaleTransform((double)sw / r.Width, (double)sh / r.Height));
        var g = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(g, BitmapScalingMode.HighQuality);
        using (var gc = g.Open()) gc.DrawImage(small, target);
        dc.DrawDrawing(g);
    }

    /// <summary>Block-average pixelation done on pixels, so the result does not depend on WPF scaling modes.</summary>
    static BitmapSource Pixelate(BitmapSource crop, int block)
    {
        var frame = ImageIo.ToFrame(crop);
        int w = frame.Width, h = frame.Height;
        var px = frame.Pixels;
        for (var by = 0; by < h; by += block)
        for (var bx = 0; bx < w; bx += block)
        {
            int ex = Math.Min(w, bx + block), ey = Math.Min(h, by + block);
            long a = 0, r = 0, g = 0, b = 0, n = 0;
            for (var y = by; y < ey; y++)
            for (var x = bx; x < ex; x++)
            {
                var p = px[y * w + x];
                b += p & 0xFF; g += (p >> 8) & 0xFF; r += (p >> 16) & 0xFF; a += p >> 24; n++;
            }
            var avg = (uint)(a / n) << 24 | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
            for (var y = by; y < ey; y++)
            for (var x = bx; x < ex; x++)
                px[y * w + x] = avg;
        }
        return ImageIo.FromFrame(frame);
    }

    static void DrawMagnify(DrawingContext dc, BitmapSource src, MagnifyAnnotation x)
    {
        var s = Clip(x.SourceBounds, src);
        if (s.IsEmpty) return;
        var target = R(AnnotationGeometry.MagnifyTarget(x with { SourceBounds = new Rct(s.X, s.Y, s.Width, s.Height) }));
        Geometry lens = x.Shape == MagnifyShape.Circle ? new EllipseGeometry(target) : new RectangleGeometry(target, 6, 6);
        dc.PushClip(lens);
        dc.DrawImage(new CroppedBitmap(src, s), target);
        dc.Pop();
        dc.DrawGeometry(null, Pen("#FFFFFFFF", 3), lens);
        dc.DrawRectangle(null, Pen("#99FFFFFF", 1), new Rect(s.X, s.Y, s.Width, s.Height));
    }

    static readonly Brush PlaceholderFill = ImageIo.Brush("#FFD4D4D8");

    // Decoded pictures keyed by object ID and payload length; null = undecodable. Entries of the drawn document are
    // never evicted (see PrunePictures).
    static readonly ConcurrentDictionary<(Guid, int), BitmapSource?> Pictures = new();

    // Parsed built-in drawings, by stamp Id (the catalogue never changes while Snapr runs).
    static readonly ConcurrentDictionary<string, Geometry> StampGeometry = new();

    static void DrawStamp(DrawingContext dc, StampAnnotation x)
    {
        var r = R(x.Bounds);
        if (StampCatalog.Find(x.StampId) is not { } def)
        {
            dc.DrawRectangle(PlaceholderFill, Pen("#FF71717A", 1), r);   // a stamp from a newer Snapr
            return;
        }
        var brush = ImageIo.Brush(x.Color);
        if (def.Shape == StampShape.Path)
        {
            var geometry = StampGeometry.GetOrAdd(def.Id, _ => { var g = Geometry.Parse(def.Data); g.Freeze(); return g; });
            var side = Math.Min(r.Width, r.Height);
            dc.PushTransform(new TranslateTransform(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2));
            dc.PushTransform(new ScaleTransform(side / 100, side / 100));
            dc.DrawGeometry(brush, null, geometry);
            dc.Pop();
            dc.Pop();
            return;
        }
        // Badge: one character on a circle, words on a pill; white bold text sized to fit.
        if (def.Data.Length == 1)
        {
            var d = Math.Min(r.Width, r.Height);
            r = new Rect(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
        }
        dc.DrawRoundedRectangle(brush, null, r, r.Height / 2, r.Height / 2);
        var text = new FormattedText(def.Data, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Math.Max(1, r.Height * 0.55), Brushes.White, 1.0);
        var room = Math.Max(1, r.Width - r.Height * 0.5);
        if (text.Width > room) text.SetFontSize(Math.Max(1, r.Height * 0.55 * room / text.Width));
        dc.DrawText(text, new Point(r.X + (r.Width - text.Width) / 2, r.Y + (r.Height - text.Height) / 2));
    }

    /// <summary>A small picture of a built-in stamp for the Stamp panel.</summary>
    public static ImageSource StampPreview(string stampId, string color, double size)
    {
        var def = StampCatalog.Find(stampId);
        var aspect = def is null ? 1 : StampCatalog.Aspect(def);
        var box = StampPlacement.Centered(new Pt(size / 2, size / 2), aspect);
        var scale = size / StampPlacement.ClickSize;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));   // keeps the image size fixed
            DrawStamp(dc, new StampAnnotation(new Rct(size / 2 - box.W * scale / 2, size / 2 - box.H * scale / 2, box.W * scale, box.H * scale), stampId, color));
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    static void DrawImageObject(DrawingContext dc, ImageAnnotation x)
    {
        var r = R(x.Bounds);
        if (Picture(x) is { } picture) dc.DrawImage(picture, r);
        else dc.DrawRectangle(PlaceholderFill, Pen("#FF71717A", 1), r);
    }

    static int _decodes;
    /// <summary>Diagnostic: number of picture decodes so far (lets tests prove the cache holds).</summary>
    public static int PictureDecodes => _decodes;

    static BitmapSource? Picture(ImageAnnotation x)
    {
        return Pictures.GetOrAdd((x.Id, x.Png.Length), _ =>
        {
            Interlocked.Increment(ref _decodes);
            try
            {
                var bytes = Convert.FromBase64String(x.Png);
                return PngInfo.Size(bytes) is var (w, h) && (long)w * h <= SnagxReader.MaxTotalPixels ? ImageIo.DecodePng(bytes) : null;
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or FileFormatException or ArgumentException or IOException
                                           or OutOfMemoryException)
            {
                return null;
            }
        });
    }
}
