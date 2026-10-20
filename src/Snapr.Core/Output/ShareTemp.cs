namespace Snapr.Core.Output;

/// <summary>Leftover share temp folders from earlier runs (spec parity 20 §3.6).</summary>
public static class ShareTemp
{
    public const string Prefix = "snapr-share-";

    public static int Clean(string tempRoot, DateTime nowUtc, TimeSpan maxAge)
    {
        var deleted = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(tempRoot, Prefix + "*"))
            {
                try
                {
                    if (nowUtc - Directory.GetLastWriteTimeUtc(dir) <= maxAge) continue;
                    Directory.Delete(dir, recursive: true);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Share temp cleanup skipped", ex);
        }
        return deleted;
    }
}
