using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace BuyTheBoat.App;

/// <summary>Small helpers that cover places where Avalonia behaves differently from WPF, so ported code can keep WPF's flow.</summary>
internal static class ControlExtensions
{
    /// <summary>
    /// [UI] Shows the window as a popup over its owner and returns only once it closes, with the value it closed with, like WPF's ShowDialog.
    /// For callers that need the answer right away, such as the save confirmations' callbacks; it keeps the app responding while it waits (Avalonia's DispatcherFrame).
    /// </summary>
    public static T? ShowDialogAndWait<T>(this Window dialog, Window owner)
    {
        var closed = dialog.ShowDialog<T>(owner);
        var frame = new DispatcherFrame();
        closed.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);
        return closed.Result;
    }

    /// <summary>
    /// [UI] Runs the handler whenever the control's property changes, at the moment it changes.
    /// For a TextBox's Text or a CalendarDatePicker's SelectedDate: Avalonia raises TextChanged and SelectedDateChanged later, after a form's Load* methods have already turned their "ignore my own edits" flag back off, so those events would mark a freshly loaded form as edited.
    /// </summary>
    public static void WhenChanged(this Control control, AvaloniaProperty property, Action handler) =>
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == property)
            {
                handler();
            }
        };

    /// <summary>
    /// [UI] Turns off the other radio buttons beside this one, so a handler reacting to this one turning on sees the settled choice.
    /// Avalonia turns the siblings off only after announcing the new choice, where WPF did it before.
    /// </summary>
    public static void UncheckSiblingRadios(this RadioButton picked)
    {
        foreach (var sibling in (picked.Parent as Panel)?.Children.OfType<RadioButton>() ?? [])
        {
            if (sibling != picked)
            {
                sibling.IsChecked = false;
            }
        }
    }

    /// <summary>[UI] Runs the handler whenever the box's text changes, at the moment it changes (see WhenChanged).</summary>
    public static void WhenTextChanged(this TextBox box, Action handler) => box.WhenChanged(TextBox.TextProperty, handler);

    /// <summary>[UI] Runs the handler whenever the picked date changes, at the moment it changes (see WhenChanged).</summary>
    public static void WhenDateChanged(this CalendarDatePicker picker, Action handler) => picker.WhenChanged(CalendarDatePicker.SelectedDateProperty, handler);
}
