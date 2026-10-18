using System.IO;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

/// <summary>
/// Where uncaught errors (and a few caught ones, like a failed import or export) are written, so someone trying the demo can find one file and send it back.
/// AppPaths sets the location: the logs\ folder next to the .exe in the portable demo, otherwise the LocalAppData folder beside the database.
/// </summary>
public static class ErrorLog
{
    /// <summary>[CALC] The error-log file's full path, wherever AppPaths puts it (the demo's logs\ folder, or the LocalAppData folder off-demo).</summary>
    public static string FilePath => AppPaths.LogFilePath;

    /// <summary>
    /// [WRITES FILE] Logs a caught save error and returns the one line to show the user, so they never hit raw developer text; use it at every save or confirm catch instead of ex.Message.
    /// Shows the exception's message for deliberate validation (InvalidOperationException or ArgumentException, like "Enter a name"), and a safe generic line for anything unexpected.
    /// The full exception is always logged first (Record never throws).
    /// </summary>
    /// <param name="whatWasHappening">A short note on the operation in progress (e.g. "saving this expense").</param>
    /// <param name="error">The caught exception.</param>
    public static string RecordAndDescribe(string whatWasHappening, Exception error)
    {
        Record(whatWasHappening, error);

        // An internal assumption check is a program fault, not input the user can fix, despite being an ArgumentException.
        return error is (InvalidOperationException or ArgumentException) and not InternalAssumptionViolationException
            ? error.Message
            : "Something went wrong and this couldn't be completed. The details have been logged.";
    }

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

            File.AppendAllText(FilePath, FormatEntry(whatWasHappening, error, DateTime.Now));
        }
        catch
        {
            // A failure to log must never itself surface as an error.
        }
    }

    /// <summary>[CALC] Builds the text of one log entry; when the error is a failed assumption check, the entry names the assumption's ID so it can be looked up in planning/03-assumptions-glossary.md.</summary>
    /// <param name="whatWasHappening">A short note on the operation in progress when it broke.</param>
    /// <param name="error">The exception to describe.</param>
    /// <param name="at">When it happened.</param>
    internal static string FormatEntry(string whatWasHappening, Exception error, DateTime at)
    {
        var assumptionLine = error is AssumptionViolationException violation
            ? $"Assumption {violation.AssumptionId} (planning/03-assumptions-glossary.md){Environment.NewLine}"
            : "";

        return $"{at:yyyy-MM-dd HH:mm:ss} — {whatWasHappening}{Environment.NewLine}" +
            $"{error.GetType().FullName}: {error.Message}{Environment.NewLine}" +
            assumptionLine +
            $"{error.StackTrace}{Environment.NewLine}{Environment.NewLine}";
    }
}
