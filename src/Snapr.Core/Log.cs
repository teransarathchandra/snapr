namespace Snapr.Core;

public static class Log
{
    public static string LogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapr", "logs");

    public static string TodayPath => Path.Combine(LogDirectory, $"{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Appends to today's log. Never throws: logging must not cause a second failure.</summary>
    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(TodayPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{(ex is null ? "" : Environment.NewLine + ex)}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
