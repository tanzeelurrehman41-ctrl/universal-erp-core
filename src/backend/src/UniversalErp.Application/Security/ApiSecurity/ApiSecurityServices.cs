using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace UniversalErp.Application.Security.ApiSecurity;

public sealed record RateLimitResult(
    bool IsAllowed,
    double CurrentTokens,
    double MaxTokens,
    TimeSpan RetryAfter);

public sealed class TokenBucketRateLimiter
{
    private sealed class BucketState
    {
        public double Tokens { get; set; }
        public DateTimeOffset LastRefill { get; set; }
    }

    private readonly ConcurrentDictionary<string, BucketState> _buckets = new();
    private readonly double _capacity;
    private readonly double _refillPerSecond;

    public TokenBucketRateLimiter(double capacity = 100, double refillPerSecond = 10)
    {
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
    }

    public RateLimitResult CheckLimit(string key, double cost = 1.0)
    {
        var now = DateTimeOffset.UtcNow;
        var state = _buckets.GetOrAdd(key, _ => new BucketState { Tokens = _capacity, LastRefill = now });

        lock (state)
        {
            var elapsedSeconds = (now - state.LastRefill).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                state.Tokens = Math.Min(_capacity, state.Tokens + (elapsedSeconds * _refillPerSecond));
                state.LastRefill = now;
            }

            if (state.Tokens >= cost)
            {
                state.Tokens -= cost;
                return new(IsAllowed: true, CurrentTokens: state.Tokens, MaxTokens: _capacity, RetryAfter: TimeSpan.Zero);
            }

            var shortfall = cost - state.Tokens;
            var retrySeconds = shortfall / _refillPerSecond;
            return new(IsAllowed: false, CurrentTokens: state.Tokens, MaxTokens: _capacity, RetryAfter: TimeSpan.FromSeconds(retrySeconds));
        }
    }
}

public sealed class WebhookSecurityService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonceCache = new();
    private readonly TimeSpan _maxSkew;

    public WebhookSecurityService(TimeSpan? maxSkew = null)
    {
        _maxSkew = maxSkew ?? TimeSpan.FromMinutes(5);
    }

    public static string GenerateHmacSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyHmacSignature(string payload, string signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
            return false;

        var expectedSignature = GenerateHmacSignature(payload, secret);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var providedBytes = Encoding.UTF8.GetBytes(signatureHeader.Trim().ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    public bool ValidateReplayAndTimestamp(string nonce, DateTimeOffset timestamp)
    {
        var now = DateTimeOffset.UtcNow;
        var diff = (now - timestamp).Duration();
        if (diff > _maxSkew)
        {
            return false; // Timestamp outside acceptable 5-minute replay window
        }

        // Check and store nonce
        if (!_nonceCache.TryAdd(nonce, timestamp))
        {
            return false; // Nonce already seen -> Replay attack blocked
        }

        // Housekeeping: remove nonces older than max skew
        if (_nonceCache.Count > 1000)
        {
            var cutoff = now - _maxSkew;
            foreach (var kvp in _nonceCache)
            {
                if (kvp.Value < cutoff)
                {
                    _nonceCache.TryRemove(kvp.Key, out _);
                }
            }
        }

        return true;
    }

    public static string EncryptAesGcm(string plainText, byte[] key256)
    {
        if (key256.Length != 32)
            throw new ArgumentException("Key must be exactly 256 bits (32 bytes).", nameof(key256));

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[12]; // 96-bit nonce
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16]; // 128-bit authentication tag

        using var aesGcm = new AesGcm(key256, 16);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Combined: nonce(12) + tag(16) + cipher
        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public static string DecryptAesGcm(string cipherTextBase64, byte[] key256)
    {
        if (key256.Length != 32)
            throw new ArgumentException("Key must be exactly 256 bits (32 bytes).", nameof(key256));

        var combined = Convert.FromBase64String(cipherTextBase64);
        if (combined.Length < 28)
            throw new InvalidOperationException("Invalid ciphertext payload length.");

        var nonce = new byte[12];
        var tag = new byte[16];
        var cipherBytes = new byte[combined.Length - 28];

        Buffer.BlockCopy(combined, 0, nonce, 0, 12);
        Buffer.BlockCopy(combined, 12, tag, 0, 16);
        Buffer.BlockCopy(combined, 28, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aesGcm = new AesGcm(key256, 16);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
