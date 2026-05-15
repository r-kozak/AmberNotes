using System;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// Overlay ViewModel for the "Change Master Password" flow.
///
/// Shown inside SettingsView as CurrentOverlay (same layer as SaltConflictViewModel).
///
/// Flow:
///   1. User enters CurrentPassword, NewPassword, ConfirmNewPassword.
///   2. On confirm:
///      a. Validate inputs.
///      b. Verify current password matches DB key.
///      c. If Drive connected → pre-sync with old key ("Синхронізуємо...")
///         If Drive NOT connected → show inline offline warning.
///      d. GenerateNewSaltAndDeriveKey(newPassword) — writes new salt, derives new key.
///      e. PRAGMA rekey: Rekey(newHexKey).
///      f. If PendingCloudWipe was set → will auto-push on next SyncNow.
///      g. Show success message → fire Completed event.
///
/// Events: Completed, Cancelled.
/// </summary>
public partial class ChangePasswordViewModel : ViewModelBase
{
    private readonly DatabaseService    _privateDb;
    private readonly CryptoService      _crypto;
    private readonly SyncService        _syncService;
    private readonly AppSettingsService _settings;
    private readonly GoogleDriveService _driveService;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action? Completed;
    public event Action? Cancelled;

    // ── Password fields ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string _currentPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string _newPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string _confirmNewPassword = "";

    // ── UI state ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOfflineCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasStatusMessage;

    /// <summary>
    /// True = show inline offline-warning panel instead of password fields.
    /// User must confirm or cancel before proceeding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPasswordFields))]
    private bool _showOfflineWarning;

    /// <summary>Password fields are visible when offline warning is NOT showing and not done.</summary>
    public bool ShowPasswordFields => !ShowOfflineWarning && !_isDone;

    private bool _isDone;   // blocks double-fire of Completed

    // ── Vault state ───────────────────────────────────────────────────────────

    /// <summary>True if the private vault is not unlocked — change is not possible.</summary>
    public bool IsVaultLocked => _privateDb.CurrentHexKey is null;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChangePasswordViewModel(
        DatabaseService    privateDb,
        CryptoService      crypto,
        SyncService        syncService,
        AppSettingsService settings,
        GoogleDriveService driveService)
    {
        _privateDb    = privateDb;
        _crypto       = crypto;
        _syncService  = syncService;
        _settings     = settings;
        _driveService = driveService;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy) Cancelled?.Invoke();
    }

    private bool CanChangePassword() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(CurrentPassword) &&
        !string.IsNullOrWhiteSpace(NewPassword) &&
        !string.IsNullOrWhiteSpace(ConfirmNewPassword);

    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    private async Task ChangePasswordAsync(CancellationToken ct)
    {
        // Basic validation
        if (NewPassword != ConfirmNewPassword)
        {
            ShowStatus("❌ Новий пароль та підтвердження не збігаються.");
            return;
        }

        if (NewPassword.Length < 4)
        {
            ShowStatus("❌ Новий пароль занадто короткий (мінімум 4 символи).");
            return;
        }

        // Verify vault is accessible
        if (_privateDb.CurrentHexKey is null)
        {
            ShowStatus("❌ Спочатку відкрийте Приватне сховище (перейдіть у режим 🔒 Приватний).");
            return;
        }

        // Verify current password
        IsBusy = true;
        HideStatus();

        try
        {
            ShowStatus("Перевірка поточного пароля...");
            var currentHexKey = await Task.Run(
                () => _crypto.DeriveKey(CurrentPassword), ct);

            if (currentHexKey != _privateDb.CurrentHexKey)
            {
                ShowStatus("❌ Невірний поточний пароль. Спробуйте ще раз.");
                return;
            }

            // Pre-sync if connected
            if (_driveService.IsConnected)
            {
                ShowStatus("Синхронізуємо дані перед зміною пароля...");
                var syncResult = await _syncService.SyncAsync(currentHexKey, ct);
                if (!syncResult.Success)
                {
                    // Non-fatal: log and continue (data safety: continue even if sync fails)
                    ShowStatus($"⚠ Не вдалося синхронізувати ({syncResult.Message}). Продовжуємо зміну пароля...");
                    await Task.Delay(1500, ct);
                }
            }
            else
            {
                // Offline: show inline warning and stop here — user must confirm
                IsBusy = false;
                ShowOfflineWarning = true;
                return;
            }

            await PerformRekeyAsync(ct);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Скасовано.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Помилка: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// User confirmed proceeding with password change while offline.
    /// Sets PendingCloudWipe = true and performs local rekey.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ConfirmOfflineAsync(CancellationToken ct)
    {
        ShowOfflineWarning = false;
        IsBusy = true;
        HideStatus();

        try
        {
            _settings.PendingCloudWipe = true;
            await PerformRekeyAsync(ct);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Скасовано.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Помилка: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOffline() => ShowOfflineWarning = false;

    // ── Core rekey logic ──────────────────────────────────────────────────────

    private async Task PerformRekeyAsync(CancellationToken ct)
    {
        // 1. Generate new random salt, derive new hex key
        ShowStatus("Надійне шифрування бази...");
        var newHexKey = await Task.Run(
            () => _crypto.GenerateNewSaltAndDeriveKey(NewPassword), ct);

        // 2. Re-encrypt the private vault with the new key
        _privateDb.Rekey(newHexKey);

        // The Cloud Wipe & Reload will happen automatically on next SyncNow
        // (SyncService.CloudWipeAndPushAsync handles PendingCloudWipe flag)
        _settings.PendingCloudWipe = true;

        // Success
        _isDone = true;
        OnPropertyChanged(nameof(ShowPasswordFields));
        ShowStatus("✅ Пароль успішно змінено!\n\n" +
                   "Не забудьте ввести цей новий пароль на інших ваших пристроях, " +
                   "щоб вони могли продовжити синхронізацію.");

        // Clear sensitive fields
        CurrentPassword    = "";
        NewPassword        = "";
        ConfirmNewPassword = "";

        Completed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowStatus(string msg)
    {
        StatusMessage    = msg;
        HasStatusMessage = !string.IsNullOrEmpty(msg);
    }

    private void HideStatus()
    {
        StatusMessage    = "";
        HasStatusMessage = false;
    }
}
