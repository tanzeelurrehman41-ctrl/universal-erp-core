namespace UniversalErp.Application.SaaS;

public sealed record PaymentExecutionResult(
    bool Success,
    string PaymentReference,
    string Status,
    string? FailureReason,
    decimal AmountCharged,
    string CurrencyCode,
    DateTimeOffset ProcessedAt,
    IReadOnlyDictionary<string, string> ProviderMetadata);

public sealed record RefundExecutionResult(
    bool Success,
    string RefundReference,
    string Status,
    string? FailureReason,
    decimal AmountRefunded,
    DateTimeOffset ProcessedAt);

public interface IPaymentProvider
{
    string ProviderName { get; }

    Task<PaymentExecutionResult> ProcessPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string currencyCode,
        string paymentMethodRef,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<RefundExecutionResult> ProcessRefundAsync(
        Guid tenantId,
        string paymentReference,
        decimal amount,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken);

    bool VerifyWebhookSignature(
        string payload,
        string signatureHeader,
        string secret);
}
