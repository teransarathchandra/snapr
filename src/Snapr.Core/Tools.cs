namespace Snapr.Core;

public enum ToolKind { Move, Arrow, Text, Callout, Shape, Pen, Highlight, Step, Blur, Magnify, Crop, Eraser }

public enum ShapeOption { Rectangle, Ellipse, Line }

/// <summary>The style a tool draws with. Colours are "#AARRGGBB".</summary>
public sealed record ToolStyle(string Color, double Width, double FontSize, string? Fill, bool Shadow)
{
    public static ToolStyle Default { get; } = new("#FFFF3B30", 4, 20, null, false);
}

public sealed record ToolOptions(ShapeOption Shape, BlurMode Blur, int BlurStrength, double Zoom, MagnifyShape Lens,
    StepStyle Steps = StepStyle.Numbers)
{
    public static ToolOptions Default { get; } = new(ShapeOption.Rectangle, BlurMode.Pixelate, 8, 2, MagnifyShape.Circle);
}
