using System;
using System.IO;
using System.Text.Json;

namespace AmberNotes.Services;

/// <summary>
/// OAuth 2.0 configuration for Google Drive integration.
///
/// ╔══════════════════════════════════════════════════════════════╗
/// ║  SETUP REQUIRED — Google Cloud Console                       ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  1. Відкрийте https://console.cloud.google.com/              ║
/// ║  2. Створіть (або виберіть) проєкт.                          ║
/// ║  3. Увімкніть Google Drive API.                              ║
/// ║  4. Перейдіть до "APIs & Services → Credentials".            ║
/// ║                                                              ║
/// ║  Desktop Client ID:                                          ║
/// ║    • "Create Credentials → OAuth client ID"                  ║
/// ║    • Application type: Desktop app                           ║
/// ║    • Скопіюйте Client ID та Client Secret.                   ║
/// ║                                                              ║
/// ║  Android Client ID:                                          ║
/// ║    • "Create Credentials → OAuth client ID"                  ║
/// ║    • Application type: Android                               ║
/// ║    • Package name: com.kozak.AmberNotes                      ║
/// ║    • SHA-1 fingerprint вашого підписного сертифіката          ║
/// ║    • Скопіюйте Client ID (secret не потрібен).               ║
/// ║                                                              ║
/// ║  Помістіть значення у файл oauth.config.json                 ║
/// ║  (приклад структури — oauth.config.example.json).            ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
public static class GoogleAuthConfig
{
    // ── Secrets (runtime-loaded from oauth.config.json) ───────────────────────
    public static string DesktopClientId     { get; private set; } = string.Empty;
    public static string DesktopClientSecret { get; private set; } = string.Empty;
    public static string AndroidClientId     { get; private set; } = string.Empty;

    // ── OAuth Endpoints ───────────────────────────────────────────────────────
    public const string AuthEndpoint     = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint    = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint   = "https://oauth2.googleapis.com/revoke";
    public const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v1/userinfo";

    // ── Scopes ────────────────────────────────────────────────────────────────
    // drive.appdata  — hidden per-app folder only; zero access to user's Drive.
    // userinfo.email — to display the connected account email in Settings.
    public const string Scope =
        "https://www.googleapis.com/auth/drive.appdata " +
        "https://www.googleapis.com/auth/userinfo.email";

    // ── Android Custom URI Scheme redirect ────────────────────────────────────
    // Must match <data android:scheme="..." android:host="..."/> in AndroidManifest.xml
    public const string AndroidRedirectUri = "com.kozak.ambernotes://oauth2callback";

    // ── File names ────────────────────────────────────────────────────────────
    public const string TokensFileName = "googletokens.json";

    /// <summary>
    /// Name of the OAuth secrets config file to look for in the app data folder.
    /// This file is listed in .gitignore and must never be committed.
    /// Copy oauth.config.example.json → oauth.config.json and fill in your credentials.
    /// </summary>
    public const string ConfigFileName = "oauth.config.json";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when Desktop credentials have been loaded.</summary>
    public static bool IsDesktopConfigured =>
        !string.IsNullOrWhiteSpace(DesktopClientId) &&
        !string.IsNullOrWhiteSpace(DesktopClientSecret);

    /// <summary>True when Android Client ID has been loaded.</summary>
    public static bool IsAndroidConfigured =>
        !string.IsNullOrWhiteSpace(AndroidClientId);

    /// <summary>True when the current platform credentials are loaded.</summary>
    public static bool IsCurrentPlatformConfigured =>
        OperatingSystem.IsAndroid() ? IsAndroidConfigured : IsDesktopConfigured;

    // ── Loaders ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads OAuth credentials from a file path (Desktop).
    /// If the file does not exist or is malformed, the properties stay empty
    /// and <see cref="IsCurrentPlatformConfigured"/> returns false — the app
    /// will show a "setup required" hint in Settings instead of crashing.
    /// </summary>
    public static void Load(string configFilePath)
    {
        if (!File.Exists(configFilePath))
            return;

        try
        {
            using var stream = File.OpenRead(configFilePath);
            Load(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GoogleAuthConfig] Failed to load {configFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads OAuth credentials from a stream (Android Assets or any other source).
    /// The stream is read to end and then closed by the caller.
    /// </summary>
    public static void Load(Stream stream)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<OAuthConfigFile>(stream,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (cfg is null) return;

            if (!string.IsNullOrWhiteSpace(cfg.DesktopClientId))
                DesktopClientId = cfg.DesktopClientId;

            if (!string.IsNullOrWhiteSpace(cfg.DesktopClientSecret))
                DesktopClientSecret = cfg.DesktopClientSecret;

            if (!string.IsNullOrWhiteSpace(cfg.AndroidClientId))
                AndroidClientId = cfg.AndroidClientId;
        }
        catch (Exception ex)
        {
            // Non-fatal: app continues without Google Drive support.
            System.Diagnostics.Debug.WriteLine(
                $"[GoogleAuthConfig] Failed to load config from stream: {ex.Message}");
        }
    }

    // ── Private DTO ───────────────────────────────────────────────────────────

    private sealed class OAuthConfigFile
    {
        public string? DesktopClientId     { get; set; }
        public string? DesktopClientSecret { get; set; }
        public string? AndroidClientId     { get; set; }
    }
}
