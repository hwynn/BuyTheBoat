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
            MaxWidth = 560,
            CanResize = false,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        var result = dismissResult;

        // Each button records its answer and closes the box.
        Button MakeButton(string label, MessageBoxResult answer, bool isDefault)
        {
            var created = new Button { Content = label, MinWidth = 80, IsDefault = isDefault, HorizontalContentAlignment = HorizontalAlignment.Center };
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

        var message = new DockPanel { LastChildFill = true };
        if (Symbol(image) is { } symbol)
        {
            DockPanel.SetDock(symbol, Dock.Left);
            message.Children.Add(symbol);
        }
        message.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var layout = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        layout.Children.Add(message);
        layout.Children.Add(buttons);
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

    /// <summary>[CALC] The round colored symbol for the message's kind, or null for none.</summary>
    private static Control? Symbol(MessageBoxImage image)
    {
        (string glyph, string color)? look = image switch
        {
            MessageBoxImage.Information => ("i", "#FF1E66D0"),
            MessageBoxImage.Question => ("?", "#FF1E66D0"),
            MessageBoxImage.Warning => ("!", "#FF8A6A00"),
            MessageBoxImage.Error => ("×", "#FFB00020"),
            _ => null,
        };
        if (look is not { } chosen)
        {
            return null;
        }

        return new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = Brush.Parse(chosen.color),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = chosen.glyph,
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
