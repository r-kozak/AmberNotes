using Avalonia.Controls;

namespace AmberNotes.Views;

public partial class PrivateUnlockView : UserControl
{
    public PrivateUnlockView()
    {
        InitializeComponent();

        // Auto-focus the password field when the overlay appears
        AttachedToVisualTree += (_, _) =>
        {
            PasswordInput?.Focus();
        };
    }
}
