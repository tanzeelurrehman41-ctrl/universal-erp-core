using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase7SalesServiceTests
{
    private sealed class MockTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class Phase7MockSalesRepository : ISalesRepository
    {
        public List<CustomerSummary> Customers = [];
        public List<SalesOrderSummary> Orders = [];
        public List<SalesInvoiceSummary> Invoices = [];
        public List<SalesQuotationSummary> Quotations = [];
        public List<SalesDispatchSummary> Dispatches = [];
        public List<CustomerReceiptSummary> Receipts = [];
        public List<SalesReturnSummary> Returns = [];
        public List<SalesCreditNoteSummary> CreditNotes = [];
        public CustomerCreditExposureSummary? ExposureSummary;

        public Task<IReadOnlyList<CustomerSummary>> ListCustomersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerSummary>>(Customers);
        public Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken ct) => Task.FromResult(Customers.FirstOrDefault(c => c.CustomerId == customerId));
        public Task<Guid> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<bool> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SalesOrderSummary>> ListOrdersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SalesOrderSummary>>(Orders);
        public Task<SalesOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken ct) => Task.FromResult(Orders.FirstOrDefault(o => o.OrderId == orderId));
        public Task<Guid> CreateOrderAsync(CreateSalesOrderRequest request, Guid createdBy, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task ConfirmOrderAsync(Guid orderId, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, bool>> GetVariantRequiresCogsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, bool>>(new Dictionary<Guid, bool>());
        public Task<bool> CanViewCostAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SalesInvoiceSummary>>(Invoices);
        public Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct) => Task.FromResult(Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId));
        public Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken ct) => Task.FromResult(Guid.NewGuid());

        public Task<IReadOnlyList<SalesQuotationSummary>> ListQuotationsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SalesQuotationSummary>>(Quotations);
        public Task<SalesQuotationSummary?> GetQuotationAsync(Guid quotationId, CancellationToken ct) => Task.FromResult(Quotations.FirstOrDefault(q => q.QuotationId == quotationId));
        public Task<Guid> CreateQuotationAsync(CreateSalesQuotationRequest request, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var q = new SalesQuotationSummary(
                id, request.QuotationNumber, request.CustomerId, "Test Customer",
                request.WarehouseId, "Main Warehouse", request.QuotationDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.ValidUntil ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                "draft", "PKR", 100m, 10m, 0m, 110m, request.Note, null, null, null, []);
            Quotations.Add(q);
            return Task.FromResult(id);
        }

        public Task<bool> ApproveQuotationAsync(Guid quotationId, Guid approvedBy, string? note, CancellationToken ct)
        {
            var q = Quotations.FirstOrDefault(x => x.QuotationId == quotationId);
            if (q is null) return Task.FromResult(false);
            Quotations.Remove(q);
            Quotations.Add(q with { Status = "approved", ApprovedBy = approvedBy, ApprovedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(true);
        }

        public Task<Guid> ConvertQuotationToOrderAsync(Guid quotationId, ConvertQuotationToOrderRequest request, Guid createdBy, CancellationToken ct)
        {
            var q = Quotations.FirstOrDefault(x => x.QuotationId == quotationId);
            if (q is null) return Task.FromResult(Guid.Empty);
            var orderId = Guid.NewGuid();
            var o = new SalesOrderSummary(
                orderId, request.OrderNumber, q.CustomerId, q.CustomerName,
                q.WarehouseId, q.WarehouseName, request.OrderType, "draft",
                q.CurrencyCode, request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                q.SubtotalAmount, q.TaxAmount, q.DiscountAmount, q.TotalAmount, request.Note,
                DateTimeOffset.UtcNow, []);
            Orders.Add(o);
            Quotations.Remove(q);
            Quotations.Add(q with { Status = "converted", ConvertedOrderId = orderId });
            return Task.FromResult(orderId);
        }

        public Task<bool> ApproveOrderAsync(Guid orderId, Guid approvedBy, string? note, CancellationToken ct)
        {
            var o = Orders.FirstOrDefault(x => x.OrderId == orderId);
            if (o is null) return Task.FromResult(false);
            Orders.Remove(o);
            Orders.Add(o with { OrderStatus = "approved" });
            return Task.FromResult(true);
        }

        public Task<bool> CancelOrderAsync(Guid orderId, string reason, Guid cancelledBy, CancellationToken ct)
        {
            var o = Orders.FirstOrDefault(x => x.OrderId == orderId);
            if (o is null) return Task.FromResult(false);
            Orders.Remove(o);
            Orders.Add(o with { OrderStatus = "cancelled" });
            return Task.FromResult(true);
        }

        public Task<CustomerCreditExposureSummary?> GetCustomerCreditExposureAsync(Guid customerId, CancellationToken ct) =>
            Task.FromResult(ExposureSummary);

        public Task<IReadOnlyList<SalesDispatchSummary>> ListDispatchesAsync(Guid? orderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SalesDispatchSummary>>(Dispatches);
        public Task<SalesDispatchSummary?> GetDispatchAsync(Guid dispatchId, CancellationToken ct) =>
            Task.FromResult(Dispatches.FirstOrDefault(d => d.DispatchId == dispatchId));
        public Task<Guid> CreateDispatchAsync(CreateSalesDispatchRequest request, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var d = new SalesDispatchSummary(
                id, request.DispatchNumber, request.OrderId, "SO-001",
                Guid.NewGuid(), "Customer", Guid.NewGuid(), "Warehouse",
                request.DispatchDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.Carrier, request.TrackingNumber, "draft", request.Note,
                null, null, null, null, null, []);
            Dispatches.Add(d);
            return Task.FromResult(id);
        }
        public Task<bool> ConfirmDispatchAsync(Guid dispatchId, Guid dispatchedBy, bool markDelivered, string? note, CancellationToken ct)
        {
            var d = Dispatches.FirstOrDefault(x => x.DispatchId == dispatchId);
            if (d is null) return Task.FromResult(false);
            Dispatches.Remove(d);
            Dispatches.Add(d with { Status = markDelivered ? "delivered" : "dispatched", DispatchedBy = dispatchedBy, DispatchedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<CustomerReceiptSummary>> ListReceiptsAsync(Guid? customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CustomerReceiptSummary>>(Receipts);
        public Task<CustomerReceiptSummary?> GetReceiptAsync(Guid receiptId, CancellationToken ct) =>
            Task.FromResult(Receipts.FirstOrDefault(r => r.ReceiptId == receiptId));
        public Task<Guid> CreateReceiptAsync(CreateCustomerReceiptRequest request, Guid receivedBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var r = new CustomerReceiptSummary(
                id, request.ReceiptNumber, request.CustomerId, "Customer",
                request.ReceiptDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.PaymentMethod, request.Amount, request.Amount, request.ReferenceNumber,
                "posted", Guid.NewGuid(), request.Note, receivedBy, DateTimeOffset.UtcNow, []);
            Receipts.Add(r);
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<SalesReturnSummary>> ListReturnsAsync(Guid? customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SalesReturnSummary>>(Returns);
        public Task<SalesReturnSummary?> GetReturnAsync(Guid returnId, CancellationToken ct) =>
            Task.FromResult(Returns.FirstOrDefault(r => r.ReturnId == returnId));
        public Task<Guid> CreateReturnAsync(CreateSalesReturnRequest request, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var r = new SalesReturnSummary(
                id, request.ReturnNumber, request.CustomerId, "Customer",
                request.InvoiceId, null, request.OrderId, null,
                request.ReturnDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                50m, 30m, "draft", request.Reason, null, null, null, []);
            Returns.Add(r);
            return Task.FromResult(id);
        }
        public Task<bool> ApproveReturnAsync(Guid returnId, Guid authorizedBy, CancellationToken ct)
        {
            var r = Returns.FirstOrDefault(x => x.ReturnId == returnId);
            if (r is null) return Task.FromResult(false);
            Returns.Remove(r);
            Returns.Add(r with { Status = "approved", AuthorizedBy = authorizedBy, AuthorizedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<SalesCreditNoteSummary>> ListCreditNotesAsync(Guid? customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SalesCreditNoteSummary>>(CreditNotes);
        public Task<SalesCreditNoteSummary?> GetCreditNoteAsync(Guid creditNoteId, CancellationToken ct) =>
            Task.FromResult(CreditNotes.FirstOrDefault(c => c.CreditNoteId == creditNoteId));
        public Task<Guid> CreateCreditNoteAsync(CreateSalesCreditNoteRequest request, Guid createdBy, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var cn = new SalesCreditNoteSummary(
                id, request.CreditNoteNumber, request.CustomerId, "Customer",
                request.ReturnId, request.InvoiceId, request.CreditNoteDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                "PKR", request.SubtotalAmount, request.TaxAmount, request.SubtotalAmount + request.TaxAmount,
                "draft", null, request.Reason, null, null);
            CreditNotes.Add(cn);
            return Task.FromResult(id);
        }
        public Task<bool> ApproveCreditNoteAsync(Guid creditNoteId, Guid authorizedBy, CancellationToken ct)
        {
            var cn = CreditNotes.FirstOrDefault(x => x.CreditNoteId == creditNoteId);
            if (cn is null) return Task.FromResult(false);
            CreditNotes.Remove(cn);
            CreditNotes.Add(cn with { Status = "approved", AuthorizedBy = authorizedBy, AuthorizedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(true);
        }
    }

    private static (SalesService Service, Phase7MockSalesRepository Repo) CreateService()
    {
        var repo = new Phase7MockSalesRepository();
        var tenant = new TenantContext(
            TenantId: Guid.NewGuid(),
            CompanyId: Guid.NewGuid(),
            UserId: Guid.NewGuid());
        var service = new SalesService(repo, new MockTenantContextAccessor(tenant));
        return (service, repo);
    }

    [Fact]
    public async Task CreateQuotation_ValidatesInputs_AndCreatesDraft()
    {
        var (svc, repo) = CreateService();

        // Invalid code
        var invalidCode = await svc.CreateQuotationAsync(
            new CreateSalesQuotationRequest("invalid code!", Guid.NewGuid(), Guid.NewGuid(), Lines: [
                new CreateSalesQuotationLineRequest(Guid.NewGuid(), Guid.NewGuid(), 10, Guid.NewGuid(), 100)
            ]), default);
        Assert.False(invalidCode.IsSuccess);
        Assert.Equal("INVALID_QUOTATION_NUMBER", invalidCode.Error);

        // Missing lines
        var noLines = await svc.CreateQuotationAsync(
            new CreateSalesQuotationRequest("QT-2026-001", Guid.NewGuid(), Guid.NewGuid(), Lines: []), default);
        Assert.False(noLines.IsSuccess);
        Assert.Equal("QUOTATION_REQUIRES_LINES", noLines.Error);

        // Valid
        var valid = await svc.CreateQuotationAsync(
            new CreateSalesQuotationRequest("QT-2026-001", Guid.NewGuid(), Guid.NewGuid(), Lines: [
                new CreateSalesQuotationLineRequest(Guid.NewGuid(), Guid.NewGuid(), 10, Guid.NewGuid(), 100)
            ]), default);
        Assert.True(valid.IsSuccess);
        Assert.Equal("draft", valid.Value!.Status);
        Assert.Equal("QT-2026-001", valid.Value.QuotationNumber);
    }

    [Fact]
    public async Task QuotationLifecycle_Approve_And_Convert_Succeeds()
    {
        var (svc, repo) = CreateService();

        var qRes = await svc.CreateQuotationAsync(
            new CreateSalesQuotationRequest("QT-2026-002", Guid.NewGuid(), Guid.NewGuid(), Lines: [
                new CreateSalesQuotationLineRequest(Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), 200)
            ]), default);
        var qId = qRes.Value!.QuotationId;

        // Cannot convert before approval
        var earlyConvert = await svc.ConvertQuotationToOrderAsync(qId, new ConvertQuotationToOrderRequest("SO-2026-001"), default);
        Assert.False(earlyConvert.IsSuccess);
        Assert.Equal("QUOTATION_NOT_APPROVED", earlyConvert.Error);

        // Approve quotation
        var approved = await svc.ApproveQuotationAsync(qId, new ApproveSalesQuotationRequest("Price approved"), default);
        Assert.True(approved.IsSuccess);
        Assert.Equal("approved", approved.Value!.Status);

        // Convert to order
        var orderRes = await svc.ConvertQuotationToOrderAsync(qId, new ConvertQuotationToOrderRequest("SO-2026-001"), default);
        Assert.True(orderRes.IsSuccess);
        Assert.Equal("SO-2026-001", orderRes.Value!.OrderNumber);
    }

    [Fact]
    public async Task OrderApprovalAndCancellation_TransitionsStatus()
    {
        var (svc, repo) = CreateService();
        var orderId = Guid.NewGuid();
        repo.Orders.Add(new SalesOrderSummary(
            orderId, "SO-2026-010", Guid.NewGuid(), "Customer",
            Guid.NewGuid(), "Warehouse", "standard", "draft",
            "PKR", DateOnly.FromDateTime(DateTime.UtcNow),
            500m, 50m, 0m, 550m, null, DateTimeOffset.UtcNow, []));

        // Approve
        var appRes = await svc.ApproveOrderAsync(orderId, new ApproveSalesOrderRequest("Approved by director"), default);
        Assert.True(appRes.IsSuccess);
        Assert.Equal("approved", appRes.Value!.OrderStatus);

        // Cancel
        var cancelRes = await svc.CancelOrderAsync(orderId, new CancelSalesOrderRequest("Customer request"), default);
        Assert.True(cancelRes.IsSuccess);
        Assert.Equal("cancelled", cancelRes.Value!.OrderStatus);
    }

    [Fact]
    public async Task Dispatch_CreationAndConfirmation_TransitionsStatus()
    {
        var (svc, repo) = CreateService();
        var orderId = Guid.NewGuid();

        var dRes = await svc.CreateDispatchAsync(new CreateSalesDispatchRequest(
            "DSP-2026-001", orderId, Lines: [
                new CreateSalesDispatchLineRequest(1, Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid())
            ]), default);
        Assert.True(dRes.IsSuccess);
        Assert.Equal("draft", dRes.Value!.Status);

        // Confirm dispatch with delivered flag
        var confRes = await svc.ConfirmDispatchAsync(dRes.Value.DispatchId, new ConfirmSalesDispatchRequest(MarkDelivered: true), default);
        Assert.True(confRes.IsSuccess);
        Assert.Equal("delivered", confRes.Value!.Status);

        // Re-confirming should fail
        var reConf = await svc.ConfirmDispatchAsync(dRes.Value.DispatchId, new ConfirmSalesDispatchRequest(), default);
        Assert.False(reConf.IsSuccess);
        Assert.Equal("DISPATCH_ALREADY_CONFIRMED", reConf.Error);
    }

    [Fact]
    public async Task Receipt_ValidatesAmount_AndCreatesReceipt()
    {
        var (svc, repo) = CreateService();

        var zeroAmt = await svc.CreateReceiptAsync(new CreateCustomerReceiptRequest(
            "RCT-001", Guid.NewGuid(), 0m), default);
        Assert.False(zeroAmt.IsSuccess);
        Assert.Equal("INVALID_RECEIPT_AMOUNT", zeroAmt.Error);

        var valid = await svc.CreateReceiptAsync(new CreateCustomerReceiptRequest(
            "RCT-2026-001", Guid.NewGuid(), 5_000m), default);
        Assert.True(valid.IsSuccess);
        Assert.Equal(5_000m, valid.Value!.Amount);
    }

    [Fact]
    public async Task ReturnAndCreditNote_ApproveFlow_TransitionsStatus()
    {
        var (svc, repo) = CreateService();
        var custId = Guid.NewGuid();

        // Return
        var retRes = await svc.CreateReturnAsync(new CreateSalesReturnRequest(
            "RET-2026-001", custId, Lines: [
                new CreateSalesReturnLineRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 50, 30)
            ]), default);
        Assert.True(retRes.IsSuccess);
        Assert.Equal("draft", retRes.Value!.Status);

        var appRet = await svc.ApproveReturnAsync(retRes.Value.ReturnId, default);
        Assert.True(appRet.IsSuccess);
        Assert.Equal("approved", appRet.Value!.Status);

        // Credit Note
        var cnRes = await svc.CreateCreditNoteAsync(new CreateSalesCreditNoteRequest(
            "CRN-2026-001", custId, 100m, 10m, ReturnId: retRes.Value.ReturnId), default);
        Assert.True(cnRes.IsSuccess);
        Assert.Equal("draft", cnRes.Value!.Status);

        var appCn = await svc.ApproveCreditNoteAsync(cnRes.Value.CreditNoteId, default);
        Assert.True(appCn.IsSuccess);
        Assert.Equal("approved", appCn.Value!.Status);
    }
}
