using System;
using System.IO;
using System.Security.Cryptography;

namespace AmberNotes.Services;

/// <summary>
/// Handles cryptographic operations for AmberNotes:
///   - PBKDF2-SHA256 key derivation (256,000 iterations, 256-bit output key)
///   - Salt lifecycle (generated once, stored as a plain binary file, NEVER inside the encrypted DB)
///
/// Salt file location: same directory as the database → ambernotes.salt
/// SECURITY: The derived key is returned as a hex string and must never be logged.
/// </summary>
public class CryptoService
{
    private const int Iterations    = 256_000;
    private const int KeySizeBytes  = 32;   // 256 bits
    private const int SaltSizeBytes = 32;   // 256 bits — cryptographically random

    private readonly string _saltFilePath;

    public CryptoService(string dataDirectory)
    {
        _saltFilePath = Path.Combine(dataDirectory, "ambernotes.salt");
    }

    /// <summary>
    /// Returns true when no salt file exists yet, meaning the vault has never been set up.
    /// </summary>
    public bool IsFirstRun => !File.Exists(_saltFilePath);

    /// <summary>
    /// Derives a 256-bit encryption key from <paramref name="masterPassword"/> using PBKDF2-SHA256.
    /// On the first call (no salt file) a new random salt is generated and persisted.
    /// Subsequent calls load the existing salt to reproduce the same key for the same password.
    ///
    /// SECURITY: The returned hex string is the raw AES key — never store or log it.
    /// </summary>
    public string DeriveKey(string masterPassword)
    {
        var salt = LoadOrCreateSalt();

        // Rfc2898DeriveBytes.Pbkdf2 — modern static API (.NET 6+, no deprecation warning)
        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            masterPassword,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);

        // Return as lowercase hex — passed directly to SQLCipher via PRAGMA key = "x'...'"
        return Convert.ToHexString(keyBytes).ToLowerInvariant();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private byte[] LoadOrCreateSalt()
    {
        if (File.Exists(_saltFilePath))
            return File.ReadAllBytes(_saltFilePath);

        // First run: create directory and persist a new cryptographically-random salt
        var directory = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        File.WriteAllBytes(_saltFilePath, salt);
        return salt;
    }
}
