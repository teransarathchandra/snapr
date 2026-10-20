using System.Text.Json;
using Snapr.Core.Settings;

namespace Snapr.Core.Output;

/// <summary>Sharing settings checks with the texts shown in Settings (spec parity 19 §2).</summary>
public static class ShareRules
{
    public const long MaxUploadBytes = 200L * 1024 * 1024;

    static bool IsWeb(Uri u) => u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;

    public static string? FolderError(string? folder) =>
        string.IsNullOrWhiteSpace(folder) ? "Choose the share folder."
        : !Path.IsPathFullyQualified(folder.Trim()) ? @"Use a full folder path, such as C:\Users\you\OneDrive\Snapr."
        : null;

    public static string? PrefixError(string? prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? null
        : Uri.TryCreate(prefix.Trim(), UriKind.Absolute, out var u) && IsWeb(u) ? null
        : "The link prefix must start with http:// or https://.";

    public static string? UploadUrlError(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Enter the upload URL.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || !IsWeb(u)) return "Enter a full URL, such as https://example.com/upload.";
        if (u.Scheme == Uri.UriSchemeHttps || u.IsLoopback) return null;
        return "Use https:// (http:// is only allowed for localhost).";
    }

    public static string? JsonPathError(UploadLinkMode mode, string? path) =>
        mode == UploadLinkMode.JsonField && string.IsNullOrWhiteSpace(path) ? "Enter the JSON field that holds the link, such as data.link." : null;

    const string TokenChars = "!#$%&'*+-.^_`|~";

    public static string? HeaderNameError(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : name.Trim().All(c => c < 128 && (char.IsAsciiLetterOrDigit(c) || TokenChars.Contains(c))) ? null
        : "Use a header name without spaces, such as Authorization or X-Api-Key.";

    internal static bool IsWebLink(string? s) => s is not null && Uri.TryCreate(s, UriKind.Absolute, out var u) && IsWeb(u);
}

public static class ShareLinks
{
    /// <summary>The prefix (one trailing slash ensured) plus the escaped file name, or the full path without a prefix.</summary>
    public static string Build(string? prefix, string fileName, string fullPath) =>
        string.IsNullOrWhiteSpace(prefix) ? fullPath : prefix.Trim().TrimEnd('/') + "/" + Uri.EscapeDataString(fileName);
}

public static class UploadResponse
{
    /// <summary>The link in an upload response (spec parity 19 §3), or null.</summary>
    public static string? Link(string body, UploadLinkMode mode, string? jsonPath)
    {
        if (mode == UploadLinkMode.WholeBody)
        {
            var t = body.Trim();
            return ShareRules.IsWebLink(t) ? t : null;
        }
        if (string.IsNullOrWhiteSpace(jsonPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var e = doc.RootElement;
            foreach (var seg in jsonPath.Trim().Split('.'))
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    if (e.TryGetProperty(seg, out var next)) e = next;
                    else if (e.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, seg, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } p) e = p.Value;
                    else return null;
                }
                else if (e.ValueKind == JsonValueKind.Array && int.TryParse(seg, out var i) && i >= 0 && i < e.GetArrayLength()) e = e[i];
                else return null;
            }
            var s = e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() : null;
            return ShareRules.IsWebLink(s) ? s : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
