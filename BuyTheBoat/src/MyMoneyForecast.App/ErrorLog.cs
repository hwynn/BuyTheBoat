using System.IO;
using MyMoneyForecast.Persistence;

namespace MyMoneyForecast.App;

// Where uncaught errors (and a few deliberately-caught ones, like a failed
// import/export) get written, so a friend trying the demo can find ONE file
// and send it back when something breaks. AppPaths owns the location: in the
// portable demo it's the labeled logs\ folder next to the .exe; otherwise it's
// the hidden LocalAppData folder alongside the database — either way, one place.
public static class ErrorLog
{
    /// <summary>[CALC] The error-log file's full path, wherever AppPaths puts it (the demo's logs\ folder, or the LocalAppData folder off-demo).</summary>
    public static string FilePath => AppPaths.LogFilePath;

    /// <summary>[WRITES FILE] Appends one timestamped entry — what was happening, plus the full exception — to the error log. Never throws: logging must not itself be why the app fails.</summary>
    /// <param name="whatWasHappening">A short note on the operation in progress when it broke (e.g. "importing data").</param>
    /// <param name="error">The exception to record — type, message, and stack trace are all written.</param>
    public static void Record(string whatWasHappening, Exception error)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} — {whatWasHappening}{Environment.NewLine}" +
                $"{error.GetType().FullName}: {error.Message}{Environment.NewLine}" +
                $"{error.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // A failure to log must never itself surface as an error.
        }
    }
}
