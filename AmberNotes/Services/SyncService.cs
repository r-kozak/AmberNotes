using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AmberNotes.Models;

namespace AmberNotes.Services;

/// <summary>Summary of one sync operation.</summary>
public sealed record SyncResult(
    bool   Success,
    int    Pushed,
    int    Pulled,
    int    Errors,
    string Message);

/// <summary>
/// Orchestrates encrypted two-way sync between local SQLite databases and Google Drive appdata.
///
/// All notes (public AND private) are serialised to JSON and encrypted with AES-256-GCM
/// before upload. The cloud never stores plaintext.
///
/// Sync algorithm:
///   Normal mode (PendingCloudWipe = false):
///     1. Pull — fetch all .json.enc files from Drive,
///               decrypt each, Upsert locally (LWW by UpdatedAt).
///     2. Push — GetAllIncludingDeleted from both repos,
///               encrypt each, upload to Drive.
///
///   Cloud Wipe mode (PendingCloudWipe = true):
///     1. Skip Pull (old key is gone — cloud data unreadable).
///     2. DeleteAllEncryptedNotes + DeleteSalt from Drive.
///     3. Upload fresh salt.
///     4. Push all local notes with new key.
///     5. ONLY after all uploads succeed → PendingCloudWipe = false.
///
/// SHA note on BookId: SQLite FK constraints are disabled by default (no PRAGMA foreign_keys = ON),
/// so notes with cloud BookIds that don't exist locally are safely upserted and shown
/// with their original BookId reference.
/// </summary>
public sealed class SyncService
{
    private readonly CryptoService      _crypto;
    private readonly GoogleDriveService _drive;
    private readonly DatabaseService    _privateDb;
    private readonly NoteRepository     _publicNoteRepo;
    private readonly AppSettingsService _settings;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
        WriteIndented               = false
    };

    public SyncService(
        CryptoService      crypto,
        GoogleDriveService drive,
        DatabaseService    privateDb,
        NoteRepository     publicNoteRepo,
        AppSettingsService settings)
    {
        _crypto         = crypto;
        _drive          = drive;
        _privateDb      = privateDb;
        _publicNoteRepo = publicNoteRepo;
        _settings       = settings;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a full encrypted sync with Google Drive.
    /// </summary>
    /// <param name="hexKey">
    ///   The 256-bit hex key derived from the master password + local salt.
    ///   Used for AES-256-GCM encryption of all cloud note files.
    /// </param>
    public async Task<SyncResult> SyncAsync(string hexKey, CancellationToken ct = default)
    {
        if (!_drive.IsConnected)
            return Fail("Google Drive не підключено. Підключіться в Налаштуваннях.");

        if (_settings.PendingCloudWipe)
            return await CloudWipeAndPushAsync(hexKey, ct);

        // Ensure salt is in the cloud before syncing notes (normal mode).
        // This handles the case where the cloud was wiped externally, or the user
        // pressed "Sync Now" without reconnecting Drive (salt only uploaded on connect).
        try
        {
            var localSalt = _crypto.GetSaltBytes();
            if (localSalt is not null)
            {
                var cloudSalt = await _drive.DownloadSaltAsync(ct);
                if (cloudSalt is null)
                    await _drive.UploadSaltAsync(localSalt, ct);
            }
        }
        catch (Exception ex)
        {
            return Fail($"Помилка при перевірці криптографічного якоря: {ex.Message}");
        }

        return await NormalSyncAsync(hexKey, ct);
    }

    // ── Normal sync (Pull then Push) ──────────────────────────────────────────

    private async Task<SyncResult> NormalSyncAsync(string hexKey, CancellationToken ct)
    {
        int pulled = 0, pushed = 0, errors = 0;

        // ── Pull phase ────────────────────────────────────────────────────────
        try
        {
            var cloudFiles = await _drive.ListNoteFilesAsync(ct);
            var privateRepo = _privateDb.IsUnlocked ? new NoteRepository(_privateDb) : null;

            foreach (var (driveId, _) in cloudFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Use driveId directly — avoids redundant FindFileIdAsync lookup
                    var encrypted = await _drive.DownloadNoteByDriveIdAsync(driveId, ct);
                    if (encrypted is null) continue;

                    var json = _crypto.DecryptAesGcm(encrypted, hexKey);
                    var dto  = JsonSerializer.Deserialize<NoteCloudDto>(json, JsonOpts);
                    if (dto is null) continue;

                    var note = DtoToNote(dto);

                    // Route to correct repo based on note type
                    if (note.Type == NoteType.Private && privateRepo is not null)
                        privateRepo.Upsert(note);
                    else if (note.Type == NoteType.Public)
                        _publicNoteRepo.Upsert(note);
                    // else: private note but vault locked → skip

                    pulled++;
                }
                catch (Exception)
                {
                    errors++; // wrong key or network error — keep going
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Fail($"Помилка при завантаженні з хмари: {ex.Message}");
        }

        // ── Push phase ────────────────────────────────────────────────────────
        try
        {
            var allNotes = CollectAllNotesForPush();

            foreach (var note in allNotes)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var dto       = NoteToDto(note);
                    var json      = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOpts);
                    var encrypted = _crypto.EncryptAesGcm(json, hexKey);
                    await _drive.UploadNoteFileAsync(note.Id, encrypted, ct);
                    pushed++;
                }
                catch (Exception)
                {
                    errors++;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Fail($"Помилка при вивантаженні в хмару: {ex.Message}");
        }

        return new SyncResult(true, pushed, pulled, errors,
            errors == 0
                ? $"✅ Синхронізовано: вивантажено {pushed}, завантажено {pulled} нотаток."
                : $"⚠ Синхронізовано з помилками: вивантажено {pushed}, завантажено {pulled}, помилок {errors}.");
    }

    // ── Cloud Wipe & Push (PendingCloudWipe = true) ───────────────────────────

    private async Task<SyncResult> CloudWipeAndPushAsync(string hexKey, CancellationToken ct)
    {
        int pushed = 0, errors = 0;

        try
        {
            // 1. Wipe all encrypted note files
            await _drive.DeleteAllEncryptedNotesAsync(ct);

            // 2. Wipe old salt
            await _drive.DeleteSaltAsync(ct);

            // 3. Upload fresh local salt
            var localSalt = _crypto.GetSaltBytes();
            if (localSalt is not null)
                await _drive.UploadSaltAsync(localSalt, ct);

            // 4. Push all local notes with new key
            var allNotes = CollectAllNotesForPush();
            foreach (var note in allNotes)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var dto       = NoteToDto(note);
                    var json      = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOpts);
                    var encrypted = _crypto.EncryptAesGcm(json, hexKey);
                    await _drive.UploadNoteFileAsync(note.Id, encrypted, ct);
                    pushed++;
                }
                catch (Exception)
                {
                    errors++;
                }
            }

            // 5. Only reset flag AFTER all uploads succeed
            if (errors == 0)
            {
                _settings.PendingCloudWipe = false;
                return new SyncResult(true, pushed, 0, 0,
                    $"✅ Хмару перезаписано новим ключем. Вивантажено {pushed} нотаток.");
            }

            return new SyncResult(false, pushed, 0, errors,
                $"⚠ Часткове вивантаження ({pushed} успішно, {errors} помилок). Наступна синхронізація продовжить.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Fail($"Помилка при оновленні хмари: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Collects all notes (including soft-deleted) from both repos for cloud push.</summary>
    private List<Note> CollectAllNotesForPush()
    {
        var list = new List<Note>(_publicNoteRepo.GetAllIncludingDeleted());

        if (_privateDb.IsUnlocked)
        {
            var privateRepo = new NoteRepository(_privateDb);
            list.AddRange(privateRepo.GetAllIncludingDeleted());
        }

        return list;
    }

    private static NoteCloudDto NoteToDto(Note note) => new()
    {
        Id           = note.Id,
        Title        = note.Title,
        Content      = note.Content,
        NoteDateTime = note.NoteDateTime.ToString("O"),
        CreatedAt    = note.CreatedAt.ToString("O"),
        UpdatedAt    = note.UpdatedAt.ToString("O"),
        Type         = note.Type.ToString(),
        BookId       = note.BookId,
        IsDeleted    = note.IsDeleted
    };

    private static Note DtoToNote(NoteCloudDto dto) => new()
    {
        Id           = dto.Id    ?? "",
        Title        = dto.Title ?? "",
        Content      = dto.Content ?? "",
        NoteDateTime = ParseDate(dto.NoteDateTime),
        CreatedAt    = ParseDate(dto.CreatedAt),
        UpdatedAt    = ParseDate(dto.UpdatedAt),
        Type         = dto.Type == "Private" ? NoteType.Private : NoteType.Public,
        BookId       = dto.BookId ?? "",
        IsDeleted    = dto.IsDeleted
    };

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d : DateTime.UtcNow;

    private static SyncResult Fail(string message) =>
        new(false, 0, 0, 1, $"❌ {message}");
}

// ── Cloud DTO ─────────────────────────────────────────────────────────────────

/// <summary>Wire format for notes stored in Drive as note{UUID}.json.enc</summary>
internal sealed class NoteCloudDto
{
    [JsonPropertyName("id")]           public string? Id           { get; set; }
    [JsonPropertyName("title")]        public string? Title        { get; set; }
    [JsonPropertyName("content")]      public string? Content      { get; set; }
    [JsonPropertyName("noteDateTime")] public string? NoteDateTime { get; set; }
    [JsonPropertyName("createdAt")]    public string? CreatedAt    { get; set; }
    [JsonPropertyName("updatedAt")]    public string? UpdatedAt    { get; set; }
    [JsonPropertyName("type")]         public string? Type         { get; set; }
    [JsonPropertyName("bookId")]       public string? BookId       { get; set; }
    [JsonPropertyName("isDeleted")]    public bool    IsDeleted    { get; set; }
}
