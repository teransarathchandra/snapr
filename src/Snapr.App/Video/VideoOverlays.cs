using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.App.Video;

/// <summary>Video objects drawn onto transparent pictures the size of the video, for preview and export (spec parity 17 §5–6).</summary>
public static class VideoOverlays
{
    public static BitmapSource Render(IReadOnlyList<Annotation> items, int width, int height)
    {
        var clear = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        clear.Freeze();
        return DocumentRenderer.Render(clear, DocumentState.Empty with { Annotations = items }, applyEffects: false);
    }

    /// <summary>One PNG per group of objects with the same times, written into <paramref name="folder"/>.</summary>
    public static IReadOnlyList<VideoOverlayLayer> Write(VideoProject p, int width, int height, string folder)
    {
        var layers = new List<VideoOverlayLayer>();
        foreach (var (start, end, items) in p.Layers())
        {
            var path = Path.Combine(folder, $"layer{layers.Count + 1}.png");
            File.WriteAllBytes(path, ImageIo.EncodePng(Render(items, width, height)));
            layers.Add(new VideoOverlayLayer(path, start, end));
        }
        return layers;
    }
}
