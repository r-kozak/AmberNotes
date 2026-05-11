using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AmberNotes.Services;

/// <summary>
/// Google OAuth 2.0 authentication service with PKCE support.
///
/// ──────── Desktop flow (Windows / macOS / Linux) ────────────────────────
///   1. Pick a free localhost port.
///   2. Start an HttpListener on http://localhost:{port}/.
///   3. Open the system browser at the Google authorization URL.
///   4. Google redirects to http://localhost:{port}/?code=...
///   5. Exchange the code for access + refresh tokens.
///   6. Serve a "success" HTML page to the browser and close the listener.
///
/// ──────── Android flow ───────────────────────────────────────────────────
///   1. Register a static TaskCompletionSource (PendingAndroidCode).
///   2. Open the system browser via the BrowserLauncher delegate.
///      (Android Application.OnCreate registers an Intent-based launcher.)
///   3. Google redirects to com.kozak.ambernotes://oauth2callback?code=...
///   4. Android system routes the URI to MainActivity (IntentFilter).
///   5. MainActivity calls GoogleAuthService.HandleAndroidCallback(code).
///   6. Exchange the code for tokens.
///
/// ──────── Token storage ──────────────────────────────────────────────────
///   Tokens are stored as plain JSON in the app data folder
///   (googletokens.json). They are NOT in the encrypted SQLCipher database
///   for v0.5; proper encrypted storage is planned for v0.6.
/// </summary>
public class GoogleAuthService
{
    // ── Platform browser launcher ─────────────────────────────────────────────
    //   Default covers Desktop (Process.Start with UseShellExecute).
    //   AmberNotes.Android/Application.cs overrides this in OnCreate()
    //   before the Avalonia app is started — so it is always set in time.
    public static Func<string, Task> BrowserLauncher { get; set; } = url =>
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return Task.CompletedTask;
    };

    // ── Android callback bridge ───────────────────────────────────────────────
    //   Static TCS that is awaited inside GetCodeAndroidAsync().
    //   MainActivity calls HandleAndroidCallback() when it receives the redirect.
    private static TaskCompletionSource<string?>? _pendingAndroidCode;

    /// <summary>
    /// Called by Android MainActivity when the custom-scheme redirect is received.
    /// Resolves the pending sign-in Task with the authorization code (or null on error).
    /// </summary>
    public static void HandleAndroidCallback(string? code, string? error = null)
    {
        if (error != null || string.IsNullOrEmpty(code))
            _pendingAndroidCode?.TrySetResult(null);
        else
            _pendingAndroidCode?.TrySetResult(code);
    }

    // ── Shared HttpClient ─────────────────────────────────────────────────────
    private static readonly HttpClient _http = new();

    // ── Token file path ───────────────────────────────────────────────────────
    private readonly string _tokenFilePath;

    public GoogleAuthService(string appDataFolder)
    {
        _tokenFilePath = Path.Combine(appDataFolder, GoogleAuthConfig.TokensFileName);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>True when a tokens file exists on disk.</summary>
    public bool HasStoredTokens => File.Exists(_tokenFilePath);

    /// <summary>
    /// Loads persisted tokens from disk.
    /// Returns null if the file is missing or corrupt.
    /// </summary>
    public GoogleTokens? LoadTokens()
    {
        if (!File.Exists(_tokenFilePath)) return null;
        try
        {
            var json = File.ReadAllText(_tokenFilePath);
            return JsonSerializer.Deserialize<GoogleTokens>(json);
        }
        catch { return null; }
    }

    /// <summary>Deletes the tokens file (sign-out).</summary>
    public void ClearTokens()
    {
        if (File.Exists(_tokenFilePath))
            File.Delete(_tokenFilePath);
    }

    /// <summary>
    /// Full PKCE OAuth sign-in flow.
    /// Opens the system browser, waits for the authorization code, exchanges
    /// it for tokens, fetches the email, persists to disk, and returns them.
    /// Returns null if the user cancels or an error occurs.
    /// </summary>
    public async Task<GoogleTokens?> SignInAsync(CancellationToken ct = default)
    {
        var (verifier, challenge) = PkceHelper.Generate();

        string code;
        string redirectUri;
        string clientId;
        string? clientSecret;

        if (OperatingSystem.IsAndroid())
        {
            clientId     = GoogleAuthConfig.AndroidClientId;
            clientSecret = null; // Android OAuth clients have no secret
            redirectUri  = GoogleAuthConfig.AndroidRedirectUri;

            var authUrl = BuildAuthUrl(clientId, redirectUri, challenge);
            code = await GetCodeAndroidAsync(authUrl, ct);
        }
        else
        {
            clientId     = GoogleAuthConfig.DesktopClientId;
            clientSecret = GoogleAuthConfig.DesktopClientSecret;

            var port    = GetFreePort();
            redirectUri = $"http://localhost:{port}/";

            var authUrl = BuildAuthUrl(clientId, redirectUri, challenge);
            code = await GetCodeDesktopAsync(authUrl, redirectUri, ct);
        }

        if (string.IsNullOrEmpty(code)) return null;

        var tokens = await ExchangeCodeAsync(code, verifier, redirectUri,
                                             clientId, clientSecret, ct);
        if (tokens is null) return null;

        tokens.Email = await FetchEmailAsync(tokens.AccessToken, ct);
        SaveTokens(tokens);
        return tokens;
    }

    /// <summary>
    /// Refreshes an expired access token using the stored refresh token.
    /// Updates and persists the tokens on success.
    /// Returns null when refresh fails (re-login required).
    /// </summary>
    public async Task<GoogleTokens?> RefreshAsync(
        GoogleTokens current, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(current.RefreshToken)) return null;

        var clientId     = OperatingSystem.IsAndroid()
                           ? GoogleAuthConfig.AndroidClientId
                           : GoogleAuthConfig.DesktopClientId;
        var clientSecret = OperatingSystem.IsAndroid()
                           ? null
                           : GoogleAuthConfig.DesktopClientSecret;

        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
            ["client_id"]     = clientId,
        };
        if (!string.IsNullOrEmpty(clientSecret))
            form["client_secret"] = clientSecret;

        try
        {
            using var resp = await _http.PostAsync(
                GoogleAuthConfig.TokenEndpoint,
                new FormUrlEncodedContent(form), ct);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc  = JsonDocument.Parse(json);
            var root        = doc.RootElement;

            current.AccessToken  = root.GetProperty("access_token").GetString()!;
            current.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(
                root.GetProperty("expires_in").GetInt32());

            // Google only returns a new refresh_token occasionally
            if (root.TryGetProperty("refresh_token", out var rt))
                current.RefreshToken = rt.GetString();

            SaveTokens(current);
            return current;
        }
        catch { return null; }
    }

    /// <summary>
    /// Revokes the token on Google's servers and removes local storage.
    /// Best-effort — does not throw on network errors.
    /// </summary>
    public async Task RevokeAndClearAsync(GoogleTokens tokens)
    {
        try
        {
            var token = tokens.RefreshToken ?? tokens.AccessToken;
            await _http.PostAsync(
                GoogleAuthConfig.RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["token"] = token }));
        }
        catch { /* best-effort */ }
        finally { ClearTokens(); }
    }

    // ── Private — Auth-code acquisition ──────────────────────────────────────

    /// <summary>Desktop: start HttpListener, open browser, wait for redirect.</summary>
    private static async Task<string> GetCodeDesktopAsync(
        string authUrl, string redirectUri, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        await BrowserLauncher(authUrl);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

        try
        {
            var ctx  = await listener.GetContextAsync().WaitAsync(timeoutCts.Token);
            var code = ctx.Request.QueryString["code"] ?? "";

            // Serve a friendly success page so the browser tab can be closed
            const string html = """
                <!DOCTYPE html>
                <html lang="uk">
                <head><meta charset="utf-8"><title>AmberNotes</title></head>
                <body style="font-family:sans-serif;text-align:center;padding:60px;background:#1A1A1B;color:#F4F1EA">
                  <h2 style="color:#FFBF00">✅ AmberNotes</h2>
                  <p>Авторизацію виконано успішно!<br>
                     Ви можете закрити це вікно та повернутися до застосунку.</p>
                </body>
                </html>
                """;

            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType     = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, timeoutCts.Token);
            ctx.Response.Close();

            return code;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Android: opens the browser and waits for MainActivity to call
    /// <see cref="HandleAndroidCallback"/> via the custom-scheme intent.
    /// </summary>
    private static async Task<string> GetCodeAndroidAsync(
        string authUrl, CancellationToken ct)
    {
        _pendingAndroidCode = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

        timeoutCts.Token.Register(
            () => _pendingAndroidCode.TrySetCanceled(timeoutCts.Token));

        await BrowserLauncher(authUrl);

        var code = await _pendingAndroidCode.Task;
        _pendingAndroidCode = null;
        return code ?? "";
    }

    // ── Private — Token exchange & helpers ────────────────────────────────────

    private static string BuildAuthUrl(
        string clientId, string redirectUri, string challenge)
    {
        return new StringBuilder(GoogleAuthConfig.AuthEndpoint)
            .Append("?response_type=code")
            .Append($"&client_id={Uri.EscapeDataString(clientId)}")
            .Append($"&redirect_uri={Uri.EscapeDataString(redirectUri)}")
            .Append($"&scope={Uri.EscapeDataString(GoogleAuthConfig.Scope)}")
            .Append("&access_type=offline")
            .Append("&prompt=consent")          // always get refresh_token
            .Append("&code_challenge_method=S256")
            .Append($"&code_challenge={challenge}")
            .ToString();
    }

    private static async Task<GoogleTokens?> ExchangeCodeAsync(
        string code, string verifier, string redirectUri,
        string clientId, string? clientSecret, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["client_id"]     = clientId,
            ["code_verifier"] = verifier,
        };
        if (!string.IsNullOrEmpty(clientSecret))
            form["client_secret"] = clientSecret;

        try
        {
            using var resp = await _http.PostAsync(
                GoogleAuthConfig.TokenEndpoint,
                new FormUrlEncodedContent(form), ct);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root       = doc.RootElement;

            return new GoogleTokens
            {
                AccessToken  = root.GetProperty("access_token").GetString()!,
                RefreshToken = root.TryGetProperty("refresh_token", out var rt)
                               ? rt.GetString() : null,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(
                               root.GetProperty("expires_in").GetInt32()),
            };
        }
        catch { return null; }
    }

    private static async Task<string?> FetchEmailAsync(
        string accessToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, GoogleAuthConfig.UserInfoEndpoint);
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("email", out var email)
                   ? email.GetString() : null;
        }
        catch { return null; }
    }

    private void SaveTokens(GoogleTokens tokens)
    {
        var dir = Path.GetDirectoryName(_tokenFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokens));
    }

    private static int GetFreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }
}
