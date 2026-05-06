using System;
using System.Threading.Tasks;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// Handles the App Lock screen.
///
/// First run  (IsFirstRun = true):  prompts the user to create a master password.
/// Subsequent (IsFirstRun = false): requires the correct master password to unlock.
///
/// SECURITY rules:
///   - The plaintext password is kept in memory only during SubmitAsync and wiped immediately after.
///   - The derived hex key is never stored in this ViewModel; it is forwarded to DatabaseService
///     and then discarded from this scope.
///   - Nothing related to the password or key is ever written to logs.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly CryptoService   _cryptoService;
    private readonly DatabaseService _databaseService;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string  _password        = string.Empty;

    [ObservableProperty]
    private string  _confirmPassword = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool    _isBusy;

    /// <summary>
    /// True on the very first launch (no salt file → no vault yet).
    /// The View binds to this to show/hide the "Confirm password" field.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstRun;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when authentication succeeds and the vault is open.
    /// Listeners (App.axaml.cs) should build repositories and switch to MainViewModel.
    /// </summary>
    public event Action? LoginSucceeded;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LoginViewModel(CryptoService cryptoService, DatabaseService databaseService)
    {
        _cryptoService   = cryptoService;
        _databaseService = databaseService;

        // Snapshot at startup: salt file existence determines which UI mode to show
        _isFirstRun = cryptoService.IsFirstRun;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = IsFirstRun
                ? "Придумайте майстер-пароль для захисту вашого сховища."
                : "Введіть пароль для розблокування сховища.";
            return;
        }

        if (IsFirstRun && Password != ConfirmPassword)
        {
            ErrorMessage = "Паролі не збігаються. Спробуйте ще раз.";
            return;
        }

        IsBusy = true;
        try
        {
            // ── v0.2 → v0.3 migration guard ───────────────────────────────────
            // On first run, an old unencrypted database might exist from v0.2.
            // SQLCipher cannot open it → delete it to start fresh with encryption.
            if (IsFirstRun && !_databaseService.IsNewDatabase)
                _databaseService.DeleteDatabaseFile();

            // PBKDF2 is CPU-heavy (256k iterations) — run off the UI thread
            var hexKey = await Task.Run(() => _cryptoService.DeriveKey(Password));

            if (!_databaseService.TryUnlockWithKey(hexKey))
            {
                ErrorMessage = IsFirstRun
                    ? "Не вдалося створити сховище. Спробуйте ще раз."
                    : "Невірний пароль. Спробуйте ще раз.";
                return;
            }

            // Vault is open → create schema (idempotent)
            _databaseService.Initialize();

            // Notify listeners (App.axaml.cs) to build repos and switch view
            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Помилка авторизації: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            // Wipe plaintext password from memory as soon as possible
            Password        = string.Empty;
            ConfirmPassword = string.Empty;
        }
    }
}
