using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AmberNotes.Services;

/// <summary>
/// Google Drive implementation of <see cref="ICloudStorageService"/>.
///
/// Scope: drive.appdata — the hidden per-app folder in Google Drive.
/// The user's personal files are completely inaccessible to the app.
///
/// v0.5: Authentication + connection test only.
/// v0.6: Will add UploadBackupAsync / DownloadBackupAsync / ListBackupsAsync.
/// </summary>
public class GoogleDriveService : ICloudStorageService
{
    private readonly GoogleAuthService _auth;
    private GoogleTokens? _tokens;

    private static readonly HttpClient _http = new();

    // Google Drive REST API — appDataFolder operations
    private const string FilesEndpoint =
        "https://www.googleapis.com/drive/v3/files" +
        "?spaces=appDataFolder&fields=files(id,name,modifiedTime)&pageSize=10";

    // ── ICloudStorageService ──────────────────────────────────────────────────

    public bool    IsConnected    => _tokens is not null && !string.IsNullOrEmpty(_tokens.AccessToken);
    public string? ConnectedEmail => _tokens?.Email;

    // ── Constructor ───────────────────────────────────────────────────────────

    public GoogleDriveService(GoogleAuthService authService)
    {
        _auth = authService;

        // Restore tokens persisted during a previous session
        _tokens = _auth.LoadTokens();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────────

    /// <summary>
    /// Launches the OAuth browser flow and, on success, stores the tokens.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        _tokens = await _auth.SignInAsync(ct);
        return IsConnected;
    }

    /// <summary>
    /// Revokes the token on Google's servers, clears local storage.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_tokens is not null)
            await _auth.RevokeAndClearAsync(_tokens);

        _tokens = null;
    }

    // ── Connection test ───────────────────────────────────────────────────────

    /// <summary>
    /// Calls the Drive Files API to list up to 10 items in appDataFolder.
    /// A 200 response confirms that authentication and scope are valid.
    /// Auto-refreshes the access token when it is about to expire.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        if (_tokens!.IsExpired)
        {
            _tokens = await _auth.RefreshAsync(_tokens, ct);
            if (_tokens is null) return false;
        }

        try
        {
            using var req = BuildRequest(HttpMethod.Get, FilesEndpoint);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
        return req;
    }
}
