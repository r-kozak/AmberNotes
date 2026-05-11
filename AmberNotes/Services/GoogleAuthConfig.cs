using System;

namespace AmberNotes.Services;

/// <summary>
/// OAuth 2.0 configuration constants for Google Drive integration.
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
/// ║    • Скопіюйте Client ID та Client Secret нижче.             ║
/// ║                                                              ║
/// ║  Android Client ID:                                          ║
/// ║    • "Create Credentials → OAuth client ID"                  ║
/// ║    • Application type: Android                               ║
/// ║    • Package name: com.kozak.AmberNotes                      ║
/// ║    • SHA-1 fingerprint вашого підписного сертифіката          ║
/// ║    • Скопіюйте Client ID нижче (secret не потрібен).         ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
public static class GoogleAuthConfig
{
    // ── Desktop (Windows / macOS / Linux) ────────────────────────────────────
    public const string DesktopClientId     = "";
    public const string DesktopClientSecret = "";

    // ── Android ──────────────────────────────────────────────────────────────
    // No client secret for Android — security via SHA-1 certificate fingerprint.
    public const string AndroidClientId = "";

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

    // ── Tokens file name (stored in app data folder) ──────────────────────────
    public const string TokensFileName = "googletokens.json";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when Desktop Client ID has been filled in by the developer.</summary>
    public static bool IsDesktopConfigured =>
        !DesktopClientId.StartsWith("YOUR_", StringComparison.Ordinal);

    /// <summary>True when Android Client ID has been filled in by the developer.</summary>
    public static bool IsAndroidConfigured =>
        !AndroidClientId.StartsWith("YOUR_", StringComparison.Ordinal);

    /// <summary>True when the current platform's Client ID is configured.</summary>
    public static bool IsCurrentPlatformConfigured =>
        OperatingSystem.IsAndroid() ? IsAndroidConfigured : IsDesktopConfigured;
}
