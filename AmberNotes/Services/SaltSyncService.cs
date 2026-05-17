using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Models;

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
                await ResolveKeepLocalPasswordAsync(cloudPassword, cloudSalt, ct);
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
        string? cloudHexKey = null;

        // If the user provided the cloud password, derive the cloud key
        if (cloudPassword is not null)
            cloudHexKey = _crypto.DeriveKeyFromSalt(cloudPassword, cloudSalt);

        if (cloudHexKey is not null)
        {
            if (!_privateDb.IsUnlocked)
            {
                // Fresh/new vault: unlock with the cloud key and initialize schema+tables.
                // TryUnlockWithKey creates the DB file when it doesn't exist yet,
                // then Initialize() creates the Books/Notes tables.
                // After this, PullCloudNotesAsync can write private notes into the vault.
                if (_privateDb.TryUnlockWithKey(cloudHexKey))
                    _privateDb.Initialize();
            }
            else
            {
                // Vault was already unlocked with a different (local) key → re-encrypt with cloud key
                _privateDb.Rekey(cloudHexKey);
            }
        }

        // Replace local salt file with the cloud salt (local key = cloud key from now on)
        _crypto.ReplaceSaltFromBytes(cloudSalt);

        // Pull cloud notes encrypted with the cloud key → merge locally (LWW)
        // (ТЗ: "зроби злиття даних")
        if (cloudHexKey is not null)
            await PullCloudNotesAsync(cloudHexKey, ct);
    }

    // ── Option 2: Keep Local Password ────────────────────────────────────────

    private async Task ResolveKeepLocalPasswordAsync(
        string?           cloudPassword,
        byte[]            cloudSalt,
        CancellationToken ct)
    {
        // Step 1: Pull cloud notes with cloud key, merge locally (LWW)
        // (ТЗ: "розшифруй хмарні дані, злий їх локально (LWW)")
        if (cloudPassword is not null)
        {
            var cloudHexKey = _crypto.DeriveKeyFromSalt(cloudPassword, cloudSalt);
            await PullCloudNotesAsync(cloudHexKey, ct);
        }

        // Step 2: Overwrite cloud salt with local salt
        // (ТЗ: "перезапиши файл ambernotes.salt у хмарі своїм локальним")
        var localSalt = _crypto.GetSaltBytes();
        if (localSalt is not null)
            await _drive.UploadSaltAsync(localSalt, ct);
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

    // ── Password verification ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies the cloud password by trying to decrypt one of the cloud note files.
    ///
    /// Returns true  → password is correct (or no files to verify against, or only network errors).
    /// Returns false → AES-GCM tag mismatch / payload too short → definitely wrong password.
    ///
    /// IMPORTANT: Only <see cref="System.Security.Cryptography.CryptographicException"/> and
    /// <see cref="ArgumentException"/> are treated as "wrong password". Network errors and other
    /// transient failures are skipped (try next file) so a flaky connection never rejects a correct password.
    /// </summary>
    public async Task<bool> VerifyCloudPasswordAsync(
        byte[]            cloudSalt,
        string            password,
        CancellationToken ct = default)
    {
        var hexKey     = _crypto.DeriveKeyFromSalt(password, cloudSalt);
        var cloudFiles = await _drive.ListNoteFilesAsync(ct);

        if (cloudFiles.Count == 0)
            return true; // No note files to verify against — cannot disprove

        bool anyDownloadSucceeded = false;

        foreach (var (driveId, _) in cloudFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Use driveId directly — avoids redundant FindFileIdAsync lookup
                var encrypted = await _drive.DownloadNoteByDriveIdAsync(driveId, ct);
                if (encrypted is null) continue;

                anyDownloadSucceeded = true;

                // Throws CryptographicException on wrong key (AES-GCM authentication tag mismatch)
                _crypto.DecryptAesGcm(encrypted, hexKey);
                return true; // At least one file decrypted OK → correct password
            }
            catch (OperationCanceledException) { throw; }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // AES-GCM tag mismatch → key is definitely wrong
                return false;
            }
            catch (ArgumentException)
            {
                // Payload too short (< nonce + tag) → data corrupted or wrong key
                return false;
            }
            catch
            {
                // Network / IO / other transient error — skip this file and try the next one.
                // Do NOT return false here: a network hiccup must never reject a correct password.
            }
        }

        // Reached here when every file either failed to download (network error) or returned null.
        // If at least one file was downloaded but none could be decrypted (unexpected path),
        // be conservative and deny. Otherwise allow (all failures were network-only).
        return !anyDownloadSucceeded;
    }

    // ── Pull helper ───────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Downloads all cloud notes encrypted with <paramref name="hexKey"/>,
    /// decrypts them, and upserts into the local repository (LWW by UpdatedAt).
    /// Errors on individual notes are skipped (network resilience).
    /// </summary>
    private async Task PullCloudNotesAsync(string hexKey, CancellationToken ct)
    {
        try
        {
            var cloudFiles  = await _drive.ListNoteFilesAsync(ct);
            var privateRepo = _privateDb.IsUnlocked ? new NoteRepository(_privateDb) : null;

            foreach (var (driveId, _) in cloudFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Use driveId directly — avoids redundant FindFileIdAsync lookup
                    var encrypted = await _drive.DownloadNoteByDriveIdAsync(driveId, ct);
                    if (encrypted is null) continue;

                    var json  = _crypto.DecryptAesGcm(encrypted, hexKey);
                    var dto   = JsonSerializer.Deserialize<NoteCloudDto>(json, _jsonOpts);
                    if (dto is null) continue;

                    var note = new Note
                    {
                        Id           = dto.Id       ?? "",
                        Title        = dto.Title    ?? "",
                        Content      = dto.Content  ?? "",
                        NoteDateTime = ParseDate(dto.NoteDateTime),
                        CreatedAt    = ParseDate(dto.CreatedAt),
                        UpdatedAt    = ParseDate(dto.UpdatedAt),
                        Type         = dto.Type == "Private" ? NoteType.Private : NoteType.Public,
                        BookId       = dto.BookId   ?? "",
                        IsDeleted    = dto.IsDeleted
                    };

                    if (note.Type == NoteType.Private && privateRepo is not null)
                        privateRepo.Upsert(note);
                    else if (note.Type == NoteType.Public)
                        _publicNoteRepo.Upsert(note);
                }
                catch { /* skip individual note errors */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* skip if listing fails */ }
    }

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d : DateTime.UtcNow;

    // ── Other helpers ─────────────────────────────────────────────────────────

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
