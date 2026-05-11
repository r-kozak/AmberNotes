using System.Threading;
using System.Threading.Tasks;

namespace AmberNotes.Services;

/// <summary>
/// Abstraction over a cloud backup/sync provider.
/// v0.5 — connection & auth only.
/// v0.6+ — upload / download / conflict resolution.
/// </summary>
public interface ICloudStorageService
{
    /// <summary>True when the user is authenticated and the session is active.</summary>
    bool IsConnected { get; }

    /// <summary>Email address of the authenticated Google account, or null.</summary>
    string? ConnectedEmail { get; }

    /// <summary>
    /// Initiates the OAuth sign-in flow and establishes a connection.
    /// Returns true on success.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Signs out the user, revokes the token on the server, and clears stored tokens.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Verifies the connection by making a lightweight Drive API call.
    /// Also refreshes the access token if it is close to expiry.
    /// Returns true when the appDataFolder is accessible.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
