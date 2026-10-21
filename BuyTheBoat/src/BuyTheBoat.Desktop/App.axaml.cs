using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
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
        // Window sizes in XAML cover the whole window, title bar and borders included.
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => CountFrameInSize(window));

        // Grid rows stop at the vertical scrollbar instead of running underneath it.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGrid>((_, e) => KeepRowsClearOfScrollbar(e));

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

    /// <summary>
    /// [UI] Shrinks a window that just opened so its XAML Width and Height measure the whole window, and keeps it centred where it opened.
    /// Avalonia's Width and Height measure only the inside, so without this every window comes out 16 pixels wider and about 39 taller than its XAML size.
    /// </summary>
    private static void CountFrameInSize(Window window)
    {
        // FrameSize (title bar and borders included) is only known once the window is open.
        if (window.FrameSize is not { } frame)
        {
            return;
        }

        // Sides that size to their content have nothing to give back.
        // A window already fitted to the screen (MainWindow.FitToScreen) keeps its height: that already left room for the title bar.
        var shrinkWidth = window.SizeToContent.HasFlag(SizeToContent.Width) || double.IsNaN(window.Width)
            ? 0
            : frame.Width - window.ClientSize.Width;
        var shrinkHeight = window.SizeToContent.HasFlag(SizeToContent.Height) || double.IsNaN(window.Height) || window.Height >= window.MaxHeight
            ? 0
            : frame.Height - window.ClientSize.Height;
        if (shrinkWidth <= 0 && shrinkHeight <= 0)
        {
            return;
        }

        window.Width -= shrinkWidth;
        window.Height -= shrinkHeight;
        var scaling = window.RenderScaling;
        window.Position = new PixelPoint(
            window.Position.X + (int)(shrinkWidth * scaling / 2),
            window.Position.Y + (int)(shrinkHeight * scaling / 2));
    }

    /// <summary>[UI] Makes a grid's rows end where its vertical scrollbar begins, so the last column's text isn't hidden under the scrollbar.</summary>
    private static void KeepRowsClearOfScrollbar(TemplateAppliedEventArgs e)
    {
        // The grid's template spans the rows across the scrollbar's column, set inside the template where a style can't change it.
        if (e.NameScope.Find<DataGridRowsPresenter>("PART_RowsPresenter") is { } rows)
        {
            Grid.SetColumnSpan(rows, 2);
        }
    }
}
