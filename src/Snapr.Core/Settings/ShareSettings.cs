namespace Snapr.Core.Settings;

public enum UploadLinkMode { WholeBody, JsonField }

/// <summary>A custom upload endpoint (spec parity 19 §2). HeaderValueProtected is base64 DPAPI data, never plain text.</summary>
public sealed record UploadSettings(string Url, string FieldName = "file", string? HeaderName = null, string? HeaderValueProtected = null,
    UploadLinkMode LinkMode = UploadLinkMode.WholeBody, string? JsonPath = null);

/// <summary>Where Share sends files (spec parity 19 §2).</summary>
public sealed record ShareSettings(string? Folder = null, string? UrlPrefix = null, UploadSettings? Upload = null);
