using UniversalErp.Application.SaaS;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class SandboxPaymentProviderTests
{
    private readonly SandboxPaymentProvider _provider = new();

    [Fact]
    public async Task ProcessPaymentAsync_WithValidMethod_ReturnsSucceededPayment()
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var key = "idemp_test_key_001";

        var result = await _provider.ProcessPaymentAsync(
            tenantId,
            invoiceId,
            amount: 45000.00m,
            currencyCode: "PKR",
            paymentMethodRef: "pm_card_visa_test",
            idempotencyKey: key,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("succeeded", result.Status);
        Assert.StartsWith("PAY-SBX-", result.PaymentReference);
        Assert.Equal(45000.00m, result.AmountCharged);
        Assert.Null(result.FailureReason);
        Assert.True(result.ProviderMetadata.ContainsKey("sandbox"));
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithDeclinedMethod_SimulatesCardDecline()
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var key = "idemp_test_fail_001";

        var result = await _provider.ProcessPaymentAsync(
            tenantId,
            invoiceId,
            amount: 45000.00m,
            currencyCode: "PKR",
            paymentMethodRef: "pm_card_decline_test",
            idempotencyKey: key,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("failed", result.Status);
        Assert.StartsWith("PAY-FAIL-", result.PaymentReference);
        Assert.Equal(0m, result.AmountCharged);
        Assert.Contains("decline", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRefundAsync_ReturnsRefundedResult()
    {
        var tenantId = Guid.NewGuid();
        var key = "idemp_refund_001";

        var result = await _provider.ProcessRefundAsync(
            tenantId,
            paymentReference: "PAY-SBX-12345678",
            amount: 15000.00m,
            reason: "Customer requested downgrade refund",
            idempotencyKey: key,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("refunded", result.Status);
        Assert.StartsWith("REF-SBX-", result.RefundReference);
        Assert.Equal(15000.00m, result.AmountRefunded);
    }

    [Fact]
    public void VerifyWebhookSignature_WithValidHMAC_ReturnsTrue()
    {
        var payload = "{\"event\":\"invoice.paid\",\"amount\":45000}";
        var secret = "whsec_test_secret_1234567890abcdef";

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var header = $"sha256={hash}";

        var isValid = _provider.VerifyWebhookSignature(payload, header, secret);
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyWebhookSignature_WithTamperedPayload_ReturnsFalse()
    {
        var payload = "{\"event\":\"invoice.paid\",\"amount\":45000}";
        var tampered = "{\"event\":\"invoice.paid\",\"amount\":99999}";
        var secret = "whsec_test_secret_1234567890abcdef";

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var header = $"sha256={hash}";

        var isValid = _provider.VerifyWebhookSignature(tampered, header, secret);
        Assert.False(isValid);
    }
}
