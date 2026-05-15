using System;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
///
/// Navigation: MainViewModel sets CurrentPage = SettingsViewModel.
/// The "← Назад" button calls the _goBack callback → CurrentPage = listVm.
///
/// Responsibilities:
///   • Connect / disconnect Google Drive.
///   • After connect: detect salt conflict and show SaltConflictViewModel overlay.
///   • Display connection status + connected email.
///   • Test the Drive appDataFolder connection.
///   • Sync Now — encrypted two-way sync with Google Drive.
///   • Show setup hints when Client IDs are not yet configured.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly GoogleDriveService _driveService;
    private readonly SaltSyncService    _saltSync;
    private readonly SyncService        _syncService;
    private readonly DatabaseService    _privateDb;
    private readonly CryptoService      _cryptoSvc;
    private readonly AppSettingsService _appSettings;
    private readonly NoteRepository     _publicNoteRepo;
    private readonly Action             _goBack;

    // ── Status state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private string? _googleEmail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ToggleGoogleDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _showSetupHint;

    // ── Sync password (shown when private vault is locked and user clicks Sync Now) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSyncPasswordField))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private bool _needsSyncPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private string _syncPassword = "";

    /// <summary>True when vault is locked — user must enter password to sync.</summary>
    public bool ShowSyncPasswordField => NeedsSyncPassword;

    // ── Overlays ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Salt conflict resolution overlay (shown after connecting Drive when salts differ).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOverlay))]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    private SaltConflictViewModel? _conflictViewModel;

    /// <summary>Change master password overlay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOverlay))]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    private ChangePasswordViewModel? _changePasswordViewModel;

    /// <summary>Cloud Explorer overlay (Amber Cloud Explorer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOverlay))]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    private CloudExplorerViewModel? _cloudExplorerViewModel;

    /// <summary>
    /// The currently active overlay (one at a time).
    /// Priority: ConflictViewModel → ChangePasswordViewModel → CloudExplorerViewModel.
    /// Bound to the ContentControl in SettingsView Layer 1.
    /// DataTemplates in the view resolve ViewModel → View.
    /// </summary>
    public ViewModelBase? CurrentOverlay =>
        (ViewModelBase?)ConflictViewModel
        ?? (ViewModelBase?)ChangePasswordViewModel
        ?? (ViewModelBase?)CloudExplorerViewModel;

    public bool IsOverlayVisible => CurrentOverlay is not null;

    // Keep for backward compat — used in the existing overlay grid IsVisible binding
    public bool IsConflictVisible => IsOverlayVisible;

    // ── Computed ──────────────────────────────────────────────────────────────

    public bool   IsNotBusy        => !IsBusy;
    public string ConnectButtonText => IsGoogleConnected ? "Відключити" : "Підключити Google Drive";
    public string StatusText        => IsGoogleConnected ? "Підключено"  : "Не підключено";
    public string StatusIcon        => IsGoogleConnected ? "✅"          : "☁";

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsViewModel(
        GoogleDriveService driveService,
        SaltSyncService    saltSync,
        SyncService        syncService,
        DatabaseService    privateDb,
        CryptoService      cryptoSvc,
        AppSettingsService appSettings,
        NoteRepository     publicNoteRepo,
        Action             goBack)
    {
        _driveService   = driveService;
        _saltSync       = saltSync;
        _syncService    = syncService;
        _privateDb      = privateDb;
        _cryptoSvc      = cryptoSvc;
        _appSettings    = appSettings;
        _publicNoteRepo = publicNoteRepo;
        _goBack         = goBack;

        ShowSetupHint = !GoogleAuthConfig.IsCurrentPlatformConfigured;
        RefreshConnectionState();

        // Determine if password is needed for sync at startup
        NeedsSyncPassword = !_privateDb.IsUnlocked;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ToggleGoogleDriveAsync(CancellationToken ct)
    {
        if (IsGoogleConnected)
        {
            await _driveService.DisconnectAsync();
            RefreshConnectionState();
            ShowStatus("Google Drive відключено.");
            return;
        }

        if (!GoogleAuthConfig.IsCurrentPlatformConfigured)
        {
            ShowStatus("Спочатку налаштуйте OAuth Client ID у GoogleAuthConfig.cs " +
                       "та Google Cloud Console (інструкція в картці нижче).");
            return;
        }

        IsBusy = true;
        ShowStatus("Відкриваємо браузер для авторизації Google...");

        try
        {
            var success = await _driveService.ConnectAsync(ct);
            RefreshConnectionState();

            if (!success)
            {
                ShowStatus("Авторизацію скасовано або виникла помилка. Спробуйте ще раз.");
                return;
            }

            ShowStatus($"✅ Підключено як {_driveService.ConnectedEmail}. Перевірка ключа шифрування...");

            // ── Salt conflict detection ───────────────────────────────────────
            var conflict = await _saltSync.DetectConflictAsync(ct);

            if (conflict is null)
            {
                ShowStatus($"✅ Підключено як {_driveService.ConnectedEmail}. Ключ шифрування синхронізовано.");
                return;
            }

            // Show conflict dialog overlay.
            // When IsLocalEmpty=true, the dialog shows only Option 1 (password from cloud).
            // When IsLocalEmpty=false, shows all 3 options.
            ShowStatus("");
            ShowConflictDialog(conflict);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Авторизацію скасовано.");
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

    [RelayCommand(CanExecute = nameof(IsGoogleConnected))]
    private async Task TestConnectionAsync(CancellationToken ct)
    {
        IsBusy = true;
        ShowStatus("Перевірка з'єднання з Google Drive...");

        try
        {
            var ok = await _driveService.TestConnectionAsync(ct);
            ShowStatus(ok
                ? "✅ З'єднання успішне! AppData папка застосунку доступна."
                : "❌ З'єднання не вдалося. Можливо, токен прострочено — спробуйте підключитися знову.");
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

    // ── Sync Now command ──────────────────────────────────────────────────────

    private bool CanSyncNow() =>
        IsGoogleConnected && IsNotBusy &&
        (!NeedsSyncPassword || !string.IsNullOrWhiteSpace(SyncPassword));

    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync(CancellationToken ct)
    {
        IsBusy = true;
        ShowStatus("Підготовка до синхронізації...");

        try
        {
            // Get hex key: use current session key if vault is unlocked,
            // otherwise derive it from the entered sync password.
            string? hexKey = _privateDb.CurrentHexKey;

            if (hexKey is null && NeedsSyncPassword)
            {
                ShowStatus("Надійне шифрування бази...");
                hexKey = await Task.Run(
                    () => _cryptoSvc.DeriveKey(SyncPassword), ct);

                // Attempt to unlock the private vault with derived key (verify password)
                if (!_privateDb.IsNewDatabase && !_privateDb.TryUnlockWithKey(hexKey))
                {
                    ShowStatus("❌ Невірний пароль. Перевірте та спробуйте знову.");
                    return;
                }

                // Password validated → no longer need the password field
                NeedsSyncPassword = false;
                SyncPassword      = "";
            }

            if (hexKey is null)
            {
                ShowStatus("❌ Будь ласка, відкрийте Приватне сховище або введіть пароль.");
                return;
            }

            ShowStatus("Безпечне вивантаження в хмару...");
            var result = await _syncService.SyncAsync(hexKey, ct);
            ShowStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Синхронізацію скасовано.");
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

    // ── Cloud Explorer command ────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsGoogleConnected))]
    private void OpenCloudExplorer()
    {
        var explorerVm = new CloudExplorerViewModel(
            _driveService,
            _publicNoteRepo,
            _privateDb,
            _cryptoSvc,
            () => CloudExplorerViewModel = null);

        explorerVm.Closed += () => CloudExplorerViewModel = null;

        CloudExplorerViewModel = explorerVm;
    }

    // ── Change Password command ───────────────────────────────────────────────

    [RelayCommand]
    private void OpenChangePassword()
    {
        var changePwdVm = new ChangePasswordViewModel(
            _privateDb, _cryptoSvc, _syncService,
            _appSettings, _driveService);

        changePwdVm.Completed += () =>
        {
            ChangePasswordViewModel = null;
            ShowStatus("✅ Пароль успішно змінено! Наступна синхронізація перезапише хмару новим ключем.");
        };

        changePwdVm.Cancelled += () =>
        {
            ChangePasswordViewModel = null;
        };

        ChangePasswordViewModel = changePwdVm;
    }

    // ── Conflict dialog helper ────────────────────────────────────────────────

    private void ShowConflictDialog(SaltConflictInfo conflict)
    {
        var conflictVm = new SaltConflictViewModel(_saltSync, conflict);

        conflictVm.Resolved += () =>
        {
            ConflictViewModel = null;
            ShowStatus($"✅ Конфлікт вирішено. Підключено як {_driveService.ConnectedEmail}.");
        };

        conflictVm.Cancelled += () =>
        {
            ConflictViewModel = null;
            ShowStatus("Вирішення конфлікту скасовано. Синхронізація не виконана.");
        };

        ConflictViewModel = conflictVm;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshConnectionState()
    {
        IsGoogleConnected = _driveService.IsConnected;
        GoogleEmail       = _driveService.ConnectedEmail;
    }

    private void ShowStatus(string message)
    {
        StatusMessage    = message;
        HasStatusMessage = !string.IsNullOrEmpty(message);
    }
}
