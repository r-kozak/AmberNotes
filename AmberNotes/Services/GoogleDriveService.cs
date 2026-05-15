using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AmberNotes.Services;

// ── Cloud Explorer DTO ────────────────────────────────────────────────────────

/// <summary>
/// Metadata of a single file in Google Drive appDataFolder.
/// Used by CloudExplorerViewModel for the read-only audit view.
/// </summary>
public sealed record CloudFileInfo(
    string   DriveId,
    string   FileName,
    /// <summary>Note UUID extracted from "note{UUID}.json.enc"; null for salt / unknown files.</summary>
    string?  NoteId,
    /// <summary>True when FileName == "ambernotes.salt".</summary>
    bool     IsSalt,
    long     SizeBytes,
    DateTime ModifiedTime);

/// <summary>
/// Google Drive implementation of <see cref="ICloudStorageService"/>.
///
/// Scope: drive.appdata — the hidden per-app folder in Google Drive.
/// The user's personal files are completely inaccessible to the app.
///
/// v0.5: Authentication + connection test.
/// v0.6: Salt CRUD, encrypted note upload/download, file listing, deletion.
/// </summary>
public class GoogleDriveService : ICloudStorageService
{
    private readonly GoogleAuthService _auth;
    private GoogleTokens? _tokens;

    private static readonly HttpClient _http = new();

    // ── Drive API constants ───────────────────────────────────────────────────
    private const string DriveFilesBase   = "https://www.googleapis.com/drive/v3/files";
    private const string DriveUploadBase  = "https://www.googleapis.com/upload/drive/v3/files";
    private const string AppDataFolderSpace = "appDataFolder";
    private const string SaltFileName    = "ambernotes.salt";
    public  const string NoteFilePrefix  = "note";
    public  const string NoteFileExt     = ".json.enc";

    // ── ICloudStorageService ──────────────────────────────────────────────────

    public bool    IsConnected    => _tokens is not null && !string.IsNullOrEmpty(_tokens.AccessToken);
    public string? ConnectedEmail => _tokens?.Email;

    // ── Constructor ───────────────────────────────────────────────────────────

    public GoogleDriveService(GoogleAuthService authService)
    {
        _auth   = authService;
        _tokens = _auth.LoadTokens();
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        _tokens = await _auth.SignInAsync(ct);
        return IsConnected;
    }

    public async Task DisconnectAsync()
    {
        if (_tokens is not null)
            await _auth.RevokeAndClearAsync(_tokens);
        _tokens = null;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;
        await EnsureTokenFreshAsync(ct);
        try
        {
            var url = $"{DriveFilesBase}?spaces={AppDataFolderSpace}&pageSize=1&fields=files(id)";
            using var req  = BuildRequest(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Salt file operations ──────────────────────────────────────────────────

    /// <summary>
    /// Downloads the cloud salt file. Returns null if not found.
    /// </summary>
    public async Task<byte[]?> DownloadSaltAsync(CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var fileId = await FindFileIdAsync(SaltFileName, ct);
        if (fileId is null) return null;
        return await DownloadFileAsync(fileId, ct);
    }

    /// <summary>
    /// Uploads (creates or replaces) the salt file in appDataFolder.
    /// </summary>
    public async Task UploadSaltAsync(byte[] saltBytes, CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var existingId = await FindFileIdAsync(SaltFileName, ct);
        if (existingId is not null)
            await UpdateFileAsync(existingId, saltBytes, ct);
        else
            await CreateFileAsync(SaltFileName, saltBytes, "application/octet-stream", ct);
    }

    /// <summary>
    /// Deletes the salt file from Drive if it exists.
    /// </summary>
    public async Task DeleteSaltAsync(CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var fileId = await FindFileIdAsync(SaltFileName, ct);
        if (fileId is not null)
            await DeleteFileByIdAsync(fileId, ct);
    }

    // ── Encrypted note file operations ────────────────────────────────────────

    /// <summary>
    /// Uploads an encrypted note file.
    /// Name format: <c>note{UUID}.json.enc</c>
    /// </summary>
    public async Task UploadNoteFileAsync(string noteId, byte[] encryptedData, CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var name       = $"{NoteFilePrefix}{noteId}{NoteFileExt}";
        var existingId = await FindFileIdAsync(name, ct);
        if (existingId is not null)
            await UpdateFileAsync(existingId, encryptedData, ct);
        else
            await CreateFileAsync(name, encryptedData, "application/octet-stream", ct);
    }

    /// <summary>
    /// Downloads an encrypted note file by note UUID. Returns null if not found.
    /// </summary>
    public async Task<byte[]?> DownloadNoteFileAsync(string noteId, CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var name   = $"{NoteFilePrefix}{noteId}{NoteFileExt}";
        var fileId = await FindFileIdAsync(name, ct);
        if (fileId is null) return null;
        return await DownloadFileAsync(fileId, ct);
    }

    /// <summary>
    /// Deletes a single encrypted note file by note UUID.
    /// </summary>
    public async Task DeleteNoteFileAsync(string noteId, CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var name   = $"{NoteFilePrefix}{noteId}{NoteFileExt}";
        var fileId = await FindFileIdAsync(name, ct);
        if (fileId is not null)
            await DeleteFileByIdAsync(fileId, ct);
    }

    /// <summary>
    /// Lists ALL encrypted note files in appDataFolder (handles nextPageToken pagination).
    /// Returns a list of (driveFileId, noteId) pairs.
    /// </summary>
    public async Task<List<(string DriveId, string NoteId)>> ListNoteFilesAsync(CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);
        var result    = new List<(string, string)>();
        string? token = null;

        do
        {
            var url = $"{DriveFilesBase}?spaces={AppDataFolderSpace}" +
                      $"&q=name+contains+%27{NoteFilePrefix}%27" +
                      $"&fields=nextPageToken,files(id,name)" +
                      $"&pageSize=100" +
                      (token is not null ? $"&pageToken={Uri.EscapeDataString(token)}" : "");

            using var req  = BuildRequest(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc  = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("files", out var files))
                foreach (var file in files.EnumerateArray())
                {
                    var driveId = file.GetProperty("id").GetString() ?? "";
                    var name    = file.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith(NoteFilePrefix) && name.EndsWith(NoteFileExt))
                    {
                        var noteId = name[NoteFilePrefix.Length..^NoteFileExt.Length];
                        result.Add((driveId, noteId));
                    }
                }

            token = doc.RootElement.TryGetProperty("nextPageToken", out var nt)
                    ? nt.GetString()
                    : null;

        } while (token is not null);

        return result;
    }

    /// <summary>
    /// Deletes ALL encrypted note files from appDataFolder.
    /// Used for Hard Reset and Cloud Wipe operations.
    /// </summary>
    public async Task DeleteAllEncryptedNotesAsync(CancellationToken ct = default)
    {
        var files = await ListNoteFilesAsync(ct);
        foreach (var (driveId, _) in files)
        {
            try { await DeleteFileByIdAsync(driveId, ct); }
            catch { /* best-effort per file */ }
        }
    }

    // ── Cloud Explorer: full inventory with metadata ───────────────────────────

    /// <summary>
    /// Lists ALL files in appDataFolder (salt + note files) with their full metadata:
    /// id, name, size, modifiedTime.
    ///
    /// Handles nextPageToken pagination — guaranteed to return every file even when
    /// the folder contains more than 100 items.
    ///
    /// Used exclusively by CloudExplorerViewModel (read-only audit).
    /// </summary>
    public async Task<List<CloudFileInfo>> ListAllFilesWithDetailsAsync(CancellationToken ct = default)
    {
        await EnsureTokenFreshAsync(ct);

        var result      = new List<CloudFileInfo>();
        string? token   = null;

        do
        {
            var url = $"{DriveFilesBase}?spaces={AppDataFolderSpace}" +
                      $"&fields=nextPageToken,files(id,name,size,modifiedTime)" +
                      $"&pageSize=100" +
                      (token is not null ? $"&pageToken={Uri.EscapeDataString(token)}" : "");

            using var req  = BuildRequest(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine($"[CloudExplorer] API page response: {json[..Math.Min(json.Length, 500)]}");

            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var driveId  = file.GetProperty("id").GetString()   ?? "";
                    var name     = file.GetProperty("name").GetString() ?? "";

                    // size may be absent for empty files or folders
                    var sizeStr  = file.TryGetProperty("size", out var szProp) ? szProp.GetString() : null;
                    long sizeBytes = long.TryParse(sizeStr, out var sb) ? sb : 0L;

                    // modifiedTime is RFC 3339 / ISO 8601
                    var modStr = file.TryGetProperty("modifiedTime", out var modProp)
                                 ? modProp.GetString()
                                 : null;
                    DateTime modifiedTime = DateTime.TryParse(modStr, null,
                        DateTimeStyles.RoundtripKind, out var dt)
                        ? dt
                        : DateTime.UtcNow;

                    bool    isSalt = string.Equals(name, SaltFileName, StringComparison.Ordinal);
                    string? noteId = null;

                    if (!isSalt && name.StartsWith(NoteFilePrefix, StringComparison.Ordinal)
                                && name.EndsWith(NoteFileExt, StringComparison.Ordinal))
                    {
                        noteId = name[NoteFilePrefix.Length..^NoteFileExt.Length];
                    }

                    result.Add(new CloudFileInfo(driveId, name, noteId, isSalt, sizeBytes, modifiedTime));
                }
            }

            token = doc.RootElement.TryGetProperty("nextPageToken", out var nt)
                    ? nt.GetString()
                    : null;

        } while (token is not null);

        System.Diagnostics.Debug.WriteLine($"[CloudExplorer] Total files fetched from Drive: {result.Count}");
        return result;
    }

    // ── Low-level Drive helpers ───────────────────────────────────────────────

    /// <summary>Finds the Drive file ID for the given filename, or null.</summary>
    private async Task<string?> FindFileIdAsync(string name, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString($"name = '{name}'");
        var url = $"{DriveFilesBase}?spaces={AppDataFolderSpace}" +
                  $"&q={encodedName}" +
                  $"&fields=files(id)&pageSize=1";

        using var req  = BuildRequest(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var doc  = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("files", out var files))
            foreach (var file in files.EnumerateArray())
                if (file.TryGetProperty("id", out var id))
                    return id.GetString();

        return null;
    }

    /// <summary>Downloads a file by Drive file ID.</summary>
    private async Task<byte[]> DownloadFileAsync(string driveFileId, CancellationToken ct)
    {
        var url = $"{DriveFilesBase}/{driveFileId}?alt=media";
        using var req  = BuildRequest(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Creates a new file in appDataFolder using multipart upload.</summary>
    private async Task CreateFileAsync(string name, byte[] data, string mimeType, CancellationToken ct)
    {
        var url = $"{DriveUploadBase}?uploadType=multipart&spaces={AppDataFolderSpace}";

        var metadataJson = JsonSerializer.Serialize(new
        {
            name,
            parents = new[] { AppDataFolderSpace }
        });

        using var content = new MultipartContent("related");
        var metaPart = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metaPart);
        var dataPart = new ByteArrayContent(data);
        dataPart.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(dataPart);

        using var req  = BuildRequest(HttpMethod.Post, url);
        req.Content    = content;
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Updates the content of an existing Drive file.</summary>
    private async Task UpdateFileAsync(string driveFileId, byte[] data, CancellationToken ct)
    {
        var url = $"{DriveUploadBase}/{driveFileId}?uploadType=media";
        using var req  = BuildRequest(HttpMethod.Patch, url);
        req.Content    = new ByteArrayContent(data);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Permanently deletes a Drive file by its ID.</summary>
    private async Task DeleteFileByIdAsync(string driveFileId, CancellationToken ct)
    {
        var url = $"{DriveFilesBase}/{driveFileId}";
        using var req  = BuildRequest(HttpMethod.Delete, url);
        using var resp = await _http.SendAsync(req, ct);
        // 204 No Content = success; 404 = already deleted — both are acceptable
        if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    private async Task EnsureTokenFreshAsync(CancellationToken ct)
    {
        if (_tokens?.IsExpired == true)
            _tokens = await _auth.RefreshAsync(_tokens, ct);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
        return req;
    }
}
