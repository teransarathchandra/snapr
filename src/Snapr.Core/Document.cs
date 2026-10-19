namespace Snapr.Core;

public enum CaptureKind { Region, Window, Fullscreen, Scrolling, Clipboard, Imported, Panoramic }

public sealed record BorderEffect(string Color, int Width);

public sealed record DocumentEffects(BorderEffect? Border = null, bool Shadow = false, bool TornEdge = false)
{
    public static DocumentEffects None { get; } = new();
}

/// <summary>Everything the user can undo. Immutable; every edit produces a new instance.</summary>
public sealed record DocumentState(IReadOnlyList<Annotation> Annotations, Rct? Crop, DocumentEffects Effects, int NextStepNumber)
{
    public static DocumentState Empty { get; } = new(Array.Empty<Annotation>(), null, DocumentEffects.None, 1);

    /// <summary>Image edits replayed over the original pixels, in order (spec parity 4). Empty for older files.</summary>
    public IReadOnlyList<ImageEdit> Edits { get; init; } = [];
}

public sealed record CaptureMeta(DateTimeOffset CapturedAt, CaptureKind Kind, string? SourceTitle);

public sealed class CaptureDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required byte[] BasePng { get; init; }
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    public double Dpi { get; init; } = 96;
    public required CaptureMeta Meta { get; init; }
    public DocumentState State { get; set; } = DocumentState.Empty;
}
