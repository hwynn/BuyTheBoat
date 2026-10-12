using System.Windows;
using System.Windows.Threading;

namespace BuyTheBoat.App;

public partial class App : Application
{
    /// <summary>
    /// Logs errors that would otherwise close the app without a trace (ErrorLog) and tells the user where the log is, so a demo tester can send it in.
    /// A UI-thread error leaves the app running (e.Handled = true); a background-thread error can't be recovered, but is logged before the app closes.
    /// </summary>
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            ErrorLog.Record("Unexpected error (the app kept running)", e.Exception);

            try
            {
                MessageBox.Show(
                    "Something went wrong, but the app is still running.\n\n" +
                    $"The details were saved to:\n{ErrorLog.FilePath}\n\n" +
                    "Please include that file when reporting the problem.",
                    "Something went wrong",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // If even showing the notice fails, the log above still has it.
            }

            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                ErrorLog.Record("Fatal background error (the app could not continue)", error);
            }
        };
    }
}
