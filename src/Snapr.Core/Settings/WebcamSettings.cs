namespace Snapr.Core.Settings;

public enum WebcamShape { Circle, RoundedRectangle }

public enum WebcamSize { Small, Medium, Large }

/// <summary>The webcam bubble shown while recording (spec parity 18 §2). DeviceId null = the first camera Windows lists.</summary>
public sealed record WebcamSettings(bool Enabled = false, string? DeviceId = null, WebcamShape Shape = WebcamShape.Circle,
    WebcamSize Size = WebcamSize.Medium, bool Mirror = true);
