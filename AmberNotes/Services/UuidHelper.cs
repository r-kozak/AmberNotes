using System;
using System.Security.Cryptography;
using System.Text;

namespace AmberNotes.Services;

/// <summary>
/// Provides UUID generation utilities used throughout AmberNotes.
///
/// • NewV4() — random UUID for new records.
/// • NewV5(name) — deterministic UUID based on SHA-1 for DB migration
///   (same input → same UUID, which prevents duplicates on first merge).
///
/// UUID v5 uses the standard DNS namespace (RFC 4122 §4.3).
/// </summary>
public static class UuidHelper
{
    // DNS namespace UUID bytes (big-endian): 6ba7b810-9dad-11d1-80b4-00c04fd430c8
    private static readonly byte[] _namespace =
    [
        0x6b, 0xa7, 0xb8, 0x10,
        0x9d, 0xad,
        0x11, 0xd1,
        0x80, 0xb4,
        0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8
    ];

    /// <summary>Generates a new random UUID v4 string (lowercase, hyphenated).</summary>
    public static string NewV4() => Guid.NewGuid().ToString();

    /// <summary>
    /// Generates a deterministic UUID v5 (SHA-1, DNS namespace) from <paramref name="name"/>.
    ///
    /// Identical inputs always produce the same UUID, making it safe to use for
    /// DB schema migrations — existing records get stable IDs that won't collide
    /// when two identical local databases are merged for the first time.
    /// </summary>
    public static string NewV5(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[_namespace.Length + nameBytes.Length];
        _namespace.CopyTo(input, 0);
        nameBytes.CopyTo(input, _namespace.Length);

        var hash = SHA1.HashData(input); // 20 bytes

        // RFC 4122 §4.3 — set version 5 in bits 4-7 of byte 6
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        // RFC 4122 §4.1.1 — set variant bits 10xx in bits 6-7 of byte 8
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        // Format first 16 bytes as standard UUID string (big-endian — NOT Guid.ToByteArray()!)
        return
            $"{hash[0]:x2}{hash[1]:x2}{hash[2]:x2}{hash[3]:x2}-" +
            $"{hash[4]:x2}{hash[5]:x2}-" +
            $"{hash[6]:x2}{hash[7]:x2}-" +
            $"{hash[8]:x2}{hash[9]:x2}-" +
            $"{hash[10]:x2}{hash[11]:x2}{hash[12]:x2}{hash[13]:x2}{hash[14]:x2}{hash[15]:x2}";
    }
}
