using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapr.Core;

namespace Snapr.App.Editor;

/// <summary>Original 20×20 stroke icons, drawn as paths so they never depend on an icon font being installed.</summary>
public static class ToolIcons
{
    static readonly Dictionary<ToolKind, string> Data = new()
    {
        [ToolKind.Move] = "M5,3 L5,17 L9,13 L12,19 L14,18 L11,12 L16,12 Z",
        [ToolKind.Arrow] = "M4,16 L15,5 M9,5 L15,5 L15,11",
        [ToolKind.Text] = "M4,4 L16,4 M10,4 L10,17",
        [ToolKind.Callout] = "M3,4 H17 V13 H9 L5,17 V13 H3 Z",
        [ToolKind.Shape] = "M3,5 H17 V15 H3 Z",
        [ToolKind.Pen] = "M3,16 C6,8 10,15 17,4",
        [ToolKind.Highlight] = "M3,11 H17 V16 H3 Z M6,11 L9,4 H12 L14,11",
        [ToolKind.Step] = "M10,3 A7,7 0 1 1 9.99,3 Z M9,7.5 L10.5,6.5 V14",
        [ToolKind.Blur] = "M3,3 H8 V8 H3 Z M12,3 H17 V8 H12 Z M3,12 H8 V17 H3 Z M12,12 H17 V17 H12 Z",
        [ToolKind.Magnify] = "M8.5,3 A5.5,5.5 0 1 1 8.49,3 Z M12.5,12.5 L17,17",
        [ToolKind.Crop] = "M6,2 V14 H18 M2,6 H14 V18",
        [ToolKind.Eraser] = "M4,14 L11,7 L16,12 L11,17 H6 Z M8,10 L13,15",
        [ToolKind.Stamp] = "M7,3 H13 V8 L15,11 H5 L7,8 Z M3,13 H17 V16 H3 Z",
        [ToolKind.CutOut] = "M3,4 H17 M3,8 H17 M3,12 L5,14 L7,12 L9,14 L11,12 L13,14 L15,12 L17,14 M3,16 H17",
        [ToolKind.Selection] = "M3,3 H6 M8,3 H12 M14,3 H17 V6 M17,8 V12 M17,14 V17 H14 M12,17 H8 M6,17 H3 V14 M3,12 V8 M3,6 V3",
        [ToolKind.Spotlight] = "M10,3 A7,7 0 1 1 9.99,3 Z M10,6.5 A3.5,3.5 0 1 1 9.99,6.5 Z",
        [ToolKind.Fill] = "M3,10 L9,4 L15,10 L9,16 Z M3,10 H15 M16,12 C17.5,14.5 18,15.5 16.8,16.6 C15.8,17.4 14.6,16 16,12 Z",
    };

    public static UIElement Create(ToolKind tool) => new Path
    {
        Data = Geometry.Parse(Data[tool]),
        Stroke = (Brush)Application.Current.FindResource("TextBrush"),
        StrokeThickness = 1.6,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = 20,
        Height = 20,
    };
}
