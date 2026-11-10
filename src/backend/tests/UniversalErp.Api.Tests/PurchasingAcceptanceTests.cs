using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class PurchasingAcceptanceTests
{
    private sealed class InMemoryPurchasingRepository : IPurchasingRepository
    {
        public readonly Dictionary<Guid, SupplierSummary> Suppliers = new();
        public readonly Dictionary<Guid, PurchaseOrderSummary> PurchaseOrders = new();
        public readonly Dictionary<Guid, GrnSummary> Grns = new();
        public readonly Dictionary<Guid, Guid> ClientTxToGrn = new();
        public readonly List<(Guid EventId, Guid GrnId, string EventType, string Payload)> OutboxEvents = new();

        public Task<IReadOnlyList<SupplierSummary>> ListSuppliersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SupplierSummary>>(Suppliers.Values.ToList());

        public Task<Guid> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken cancellationToken)
        {
            if (Suppliers.Values.Any(s => s.SupplierCode.Equals(request.SupplierCode, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("duplicate key value violates unique constraint 'supplier_code'");
            }

            var id = Guid.NewGuid();
            Suppliers[id] = new SupplierSummary(
                id, request.SupplierCode, request.Name, request.ContactName, request.Email,
                request.Phone, request.CurrencyCode, request.PaymentTermsDays, true);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest request, CancellationToken cancellationToken)
        {
            if (!Suppliers.TryGetValue(supplierId, out var existing)) return Task.FromResult(false);
            Suppliers[supplierId] = existing with
            {
                Name = request.Name,
                ContactName = request.ContactName,
                Email = request.Email,
                Phone = request.Phone,
                PaymentTermsDays = request.PaymentTermsDays,
                IsActive = request.IsActive
            };
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PurchaseOrderSummary>> ListPurchaseOrdersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PurchaseOrderSummary>>(PurchaseOrders.Values.ToList());

        public Task<PurchaseOrderSummary?> GetPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(PurchaseOrders.TryGetValue(purchaseOrderId, out var po) ? po : null);

        public Task<Guid> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (PurchaseOrders.Values.Any(p => p.PoNumber.Equals(request.PoNumber, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("duplicate key value violates unique constraint 'po_number'");
            }

            var id = Guid.NewGuid();
            var supplier = Suppliers.TryGetValue(request.SupplierId, out var s) ? s.Name : "Supplier";
            var lines = request.Lines.Select((l, idx) => new PurchaseOrderLineSummary(
                idx + 1, l.VariantId, "SKU-" + (idx + 1), l.OrderedQty, 0m, l.UnitOfMeasureId, "EA", l.UnitPrice, "open"
            )).ToList();

            PurchaseOrders[id] = new PurchaseOrderSummary(
                id, request.PoNumber, request.SupplierId, supplier, request.WarehouseId, "Main Warehouse",
                "draft", "PKR", request.OrderDate, request.ExpectedDate, request.Note, lines);

            return Task.FromResult(id);
        }

        public Task ConfirmPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
        {
            if (PurchaseOrders.TryGetValue(purchaseOrderId, out var existing))
            {
                PurchaseOrders[purchaseOrderId] = existing with { OrderStatus = "confirmed" };
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GrnSummary>> ListGrnsAsync(Guid? purchaseOrderId, CancellationToken cancellationToken)
        {
            var list = purchaseOrderId.HasValue
                ? Grns.Values.Where(g => g.PurchaseOrderId == purchaseOrderId.Value).ToList()
                : Grns.Values.ToList();
            return Task.FromResult<IReadOnlyList<GrnSummary>>(list);
        }

        public Task<GrnSummary?> GetGrnAsync(Guid grnId, CancellationToken cancellationToken) =>
            Task.FromResult(Grns.TryGetValue(grnId, out var g) ? g : null);

        public Task<Guid> PostGrnAsync(PostGrnRequest request, Guid createdBy, CancellationToken cancellationToken)
        {
            if (ClientTxToGrn.TryGetValue(request.ClientTransactionId, out var existingGrnId))
            {
                throw new InvalidOperationException("23505: duplicate key value violates unique constraint 'client_transaction_id'");
            }

            if (!PurchaseOrders.TryGetValue(request.PurchaseOrderId, out var po))
            {
                throw new InvalidOperationException("Purchase order not found.");
            }

            var grnId = Guid.NewGuid();
            var grnLines = request.Lines.Select((l, idx) => new GrnLineSummary(
                idx + 1, l.PoLineNumber, l.VariantId, "SKU", l.BinId, "BIN-01", l.ReceivedQty, "EA", l.UnitPrice
            )).ToList();

            var summary = new GrnSummary(
                grnId, request.GrnNumber, request.PurchaseOrderId, po.PoNumber, po.SupplierId, po.SupplierName,
                po.WarehouseId, po.WarehouseName, "posted", request.ReceivedDate, request.SupplierReference,
                request.Note, DateTimeOffset.UtcNow, grnLines);

            Grns[grnId] = summary;
            ClientTxToGrn[request.ClientTransactionId] = grnId;

            // Update PO lines received quantity and status
            var updatedPoLines = new List<PurchaseOrderLineSummary>();
            foreach (var poLine in po.Lines)
            {
                var grnLineMatch = request.Lines.FirstOrDefault(gl => gl.PoLineNumber == poLine.LineNumber);
                if (grnLineMatch != null)
                {
                    var newRcvd = poLine.ReceivedQty + grnLineMatch.ReceivedQty;
                    var newStatus = newRcvd >= poLine.OrderedQty ? "received" : "partially_received";
                    updatedPoLines.Add(poLine with { ReceivedQty = newRcvd, LineStatus = newStatus });
                }
                else
                {
                    updatedPoLines.Add(poLine);
                }
            }

            var allReceived = updatedPoLines.All(l => l.LineStatus is "received" or "cancelled");
            var poStatus = allReceived ? "received" : "partially_received";
            PurchaseOrders[request.PurchaseOrderId] = po with { OrderStatus = poStatus, Lines = updatedPoLines };

            OutboxEvents.Add((Guid.NewGuid(), grnId, "grn.posted", "{}"));
            return Task.FromResult(grnId);
        }
    }

    private sealed class InMemorySupplyChainOutboxRepository(
        InMemoryPurchasingRepository purchasingRepo,
        Dictionary<string, decimal> inventoryBalances) : ISupplyChainOutboxRepository
    {
        public Task<OutboxProcessResult> ProcessPendingOutboxEventsAsync(
            Guid tenantId, Guid companyId, Guid userId, CancellationToken cancellationToken)
        {
            var processed = 0;
            var movementsCreated = 0;

            foreach (var evt in purchasingRepo.OutboxEvents.ToList())
            {
                if (purchasingRepo.Grns.TryGetValue(evt.GrnId, out var grn))
                {
                    foreach (var line in grn.Lines)
                    {
                        var key = $"{grn.WarehouseId}:{line.BinId}:{line.VariantId}";
                        inventoryBalances[key] = (inventoryBalances.TryGetValue(key, out var curr) ? curr : 0m) + line.ReceivedQty;
                    }
                    movementsCreated++;
                    processed++;
                }
            }

            purchasingRepo.OutboxEvents.Clear();
            return Task.FromResult(new OutboxProcessResult(processed, movementsCreated, 1, ["Processed successfully"]));
        }

        public Task<int> RedriveDeadLetterEventsAsync(
            Guid tenantId, Guid companyId, Guid? eventId, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private static readonly TenantContext DemoContext = new(
        TenantId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
        CompanyId: Guid.Parse("22222222-2222-4222-8222-222222222223"),
        UserId: Guid.Parse("3719490d-8010-4e2e-9bd7-a83f783a075b"));

    private sealed class ContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Complete_Purchasing_To_Inventory_Lifecycle_Acceptance_Test()
    {
        var purchasingRepo = new InMemoryPurchasingRepository();
        var inventoryBalances = new Dictionary<string, decimal>();
        var outboxRepo = new InMemorySupplyChainOutboxRepository(purchasingRepo, inventoryBalances);
        var ctxAccessor = new ContextAccessor(DemoContext);
        var outboxService = new SupplyChainOutboxService(outboxRepo, ctxAccessor);
        var purchasingService = new PurchasingService(purchasingRepo, ctxAccessor, outboxService);

        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var uomId = Guid.NewGuid();

        // ── Phase 1: Supplier Management ────────────────────────────────────
        var supReq = new CreateSupplierRequest("SUP-DEMO-01", "Demo Tooling Supplier", "John Doe", "john@supplier.com", "+923001234567", null, "PKR", 30);
        var supResult = await purchasingService.CreateSupplierAsync(supReq, CancellationToken.None);
        Assert.True(supResult.IsSuccess);
        Assert.Equal("SUP-DEMO-01", supResult.Value!.SupplierCode);

        // Validation: Duplicate supplier code rejected
        var dupSupResult = await purchasingService.CreateSupplierAsync(supReq, CancellationToken.None);
        Assert.False(dupSupResult.IsSuccess);
        Assert.Equal("DUPLICATE_SUPPLIER_CODE", dupSupResult.Error);

        var supplierId = supResult.Value.SupplierId;

        // ── Phase 2: Purchase Order Creation (Draft) ─────────────────────────
        var poReq = new CreatePurchaseOrderRequest(
            "PO-DEMO-2026-001",
            supplierId,
            warehouseId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            "Initial demo stock order",
            [new CreatePurchaseOrderLineRequest(variantId, 50m, uomId, 150.00m)]
        );

        var poResult = await purchasingService.CreatePurchaseOrderAsync(poReq, CancellationToken.None);
        Assert.True(poResult.IsSuccess);
        var poId = poResult.Value!.PurchaseOrderId;
        Assert.Equal("draft", poResult.Value.OrderStatus);
        Assert.Equal(50m, poResult.Value.Lines[0].OrderedQty);
        Assert.Equal(0m, poResult.Value.Lines[0].ReceivedQty);
        Assert.Equal("open", poResult.Value.Lines[0].LineStatus);

        // Invariant: Cannot receive against unconfirmed (draft) PO
        var prematureGrnReq = new PostGrnRequest(
            Guid.NewGuid(), "GRN-EARLY-01", poId, DateOnly.FromDateTime(DateTime.UtcNow), null, null,
            [new PostGrnLineRequest(1, variantId, binId, 20m, uomId, 150.00m)]);
        var prematureGrnResult = await purchasingService.PostGrnAsync(prematureGrnReq, CancellationToken.None);
        Assert.False(prematureGrnResult.IsSuccess);
        Assert.Equal("PO_NOT_CONFIRMED", prematureGrnResult.Error);

        // ── Phase 3: Purchase Order Confirmation / Approval ─────────────────
        var confirmResult = await purchasingService.ConfirmPurchaseOrderAsync(poId, CancellationToken.None);
        Assert.True(confirmResult.IsSuccess);
        Assert.Equal("confirmed", confirmResult.Value!.OrderStatus);

        // Invariant: Cannot re-confirm an already confirmed PO
        var reconfirmResult = await purchasingService.ConfirmPurchaseOrderAsync(poId, CancellationToken.None);
        Assert.False(reconfirmResult.IsSuccess);
        Assert.Equal("PO_NOT_IN_DRAFT", reconfirmResult.Error);

        // ── Phase 4: Partial Goods Receipt (20 EA received) ─────────────────
        var grnTx1 = Guid.NewGuid();
        var grnReq1 = new PostGrnRequest(
            grnTx1, "GRN-DEMO-001", poId, DateOnly.FromDateTime(DateTime.UtcNow), "DELIVERY-CHALAN-101", "First delivery",
            [new PostGrnLineRequest(1, variantId, binId, 20m, uomId, 150.00m)]);

        var grnResult1 = await purchasingService.PostGrnAsync(grnReq1, CancellationToken.None);
        Assert.True(grnResult1.IsSuccess);
        Assert.Equal("posted", grnResult1.Value!.GrnStatus);

        // Verify PO transitioned to partially_received
        var poAfterGrn1 = await purchasingService.GetPurchaseOrderAsync(poId, CancellationToken.None);
        Assert.NotNull(poAfterGrn1);
        Assert.Equal("partially_received", poAfterGrn1!.OrderStatus);
        Assert.Equal(20m, poAfterGrn1.Lines[0].ReceivedQty);
        Assert.Equal("partially_received", poAfterGrn1.Lines[0].LineStatus);

        // Invariant: Inventory balance is updated to 20 EA
        var balKey = $"{warehouseId}:{binId}:{variantId}";
        Assert.Equal(20m, inventoryBalances[balKey]);

        // ── Phase 5: Idempotent GRN Replay Protection ───────────────────────
        // Client retries same GRN with same transaction ID
        var grnReplayResult = await purchasingService.PostGrnAsync(grnReq1, CancellationToken.None);
        Assert.True(grnReplayResult.IsSuccess);
        Assert.Equal(grnResult1.Value.GrnId, grnReplayResult.Value!.GrnId);

        // Invariant: Balance must NOT be doubled on retry
        Assert.Equal(20m, inventoryBalances[balKey]);
        Assert.Equal(20m, (await purchasingService.GetPurchaseOrderAsync(poId, CancellationToken.None))!.Lines[0].ReceivedQty);

        // ── Phase 6: Final Goods Receipt (Remaining 30 EA) ───────────────────
        var grnTx2 = Guid.NewGuid();
        var grnReq2 = new PostGrnRequest(
            grnTx2, "GRN-DEMO-002", poId, DateOnly.FromDateTime(DateTime.UtcNow), "DELIVERY-CHALAN-102", "Final balance",
            [new PostGrnLineRequest(1, variantId, binId, 30m, uomId, 150.00m)]);

        var grnResult2 = await purchasingService.PostGrnAsync(grnReq2, CancellationToken.None);
        Assert.True(grnResult2.IsSuccess);

        // Verify PO transitioned to fully received
        var poFinal = await purchasingService.GetPurchaseOrderAsync(poId, CancellationToken.None);
        Assert.NotNull(poFinal);
        Assert.Equal("received", poFinal!.OrderStatus);
        Assert.Equal(50m, poFinal.Lines[0].ReceivedQty);
        Assert.Equal("received", poFinal.Lines[0].LineStatus);

        // Invariant: Inventory balance is now exactly 50 EA
        Assert.Equal(50m, inventoryBalances[balKey]);
    }
}
