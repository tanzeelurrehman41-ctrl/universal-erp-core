using System;
using System.Security.Cryptography;
using System.Text;

namespace UniversalErp.Application.Integrations;

public class WebhookSigningEngine
{
    public const int MaxRetryAttempts = 5;
    public const int BaseRetrySeconds = 30;

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

    public bool VerifySignature(string payload, string secret, string expectedSignatureHeader, string timestampHeader)
    {
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
