using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.App.Services;

public sealed record CombineChoice(CombineLayout Layout, CombineAlign Align, int Gap, string Background)
{
    public static CombineChoice Default { get; } = new(CombineLayout.SideBySide, CombineAlign.Centre, 20, "#FFFFFFFF");
}

public sealed record CombineOutput(BitmapSource Base, IReadOnlyList<ImageAnnotation> Objects);

/// <summary>Combine images (spec parity 10 §5): each capture flattened like Export, laid out as Image objects on a plain canvas.</summary>
public static class CombineService
{
    /// <summary>The capture as Export writes it: image edits, objects, crop and effects.</summary>
    public static BitmapSource Flatten(CaptureDocument doc)
    {
        var original = ImageIo.ToBgra(ImageIo.DecodePng(doc.BasePng));
        var edited = new EditedImageCache().Get(original, doc.State.Edits);
        return DocumentRenderer.Render(edited, doc.State, applyEffects: true);
    }

    /// <summary>Null when the result would be too large.</summary>
    public static CombineOutput? Build(IReadOnlyList<BitmapSource> pictures, CombineChoice choice)
    {
        if (!ColorText.TryArgb(choice.Background, out var argb)) throw new ArgumentException("The background must be #AARRGGBB.", nameof(choice));
        var layout = CombineArrange.Arrange(pictures.Select(p => (p.PixelWidth, p.PixelHeight)).ToArray(), choice.Layout, choice.Align, choice.Gap);
        if (CombineArrange.TooLarge(layout)) return null;
        var px = new uint[layout.Width * layout.Height];
        Array.Fill(px, argb);
        var background = ImageIo.FromFrame(new Frame(layout.Width, layout.Height, px));
        var objects = pictures.Select((p, i) => new ImageAnnotation(layout.Places[i], Convert.ToBase64String(ImageIo.EncodePng(ImageIo.ToBgra(p))))).ToArray();
        return new CombineOutput(background, objects);
    }
}
