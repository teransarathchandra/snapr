namespace Snapr.Core.Settings;

public enum StartupChange { None, Write, Delete }

/// <summary>The HKCU Run value that starts Snapr with Windows. Pure rules; the registry lives in the App.</summary>
public static class StartupEntry
{
    public const string ValueName = "Snapr";

    public static string Command(string exePath) => $"\"{exePath}\"";

    /// <summary>wanted: true = should exist, false = should not, null = keep on/off as is and only refresh the path (launch).</summary>
    public static StartupChange Decide(bool? wanted, string? current, string exePath)
    {
        var same = string.Equals(current, Command(exePath), StringComparison.OrdinalIgnoreCase);
        return wanted switch
        {
            true => same ? StartupChange.None : StartupChange.Write,
            false => current is null ? StartupChange.None : StartupChange.Delete,
            null => current is null || same ? StartupChange.None : StartupChange.Write,
        };
    }
}
