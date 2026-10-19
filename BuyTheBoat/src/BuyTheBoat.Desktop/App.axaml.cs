using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace BuyTheBoat.App;

public partial class App : Application
{
    /// <summary>[STEP] Loads the app's theme and styles from App.axaml.</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// [STEP] Opens the main window, and from then on logs any error on the UI thread (ErrorLog) and tells the user where the log is, so a demo tester can send it in.
    /// The app keeps running after such an error.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Record("Unexpected error (the app kept running)", e.Exception);
                e.Handled = true;

                try
                {
                    _ = MessageBox.Show(
                        desktop.MainWindow,
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
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
