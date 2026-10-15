using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.Core;

namespace Snapr.App.Rendering;

/// <summary>Produces the current capture image by replaying image edits over the original pixels (spec parity 4 §6).</summary>
public static class ImageEditRenderer
{
    public static BitmapSource Apply(BitmapSource image, ImageEdit edit) => edit switch
    {
        RotateEdit r => ImageIo.ToBgra(new TransformedBitmap(image, new RotateTransform(90 * r.QuarterTurns))),
        FlipEdit f => ImageIo.ToBgra(new TransformedBitmap(image, new ScaleTransform(f.Horizontal ? -1 : 1, f.Horizontal ? 1 : -1))),
        ResizeEdit z => Draw(z.Width, z.Height, null, BitmapScalingMode.HighQuality,
            dc => dc.DrawImage(image, new Rect(0, 0, z.Width, z.Height))),
        MarginsEdit m => Draw(image.PixelWidth + m.Left + m.Right, image.PixelHeight + m.Top + m.Bottom, m.Color, BitmapScalingMode.NearestNeighbor,
            dc => dc.DrawImage(image, new Rect(m.Left, m.Top, image.PixelWidth, image.PixelHeight))),
        FillEdit f when ColorText.TryArgb(f.Color, out var argb) =>
            ImageIo.FromFrame(FloodFill.Fill(ImageIo.ToFrame(image), f.X, f.Y, argb, f.Tolerance)),
        CutOutEdit c => Cut(image, c),
        _ => image,
    };

    /// <summary>Copies the pieces before and after the strip next to each other; Torn adds a white zigzag seam (spec parity 7 §2.2).</summary>
    static BitmapSource Cut(BitmapSource image, CutOutEdit c)
    {
        int w = image.PixelWidth, h = image.PixelHeight, gap = c.End - c.Start;
        int nw = c.Vertical ? w - gap : w, nh = c.Vertical ? h : h - gap;
        int after = (c.Vertical ? w : h) - c.End;
        return Draw(nw, nh, null, BitmapScalingMode.NearestNeighbor, dc =>
        {
            if (c.Start > 0)
            {
                var first = c.Vertical ? new Int32Rect(0, 0, c.Start, h) : new Int32Rect(0, 0, w, c.Start);
                dc.DrawImage(new CroppedBitmap(image, first), new Rect(0, 0, first.Width, first.Height));
            }
            if (after > 0)
            {
                var rest = c.Vertical ? new Int32Rect(c.End, 0, after, h) : new Int32Rect(0, c.End, w, after);
                dc.DrawImage(new CroppedBitmap(image, rest), c.Vertical ? new Rect(c.Start, 0, after, h) : new Rect(0, c.Start, w, after));
            }
            if (c.Join == CutJoin.Torn) dc.DrawGeometry(Brushes.White, null, TornSeam(c, c.Vertical ? nh : nw));
        });
    }

    static Geometry TornSeam(CutOutEdit c, int length)
    {
        var points = CutOutGeometry.TornBand(length)
            .Select(p => c.Vertical ? new Point(c.Start + p.Y, p.X) : new Point(p.X, c.Start + p.Y)).ToArray();
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            ctx.PolyLineTo(points[1..], isStroked: false, isSmoothJoin: false);
        }
        g.Freeze();
        return g;
    }

    static BitmapSource Draw(int w, int h, string? background, BitmapScalingMode mode, Action<DrawingContext> draw)
    {
        var v = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(v, mode);
        using (var dc = v.RenderOpen())
        {
            if (background is not null) dc.DrawRectangle(ImageIo.Brush(background), null, new Rect(0, 0, w, h));
            draw(dc);
        }
        return ImageIo.ToBgra(ImageIo.Rasterize(v, w, h));
    }
}

/// <summary>
/// Edited images by edit-list prefix, so undo/redo is instant and a new edit only replays what is new.
/// Replays also leave a checkpoint every few edits, so undoing far back never restarts from the original.
/// Total pixel memory is capped; the least recently used entries go first.
/// </summary>
public sealed class EditedImageCache(long budgetBytes = 512L << 20)
{
    const int CheckpointEvery = 4;

    readonly List<(ImageEdit[] Edits, BitmapSource Image)> _items = [];   // least recently used first
    BitmapSource? _original;

    /// <summary>How many single edits were replayed so far (diagnostic).</summary>
    public int Replays { get; private set; }

    public BitmapSource Get(BitmapSource original, IReadOnlyList<ImageEdit> edits)
    {
        if (!ReferenceEquals(original, _original))
        {
            _items.Clear();
            _original = original;
        }
        if (edits.Count == 0) return original;

        var image = original;
        var done = 0;
        var best = -1;
        for (var k = 0; k < _items.Count; k++)
        {
            var cached = _items[k].Edits;
            if (cached.Length > done && cached.Length <= edits.Count && cached.SequenceEqual(edits.Take(cached.Length)))
            {
                best = k;
                done = cached.Length;
            }
        }
        if (best >= 0)
        {
            var hit = _items[best];
            image = hit.Image;
            _items.RemoveAt(best);
            _items.Add(hit);   // now the most recently used
        }

        for (var i = done; i < edits.Count; i++)
        {
            image = ImageEditRenderer.Apply(image, edits[i]);
            Replays++;
            if ((i + 1) % CheckpointEvery == 0 || i + 1 == edits.Count) Store(edits.Take(i + 1).ToArray(), image);
        }

        // The returned image is the most recently used entry, so it is never the one evicted.
        while (_items.Count > 1 && _items.Sum(x => (long)x.Image.PixelWidth * x.Image.PixelHeight * 4) > budgetBytes)
            _items.RemoveAt(0);
        return image;
    }

    void Store(ImageEdit[] key, BitmapSource image)
    {
        _items.RemoveAll(x => x.Edits.SequenceEqual(key));
        _items.Add((key, image));
    }
}
