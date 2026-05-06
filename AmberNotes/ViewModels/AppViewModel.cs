using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmberNotes.ViewModels;

/// <summary>
/// Top-level application ViewModel that owns the root navigation state.
///
/// On startup, CurrentViewModel = LoginViewModel (App Lock screen).
/// After successful authentication, SwitchToMain() replaces it with MainViewModel.
///
/// MainWindow.axaml binds its ContentControl.Content to CurrentViewModel;
/// Avalonia's ViewLocator (Application.DataTemplates) maps the VM to the correct View.
/// </summary>
public partial class AppViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    public AppViewModel(LoginViewModel loginViewModel)
    {
        _currentViewModel = loginViewModel;
    }

    /// <summary>
    /// Called by App.axaml.cs after LoginViewModel.LoginSucceeded fires.
    /// Replaces the lock screen with the main notes dashboard.
    /// </summary>
    public void SwitchToMain(NoteRepository noteRepo, BookRepository bookRepo)
    {
        CurrentViewModel = new MainViewModel(noteRepo, bookRepo);
    }
}
