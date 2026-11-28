using System;
using System.Security.Cryptography;
using System.Text;

namespace UniversalErp.Application.Integrations;

public class WebhookSigningEngine
{
    public const int MaxRetryAttempts = 5;
    public const int BaseRetrySeconds = 30;

    public static byte[] ResolveMasterKey()
    {
        var keyEnv = Environment.GetEnvironmentVariable("WEBHOOK_ENCRYPTION_KEY")
            ?? Environment.GetEnvironmentVariable("UNIVERSAL_ERP_INTEGRATION_KEY");
        if (!string.IsNullOrWhiteSpace(keyEnv))
        {
            var keyBytes = Encoding.UTF8.GetBytes(keyEnv);
            using var sha = SHA256.Create();
            return sha.ComputeHash(keyBytes);
        }

        // Deterministic protected key derived from application seed for isolated staging/dev
        using var defaultSha = SHA256.Create();
        return defaultSha.ComputeHash(Encoding.UTF8.GetBytes("UniversalErp.WebhookSigning.MasterKey.v1.StagingSecret"));
    }

    public string EncryptSecret(string plaintextSecret, byte[]? key = null)
    {
        key ??= ResolveMasterKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextSecret);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[16];
        var ciphertext = new byte[plaintextBytes.Length];

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    public string DecryptSecret(string encryptedBase64, byte[]? key = null)
    {
        key ??= ResolveMasterKey();
        var combined = Convert.FromBase64String(encryptedBase64);
        if (combined.Length < 28)
            throw new CryptographicException("Invalid encrypted secret payload.");

        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[combined.Length - 28];

        Buffer.BlockCopy(combined, 0, nonce, 0, 12);
        Buffer.BlockCopy(combined, 12, tag, 0, 16);
        Buffer.BlockCopy(combined, 28, ciphertext, 0, ciphertext.Length);

        var plaintextBytes = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public (string SignatureHeader, string TimestampHeader) ComputeHeaders(string payload, string secret, DateTimeOffset timestamp)
    {
        var timestampStr = timestamp.ToUnixTimeSeconds().ToString();
        var signedContent = $"{timestampStr}.{payload}";
        var signature = ComputeSignature(signedContent, secret);
        return (signature, timestampStr);
    }

    public bool VerifySignature(string payload, string secret, string expectedSignatureHeader, string timestampHeader, long maxAgeSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(expectedSignatureHeader))
            return false;

        if (!long.TryParse(timestampHeader, out var timestamp))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > maxAgeSeconds)
            return false; // Replay prevention: expired timestamp

        var signedContent = $"{timestampHeader}.{payload}";
        var expectedSignature = ComputeSignature(signedContent, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(expectedSignatureHeader));
    }

    public string GenerateSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string HashSecret(string plaintextSecret)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(plaintextSecret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public DateTimeOffset? CalculateNextRetry(int currentAttemptCount, DateTimeOffset now)
    {
        if (currentAttemptCount >= MaxRetryAttempts)
            return null; // Exhausted -> Dead-letter

        // Bounded exponential backoff: 2^attempt * 30 seconds
        var seconds = (int)Math.Pow(2, currentAttemptCount) * BaseRetrySeconds;
        var maxBackoff = 86400; // 24 hours max
        var delay = Math.Min(seconds, maxBackoff);

        return now.AddSeconds(delay);
    }
}
