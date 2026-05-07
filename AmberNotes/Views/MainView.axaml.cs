using Avalonia.Controls;

namespace AmberNotes.Views;

/// <summary>
/// Code-behind for MainView.
/// All navigation logic lives in MainViewModel (CurrentPage).
/// This file is intentionally minimal — pure MVVM, no code-behind logic.
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }
}
