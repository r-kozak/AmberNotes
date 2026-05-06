using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AmberNotes.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();

        // Auto-focus the password field as soon as the view is shown
        AttachedToVisualTree += (_, _) => PasswordInput.Focus();
    }
}
