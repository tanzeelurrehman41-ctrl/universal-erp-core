using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
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

    private sealed class FakeFifoLayer
    {
        public Guid LayerId { get; init; } = Guid.NewGuid();
        public Guid VariantId { get; init; }
        public DateTimeOffset ReceivedAt { get; init; }
        public decimal RemainingQty { get; set; }
        public decimal UnitCost { get; init; }
    }

    private sealed class FakeSalesRepository : ISalesRepository
    {
        public readonly List<CustomerSummary> Customers = [];
        public readonly List<SalesOrderSummary> Orders = [];
        public readonly List<SalesInvoiceSummary> Invoices = [];
        public readonly List<(Guid InvoiceId, Guid VariantId, decimal QtyDelta)> Movements = [];
        public readonly List<FakeFifoLayer> FifoLayers = [];
        public readonly List<(Guid InvoiceId, Guid LayerId, decimal ConsumedQty, decimal UnitCost)> Allocations = [];
        public readonly Dictionary<Guid, bool> VariantRequiresCogs = [];
        public readonly Dictionary<Guid, decimal> StockByVariant = [];

        public bool ThrowDuplicateCustomer = false;
        public bool ThrowDuplicateOrder = false;
        public bool ThrowInsufficientStock = false;
        public bool ThrowDuplicateInvoice = false;
        public bool ThrowGlPostingFailed = false;
        public bool AllowCostView = true;

        public void SeedFifoLayer(Guid variantId, decimal qty, decimal unitCost, DateTimeOffset? receivedAt = null)
        {
            FifoLayers.Add(new FakeFifoLayer
            {
                VariantId = variantId,
                RemainingQty = qty,
                UnitCost = unitCost,
                ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
            });
            StockByVariant[variantId] = (StockByVariant.TryGetValue(variantId, out var existing) ? existing : 0m) + qty;
        }

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

        public Task<bool> CanViewCostAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AllowCostView);

        public Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesInvoiceSummary>>(Invoices);

        public Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId));

        public Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (ThrowInsufficientStock) throw new InvalidOperationException("INSUFFICIENT_STOCK: Required 10 but available 2");
            if (ThrowDuplicateInvoice || Invoices.Any(i => i.ClientTransactionId == request.ClientTransactionId))
            {
                throw new Exception("23505: duplicate key value");
            }

            // Simulate transactional staging across stock, FIFO layers, allocations, and GL
            var stagedStock = new Dictionary<Guid, decimal>(StockByVariant);
            var stagedLayerQty = FifoLayers.ToDictionary(l => l.LayerId, l => l.RemainingQty);
            var stagedMovements = new List<(Guid InvoiceId, Guid VariantId, decimal QtyDelta)>();
            var stagedAllocations = new List<(Guid InvoiceId, Guid LayerId, decimal ConsumedQty, decimal UnitCost)>();

            var id = Guid.NewGuid();
            var resultLines = new List<SalesInvoiceLineSummary>();

            var reqLines = request.Lines ?? [];
            for (var i = 0; i < reqLines.Count; i++)
            {
                var l = reqLines[i];
                var requiresCogs = !VariantRequiresCogs.TryGetValue(l.VariantId, out var reqCogs) || reqCogs;
                decimal lineUnitCost = 0m;

                if (requiresCogs)
                {
                    var availableSlices = FifoLayers
                        .Where(fl => fl.VariantId == l.VariantId)
                        .Select(fl => new FifoCostLayerSlice(fl.LayerId, fl.ReceivedAt, stagedLayerQty[fl.LayerId], fl.UnitCost))
                        .ToList();

                    var allocRes = FifoCostAllocator.Allocate(availableSlices, l.Quantity);
                    lineUnitCost = allocRes.EffectiveUnitCost;
                    foreach (var alloc in allocRes.Allocations)
                    {
                        stagedLayerQty[alloc.LayerId] -= alloc.ConsumedQty;
                        stagedAllocations.Add((id, alloc.LayerId, alloc.ConsumedQty, alloc.UnitCost));
                    }
                }

                if (stagedStock.ContainsKey(l.VariantId))
                {
                    stagedStock[l.VariantId] -= l.Quantity;
                }
                stagedMovements.Add((id, l.VariantId, -l.Quantity));

                resultLines.Add(new SalesInvoiceLineSummary(
                    i + 1, l.OrderLineNumber, l.VariantId, "TEST-SKU", l.WarehouseId, "WH-1",
                    l.BinId, "BIN-1", l.Quantity, l.UnitOfMeasureId, "EA", l.UnitPrice,
                    lineUnitCost, l.TaxRate, l.Quantity * l.UnitPrice * (1 + l.TaxRate)));
            }

            if (ThrowGlPostingFailed)
            {
                // Rollback: discard all staged stock, movements, and FIFO allocations
                throw new InvalidOperationException("GL_POSTING_FAILED: Simulated accounting posting failure.");
            }

            var subtotal = reqLines.Sum(l => l.Quantity * l.UnitPrice);
            var tax = reqLines.Sum(l => l.Quantity * l.UnitPrice * l.TaxRate);
            var total = subtotal + tax - request.DiscountAmount;
            var paid = request.PaymentMethod == "credit" ? 0m : total;
            var inv = new SalesInvoiceSummary(
                id, request.InvoiceNumber, request.ClientTransactionId, request.OrderId,
                request.CustomerId, "Test Customer", request.PaymentMethod, "posted",
                request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                subtotal, tax, request.DiscountAmount, total,
                paid, request.Note, DateTimeOffset.UtcNow, resultLines);

            foreach (var kvp in stagedStock)
                StockByVariant[kvp.Key] = kvp.Value;
            foreach (var layer in FifoLayers)
                layer.RemainingQty = stagedLayerQty[layer.LayerId];
            Movements.AddRange(stagedMovements);
            Allocations.AddRange(stagedAllocations);
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
    public async Task PostInvoice_Valid_5Ea_At_75_Selling_Computes_Fifo_Cost_Without_Client_Input()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 23m, 45m, DateTimeOffset.Parse("2026-11-10T00:00:00Z"));
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-VALID-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(375m, result.Value!.TotalAmount);
        Assert.Equal(375m, result.Value!.PaidAmount);
        Assert.Equal(45m, result.Value!.Lines[0].UnitCost);
        Assert.Equal(18m, repo.StockByVariant[variantId]);
        Assert.Equal(18m, repo.FifoLayers[0].RemainingQty);
        Assert.Single(repo.Movements);
        Assert.Equal(-5m, repo.Movements[0].QtyDelta);
    }

    [Fact]
    public async Task PostInvoice_Consumes_Oldest_Fifo_Layer_First_And_Partially()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 10m, 40m, DateTimeOffset.Parse("2026-11-01T00:00:00Z")); // Older
        repo.SeedFifoLayer(variantId, 10m, 55m, DateTimeOffset.Parse("2026-11-05T00:00:00Z")); // Newer
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 4m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-FIFO-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(40m, result.Value!.Lines[0].UnitCost);
        Assert.Equal(6m, repo.FifoLayers[0].RemainingQty);  // Oldest partially consumed
        Assert.Equal(10m, repo.FifoLayers[1].RemainingQty); // Newer untouched
        Assert.Single(repo.Allocations);
        Assert.Equal(40m, repo.Allocations[0].UnitCost);
    }

    [Fact]
    public async Task PostInvoice_Consumes_Multiple_Layers_With_Different_Costs()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 3m, 40m, DateTimeOffset.Parse("2026-11-01T00:00:00Z")); // 3 * 40 = 120
        repo.SeedFifoLayer(variantId, 5m, 50m, DateTimeOffset.Parse("2026-11-02T00:00:00Z")); // 2 * 50 = 100
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-FIFO-002", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Total COGS = 120 + 100 = 220; effective unit cost = 220 / 5 = 44
        Assert.Equal(44m, result.Value!.Lines[0].UnitCost);
        Assert.Equal(0m, repo.FifoLayers[0].RemainingQty);
        Assert.Equal(3m, repo.FifoLayers[1].RemainingQty);
        Assert.Equal(2, repo.Allocations.Count);
    }

    [Fact]
    public async Task PostInvoice_Client_Supplied_UnitCost_Cannot_Override_Server_Valuation()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 10m, 45m, DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        // Malicious or stale client sends UnitCost: 999m
        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 2m, Guid.NewGuid(), UnitPrice: 75m, UnitCost: 999m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-TAMPER-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(45m, result.Value!.Lines[0].UnitCost);
        Assert.Equal(45m, repo.Allocations[0].UnitCost);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Insufficient_Valuation_Layers_Without_Modifying_Stock()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 3m, 45m, DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        repo.StockByVariant[variantId] = 10m; // Physical stock exists, but valuation layers only cover 3 EA
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-SHORT-LAYER", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INSUFFICIENT_VALUATION_LAYERS", result.Error);
        Assert.Empty(repo.Invoices);
        Assert.Empty(repo.Movements);
        Assert.Equal(10m, repo.StockByVariant[variantId]);
        Assert.Equal(3m, repo.FifoLayers[0].RemainingQty);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Missing_Valuation_Data_Without_Modifying_Stock()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.VariantRequiresCogs[variantId] = true;
        repo.StockByVariant[variantId] = 10m; // Zero valuation layers seeded
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 2m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-NO-LAYER", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("MISSING_VALUATION_DATA", result.Error);
        Assert.Empty(repo.Invoices);
        Assert.Empty(repo.Movements);
        Assert.Equal(10m, repo.StockByVariant[variantId]);
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
    public async Task PostInvoice_Idempotent_Retry_Does_Not_Consume_Fifo_Layers_Twice()
    {
        var repo = new FakeSalesRepository();
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 10m, 45m, DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var txId = Guid.NewGuid();
        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 4m, Guid.NewGuid(), 75m);
        var req = new PostSalesInvoiceRequest(txId, "INV-IDEM-001", Guid.NewGuid(), "cash", Lines: [line]);

        var first = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(6m, repo.FifoLayers[0].RemainingQty);

        // Replay with identical ClientTransactionId
        var retry = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value!.InvoiceId, retry.Value!.InvoiceId);
        Assert.Single(repo.Invoices);
        Assert.Single(repo.Movements);
        Assert.Single(repo.Allocations);
        Assert.Equal(6m, repo.FifoLayers[0].RemainingQty);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Zero_And_Negative_Selling_Price()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var zeroLine = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), UnitPrice: 0m);
        var zeroRes = await service.PostInvoiceAsync(new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-ZERO-001", Guid.NewGuid(), "cash", Lines: [zeroLine]), CancellationToken.None);
        Assert.False(zeroRes.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", zeroRes.Error);

        var negLine = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), UnitPrice: -15m);
        var negRes = await service.PostInvoiceAsync(new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-NEG-001", Guid.NewGuid(), "cash", Lines: [negLine]), CancellationToken.None);
        Assert.False(negRes.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", negRes.Error);
        Assert.Empty(repo.Invoices);
    }

    [Fact]
    public async Task PostInvoice_Rejects_Zero_Or_Negative_Invoice_Total_From_Discount()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), UnitPrice: 75m);
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
    public async Task PostInvoice_Failed_Gl_Posting_Rolls_Back_Invoice_Stock_And_Fifo_Layers()
    {
        var repo = new FakeSalesRepository { ThrowGlPostingFailed = true };
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 23m, 45m, DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 5m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-GLFAIL-001", Guid.NewGuid(), "cash", Lines: [line]);

        var result = await service.PostInvoiceAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("GL_POSTING_FAILED", result.Error);
        Assert.Empty(repo.Invoices);
        Assert.Empty(repo.Movements);
        Assert.Empty(repo.Allocations);
        Assert.Equal(23m, repo.StockByVariant[variantId]);
        Assert.Equal(23m, repo.FifoLayers[0].RemainingQty);
    }

    [Fact]
    public async Task PostInvoice_And_ListInvoices_Redact_UnitCost_For_Ordinary_Cashier()
    {
        var repo = new FakeSalesRepository { AllowCostView = false };
        var variantId = Guid.NewGuid();
        repo.SeedFifoLayer(variantId, 10m, 45m, DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var line = new PostSalesInvoiceLineRequest(variantId, Guid.NewGuid(), Guid.NewGuid(), 2m, Guid.NewGuid(), UnitPrice: 75m);
        var req = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-CASHIER-001", Guid.NewGuid(), "cash", Lines: [line]);

        var postResult = await service.PostInvoiceAsync(req, CancellationToken.None);
        Assert.True(postResult.IsSuccess);
        Assert.Null(postResult.Value!.Lines[0].UnitCost);

        var listed = await service.ListInvoicesAsync(CancellationToken.None);
        Assert.Single(listed);
        Assert.Null(listed[0].Lines[0].UnitCost);

        // Authorized finance/inventory user sees the server-computed FIFO cost
        repo.AllowCostView = true;
        var financeView = await service.GetInvoiceAsync(postResult.Value!.InvoiceId, CancellationToken.None);
        Assert.NotNull(financeView);
        Assert.Equal(45m, financeView!.Lines[0].UnitCost);
    }

    [Fact]
    public async Task Zero_Price_Sales_Have_No_Promotional_Workflow_And_Existing_Order_Workflows_Remain_Intact()
    {
        var repo = new FakeSalesRepository();
        var service = new SalesService(repo, new FakeTenantContextAccessor(TestContext));

        var freeLine = new PostSalesInvoiceLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, Guid.NewGuid(), UnitPrice: 0m);
        var invReq = new PostSalesInvoiceRequest(Guid.NewGuid(), "POS-PROMO-001", Guid.NewGuid(), "cash", Lines: [freeLine]);
        var invResult = await service.PostInvoiceAsync(invReq, CancellationToken.None);

        Assert.False(invResult.IsSuccess);
        Assert.Equal("INVALID_SELLING_PRICE", invResult.Error);

        var orderLine = new CreateSalesOrderLineRequest(Guid.NewGuid(), Guid.NewGuid(), 1m, Guid.NewGuid(), UnitPrice: 75m);
        var orderReq = new CreateSalesOrderRequest("SO-STD-001", Guid.NewGuid(), Guid.NewGuid(), OrderType: "standard", Lines: [orderLine]);
        var orderCreate = await service.CreateOrderAsync(orderReq, CancellationToken.None);
        Assert.True(orderCreate.IsSuccess);

        var orderConfirm = await service.ConfirmOrderAsync(orderCreate.Value!.OrderId, CancellationToken.None);
        Assert.True(orderConfirm.IsSuccess);
        Assert.Equal("confirmed", orderConfirm.Value!.OrderStatus);
    }
}
