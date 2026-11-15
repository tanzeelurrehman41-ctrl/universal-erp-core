using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase6PurchasingServiceTests
{
    private sealed class MockTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    private sealed class Phase6MockPurchasingRepository : IPurchasingRepository
    {
        public PurchasingTolerancePolicySummary Policy = new(Guid.NewGuid(), 5.0m, 5.0m, true, DateTimeOffset.UtcNow);
        public List<PurchaseOrderSummary> PurchaseOrders = [];
        public List<PurchaseInspectionSummary> Inspections = [];
        public List<QuarantineReleaseSummary> QuarantineReleases = [];
        public List<SupplierInvoiceSummary> Invoices = [];
        public List<PurchaseReturnSummary> Returns = [];
        public List<SupplierDebitNoteSummary> DebitNotes = [];
        public List<SupplierBalanceSummary> Balances = [];

        public Task<IReadOnlyList<SupplierSummary>> ListSuppliersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SupplierSummary>>([]);
        public Task<Guid> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<bool> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest request, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<PurchaseOrderSummary>> ListPurchaseOrdersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PurchaseOrderSummary>>(PurchaseOrders);
        public Task<PurchaseOrderSummary?> GetPurchaseOrderAsync(Guid id, CancellationToken ct) => Task.FromResult(PurchaseOrders.FirstOrDefault(p => p.PurchaseOrderId == id));
        public Task<Guid> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest req, Guid createdBy, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task ConfirmPurchaseOrderAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<GrnSummary>> ListGrnsAsync(Guid? poId, CancellationToken ct) => Task.FromResult<IReadOnlyList<GrnSummary>>([]);
        public Task<GrnSummary?> GetGrnAsync(Guid id, CancellationToken ct) => Task.FromResult<GrnSummary?>(null);
        public Task<Guid> PostGrnAsync(PostGrnRequest req, Guid createdBy, CancellationToken ct) => Task.FromResult(Guid.NewGuid());

        public Task<PurchasingTolerancePolicySummary> GetTolerancePolicyAsync(CancellationToken ct) => Task.FromResult(Policy);
        public Task<bool> UpdateTolerancePolicyAsync(UpdatePurchasingTolerancePolicyRequest req, CancellationToken ct)
        {
            Policy = Policy with { PriceTolerancePercent = req.PriceTolerancePercent, QuantityTolerancePercent = req.QuantityTolerancePercent, AutoApproveWithinTolerance = req.AutoApproveWithinTolerance };
            return Task.FromResult(true);
        }

        public Task<bool> ApprovePurchaseOrderAsync(Guid id, Guid approvedBy, string? note, CancellationToken ct)
        {
            var po = PurchaseOrders.FirstOrDefault(p => p.PurchaseOrderId == id);
            if (po is null) return Task.FromResult(false);
            PurchaseOrders.Remove(po);
            PurchaseOrders.Add(po with { OrderStatus = "approved" });
            return Task.FromResult(true);
        }

        public Task<bool> CancelPurchaseOrderAsync(Guid id, string reason, CancellationToken ct)
        {
            var po = PurchaseOrders.FirstOrDefault(p => p.PurchaseOrderId == id);
            if (po is null) return Task.FromResult(false);
            PurchaseOrders.Remove(po);
            PurchaseOrders.Add(po with { OrderStatus = "cancelled" });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PurchaseInspectionSummary>> ListInspectionsAsync(Guid? grnId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PurchaseInspectionSummary>>(Inspections);
        public Task<PurchaseInspectionSummary?> GetInspectionAsync(Guid id, CancellationToken ct) => Task.FromResult(Inspections.FirstOrDefault(i => i.InspectionId == id));
        public Task<Guid> CreateInspectionAsync(CreatePurchaseInspectionRequest req, Guid inspectorId, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            Inspections.Add(new PurchaseInspectionSummary(id, req.InspectionNumber, req.GrnId, "GRN-01", inspectorId, DateTimeOffset.UtcNow, "passed", req.Notes, []));
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<QuarantineReleaseSummary>> ListQuarantineReleasesAsync(Guid? grnId, CancellationToken ct) => Task.FromResult<IReadOnlyList<QuarantineReleaseSummary>>(QuarantineReleases);
        public Task<Guid> ReleaseQuarantineStockAsync(ReleaseQuarantineStockRequest req, Guid authorizedBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            QuarantineReleases.Add(new QuarantineReleaseSummary(id, req.ReleaseNumber, req.InspectionLineId, req.GrnId, req.VariantId, "SKU-01", req.SourceBinId, "QUAR-BIN", req.TargetBinId, "AVAIL-BIN", req.ReleasedQty, req.ReleaseDestination, authorizedBy, DateTimeOffset.UtcNow, req.Notes));
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<SupplierInvoiceSummary>> ListSupplierInvoicesAsync(Guid? supId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupplierInvoiceSummary>>(Invoices);
        public Task<SupplierInvoiceSummary?> GetSupplierInvoiceAsync(Guid id, CancellationToken ct) => Task.FromResult(Invoices.FirstOrDefault(i => i.InvoiceId == id));
        public Task<Guid> CreateSupplierInvoiceAsync(CreateSupplierInvoiceRequest req, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var subtotal = req.Lines.Sum(l => l.InvoicedQty * l.UnitPrice);
            Invoices.Add(new SupplierInvoiceSummary(id, req.InvoiceNumber, req.SupplierId, "Vendor", req.PurchaseOrderId, null, req.GrnId, null, req.InvoiceDate, req.DueDate, req.CurrencyCode, subtotal, req.TaxAmount, subtotal + req.TaxAmount, "unmatched", "draft", "unpaid", 0, 0, null, null, null, null, []));
            return Task.FromResult(id);
        }
        public Task<ThreeWayMatchEvaluation> MatchSupplierInvoiceAsync(Guid id, CancellationToken ct)
        {
            var inv = Invoices.First(i => i.InvoiceId == id);
            Invoices.Remove(inv);
            Invoices.Add(inv with { MatchingStatus = "matched", Status = "approved" });
            return Task.FromResult(new ThreeWayMatchEvaluation(true, false, inv.TotalAmount, inv.TotalAmount, 0, "matched", [], []));
        }
        public Task<bool> ApproveSupplierInvoiceAsync(Guid id, bool force, string? note, Guid approvedBy, CancellationToken ct)
        {
            var inv = Invoices.FirstOrDefault(i => i.InvoiceId == id);
            if (inv is null) return Task.FromResult(false);
            Invoices.Remove(inv);
            Invoices.Add(inv with { Status = "approved" });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PurchaseReturnSummary>> ListPurchaseReturnsAsync(Guid? supId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PurchaseReturnSummary>>(Returns);
        public Task<PurchaseReturnSummary?> GetPurchaseReturnAsync(Guid id, CancellationToken ct) => Task.FromResult(Returns.FirstOrDefault(r => r.ReturnId == id));
        public Task<Guid> CreatePurchaseReturnAsync(CreatePurchaseReturnRequest req, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            Returns.Add(new PurchaseReturnSummary(id, req.ReturnNumber, req.SupplierId, "Vendor", req.PurchaseOrderId, null, req.GrnId, null, req.SupplierInvoiceId, null, "draft", req.Reason, null, null, DateTimeOffset.UtcNow, []));
            return Task.FromResult(id);
        }
        public Task<bool> ApprovePurchaseReturnAsync(Guid id, Guid authorizedBy, CancellationToken ct)
        {
            var ret = Returns.FirstOrDefault(r => r.ReturnId == id);
            if (ret is null) return Task.FromResult(false);
            Returns.Remove(ret);
            Returns.Add(ret with { Status = "approved" });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<SupplierDebitNoteSummary>> ListSupplierDebitNotesAsync(Guid? supId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupplierDebitNoteSummary>>(DebitNotes);
        public Task<Guid> CreateSupplierDebitNoteAsync(CreateSupplierDebitNoteRequest req, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            DebitNotes.Add(new SupplierDebitNoteSummary(id, req.DebitNoteNumber, req.SupplierId, "Vendor", req.PurchaseReturnId, null, req.SupplierInvoiceId, null, req.DebitNoteDate, "PKR", req.Amount, "draft", null, null, null, req.Notes));
            return Task.FromResult(id);
        }
        public Task<bool> ApproveSupplierDebitNoteAsync(Guid id, Guid authorizedBy, CancellationToken ct)
        {
            var dn = DebitNotes.FirstOrDefault(d => d.DebitNoteId == id);
            if (dn is null) return Task.FromResult(false);
            DebitNotes.Remove(dn);
            DebitNotes.Add(dn with { Status = "approved" });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<SupplierBalanceSummary>> ListSupplierBalancesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SupplierBalanceSummary>>(Balances);
        public Task<SupplierBalanceSummary?> GetSupplierBalanceAsync(Guid id, CancellationToken ct) => Task.FromResult(Balances.FirstOrDefault(b => b.SupplierId == id));
    }

    private static (PurchasingService Service, Phase6MockPurchasingRepository Repo) CreateService()
    {
        var tenantContext = new TenantContext(
            TenantId: Guid.NewGuid(),
            CompanyId: Guid.NewGuid(),
            UserId: Guid.NewGuid());
        var repo = new Phase6MockPurchasingRepository();
        var accessor = new MockTenantContextAccessor(tenantContext);
        var svc = new PurchasingService(repo, accessor);
        return (svc, repo);
    }

    [Fact]
    public async Task ApprovePurchaseOrder_TransitionsStatusToApproved()
    {
        var (svc, repo) = CreateService();
        var poId = Guid.NewGuid();
        repo.PurchaseOrders.Add(new PurchaseOrderSummary(
            poId, "PO-001", Guid.NewGuid(), "Vendor", Guid.NewGuid(), "Warehouse",
            "draft", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null, []));

        var res = await svc.ApprovePurchaseOrderAsync(poId, new ApprovePurchaseOrderRequest("Authorized"), CancellationToken.None);

        Assert.True(res.IsSuccess);
        Assert.Equal("approved", res.Value.OrderStatus);
    }

    [Fact]
    public async Task CancelPurchaseOrder_RequiresReason()
    {
        var (svc, repo) = CreateService();
        var poId = Guid.NewGuid();
        repo.PurchaseOrders.Add(new PurchaseOrderSummary(
            poId, "PO-001", Guid.NewGuid(), "Vendor", Guid.NewGuid(), "Warehouse",
            "draft", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null, []));

        var res = await svc.CancelPurchaseOrderAsync(poId, new CancelPurchaseOrderRequest(""), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("CANCELLATION_REASON_REQUIRED", res.Error);
    }

    [Fact]
    public async Task CreateInspection_ValidatesRequiredLines()
    {
        var (svc, _) = CreateService();

        var res = await svc.CreateInspectionAsync(new CreatePurchaseInspectionRequest(
            "INSP-001", Guid.NewGuid(), "Notes", []), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("INSPECTION_REQUIRES_LINES", res.Error);
    }

    [Fact]
    public async Task ReleaseQuarantineStock_ValidatesPositiveQuantity()
    {
        var (svc, _) = CreateService();

        var res = await svc.ReleaseQuarantineStockAsync(new ReleaseQuarantineStockRequest(
            "REL-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            0m, "available", null), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("INVALID_RELEASE_QUANTITY", res.Error);
    }

    [Fact]
    public async Task CreateSupplierInvoice_ValidatesInvoiceLines()
    {
        var (svc, _) = CreateService();

        var res = await svc.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            "INV-001", Guid.NewGuid(), null, null, DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PKR", 0m, []), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("INVOICE_REQUIRES_LINES", res.Error);
    }

    [Fact]
    public async Task MatchSupplierInvoice_ReturnsEvaluation()
    {
        var (svc, repo) = CreateService();
        var invId = Guid.NewGuid();
        repo.Invoices.Add(new SupplierInvoiceSummary(
            invId, "INV-001", Guid.NewGuid(), "Vendor", null, null, null, null,
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            "PKR", 500m, 0m, 500m, "unmatched", "draft", "unpaid", 0, 0, null, null, null, null, []));

        var res = await svc.MatchSupplierInvoiceAsync(invId, CancellationToken.None);

        Assert.True(res.IsSuccess);
        Assert.True(res.Value.IsMatched);
    }

    [Fact]
    public async Task CreatePurchaseReturn_ValidatesLines()
    {
        var (svc, _) = CreateService();

        var res = await svc.CreatePurchaseReturnAsync(new CreatePurchaseReturnRequest(
            "RET-001", Guid.NewGuid(), null, null, null, "Defective item", []), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("RETURN_REQUIRES_LINES", res.Error);
    }

    [Fact]
    public async Task CreateSupplierDebitNote_ValidatesPositiveAmount()
    {
        var (svc, _) = CreateService();

        var res = await svc.CreateSupplierDebitNoteAsync(new CreateSupplierDebitNoteRequest(
            "DN-001", Guid.NewGuid(), null, null, DateOnly.FromDateTime(DateTime.UtcNow), 0m, null), CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal("INVALID_DEBIT_NOTE_AMOUNT", res.Error);
    }
}
