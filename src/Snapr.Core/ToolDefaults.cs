namespace Snapr.Core;

public static class ToolDefaults
{
    public static ToolStyle For(ToolKind tool) => tool switch
    {
        ToolKind.Highlight => new("#FFFFCC00", 4, 20, null, false),
        ToolKind.Text => new("#FFFF3B30", 4, 28, null, false),
        ToolKind.Callout => new("#FFFF3B30", 3, 18, "#FFFFFFFF", true),
        ToolKind.Step => new("#FFFF3B30", 4, 20, null, true),
        ToolKind.Arrow => ToolStyle.Default with { Shadow = true },
        _ => ToolStyle.Default,
    };
}
