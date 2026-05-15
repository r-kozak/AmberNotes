using System;
using System.IO;
using System.Security.Cryptography;

namespace AmberNotes.Services;

/// <summary>
/// Handles cryptographic operations for AmberNotes:
///   - PBKDF2-SHA256 key derivation (256,000 iterations, 256-bit output key)
///   - Salt lifecycle (generated once, stored as plain binary, NEVER inside the encrypted DB)
///   - AES-256-GCM encryption/decryption for cloud note files (v0.6+)
///
/// Salt file location: same directory as the database → ambernotes.salt
/// SECURITY: The derived key is returned as a hex string and must never be logged.
/// </summary>
public class CryptoService
{
    private const int Iterations    = 256_000;
    private const int KeySizeBytes  = 32;   // 256 bits
    private const int SaltSizeBytes = 32;   // 256 bits — cryptographically random

    // AES-GCM constants for cloud note encryption
    private const int GcmNonceSize = 12;    // 96 bits (recommended for AES-GCM)
    private const int GcmTagSize   = 16;    // 128 bits

    private readonly string _saltFilePath;

    public CryptoService(string dataDirectory)
    {
        _saltFilePath = Path.Combine(dataDirectory, "ambernotes.salt");
    }

    // ── Salt lifecycle ────────────────────────────────────────────────────────

    /// <summary>True when no salt file exists — vault has never been created.</summary>
    public bool IsFirstRun => !File.Exists(_saltFilePath);

    /// <summary>
    /// Returns the raw 32-byte salt, or null if the salt file doesn't exist yet.
    /// Used for salt upload/comparison during cloud sync.
    /// </summary>
    public byte[]? GetSaltBytes() =>
        File.Exists(_saltFilePath) ? File.ReadAllBytes(_saltFilePath) : null;

    /// <summary>
    /// Overwrites the local salt file with the provided bytes.
    /// Used in salt-conflict Option 1: adopt cloud password+salt.
    /// </summary>
    public void ReplaceSaltFromBytes(byte[] newSalt)
    {
        var dir = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(_saltFilePath, newSalt);
    }

    // ── Key derivation ────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a 256-bit encryption key using the stored (or newly created) salt.
    /// On first call (no salt file) a new random salt is generated and persisted.
    /// SECURITY: returned hex key must never be stored or logged.
    /// </summary>
    public string DeriveKey(string masterPassword)
    {
        var salt = LoadOrCreateSalt();
        return DeriveKeyCore(masterPassword, salt);
    }

    /// <summary>
    /// Derives a 256-bit key from the specified password + explicit salt bytes.
    /// Does NOT read or modify the local salt file — suitable for testing a cloud
    /// password or for one-time decryption of cloud files without affecting local state.
    /// SECURITY: returned hex key must never be stored or logged.
    /// </summary>
    public string DeriveKeyFromSalt(string password, byte[] salt) =>
        DeriveKeyCore(password, salt);

    /// <summary>
    /// Generates a brand-new random salt, saves it to disk, and derives a key
    /// from the given password using that new salt.
    /// Used when changing the master password (Step 29).
    /// SECURITY: returned hex key must never be stored or logged.
    /// </summary>
    public string GenerateNewSaltAndDeriveKey(string newPassword)
    {
        var dir = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var newSalt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        File.WriteAllBytes(_saltFilePath, newSalt);
        return DeriveKeyCore(newPassword, newSalt);
    }

    // ── AES-256-GCM for cloud note files ─────────────────────────────────────

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM using a 256-bit key
    /// derived from the provided hex key string.
    ///
    /// Output layout: [12-byte nonce][ciphertext][16-byte auth-tag]
    /// </summary>
    public byte[] EncryptAesGcm(byte[] plaintext, string hexKey)
    {
        var keyBytes = Convert.FromHexString(hexKey);
        var nonce    = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var cipher   = new byte[plaintext.Length];
        var tag      = new byte[GcmTagSize];

        using var aes = new AesGcm(keyBytes, GcmTagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        // Concatenate: nonce + ciphertext + tag
        var result = new byte[GcmNonceSize + cipher.Length + GcmTagSize];
        nonce.CopyTo(result, 0);
        cipher.CopyTo(result, GcmNonceSize);
        tag.CopyTo(result, GcmNonceSize + cipher.Length);
        return result;
    }

    /// <summary>
    /// Decrypts data produced by <see cref="EncryptAesGcm"/>.
    /// Throws <see cref="CryptographicException"/> if the tag is invalid (wrong key / tampered).
    /// </summary>
    public byte[] DecryptAesGcm(byte[] encrypted, string hexKey)
    {
        if (encrypted.Length < GcmNonceSize + GcmTagSize)
            throw new ArgumentException("Encrypted payload is too short.");

        var keyBytes  = Convert.FromHexString(hexKey);
        var nonce     = encrypted[..GcmNonceSize];
        var tag       = encrypted[^GcmTagSize..];
        var cipher    = encrypted[GcmNonceSize..^GcmTagSize];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(keyBytes, GcmTagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string DeriveKeyCore(string password, byte[] salt)
    {
        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        // Виведіть це в консоль або лог під час запуску
        System.Diagnostics.Debug.WriteLine($"DB Hex Key: 0x{BitConverter.ToString(derivedKey).Replace("-", "")}");

        return Convert.ToHexString(derivedKey).ToLowerInvariant();
    }

    private byte[] LoadOrCreateSalt()
    {
        if (File.Exists(_saltFilePath))
            return File.ReadAllBytes(_saltFilePath);

        var dir = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        File.WriteAllBytes(_saltFilePath, salt);
        return salt;
    }
}
