using Avalonia;

namespace BuyTheBoat.App;

internal static class Program
{
    /// <summary>[STEP] Starts the app and opens the main window; returns when the last window closes.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // A background-thread error can't be recovered, but is logged before the app closes.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                ErrorLog.Record("Fatal background error (the app could not continue)", error);
            }
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>[CALC] The app's Avalonia setup; also used by the XAML previewer in an IDE.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
