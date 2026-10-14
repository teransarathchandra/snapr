using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Services;

/// <summary>Copies editor objects as JSON (for pasting back into Snapr) plus a bitmap (for other apps).</summary>
public static class AnnotationClipboard
{
    public const string Format = "Snapr.Annotation.v1";

    // Reject payloads with missing/null required members (a partial object would crash rendering). Not used for .snapr files.
    static readonly JsonSerializerOptions Strict = new(SnaprJson.Options)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public const int MaxObjects = 10_000;

    public static string Serialize(IReadOnlyList<Annotation> items) => JsonSerializer.Serialize(items.ToArray(), SnaprJson.Options);

    /// <summary>
    /// The objects in a payload: a JSON array, or a single object as written by earlier versions.
    /// Null for anything that is not entirely valid (other apps can put any text under this name).
    /// </summary>
    public static IReadOnlyList<Annotation>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (json.TrimStart().StartsWith('['))
            {
                var items = JsonSerializer.Deserialize<Annotation?[]>(json, Strict);
                return items is { Length: > 0 and <= MaxObjects } && items.All(a => a is not null) ? items.Select(a => a!).ToArray() : null;
            }
            return JsonSerializer.Deserialize<Annotation>(json, Strict) is { } one ? new[] { one } : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }

    public static bool Set(IReadOnlyList<Annotation> items, BitmapSource image)
    {
        var data = new DataObject();
        data.SetData(Format, Serialize(items));
        data.SetImage(ImageIo.FlattenOnWhite(image));
        return ClipboardImage.Try(() => Clipboard.SetDataObject(data, true));
    }

    public static IReadOnlyList<Annotation>? Get()
    {
        string? json = null;
        try { ClipboardImage.Try(() => json = Clipboard.ContainsData(Format) ? Clipboard.GetData(Format) as string : null); }
        catch (Exception) { return null; } // foreign data under our format name: fall back to the image path
        return Deserialize(json);
    }
}
