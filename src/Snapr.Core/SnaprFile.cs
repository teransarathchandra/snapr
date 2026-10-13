using System.IO.Compression;
using System.Text.Json;

namespace Snapr.Core;

/// <summary>.snapr = ZIP with document.json, base.png and thumbnail.png.</summary>
public static class SnaprFile
{
    public const int FormatVersion = 1;

    sealed record Manifest(int FormatVersion, Guid Id, int PixelWidth, int PixelHeight, double Dpi, CaptureMeta? Meta, DocumentState? State);

    public static void Save(CaptureDocument doc, byte[] thumbnailPng, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            var manifest = new Manifest(FormatVersion, doc.Id, doc.PixelWidth, doc.PixelHeight, doc.Dpi, doc.Meta, doc.State);
            Write(zip, "document.json", JsonSerializer.SerializeToUtf8Bytes(manifest, SnaprJson.Options));
            Write(zip, "base.png", doc.BasePng);
            Write(zip, "thumbnail.png", thumbnailPng);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static CaptureDocument Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        Manifest m;
        try
        {
            m = JsonSerializer.Deserialize<Manifest>(Read(zip, "document.json"), SnaprJson.Options)
                ?? throw new InvalidDataException("document.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("document.json is not valid.", ex);
        }
        if (m.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Unsupported .snapr format version {m.FormatVersion}.");
        return new CaptureDocument
        {
            Id = m.Id,
            BasePng = Read(zip, "base.png"),
            PixelWidth = m.PixelWidth,
            PixelHeight = m.PixelHeight,
            Dpi = m.Dpi,
            Meta = m.Meta ?? throw new InvalidDataException("document.json has no meta."),
            State = m.State ?? DocumentState.Empty,
        };
    }

    public static byte[] ReadThumbnail(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip, "thumbnail.png");
    }

    static byte[] Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} is missing.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static void Write(ZipArchive zip, string name, byte[] data)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        s.Write(data);
    }
}
