namespace UniversalErp.Application.Sales;

// ── Customer Requests & Summaries ──────────────────────────────────────────

public sealed record CreateCustomerRequest(
    string CustomerCode,
    string Name,
    string CustomerType = "retail",
    string? ContactName = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? NtnNumber = null,
    string? StrnNumber = null,
    decimal CreditLimit = 0,
    int PaymentTermsDays = 0,
    string CurrencyCode = "PKR");

public sealed record UpdateCustomerRequest(
    string Name,
    string CustomerType = "retail",
    string? ContactName = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? NtnNumber = null,
    string? StrnNumber = null,
    decimal CreditLimit = 0,
    int PaymentTermsDays = 0,
    bool IsActive = true);

public sealed record CustomerSummary(
    Guid CustomerId,
    string CustomerCode,
    string Name,
    string CustomerType,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? NtnNumber,
    string? StrnNumber,
    decimal CreditLimit,
    decimal OutstandingBalance,
    string CurrencyCode,
    int PaymentTermsDays,
    bool IsActive);

// ── Sales Order Requests & Summaries ───────────────────────────────────────

public sealed record CreateSalesOrderLineRequest(
    Guid VariantId,
    Guid BinId,
    decimal OrderedQty,
    Guid UnitOfMeasureId,
    decimal UnitPrice,
    decimal TaxRate = 0);

public sealed record CreateSalesOrderRequest(
    string OrderNumber,
    Guid CustomerId,
    Guid WarehouseId,
    string OrderType = "standard",
    DateOnly? OrderDate = null,
    string? Note = null,
    IReadOnlyList<CreateSalesOrderLineRequest>? Lines = null);

public sealed record SalesOrderLineSummary(
    int LineNumber,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal OrderedQty,
    decimal FulfilledQty,
    Guid UnitOfMeasureId,
    string UomCode,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineTotal,
    string LineStatus);

public sealed record SalesOrderSummary(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseName,
    string OrderType,
    string OrderStatus,
    string CurrencyCode,
    DateOnly OrderDate,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? Note,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SalesOrderLineSummary> Lines);

// ── Sales Invoice & POS Requests & Summaries ───────────────────────────────

public sealed record PostSalesInvoiceLineRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    decimal UnitPrice,
    decimal UnitCost = 0,
    decimal TaxRate = 0,
    int? OrderLineNumber = null);

public sealed record PostSalesInvoiceRequest(
    Guid ClientTransactionId,
    string InvoiceNumber,
    Guid CustomerId,
    string PaymentMethod = "cash",
    Guid? OrderId = null,
    DateOnly? InvoiceDate = null,
    decimal DiscountAmount = 0,
    string? Note = null,
    IReadOnlyList<PostSalesInvoiceLineRequest>? Lines = null);

public sealed record SalesInvoiceLineSummary(
    int LineNumber,
    int? OrderLineNumber,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    decimal Quantity,
    Guid UnitOfMeasureId,
    string UomCode,
    decimal UnitPrice,
    decimal? UnitCost,
    decimal TaxRate,
    decimal LineTotal);

public sealed record SalesInvoiceSummary(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid ClientTransactionId,
    Guid? OrderId,
    Guid CustomerId,
    string CustomerName,
    string PaymentMethod,
    string InvoiceStatus,
    DateOnly InvoiceDate,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    string? Note,
    DateTimeOffset PostedAt,
    IReadOnlyList<SalesInvoiceLineSummary> Lines,
    Guid? DispatchId = null,
    DateOnly? DueDate = null,
    Guid? GlJournalId = null);

// ── Repository Interface ───────────────────────────────────────────────────

public interface ISalesRepository
{
    // Customers
    Task<IReadOnlyList<CustomerSummary>> ListCustomersAsync(CancellationToken cancellationToken);
    Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken);
    Task<Guid> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<bool> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken);

    // Sales Orders
    Task<IReadOnlyList<SalesOrderSummary>> ListOrdersAsync(CancellationToken cancellationToken);
    Task<SalesOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<Guid> CreateOrderAsync(CreateSalesOrderRequest request, Guid createdBy, CancellationToken cancellationToken);
    Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken);

    // Invoices & POS
    Task<IReadOnlyDictionary<Guid, bool>> GetVariantRequiresCogsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);
    Task<bool> CanViewCostAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken);
    Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken);

    // ── Phase 7 Enhancements ─────────────────────────────────────────────────
    // Quotations
    Task<IReadOnlyList<SalesQuotationSummary>> ListQuotationsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SalesQuotationSummary>>([]);
    Task<SalesQuotationSummary?> GetQuotationAsync(Guid quotationId, CancellationToken ct) =>
        Task.FromResult<SalesQuotationSummary?>(null);
    Task<Guid> CreateQuotationAsync(CreateSalesQuotationRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);
    Task<bool> ApproveQuotationAsync(Guid quotationId, Guid approvedBy, string? note, CancellationToken ct) =>
        Task.FromResult(false);
    Task<Guid> ConvertQuotationToOrderAsync(Guid quotationId, ConvertQuotationToOrderRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);

    // Order Approvals & Cancellation
    Task<bool> ApproveOrderAsync(Guid orderId, Guid approvedBy, string? note, CancellationToken ct) =>
        Task.FromResult(false);
    Task<bool> CancelOrderAsync(Guid orderId, string reason, Guid cancelledBy, CancellationToken ct) =>
        Task.FromResult(false);

    // Customer Credit Check
    Task<CustomerCreditExposureSummary?> GetCustomerCreditExposureAsync(Guid customerId, CancellationToken ct) =>
        Task.FromResult<CustomerCreditExposureSummary?>(null);

    // Dispatches
    Task<IReadOnlyList<SalesDispatchSummary>> ListDispatchesAsync(Guid? orderId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SalesDispatchSummary>>([]);
    Task<SalesDispatchSummary?> GetDispatchAsync(Guid dispatchId, CancellationToken ct) =>
        Task.FromResult<SalesDispatchSummary?>(null);
    Task<Guid> CreateDispatchAsync(CreateSalesDispatchRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);
    Task<bool> ConfirmDispatchAsync(Guid dispatchId, Guid dispatchedBy, bool markDelivered, string? note, CancellationToken ct) =>
        Task.FromResult(false);

    // Invoice from Dispatch
    Task<Guid> CreateInvoiceFromDispatchAsync(CreateInvoiceFromDispatchRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);

    // Customer Receipts
    Task<IReadOnlyList<CustomerReceiptSummary>> ListReceiptsAsync(Guid? customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CustomerReceiptSummary>>([]);
    Task<CustomerReceiptSummary?> GetReceiptAsync(Guid receiptId, CancellationToken ct) =>
        Task.FromResult<CustomerReceiptSummary?>(null);
    Task<Guid> CreateReceiptAsync(CreateCustomerReceiptRequest request, Guid receivedBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);

    // Sales Returns & Credit Notes
    Task<IReadOnlyList<SalesReturnSummary>> ListReturnsAsync(Guid? customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SalesReturnSummary>>([]);
    Task<SalesReturnSummary?> GetReturnAsync(Guid returnId, CancellationToken ct) =>
        Task.FromResult<SalesReturnSummary?>(null);
    Task<Guid> CreateReturnAsync(CreateSalesReturnRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);
    Task<bool> ApproveReturnAsync(Guid returnId, Guid authorizedBy, CancellationToken ct) =>
        Task.FromResult(false);
    Task<IReadOnlyList<SalesCreditNoteSummary>> ListCreditNotesAsync(Guid? customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SalesCreditNoteSummary>>([]);
    Task<SalesCreditNoteSummary?> GetCreditNoteAsync(Guid creditNoteId, CancellationToken ct) =>
        Task.FromResult<SalesCreditNoteSummary?>(null);
    Task<Guid> CreateCreditNoteAsync(CreateSalesCreditNoteRequest request, Guid createdBy, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);
    Task<bool> ApproveCreditNoteAsync(Guid creditNoteId, Guid authorizedBy, CancellationToken ct) =>
        Task.FromResult(false);

    // Accounts Receivable & Statements
    Task<IReadOnlyList<CustomerArAgingSummary>> ListCustomerArAgingAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CustomerArAgingSummary>>([]);
    Task<CustomerStatementSummary?> GetCustomerStatementAsync(Guid customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        Task.FromResult<CustomerStatementSummary?>(null);
}
