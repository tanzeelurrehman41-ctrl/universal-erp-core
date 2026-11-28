using System.Security.Cryptography;
using System.Text;

namespace UniversalErp.Application.SaaS;

public sealed class SandboxPaymentProvider : IPaymentProvider
{
    public string ProviderName => "sandbox";

    public Task<PaymentExecutionResult> ProcessPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string currencyCode,
        string paymentMethodRef,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Sandbox payment simulation rules
        var normalizedMethod = paymentMethodRef.ToLowerInvariant().Trim();

        if (normalizedMethod.Contains("decline") || normalizedMethod.Contains("fail"))
        {
            return Task.FromResult(new PaymentExecutionResult(
                Success: false,
                PaymentReference: $"PAY-FAIL-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
                Status: "failed",
                FailureReason: "Simulated sandbox card decline: Insufficient test funds.",
                AmountCharged: 0.00m,
                CurrencyCode: currencyCode,
                ProcessedAt: DateTimeOffset.UtcNow,
                ProviderMetadata: new Dictionary<string, string>
                {
                    ["sandbox"] = "true",
                    ["simulated_decline"] = "true",
                    ["idempotency_key"] = idempotencyKey
                }));
        }

        var refCode = $"PAY-SBX-{BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..16].Replace("-", "")}";

        return Task.FromResult(new PaymentExecutionResult(
            Success: true,
            PaymentReference: refCode,
            Status: "succeeded",
            FailureReason: null,
            AmountCharged: amount,
            CurrencyCode: currencyCode,
            ProcessedAt: DateTimeOffset.UtcNow,
            ProviderMetadata: new Dictionary<string, string>
            {
                ["sandbox"] = "true",
                ["provider"] = "UniversalErp.SandboxPaymentEngine",
                ["idempotency_key"] = idempotencyKey,
                ["payment_method"] = paymentMethodRef
            }));
    }

    public Task<RefundExecutionResult> ProcessRefundAsync(
        Guid tenantId,
        string paymentReference,
        decimal amount,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var refundRef = $"REF-SBX-{BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..16].Replace("-", "")}";

        return Task.FromResult(new RefundExecutionResult(
            Success: true,
            RefundReference: refundRef,
            Status: "refunded",
            FailureReason: null,
            AmountRefunded: amount,
            ProcessedAt: DateTimeOffset.UtcNow));
    }

    public bool VerifyWebhookSignature(string payload, string signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        var cleanHeader = signatureHeader.Trim();
        if (cleanHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            cleanHeader = cleanHeader["sha256=".Length..];
        }

        var headerBytes = Encoding.UTF8.GetBytes(cleanHeader.ToLowerInvariant());
        var expectedHeaderBytes = Encoding.UTF8.GetBytes(expectedHex);

        return CryptographicOperations.FixedTimeEquals(headerBytes, expectedHeaderBytes);
    }
}
