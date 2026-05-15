using System;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// ViewModel for the Salt Conflict resolution overlay.
///
/// Shown as a sub-page inside SettingsView (ContentControl bound to ConflictViewModel)
/// when DetectConflictAsync() returns a SaltConflictInfo.
///
/// Three options displayed as selectable cards:
///   1 – UseCloudPassword   : replace local key with cloud key (enter cloud password)
///   2 – KeepLocalPassword  : keep local key, overwrite cloud salt
///   3 – HardReset          : delete all cloud data, re-upload local salt (red/danger)
///
/// On resolution → fires Resolved(choice, cloudPassword).
/// On cancel     → fires Cancelled.
/// </summary>
public partial class SaltConflictViewModel : ViewModelBase
{
    private readonly SaltSyncService  _saltSync;
    private readonly SaltConflictInfo _conflict;

    /// <summary>
    /// When true — local vault is empty; only Option 1 (cloud password) is shown.
    /// Options 2 and 3 are hidden, header text is simplified.
    /// </summary>
    public bool IsEmptyVaultMode => _conflict.IsLocalEmpty;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when user confirms a resolution choice.</summary>
    public event Action? Resolved;

    /// <summary>Fired when user taps Cancel.</summary>
    public event Action? Cancelled;

    // ── UI state ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasStatusMessage;

    // ── Selected option ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOption1Selected))]
    [NotifyPropertyChangedFor(nameof(IsOption2Selected))]
    [NotifyPropertyChangedFor(nameof(IsOption3Selected))]
    [NotifyPropertyChangedFor(nameof(ShowPasswordField))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private int _selectedOption = 1;   // default: UseCloudPassword

    public bool IsOption1Selected => SelectedOption == 1;
    public bool IsOption2Selected => SelectedOption == 2;
    public bool IsOption3Selected => SelectedOption == 3;

    /// <summary>Password field appears for options 1 and 2 (cloud password needed).</summary>
    public bool ShowPasswordField => SelectedOption == 1 || SelectedOption == 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _cloudPassword = "";

    // ── Constructor ───────────────────────────────────────────────────────────

    public SaltConflictViewModel(SaltSyncService saltSync, SaltConflictInfo conflict)
    {
        _saltSync = saltSync;
        _conflict = conflict;
    }

    // ── Commands – option selection ───────────────────────────────────────────

    [RelayCommand]
    private void SelectOption1() => SelectedOption = 1;

    [RelayCommand]
    private void SelectOption2() => SelectedOption = 2;

    [RelayCommand]
    private void SelectOption3() => SelectedOption = 3;

    // ── Command – confirm ─────────────────────────────────────────────────────

    private bool CanConfirm()
    {
        if (IsBusy) return false;
        // Options 1 and 2 require a non-empty cloud password
        if ((SelectedOption == 1 || SelectedOption == 2) && string.IsNullOrWhiteSpace(CloudPassword)) return false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken ct)
    {
        IsBusy = true;
        HideStatus();

        try
        {
            var choice = SelectedOption switch
            {
                1 => SaltConflictChoice.UseCloudPassword,
                2 => SaltConflictChoice.KeepLocalPassword,
                _ => SaltConflictChoice.HardReset
            };

            // Options 1 and 2 both require the cloud password
            string? pwd = (SelectedOption == 1 || SelectedOption == 2) ? CloudPassword : null;

            await _saltSync.ResolveConflictAsync(choice, _conflict.CloudSalt, pwd, ct);

            Resolved?.Invoke();
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

    // ── Command – cancel ──────────────────────────────────────────────────────

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

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
