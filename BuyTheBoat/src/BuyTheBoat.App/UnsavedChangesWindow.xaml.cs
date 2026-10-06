using System.Windows;

namespace BuyTheBoat.App;

// Asked when the user picks a different row in a List subtab while that tab's Form subtab has unsaved changes.
// Both answers lead to the Form subtab; they differ only in which item ends up loaded there.
public partial class UnsavedChangesWindow : Window
{
    /// <summary>[CALC] True to load the picked row and discard the unsaved changes, false to go back to the unsaved changes, null if the window was closed without choosing.</summary>
    public bool? ContinueToSelection { get; private set; }

    public UnsavedChangesWindow(string formName, string itemDescription)
    {
        InitializeComponent();
        MessageText.Text = $"You have unsaved changes to {itemDescription} in the {formName} form. Continuing to what you selected will discard them.";
    }

    /// <summary>[UI] Picks "load what I clicked" and closes.</summary>
    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        ContinueToSelection = true;
        DialogResult = true;
    }

    /// <summary>[UI] Picks "keep my unsaved changes" and closes.</summary>
    private void OnGoToUnsavedClick(object sender, RoutedEventArgs e)
    {
        ContinueToSelection = false;
        DialogResult = true;
    }
}
