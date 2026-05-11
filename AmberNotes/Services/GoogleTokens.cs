using System;
using System.Text.Json.Serialization;

namespace AmberNotes.Services;

/// <summary>
/// Persisted OAuth 2.0 token data for Google Drive access.
/// Stored as JSON in the app data folder (googletokens.json).
/// </summary>
public class GoogleTokens
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>UTC time when the access token expires.</summary>
    [JsonPropertyName("expires_at_utc")]
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Google account email of the connected user.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// True when the access token is expired (with a 60-second safety buffer).
    /// When true, <see cref="GoogleAuthService.RefreshAsync"/> should be called.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddSeconds(-60);
}
