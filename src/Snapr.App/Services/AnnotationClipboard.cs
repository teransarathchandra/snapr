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

    public static string Serialize(Annotation a) => JsonSerializer.Serialize(a, SnaprJson.Options);

    /// <summary>Null for anything that is not a valid Snapr object (other apps can put any text under this name).</summary>
    public static Annotation? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Annotation>(json, Strict); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }

    public static bool Set(Annotation a, BitmapSource image)
    {
        var data = new DataObject();
        data.SetData(Format, Serialize(a));
        data.SetImage(ImageIo.FlattenOnWhite(image));
        return ClipboardImage.Try(() => Clipboard.SetDataObject(data, true));
    }

    public static Annotation? Get()
    {
        string? json = null;
        try { ClipboardImage.Try(() => json = Clipboard.ContainsData(Format) ? Clipboard.GetData(Format) as string : null); }
        catch (Exception) { return null; } // foreign data under our format name: fall back to the image path
        return Deserialize(json);
    }
}
