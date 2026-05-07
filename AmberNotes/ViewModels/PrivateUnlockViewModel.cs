using System;
using System.Threading.Tasks;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// Drives the in-window "unlock private vault" overlay (Step 17 — Security Bridge).
///
/// Shown when the user switches to Private mode but the private database has not yet
/// been opened in this session.  Accepts the master password, runs PBKDF2 off the UI
/// thread, and on success opens the private SQLCipher DB and raises UnlockSucceeded.
///
/// The plaintext password is wiped from memory in the finally block.
/// </summary>
public partial class PrivateUnlockViewModel : ViewModelBase
{
    private readonly DatabaseService _privateDbService;
    private readonly CryptoService   _cryptoSvc;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string  _password     = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool    _isBusy;

    // ── Computed helpers ──────────────────────────────────────────────────────

    public bool HasError  => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsNotBusy => !IsBusy;

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsBusyChanged(bool value)           => OnPropertyChanged(nameof(IsNotBusy));

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the user enters the correct password and the private vault is open.
    /// Subscribers (MainViewModel) should store the provided repos and switch mode.
    /// </summary>
    public event Action<NoteRepository, BookRepository>? UnlockSucceeded;

    /// <summary>Raised when the user presses Cancel — close the overlay without switching mode.</summary>
    public event Action? Cancelled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public PrivateUnlockViewModel(DatabaseService privateDbService, CryptoService cryptoSvc)
    {
        _privateDbService = privateDbService;
        _cryptoSvc        = cryptoSvc;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Введіть майстер-пароль для доступу до приватного сховища.";
            return;
        }

        IsBusy = true;
        try
        {
            // PBKDF2 — CPU-heavy, run off the UI thread
            var hexKey = await Task.Run(() => _cryptoSvc.DeriveKey(Password));

            if (!_privateDbService.TryUnlockWithKey(hexKey))
            {
                ErrorMessage = "Невірний пароль. Спробуйте ще раз.";
                return;
            }

            // Ensure private schema exists (idempotent)
            _privateDbService.Initialize();

            var noteRepo = new NoteRepository(_privateDbService);
            var bookRepo = new BookRepository(_privateDbService);

            UnlockSucceeded?.Invoke(noteRepo, bookRepo);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Помилка розблокування: {ex.Message}";
        }
        finally
        {
            IsBusy    = false;
            Password  = string.Empty; // wipe from memory
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
