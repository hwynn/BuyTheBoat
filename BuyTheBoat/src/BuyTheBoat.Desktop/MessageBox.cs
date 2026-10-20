using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BuyTheBoat.App;

/// <summary>Which buttons a MessageBox offers; the same choices WPF's MessageBox had, so ported calls keep their arguments.</summary>
public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
}

/// <summary>The symbol shown beside a MessageBox's text.</summary>
public enum MessageBoxImage
{
    None,
    Information,
    Question,
    Warning,
    Error,
}

/// <summary>The button the user picked; closing the box any other way counts as Cancel, No, or OK, whichever it offers.</summary>
public enum MessageBoxResult
{
    OK,
    Cancel,
    Yes,
    No,
}

/// <summary>
/// A small popup with a message and up to two buttons, standing in for WPF's MessageBox, which Avalonia doesn't have.
/// Unlike WPF's, Show doesn't block: await it to get the user's answer.
/// </summary>
public static class MessageBox
{
    /// <summary>[UI] Shows the message over the given window and waits for the user to answer; with no window, it opens on its own.</summary>
    public static Task<MessageBoxResult> Show(Window? owner, string text, string caption, MessageBoxButton button, MessageBoxImage image)
    {
        var dismissResult = button switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.OK,
        };

        var window = new Window
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            Background = Brushes.White,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        var result = dismissResult;

        // Each button records its answer and closes the box.
        Button MakeButton(string label, MessageBoxResult answer, bool isDefault)
        {
            var created = new Button { Content = label, MinWidth = 88, IsDefault = isDefault, HorizontalContentAlignment = HorizontalAlignment.Center };
            created.Click += (_, _) =>
            {
                result = answer;
                window.Close();
            };
            return created;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        switch (button)
        {
            case MessageBoxButton.OKCancel:
                buttons.Children.Add(MakeButton("OK", MessageBoxResult.OK, isDefault: true));
                buttons.Children.Add(MakeButton("Cancel", MessageBoxResult.Cancel, isDefault: false));
                break;
            case MessageBoxButton.YesNo:
                buttons.Children.Add(MakeButton("Yes", MessageBoxResult.Yes, isDefault: true));
                buttons.Children.Add(MakeButton("No", MessageBoxResult.No, isDefault: false));
                break;
            default:
                buttons.Children.Add(MakeButton("OK", MessageBoxResult.OK, isDefault: true));
                break;
        }

        // Laid out like Windows' own message box: the symbol and text on white, wrapped at about the same width, and the buttons in a grey strip along the bottom.
        var message = new DockPanel { LastChildFill = true, Margin = new Thickness(24, 26, 24, 26) };
        if (Symbol(image) is { } symbol)
        {
            DockPanel.SetDock(symbol, Dock.Left);
            message.Children.Add(symbol);
        }
        message.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center });

        var buttonStrip = new Border { Background = Brush.Parse("#FFF0F0F0"), Padding = new Thickness(10), Child = buttons };
        DockPanel.SetDock(buttonStrip, Dock.Bottom);

        var layout = new DockPanel();
        layout.Children.Add(buttonStrip);
        layout.Children.Add(message);
        window.Content = layout;

        // Esc dismisses, same as the window's close button.
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };

        var answered = new TaskCompletionSource<MessageBoxResult>();
        window.Closed += (_, _) => answered.TrySetResult(result);

        if (owner is not null)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        return answered.Task;
    }

    /// <summary>[CALC] The symbol for the message's kind, drawn like Windows' own: a yellow warning triangle, a red error circle, or a blue information or question circle; null for none.</summary>
    private static Control? Symbol(MessageBoxImage image)
    {
        if (image == MessageBoxImage.None)
        {
            return null;
        }

        var symbol = new Panel { Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        if (image == MessageBoxImage.Warning)
        {
            symbol.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 16,2 L 31,29 L 1,29 Z"),
                Fill = Brush.Parse("#FFFFCC33"),
                Stroke = Brush.Parse("#FF8A6A00"),
                StrokeThickness = 1,
            });
            symbol.Children.Add(Glyph("!", Brushes.Black, top: 6));
            return symbol;
        }

        var (glyph, color) = image switch
        {
            MessageBoxImage.Error => ("✕", "#FFE81123"),
            MessageBoxImage.Question => ("?", "#FF0063B1"),
            _ => ("i", "#FF0063B1"),
        };
        symbol.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Fill = Brush.Parse(color) });
        symbol.Children.Add(Glyph(glyph, Brushes.White, top: 0));
        return symbol;
    }

    /// <summary>[CALC] The bold character drawn on a message-box symbol.</summary>
    private static TextBlock Glyph(string text, IBrush color, double top) => new()
    {
        Text = text,
        Foreground = color,
        FontWeight = FontWeight.Bold,
        FontSize = 18,
        Margin = new Thickness(0, top, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
