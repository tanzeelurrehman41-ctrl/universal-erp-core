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
        public readonly List<(Guid InvoiceId, Guid VariantId, decimal QtyDelta)> Movements = [];
        public readonly Dictionary<Guid, bool> VariantRequiresCogs = [];
        public readonly Dictionary<Guid, decimal> StockByVariant = [];

        public bool ThrowDuplicateCustomer = false;
        public bool ThrowDuplicateOrder = false;
        public bool ThrowInsufficientStock = false;
        public bool ThrowDuplicateInvoice = false;
        public bool ThrowGlPostingFailed = false;

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

        public Task<bool> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
        {
            var idx = Customers.FindIndex(c => c.CustomerId == customerId);
            if (idx < 0) return Task.FromResult(false);
            var c = Customers[idx];
            Customers[idx] = c with
            {
                Name = request.Name,
                CustomerType = request.CustomerType,
                ContactName = request.ContactName,
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                NtnNumber = request.NtnNumber,
                StrnNumber = request.StrnNumber,
                CreditLimit = request.CreditLimit,
                PaymentTermsDays = request.PaymentTermsDays,
                IsActive = request.IsActive
            };
            return Task.FromResult(true);
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

        public Task<IReadOnlyDictionary<Guid, bool>> GetVariantRequiresCogsAsync(
            IReadOnlyCollection<Guid> variantIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<Guid, bool> map = variantIds.ToDictionary(
                id => id,
                id => !VariantRequiresCogs.TryGetValue(id, out var req) || req);
            return Task.FromResult(map);
        }

        public Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesInvoiceSummary>>(Invoices);

        public Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId));

        public Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (ThrowInsufficientStock) throw new InvalidOperationException("INSUFFICIENT_STOCK: Required 10 but available 2");
            if (ThrowDuplicateInvoice) throw new Exception("23505: duplicate key value");

            // Simulate transactional staging: stock deduction + GL posting must both succeed before commit
            var stagedStock = new Dictionary<Guid, decimal>(StockByVariant);
            var stagedMovements = new List<(Guid InvoiceId, Guid VariantId, decimal QtyDelta)>();

            var id = Guid.NewGuid();
            var lines = (request.Lines ?? []).Select((l, i) => new SalesInvoiceLineSummary(
                i + 1, l.OrderLineNumber, l.VariantId, "TEST-SKU", l.WarehouseId, "WH-1",
                l.BinId, "BIN-1", l.Quantity, l.UnitOfMeasureId, "EA", l.UnitPrice,
                l.UnitCost, l.TaxRate, l.Quantity * l.UnitPrice * (1 + l.TaxRate)
            )).ToList();

            foreach (var l in request.Lines ?? [])
            {
                if (stagedStock.ContainsKey(l.VariantId))
                {
                    stagedStock[l.VariantId] -= l.Quantity;
                }
                stagedMovements.Add((id, l.VariantId, -l.Quantity));
            }

            if (ThrowGlPostingFailed)
            {
                // Rollback: discard stagedStock & stagedMovements without committing
                throw new InvalidOperationException("GL_POSTING_FAILED: Simulated accounting posting failure.");
            }

            var subtotal = (request.Lines ?? []).Sum(l => l.Quantity * l.UnitPrice);
            var tax = (request.Lines ?? []).Sum(l => l.Quantity * l.UnitPrice * l.TaxRate);
            var total = subtotal + tax - request.DiscountAmount;
            var paid = request.PaymentMethod == "credit" ? 0m : total;
            var inv = new SalesInvoiceSummary(
                id, request.InvoiceNumber, request.ClientTransactionId, request.OrderId,
                request.CustomerId, "Test Customer", request.PaymentMethod, "posted",
                request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                subtotal, tax, request.DiscountAmount, total,
                paid, request.Note, DateTimeOffset.UtcNow, lines);

            foreach (var kvp in stagedStock)
                StockByVariant[kvp.Key] = kvp.Value;
            Movements.AddRange(stagedMovements);
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

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, Guid.NewGuid(), 2000m, UnitCost: 1200m);
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
        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 500m, UnitCost: 300m);
        var req = new PostSalesInvoiceRequest(txId, "INV-IDEM-001", Guid.NewGuid(), "cash", Lines: [line]);

        var first = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(first.IsSuccess);

        // Now simulate repository throwing unique constraint violation on replay
        repo.ThrowDuplicateInvoice = true;
        var retry = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value!.InvoiceId, retry.Value!.InvoiceId);
        Assert.Single(repo.Invoices);
        Assert.Single(repo.Movements);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Zero_Selling_Price()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), UnitPrice: 0m, UnitCost: 45m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-ZERO-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", result.Error);
        Assert.Empty(repo.Invoices);
        Assert.Empty(repo.Movements);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Negative_Selling_Price()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), UnitPrice: -15m, UnitCost: 45m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-NEG-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", result.Error);
        Assert.Empty(repo.Invoices);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Zero_Or_Negative_Invoice_Total_From_Discount()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), UnitPrice: 75m, UnitCost: 45m);
        var zeroTotalReq = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-DISC-ZERO", Guid.NewGuid(), "cash", DiscountAmount: 375m, Lines: [line]);

        var resZero = await service.PostInvoiceAsync(zeroTotalReq, CancellationToken.None);
        Assert.False(resZero.IsSuccess);
        Assert.Equal("INVALID_INVOICE_TOTAL", resZero.Error);

        var negTotalReq = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-DISC-NEG", Guid.NewGuid(), "cash", DiscountAmount: 400m, Lines: [line]);
        var resNeg = await service.PostInvoiceAsync(negTotalReq, CancellationToken.None);
        Assert.False(resNeg.IsSuccess);
        Assert.Equal("INVALID_INVOICE_TOTAL", resNeg.Error);
        Assert.Empty(repo.Invoices);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Missing_Or_Zero_Unit_Cost_When_Cogs_Required()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.VariantRequiresCogs[variantId] = true; // Inventory-tracked stock variant
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var zeroCostLine = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), UnitPrice: 75m, UnitCost: 0m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-NOCOST-001", Guid.NewGuid(), "cash", Lines: [zeroCostLine]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("UNIT_COST_REQUIRED", result.Error);

        var negCostLine = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), UnitPrice: 75m, UnitCost: -10m);
        var negReq = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-NEGCOST-001", Guid.NewGuid(), "cash", Lines: [negCostLine]);

        var negResult = await service.PostInvoiceAsync(negReq, CancellationToken.None);
        Assert.False(negResult.IsSuccess);
        Assert.Equal("INVALID_UNIT_COST", negResult.Error);
        Assert.Empty(repo.Invoices);
    }

    [Fact]
    public async Task PostInvoice_Valid_5Ea_At_75_Selling_And_45_Cost_Succeeds()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.VariantRequiresCogs[variantId] = true;
        repo.StockByVariant[variantId] = 23m;
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m, UnitCost: 45m, TaxRate: 0m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-VALID-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(375m, result.Value!.TotalAmount);
        Assert.Equal(375m, result.Value!.PaidAmount);
        Assert.Equal(45m, result.Value!.Lines[0].UnitCost);
        Assert.Equal(18m, repo.StockByVariant[variantId]);
        Assert.Single(repo.Movements);
        Assert.Equal(-5m, repo.Movements[0].QtyDelta);
    }

    [Fact]
    public async Task PostInvoice_Failed_Gl_Posting_Rolls_Back_Inventory_Issue()
    {
        var repo = new FakeSalesRepository { ThrowGlPostingFailed = true };
        var variantId = Guid.NewGuid();
        repo.VariantRequiresCogs[variantId] = true;
        repo.StockByVariant[variantId] = 23m;
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m, UnitCost: 45m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-GLFAIL-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("GL_POSTING_FAILED", result.Error);
        Assert.Empty(repo.Invoices);
        Assert.Empty(repo.Movements);
        Assert.Equal(23m, repo.StockByVariant[variantId]); // Inventory unchanged after rollback
    }

    [Fact]
    public async Task Zero_Price_Sales_Have_No_Promotional_Workflow_And_Existing_Order_Workflows_Remain_Intact()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        // Promotional/free-item invoices are not supported; zero-price invoices must be rejected
        var freeLine = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, Guid.NewGuid(), UnitPrice: 0m, UnitCost: 45m);
        var invReq = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-PROMO-001", Guid.NewGuid(), "cash", Lines: [freeLine]);
        var invResult = await service.PostInvoiceAsync(invReq, CancellationToken.None);

        Assert.False(invResult.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", invResult.Error);

        // Supported standard/pos/wholesale Sales Order creation & confirmation remain intact
        var orderLine = new CreateSalesOrderLineRequest(Guid.NewGuid(), Guid.NewGuid(), 1m, Guid.NewGuid(), UnitPrice: 75m);
        var orderReq = new CreateSalesOrderRequest("SO-STD-001", Guid.NewGuid(), Guid.NewGuid(), OrderType: "standard", Lines: [orderLine]);
        var orderCreate = await service.CreateOrderAsync(orderReq, CancellationToken.None);
        Assert.True(orderCreate.IsSuccess);

        var orderConfirm = await service.ConfirmOrderAsync(orderCreate.Value!.OrderId, CancellationToken.None);
        Assert.True(orderConfirm.IsSuccess);
        Assert.Equal("confirmed", orderConfirm.Value!.OrderStatus);
    }
}
