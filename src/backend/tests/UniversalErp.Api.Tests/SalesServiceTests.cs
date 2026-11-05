using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class SalesServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class FakeSalesRepository : ISalesRepository
    {
        public readonly List<CustomerSummary> Customers = [];
        public readonly List<SalesOrderSummary> Orders = [];
        public readonly List<SalesInvoiceSummary> Invoices = [];

        public bool ThrowDuplicateCustomer = false;
        public bool ThrowDuplicateOrder = false;
        public bool ThrowInsufficientStock = false;
        public bool ThrowDuplicateInvoice = false;

        public Task<IReadOnlyList<CustomerSummary>> ListCustomersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CustomerSummary>>(Customers);

        public Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
            Task.FromResult(Customers.FirstOrDefault(c => c.CustomerId == customerId));

        public Task<Guid> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        {
            if (ThrowDuplicateCustomer) throw new Exception("23505: duplicate key value violates unique constraint");
            var id = Guid.NewGuid();
            Customers.Add(new CustomerSummary(
                id, request.CustomerCode, request.Name, request.CustomerType,
                request.ContactName, request.Email, request.Phone, request.Address,
                request.NtnNumber, request.StrnNumber, request.CreditLimit, 0,
                request.CurrencyCode, request.PaymentTermsDays, true));
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<SalesOrderSummary>> ListOrdersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesOrderSummary>>(Orders);

        public Task<SalesOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.FirstOrDefault(o => o.OrderId == orderId));

        public Task<Guid> CreateOrderAsync(CreateSalesOrderRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (ThrowDuplicateOrder) throw new Exception("23505: duplicate key value violates unique constraint");
            var id = Guid.NewGuid();
            var lines = (request.Lines ?? []).Select((l, i) => new SalesOrderLineSummary(
                i + 1, l.VariantId, "TEST-SKU", l.BinId, "BIN-1", l.OrderedQty, 0,
                l.UnitOfMeasureId, "EA", l.UnitPrice, l.TaxRate, l.OrderedQty * l.UnitPrice, "pending"
            )).ToList();

            var total = lines.Sum(l => l.LineTotal);
            Orders.Add(new SalesOrderSummary(
                id, request.OrderNumber, request.CustomerId, "Test Customer",
                request.WarehouseId, "Main Warehouse", request.OrderType, "draft",
                "PKR", request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                total, 0, 0, total, request.Note, DateTimeOffset.UtcNow, lines));
            return Task.FromResult(id);
        }

        public Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var idx = Orders.FindIndex(o => o.OrderId == orderId);
            if (idx >= 0)
            {
                var o = Orders[idx];
                Orders[idx] = new SalesOrderSummary(
                    o.OrderId, o.OrderNumber, o.CustomerId, o.CustomerName,
                    o.WarehouseId, o.WarehouseName, o.OrderType, "confirmed",
                    o.CurrencyCode, o.OrderDate, o.SubtotalAmount, o.TaxAmount,
                    o.DiscountAmount, o.TotalAmount, o.Note, o.CreatedAt, o.Lines);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesInvoiceSummary>>(Invoices);

        public Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId));

        public Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (ThrowInsufficientStock) throw new InvalidOperationException("INSUFFICIENT_STOCK: Required 10 but available 2");
            if (ThrowDuplicateInvoice) throw new Exception("23505: duplicate key value");

            var id = Guid.NewGuid();
            var lines = (request.Lines ?? []).Select((l, i) => new SalesInvoiceLineSummary(
                i + 1, l.OrderLineNumber, l.VariantId, "TEST-SKU", l.WarehouseId, "WH-1",
                l.BinId, "BIN-1", l.Quantity, l.UnitOfMeasureId, "EA", l.UnitPrice,
                l.UnitCost, l.TaxRate, l.Quantity * l.UnitPrice
            )).ToList();

            var total = lines.Sum(l => l.LineTotal);
            var inv = new SalesInvoiceSummary(
                id, request.InvoiceNumber, request.ClientTransactionId, request.OrderId,
                request.CustomerId, "Test Customer", request.PaymentMethod, "posted",
                request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                total, 0, request.DiscountAmount, total - request.DiscountAmount,
                total - request.DiscountAmount, request.Note, DateTimeOffset.UtcNow, lines);

            Invoices.Add(inv);
            return Task.FromResult(id);
        }
    }

    private static readonly TenantContext TestContext = new(
        TenantId: Guid.Parse("00000000-0000-4000-8000-000000000001"),
        CompanyId: Guid.Parse("00000000-0000-4000-8000-000000000002"),
        UserId: Guid.Parse("00000000-0000-4000-8000-000000000003"));

    [Fact]
    public async Task CreateCustomer_Validates_Code_And_Succeeds()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var req = new CreateCustomerRequest("CUST-001", "Al-Rahim Mart", "wholesale", CreditLimit: 50000);
        var result = await service.CreateCustomerAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("CUST-001", result.Value!.CustomerCode);
        Assert.Equal("wholesale", result.Value!.CustomerType);
        Assert.Equal(50000m, result.Value!.CreditLimit);
    }

    [Fact]
    public async Task CreateCustomer_Fails_On_Invalid_Code_Or_Negative_Credit()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var resBadCode = await service.CreateCustomerAsync(new CreateCustomerRequest("BAD CODE WITH SPACES", "Test"), CancellationToken.None);
        Assert.False(resBadCode.IsSuccess);
        Assert.Equal("INVALID_CUSTOMER_CODE", resBadCode.Error);

        var resBadCredit = await service.CreateCustomerAsync(new CreateCustomerRequest("CUST-01", "Test", CreditLimit: -100), CancellationToken.None);
        Assert.False(resBadCredit.IsSuccess);
        Assert.Equal("INVALID_CREDIT_LIMIT", resBadCredit.Error);
    }

    [Fact]
    public async Task CreateCustomer_Handles_Duplicate_Code()
    {
        var repo = new FakeSalesRepository { ThrowDuplicateCustomer = true };
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var result = await service.CreateCustomerAsync(new CreateCustomerRequest("CUST-001", "Al-Rahim Mart"), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("DUPLICATE_CUSTOMER_CODE", result.Error);
    }

    [Fact]
    public async Task CreateOrder_And_ConfirmOrder_Workflow()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var customerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var line = new CreateSalesOrderLineRequest(Guid.NewGuid(), Guid.NewGuid(), 10, Guid.NewGuid(), 1500m);
        var req = new CreateSalesOrderRequest("SO-2026-001", customerId, warehouseId, Lines: [line]);

        var createResult = await service.CreateOrderAsync(req, CancellationToken.None);
        Assert.True(createResult.IsSuccess);
        Assert.Equal("draft", createResult.Value!.OrderStatus);
        Assert.Equal(15000m, createResult.Value!.TotalAmount);

        var confirmResult = await service.ConfirmOrderAsync(createResult.Value!.OrderId, CancellationToken.None);
        Assert.True(confirmResult.IsSuccess);
        Assert.Equal("confirmed", confirmResult.Value!.OrderStatus);
    }

    [Fact]
    public async Task CreateOrder_Requires_Lines()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var req = new CreateSalesOrderRequest("SO-2026-001", Guid.NewGuid(), Guid.NewGuid(), Lines: []);
        var result = await service.CreateOrderAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_REQUIRES_LINES", result.Error);
    }

    [Fact]
    public async Task PostInvoice_Succeeds_And_Records_Lines()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var txId = Guid.NewGuid();
        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), 2000m, UnitCost: 1200m);
        var req = new PostSalesInvoiceRequest(txId, "INV-2026-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("INV-2026-001", result.Value!.InvoiceNumber);
        Assert.Equal("cash", result.Value!.PaymentMethod);
        Assert.Equal(10000m, result.Value!.TotalAmount);
    }

    [Fact]
    public async Task PostInvoice_Returns_InsufficientStock_Error()
    {
        var repo = new FakeSalesRepository { ThrowInsufficientStock = true };
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, Guid.NewGuid(), 2000m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "INV-2026-002", Guid.NewGuid(), "credit", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("INSUFFICIENT_STOCK", result.Error);
    }

    [Fact]
    public async Task PostInvoice_Handles_Duplicate_Or_Idempotent_Replay()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var txId = Guid.NewGuid();
        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 500m);
        var req = new PostSalesInvoiceRequest(txId, "INV-IDEM-001", Guid.NewGuid(), "cash", Lines: [line]);

        var first = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(first.IsSuccess);

        // Now simulate repository throwing unique constraint violation
        repo.ThrowDuplicateInvoice = true;
        var retry = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value!.InvoiceId, retry.Value!.InvoiceId);
    }
}
