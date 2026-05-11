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
///   • Display connection status + connected email.
///   • Test the Drive appDataFolder connection.
///   • Show setup hints when Client IDs are not yet configured.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly GoogleDriveService _driveService;
    private readonly Action             _goBack;

    // ── Status state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private string? _googleEmail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ToggleGoogleDriveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _showSetupHint;

    // ── Computed ──────────────────────────────────────────────────────────────

    public bool   IsNotBusy       => !IsBusy;
    public string ConnectButtonText => IsGoogleConnected ? "Відключити" : "Підключити Google Drive";
    public string StatusText        => IsGoogleConnected ? "Підключено" : "Не підключено";
    public string StatusIcon        => IsGoogleConnected ? "✅" : "☁";

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsViewModel(GoogleDriveService driveService, Action goBack)
    {
        _driveService = driveService;
        _goBack       = goBack;

        ShowSetupHint = !GoogleAuthConfig.IsCurrentPlatformConfigured;
        RefreshConnectionState();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ToggleGoogleDriveAsync(CancellationToken ct)
    {
        if (IsGoogleConnected)
        {
            // Disconnect
            await _driveService.DisconnectAsync();
            RefreshConnectionState();
            ShowStatus("Google Drive відключено.");
            return;
        }

        // Connect
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

            ShowStatus(success
                ? $"✅ Підключено як {_driveService.ConnectedEmail}"
                : "Авторизацію скасовано або виникла помилка. Спробуйте ще раз.");
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
