using System;
using System.Threading;
using System.Threading.Tasks;

namespace AmberNotes.Services;

/// <summary>
/// Describes a detected salt conflict: local salt vs cloud salt differ.
/// </summary>
public sealed class SaltConflictInfo
{
    /// <summary>The salt bytes that exist in the cloud.</summary>
    public byte[] CloudSalt { get; init; } = [];

    /// <summary>
    /// True when the local private DB has no content yet (fresh install or empty vault).
    /// In this case we should auto-accept the cloud key (Option 1) without showing a dialog.
    /// </summary>
    public bool IsLocalEmpty { get; init; }
}

/// <summary>
/// The user's chosen resolution when a salt conflict is detected.
/// </summary>
public enum SaltConflictChoice
{
    /// <summary>Replace local key with the cloud key (use cloud password).</summary>
    UseCloudPassword,

    /// <summary>Keep local key and overwrite cloud salt (keep local password).</summary>
    KeepLocalPassword,

    /// <summary>Delete all cloud notes + cloud salt, then re-upload local salt.</summary>
    HardReset
}

/// <summary>
/// Handles all interactions between the local salt (crypto anchor) and Google Drive.
///
/// Flow on first connect:
///   1. No cloud salt  → upload local salt (done, no conflict)
///   2. Same salt      → nothing to do
///   3. Different salt → return SaltConflictInfo for the UI to present options
///
/// After the user resolves the conflict (SaltConflictChoice), call ResolveConflictAsync().
/// </summary>
public sealed class SaltSyncService
{
    private readonly CryptoService      _crypto;
    private readonly GoogleDriveService _drive;
    private readonly DatabaseService    _privateDb;
    private readonly AppSettingsService _settings;
    private readonly NoteRepository     _publicNoteRepo;

    public SaltSyncService(
        CryptoService      crypto,
        GoogleDriveService drive,
        DatabaseService    privateDb,
        AppSettingsService settings,
        NoteRepository     publicNoteRepo)
    {
        _crypto         = crypto;
        _drive          = drive;
        _privateDb      = privateDb;
        _settings       = settings;
        _publicNoteRepo = publicNoteRepo;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the local salt matches the cloud salt.
    ///
    /// Returns:
    ///   null              → no conflict (salt uploaded or already matched)
    ///   SaltConflictInfo  → conflict detected, UI should display resolution dialog
    /// </summary>
    public async Task<SaltConflictInfo?> DetectConflictAsync(CancellationToken ct = default)
    {
        // If a pending cloud wipe is flagged, skip conflict detection.
        // The wipe will happen during the next full sync (Step 28).
        if (_settings.PendingCloudWipe)
            return null;

        var localSalt = _crypto.GetSaltBytes();
        var cloudSalt = await _drive.DownloadSaltAsync(ct);

        // No cloud salt yet → first time connecting, just upload ours
        if (cloudSalt is null)
        {
            if (localSalt is not null)
                await _drive.UploadSaltAsync(localSalt, ct);
            return null;   // no conflict
        }

        // Salts are identical → no conflict
        if (localSalt is not null && SaltsEqual(localSalt, cloudSalt))
            return null;

        // Conflict: local and cloud salts differ
        // Check if local private DB is effectively empty (no notes at all)
        bool isLocalEmpty = localSalt is null || IsPrivateDbEmpty();

        return new SaltConflictInfo
        {
            CloudSalt    = cloudSalt,
            IsLocalEmpty = isLocalEmpty
        };
    }

    /// <summary>
    /// Executes the user's chosen conflict resolution.
    /// </summary>
    /// <param name="choice">What the user decided.</param>
    /// <param name="cloudPassword">
    ///   Required for UseCloudPassword when the private vault was already unlocked
    ///   with a different key — pass null to skip DB re-key (when vault is fresh/locked).
    ///   For KeepLocalPassword and HardReset this parameter is ignored.
    /// </param>
    /// <param name="cloudSalt">The cloud salt bytes from the detected conflict.</param>
    public async Task ResolveConflictAsync(
        SaltConflictChoice choice,
        byte[]             cloudSalt,
        string?            cloudPassword = null,
        CancellationToken  ct            = default)
    {
        switch (choice)
        {
            case SaltConflictChoice.UseCloudPassword:
                await ResolveUseCloudPasswordAsync(cloudSalt, cloudPassword, ct);
                break;

            case SaltConflictChoice.KeepLocalPassword:
                await ResolveKeepLocalPasswordAsync(ct);
                break;

            case SaltConflictChoice.HardReset:
                await ResolveHardResetAsync(ct);
                break;
        }
    }

    // ── Option 1: Use Cloud Password ──────────────────────────────────────────

    private async Task ResolveUseCloudPasswordAsync(
        byte[]            cloudSalt,
        string?           cloudPassword,
        CancellationToken ct)
    {
        // If vault is unlocked with a DIFFERENT key, re-key it
        if (cloudPassword is not null && _privateDb.IsUnlocked)
        {
            var newHexKey = _crypto.DeriveKeyFromSalt(cloudPassword, cloudSalt);
            _privateDb.Rekey(newHexKey);
        }

        // Replace local salt file with the cloud salt
        _crypto.ReplaceSaltFromBytes(cloudSalt);
    }

    // ── Option 2: Keep Local Password ────────────────────────────────────────

    private async Task ResolveKeepLocalPasswordAsync(CancellationToken ct)
    {
        var localSalt = _crypto.GetSaltBytes();
        if (localSalt is not null)
            await _drive.UploadSaltAsync(localSalt, ct);   // overwrite cloud salt
    }

    // ── Option 3: Hard Reset ─────────────────────────────────────────────────

    private async Task ResolveHardResetAsync(CancellationToken ct)
    {
        // Wipe all cloud-encrypted note files
        await _drive.DeleteAllEncryptedNotesAsync(ct);

        // Delete cloud salt
        await _drive.DeleteSaltAsync(ct);

        // Re-upload local salt (our salt is now the truth)
        var localSalt = _crypto.GetSaltBytes();
        if (localSalt is not null)
            await _drive.UploadSaltAsync(localSalt, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool SaltsEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private bool IsPrivateDbEmpty()
    {
        try
        {
            // If the private DB is not even unlocked, treat as "empty" for this check
            if (!_privateDb.IsUnlocked) return true;

            var privateNoteRepo = new NoteRepository(_privateDb);
            var notes = privateNoteRepo.GetAll();
            return notes.Count == 0;
        }
        catch
        {
            return true;
        }
    }
}
