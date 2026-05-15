using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmberNotes.ViewModels;

/// <summary>
/// Top-level application ViewModel that owns the root navigation state.
///
/// App always starts in Public mode (no LoginView at startup).
/// LoginView is shown full-screen ONLY when the user explicitly taps "🔒 Приватний".
///
/// MainWindow / AppView binds its ContentControl.Content to CurrentViewModel;
/// Avalonia's ViewLocator (Application.DataTemplates) resolves VM → View automatically.
/// </summary>
public partial class AppViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentViewModel = null!;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the current screen with the main notes dashboard.
    /// Returns the created MainViewModel so App.axaml.cs can subscribe to its events.
    /// </summary>
    public MainViewModel SwitchToMain(
        NoteRepository     publicNoteRepo,
        BookRepository     publicBookRepo,
        DatabaseService    privateDbService,
        CryptoService      cryptoSvc,
        GoogleDriveService driveService,
        SaltSyncService    saltSyncService)
    {
        var mainVm = new MainViewModel(
            publicNoteRepo, publicBookRepo,
            privateDbService, cryptoSvc, driveService, saltSyncService);

        CurrentViewModel = mainVm;
        return mainVm;
    }

    /// <summary>
    /// Shows the LoginView full-screen to unlock/create the private vault.
    /// On success  → sets private repos on mainVm, switches to Private mode, restores mainVm.
    /// On cancel   → restores mainVm (stays in Public mode, no mode change).
    /// </summary>
    public void ShowPrivateLogin(
        CryptoService   cryptoSvc,
        DatabaseService privateDbService,
        MainViewModel   mainVm)
    {
        var loginVm = new LoginViewModel(cryptoSvc, privateDbService)
        {
            CanCancel = true
        };

        loginVm.LoginSucceeded += () =>
        {
            // Build private repositories now that the vault is open
            var privateNoteRepo = new NoteRepository(privateDbService);
            var privateBookRepo = new BookRepository(privateDbService);

            // Give repos to MainViewModel and activate Private mode
            mainVm.SetPrivateRepos(privateNoteRepo, privateBookRepo);
            ModeService.Instance.SetMode(AppMode.Private);

            // Return to the main dashboard
            CurrentViewModel = mainVm;
        };

        loginVm.LoginCancelled += () =>
        {
            // User cancelled — just go back, stay in Public mode
            CurrentViewModel = mainVm;
        };

        CurrentViewModel = loginVm;
    }
}
