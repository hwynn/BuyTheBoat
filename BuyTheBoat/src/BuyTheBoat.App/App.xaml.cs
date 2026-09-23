using System.Windows;
using System.Windows.Threading;

namespace BuyTheBoat.App;

public partial class App : Application
{
    // Catches errors that would otherwise close the app without a trace: writes
    // them to a findable log (ErrorLog, alongside the database) and tells the
    // user where it is, so a demo tester can send it in — the app's bug-report
    // path. A UI-thread error keeps the app running afterward (e.Handled = true)
    // so a single broken action doesn't lose everything; a background-thread
    // error can't be resumed, but is still logged before the app closes.
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
