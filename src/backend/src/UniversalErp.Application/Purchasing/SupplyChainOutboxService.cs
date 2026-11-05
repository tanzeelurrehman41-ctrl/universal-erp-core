using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Purchasing;

public sealed class SupplyChainOutboxService(
    ISupplyChainOutboxRepository repository,
    ITenantContextAccessor tenantContext)
{
    public async Task<Result<OutboxProcessResult>> ProcessPendingEventsAsync(CancellationToken cancellationToken)
    {
        var context = tenantContext.Current;
        var result = await repository.ProcessPendingOutboxEventsAsync(
            context.TenantId,
            context.CompanyId,
            context.UserId,
            cancellationToken);

        return Result<OutboxProcessResult>.Success(result);
    }

    public async Task<Result<int>> RedriveDeadLetterEventsAsync(Guid? eventId, CancellationToken cancellationToken)
    {
        var context = tenantContext.Current;
        var redriven = await repository.RedriveDeadLetterEventsAsync(
            context.TenantId,
            context.CompanyId,
            eventId,
            cancellationToken);

        return Result<int>.Success(redriven);
    }
}
