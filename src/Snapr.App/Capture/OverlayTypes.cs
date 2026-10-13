using Snapr.Core.Capture;

namespace Snapr.App.Capture;

public enum OverlayMode { Image, Video, ScrollingWindow }

/// <summary>What happens with an image selection: open in the editor, copy only, or OCR.</summary>
public enum OverlayAction { Edit, Copy, GrabText }

public sealed record OverlayResult(Selection Selection, OverlayAction Action);
