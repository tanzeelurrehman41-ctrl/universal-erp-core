namespace UniversalErp.Application.Purchasing;

public sealed record OutboxProcessResult(
    int ProcessedEvents,
    int StockMovementsCreated,
    int JournalsPosted,
    IReadOnlyList<string> Details,
    int FailedEvents = 0,
    int DeadLetterEvents = 0);

public interface ISupplyChainOutboxRepository
{
    Task<OutboxProcessResult> ProcessPendingOutboxEventsAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<int> RedriveDeadLetterEventsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? eventId,
        CancellationToken cancellationToken);
}
