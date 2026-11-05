using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class SupplyChainOutboxServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class FakeOutboxRepository : ISupplyChainOutboxRepository
    {
        public int CallCount = 0;
        public OutboxProcessResult ResultToReturn = new(1, 1, 1, ["Movement created", "Journal posted"]);

        public Task<OutboxProcessResult> ProcessPendingOutboxEventsAsync(
            Guid tenantId,
            Guid companyId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ResultToReturn);
        }

        public Task<int> RedriveDeadLetterEventsAsync(
            Guid tenantId,
            Guid companyId,
            Guid? eventId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }
    }

    private static readonly TenantContext TestContext = new(
        TenantId: Guid.Parse("00000000-0000-4000-8000-000000000001"),
        CompanyId: Guid.Parse("00000000-0000-4000-8000-000000000002"),
        UserId: Guid.Parse("00000000-0000-4000-8000-000000000003"));

    [Fact]
    public async Task ProcessPendingEvents_Invokes_Repository_With_TenantContext()
    {
        var repo = new FakeOutboxRepository();
        var service = new SupplyChainOutboxService(repo, new FakeTenantContextAccessor(TestContext));

        var result = await service.ProcessPendingEventsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repo.CallCount);
        Assert.Equal(1, result.Value!.ProcessedEvents);
        Assert.Equal(1, result.Value!.StockMovementsCreated);
        Assert.Equal(1, result.Value!.JournalsPosted);
    }
}
