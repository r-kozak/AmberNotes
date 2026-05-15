using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// Overlay ViewModel for "Amber Cloud Explorer" — the read-only audit panel
/// that shows every encrypted "brick" of the user's data in Google Drive appDataFolder,
/// cross-referenced with the local database.
///
/// Shown inside SettingsView as CurrentOverlay.
/// Algorithm: Full Outer Join (cloud ↔ live local notes ↔ deleted local notes).
///
/// Status assignment rules:
///   🟣 SystemFile    — ambernotes.salt present in cloud
///   🟢 Synced        — cloud file + live local note (UUID matches, is_deleted = false)
///   🔵 CloudOnly     — cloud file found, UUID absent from local DB
///   🟡 LocalOnly     — live local note, no cloud file (or local-only salt)
///   🔴 PendingDelete — note is_deleted=true locally, cloud file modifiedTime &lt; local UpdatedAt
///   🪦 Tombstone     — note is_deleted=true locally, cloud file modifiedTime >= local UpdatedAt
///
///   Deleted local notes that have NO cloud presence are NOT displayed (per spec).
/// </summary>
public partial class CloudExplorerViewModel : ViewModelBase
{
    private readonly GoogleDriveService _driveService;
    private readonly NoteRepository     _publicNoteRepo;
    private readonly DatabaseService    _privateDb;
    private readonly CryptoService      _cryptoSvc;
    private readonly Action             _close;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action? Closed;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCloudCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasItems;

    /// <summary>Number of files that actually exist in Google Drive (LocalOnly excluded).</summary>
    [ObservableProperty]
    private int _totalFilesCount;

    /// <summary>Total size of cloud files formatted as KB / MB.</summary>
    [ObservableProperty]
    private string _totalCloudSize = "—";

    /// <summary>One-line status breakdown: "🟢 5  🔵 1  🟡 2  🔴 0  🪦 3"</summary>
    [ObservableProperty]
    private string _statusSummary = "";

    /// <summary>The flat list bound to the UI ListBox.</summary>
    public ObservableCollection<CloudFileItem> CloudItems { get; } = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public CloudExplorerViewModel(
        GoogleDriveService driveService,
        NoteRepository     publicNoteRepo,
        DatabaseService    privateDb,
        CryptoService      cryptoSvc,
        Action             close)
    {
        _driveService   = driveService;
        _publicNoteRepo = publicNoteRepo;
        _privateDb      = privateDb;
        _cryptoSvc      = cryptoSvc;
        _close          = close;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Close()
    {
        Closed?.Invoke();
        _close();
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RefreshCloudAsync(CancellationToken ct)
    {
        IsBusy       = true;
        HasError     = false;
        HasItems     = false;
        ErrorMessage = "";
        CloudItems.Clear();
        TotalCloudSize  = "—";
        TotalFilesCount = 0;
        StatusSummary   = "";

        try
        {
            // ── Network: fetch cloud inventory (proper async, no Task.Run needed) ──
            var cloudFiles = await _driveService.ListAllFilesWithDetailsAsync(ct);
            System.Diagnostics.Debug.WriteLine(
                $"[CloudExplorer] RefreshCloud: {cloudFiles.Count} files from Drive.");

            // ── CPU-bound: local DB queries + Full Outer Join ───────────────────
            var items = await Task.Run(() => BuildItems(cloudFiles), ct);

            // ── UI update on dispatcher thread ─────────────────────────────────
            foreach (var item in items)
                CloudItems.Add(item);

            HasItems = CloudItems.Count > 0;

            // Stats: only files that physically exist in cloud
            var cloudPresent = items.Where(i => i.Status != CloudItemStatus.LocalOnly).ToList();
            TotalFilesCount  = cloudPresent.Count;
            TotalCloudSize   = FormatBytes(cloudPresent.Sum(i => i.SizeBytes));

            int synced    = items.Count(i => i.Status == CloudItemStatus.Synced);
            int cloudOnly = items.Count(i => i.Status == CloudItemStatus.CloudOnly);
            int localOnly = items.Count(i => i.Status == CloudItemStatus.LocalOnly);
            int pending   = items.Count(i => i.Status == CloudItemStatus.PendingDelete);
            int tombstone = items.Count(i => i.Status == CloudItemStatus.Tombstone);

            StatusSummary = $"🟢 {synced}  🔵 {cloudOnly}  🟡 {localOnly}  🔴 {pending}  🪦 {tombstone}";
            System.Diagnostics.Debug.WriteLine($"[CloudExplorer] Summary: {StatusSummary}");
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Операцію скасовано.";
            HasError     = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudExplorer] ERROR: {ex}");
            ErrorMessage = $"Помилка під час отримання даних: {ex.Message}";
            HasError     = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Aggregation algorithm ─────────────────────────────────────────────────

    /// <summary>
    /// Full Outer Join between cloud inventory and local notes.
    /// Runs on a background thread (Task.Run) — safe for synchronous SQLite calls.
    /// </summary>
    private List<CloudFileItem> BuildItems(List<CloudFileInfo> cloudFiles)
    {
        var result = new List<CloudFileItem>();

        // ── 1. Local data ──────────────────────────────────────────────────────

        // Live notes (is_deleted = false) from both repos
        var livePublic  = _publicNoteRepo.GetAll();
        var livePrivate = _privateDb.IsUnlocked
            ? new NoteRepository(_privateDb).GetAll()
            : new List<Note>();
        var allLive  = livePublic.Concat(livePrivate).ToList();
        var liveById = allLive.ToDictionary(n => n.Id);

        System.Diagnostics.Debug.WriteLine(
            $"[CloudExplorer] Local live notes: {allLive.Count} (public={livePublic.Count}, private={livePrivate.Count})");

        // Deleted notes (is_deleted = true)
        var delPublic  = _publicNoteRepo.GetAllIncludingDeleted().Where(n => n.IsDeleted).ToList();
        var delPrivate = _privateDb.IsUnlocked
            ? new NoteRepository(_privateDb).GetAllIncludingDeleted().Where(n => n.IsDeleted).ToList()
            : new List<Note>();
        var allDeleted  = delPublic.Concat(delPrivate).ToList();
        var deletedById = allDeleted.ToDictionary(n => n.Id);

        System.Diagnostics.Debug.WriteLine(
            $"[CloudExplorer] Local deleted notes: {allDeleted.Count}");

        // Local salt existence check
        bool localSaltExists = _cryptoSvc.GetSaltBytes() is not null;

        // ── 2. Track which note UUIDs we've seen in the cloud ─────────────────
        var cloudNoteIds = new HashSet<string>(StringComparer.Ordinal);
        bool cloudHasSalt = cloudFiles.Any(f => f.IsSalt);

        // ── 3. Process every cloud file ───────────────────────────────────────
        foreach (var cf in cloudFiles)
        {
            if (cf.IsSalt)
            {
                // Salt in cloud → always SystemFile
                result.Add(new CloudFileItem
                {
                    FileName      = "Криптографічний якір",
                    SizeFormatted = FormatBytes(cf.SizeBytes),
                    SizeBytes     = cf.SizeBytes,
                    ModifiedDate  = cf.ModifiedTime,
                    Status        = CloudItemStatus.SystemFile
                });
                System.Diagnostics.Debug.WriteLine(
                    $"[CloudExplorer] Salt in cloud: {cf.SizeBytes} bytes, modified {cf.ModifiedTime:O}");
                continue;
            }

            if (cf.NoteId is null)
            {
                // Unrecognized file (not a note, not salt) — skip silently
                System.Diagnostics.Debug.WriteLine(
                    $"[CloudExplorer] Skipping unrecognized cloud file: {cf.FileName}");
                continue;
            }

            cloudNoteIds.Add(cf.NoteId);

            if (liveById.TryGetValue(cf.NoteId, out var liveNote))
            {
                // Cloud file + live local note → Synced
                result.Add(new CloudFileItem
                {
                    FileName      = BuildNoteName(liveNote.Title, cf.NoteId),
                    SizeFormatted = FormatBytes(cf.SizeBytes),
                    SizeBytes     = cf.SizeBytes,
                    ModifiedDate  = cf.ModifiedTime,
                    Status        = CloudItemStatus.Synced
                });
            }
            else if (deletedById.TryGetValue(cf.NoteId, out var deletedNote))
            {
                // Cloud file + deleted local note → compare timestamps to decide status
                // If cloud is OLDER than local deletion → tombstone not pushed yet (PendingDelete)
                // If cloud is NEWER (or equal) → cloud already has the tombstone
                var status = cf.ModifiedTime < deletedNote.UpdatedAt
                    ? CloudItemStatus.PendingDelete
                    : CloudItemStatus.Tombstone;

                System.Diagnostics.Debug.WriteLine(
                    $"[CloudExplorer] Note {cf.NoteId}: cloud={cf.ModifiedTime:O}, " +
                    $"localDeletion={deletedNote.UpdatedAt:O} → {status}");

                result.Add(new CloudFileItem
                {
                    FileName      = BuildNoteName(deletedNote.Title, cf.NoteId),
                    SizeFormatted = FormatBytes(cf.SizeBytes),
                    SizeBytes     = cf.SizeBytes,
                    ModifiedDate  = cf.ModifiedTime,
                    Status        = status
                });
            }
            else
            {
                // Cloud file, but UUID not in local DB at all → CloudOnly (awaiting Pull)
                result.Add(new CloudFileItem
                {
                    FileName      = $"Нотатка [{ShortId(cf.NoteId)}]",
                    SizeFormatted = FormatBytes(cf.SizeBytes),
                    SizeBytes     = cf.SizeBytes,
                    ModifiedDate  = cf.ModifiedTime,
                    Status        = CloudItemStatus.CloudOnly
                });
                System.Diagnostics.Debug.WriteLine(
                    $"[CloudExplorer] CloudOnly note UUID: {cf.NoteId}");
            }
        }

        // ── 4. LocalOnly: salt exists locally but NOT in cloud ─────────────────
        // Per spec: "якщо ambernotes.salt в хмарі відсутня, то вона LocalOnly"
        if (!cloudHasSalt && localSaltExists)
        {
            result.Insert(0, new CloudFileItem
            {
                FileName      = "Криптографічний якір",
                SizeFormatted = "-",
                SizeBytes     = 0,
                ModifiedDate  = DateTime.UtcNow,
                Status        = CloudItemStatus.LocalOnly
            });
            System.Diagnostics.Debug.WriteLine("[CloudExplorer] Salt: LocalOnly (not yet in cloud)");
        }

        // ── 5. LocalOnly: live local notes with no cloud counterpart ──────────
        foreach (var note in allLive)
        {
            if (!cloudNoteIds.Contains(note.Id))
            {
                result.Add(new CloudFileItem
                {
                    FileName      = BuildNoteName(note.Title, note.Id),
                    SizeFormatted = "-",
                    SizeBytes     = 0,
                    ModifiedDate  = note.UpdatedAt,
                    Status        = CloudItemStatus.LocalOnly
                });
                System.Diagnostics.Debug.WriteLine(
                    $"[CloudExplorer] LocalOnly live note: {note.Id}");
            }
        }

        // ── 6. Deleted local notes with NO cloud presence → DO NOT SHOW (per spec) ──

        // ── 7. Sort: SystemFile → LocalOnly → Synced → CloudOnly → PendingDelete → Tombstone ──
        result = result
            .OrderBy(i => StatusOrder(i.Status))
            .ThenByDescending(i => i.ModifiedDate)
            .ToList();

        System.Diagnostics.Debug.WriteLine(
            $"[CloudExplorer] BuildItems complete: {result.Count} rows total.");

        return result;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static int StatusOrder(CloudItemStatus s) => s switch
    {
        CloudItemStatus.SystemFile    => 0,
        CloudItemStatus.LocalOnly     => 1,
        CloudItemStatus.Synced        => 2,
        CloudItemStatus.CloudOnly     => 3,
        CloudItemStatus.PendingDelete => 4,
        CloudItemStatus.Tombstone     => 5,
        _                             => 9
    };

    private static string BuildNoteName(string title, string noteId) =>
        string.IsNullOrWhiteSpace(title)
            ? $"Нотатка [{ShortId(noteId)}]"
            : title;

    private static string ShortId(string id) =>
        id.Length >= 8 ? $"{id[..8]}…" : id;

    private static string FormatBytes(long bytes) => bytes switch
    {
        0                 => "—",
        < 1024            => $"{bytes} Б",
        < 1024 * 1024     => $"{bytes / 1024.0:F1} КБ",
        _                 => $"{bytes / (1024.0 * 1024.0):F2} МБ"
    };
}
