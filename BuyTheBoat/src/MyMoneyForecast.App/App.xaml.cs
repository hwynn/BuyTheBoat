using System.Windows;
using System.Windows.Threading;

namespace MyMoneyForecast.App;

public partial class App : Application
{
    // TEMPORARY DIAGNOSTIC (2026-08-12) — remove once the empty-grids-on-
    // launch report is resolved. No handler existed here before, so any
    // exception during MainWindow's own construction/startup would normally
    // crash the process outright — this catches it instead and shows exactly
    // what it was and where, in case something IS throwing but somehow not
    // presenting as a visible crash. e.Handled = true lets the app keep
    // running afterward so the resulting UI state (e.g., are the grids
    // empty specifically BECAUSE of this) is also visible.
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mmf-diagnostic.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} UNHANDLED EXCEPTION: {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}\n");
            }
            catch
            {
                // Diagnostic logging itself must never be why the app fails.
            }

            e.Handled = true;
        };
    }
}
