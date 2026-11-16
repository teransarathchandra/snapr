using System.IO;
using Microsoft.Win32;
using Snapr.Core.Settings;

namespace Snapr.App.Services;

/// <summary>Reads and writes the per-user "Start with Windows" Run value. Registry failures propagate; callers filter with <see cref="IsRegistryError"/>.</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupEntry.ValueName) as string;
    }

    public static void Apply(StartupChange change, string exePath)
    {
        switch (change)
        {
            case StartupChange.Write:
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    key.SetValue(StartupEntry.ValueName, StartupEntry.Command(exePath), RegistryValueKind.String);
                break;
            case StartupChange.Delete:
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                    key?.DeleteValue(StartupEntry.ValueName, throwOnMissingValue: false);
                break;
        }
    }

    public static bool IsRegistryError(Exception ex) => ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
}
