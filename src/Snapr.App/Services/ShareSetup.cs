using Snapr.Core.Output;
using Snapr.Core.Settings;

namespace Snapr.App.Services;

public enum ShareTarget { Folder, Upload, Windows }

/// <summary>Whether a share target is set up, and the Settings checks for the SHARING section.</summary>
public static class ShareSetup
{
    public static bool IsReady(ShareSettings s, ShareTarget target) => target switch
    {
        ShareTarget.Folder => ShareRules.FolderError(s.Folder) is null,
        ShareTarget.Upload => s.Upload is { } u && ShareRules.UploadUrlError(u.Url) is null,
        _ => true,
    };

    /// <summary>The first error for the entered values; folder checks only when a folder is entered, upload checks only when an upload URL is entered.</summary>
    public static string? Validate(string? folder, string? prefix, string? uploadUrl, UploadLinkMode mode, string? jsonPath, string? headerName = null)
    {
        if (!string.IsNullOrWhiteSpace(folder) && (ShareRules.FolderError(folder) ?? ShareRules.PrefixError(prefix)) is { } e) return e;
        if (!string.IsNullOrWhiteSpace(uploadUrl) && (ShareRules.UploadUrlError(uploadUrl) ?? ShareRules.JsonPathError(mode, jsonPath) ?? ShareRules.HeaderNameError(headerName)) is { } e2) return e2;
        return null;
    }
}
