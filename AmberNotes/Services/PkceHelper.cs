using System;
using System.Security.Cryptography;
using System.Text;

namespace AmberNotes.Services;

/// <summary>
/// Generates PKCE (Proof Key for Code Exchange) code_verifier + code_challenge.
/// RFC 7636: https://datatracker.ietf.org/doc/html/rfc7636
/// </summary>
internal static class PkceHelper
{
    /// <summary>
    /// Generates a random code_verifier and computes the S256 code_challenge.
    /// </summary>
    internal static (string Verifier, string Challenge) Generate()
    {
        // 96 random bytes → 128-char base64url verifier (spec allows 43–128)
        var bytes    = RandomNumberGenerator.GetBytes(96);
        var verifier = Base64UrlEncode(bytes);

        // challenge = BASE64URL(SHA256(ASCII(verifier)))
        var hashBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(hashBytes);

        return (verifier, challenge);
    }

    internal static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
