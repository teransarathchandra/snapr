using System.Security.Cryptography;
using System.Text;

namespace Snapr.App.Services;

/// <summary>Keeps the upload header value encrypted for the current Windows user (DPAPI).</summary>
public static class SecretStore
{
    public static string Protect(string s) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));

    /// <summary>The original text, or null when the value is missing or can't be decrypted.</summary>
    public static string? Unprotect(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
