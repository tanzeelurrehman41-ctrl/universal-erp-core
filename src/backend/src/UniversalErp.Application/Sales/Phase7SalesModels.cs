namespace UniversalErp.Application.Sales;

// ── Customer Quotations ────────────────────────────────────────────────────────

public sealed record CreateSalesQuotationLineRequest(
    Guid VariantId,
    Guid BinId,
    decimal RequestedQty,
    Guid UnitOfMeasureId,
    decimal UnitPrice,
    decimal TaxRate = 0,
    decimal DiscountPercent = 0);

public sealed record CreateSalesQuotationRequest(
    string QuotationNumber,
    Guid CustomerId,
    Guid WarehouseId,
    DateOnly? QuotationDate = null,
    DateOnly? ValidUntil = null,
    string? Note = null,
    IReadOnlyList<CreateSalesQuotationLineRequest>? Lines = null);

public sealed record SalesQuotationLineSummary(
    int LineNumber,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal RequestedQty,
    Guid UnitOfMeasureId,
    string UomCode,
    decimal UnitPrice,
    decimal TaxRate,
    decimal DiscountPercent,
    decimal LineTotal);

public sealed record SalesQuotationSummary(
    Guid QuotationId,
    string QuotationNumber,
    Guid CustomerId,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseName,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    string Status,
    string CurrencyCode,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? Note,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    Guid? ConvertedOrderId,
    IReadOnlyList<SalesQuotationLineSummary> Lines);

public sealed record ApproveSalesQuotationRequest(string? Note = null);

public sealed record ConvertQuotationToOrderRequest(
    string OrderNumber,
    string OrderType = "standard",
    DateOnly? OrderDate = null,
    string? Note = null);

// ── Sales Order Revisions & Approvals ──────────────────────────────────────────

public sealed record ApproveSalesOrderRequest(string? Note = null);

public sealed record CancelSalesOrderRequest(string Reason);

// ── Credit Limit & Exposure ───────────────────────────────────────────────────

public sealed record CustomerCreditExposureSummary(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal CreditLimit,
    decimal OutstandingBalance,
    decimal UninvoicedOrdersTotal,
    decimal TotalExposure,
    decimal AvailableCredit,
    bool IsCreditExceeded,
    int PaymentTermsDays);

public sealed record CreditCheckResult(
    bool IsAllowed,
    bool RequiresOverride,
    decimal CurrentExposure,
    decimal CreditLimit,
    decimal ExcessAmount,
    string Message);

// ── Warehouse Dispatch & Fulfillment ──────────────────────────────────────────

public sealed record CreateSalesDispatchLineRequest(
    int OrderLineNumber,
    Guid VariantId,
    Guid BinId,
    decimal DispatchedQty,
    Guid UnitOfMeasureId);

public sealed record CreateSalesDispatchRequest(
    string DispatchNumber,
    Guid OrderId,
    DateOnly? DispatchDate = null,
    string? Carrier = null,
    string? TrackingNumber = null,
    string? Note = null,
    IReadOnlyList<CreateSalesDispatchLineRequest>? Lines = null);

public sealed record SalesDispatchLineSummary(
    int LineNumber,
    int OrderLineNumber,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal DispatchedQty,
    Guid UnitOfMeasureId,
    string UomCode,
    decimal UnitCost,
    decimal TotalCost);

public sealed record SalesDispatchSummary(
    Guid DispatchId,
    string DispatchNumber,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseName,
    DateOnly DispatchDate,
    string? Carrier,
    string? TrackingNumber,
    string Status,
    string? Note,
    Guid? GlJournalId,
    Guid? MovementId,
    Guid? DispatchedBy,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<SalesDispatchLineSummary> Lines);

public sealed record ConfirmSalesDispatchRequest(
    bool MarkDelivered = false,
    string? Note = null);

// ── Invoicing from Dispatch / Order ───────────────────────────────────────────

public sealed record CreateInvoiceFromDispatchRequest(
    Guid ClientTransactionId,
    string InvoiceNumber,
    Guid DispatchId,
    string PaymentMethod = "credit",
    DateOnly? InvoiceDate = null,
    DateOnly? DueDate = null,
    decimal DiscountAmount = 0,
    string? Note = null);

// ── Customer Receipts & Payment Allocations ───────────────────────────────────

public sealed record CustomerReceiptAllocationRequest(
    Guid InvoiceId,
    decimal AllocatedAmount);

public sealed record CreateCustomerReceiptRequest(
    string ReceiptNumber,
    Guid CustomerId,
    decimal Amount,
    string PaymentMethod = "bank_transfer",
    DateOnly? ReceiptDate = null,
    string? ReferenceNumber = null,
    string? Note = null,
    IReadOnlyList<CustomerReceiptAllocationRequest>? Allocations = null);

public sealed record CustomerReceiptAllocationSummary(
    Guid AllocationId,
    Guid ReceiptId,
    Guid InvoiceId,
    string InvoiceNumber,
    decimal AllocatedAmount,
    DateTimeOffset AllocatedAt);

public sealed record CustomerReceiptSummary(
    Guid ReceiptId,
    string ReceiptNumber,
    Guid CustomerId,
    string CustomerName,
    DateOnly ReceiptDate,
    string PaymentMethod,
    decimal Amount,
    decimal UnallocatedAmount,
    string? ReferenceNumber,
    string Status,
    Guid? GlJournalId,
    string? Note,
    Guid? ReceivedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CustomerReceiptAllocationSummary> Allocations);

// ── Sales Returns & Customer Credit Notes ─────────────────────────────────────

public sealed record CreateSalesReturnLineRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal ReturnedQty,
    decimal UnitPrice,
    decimal UnitCost = 0,
    string? Reason = null);

public sealed record CreateSalesReturnRequest(
    string ReturnNumber,
    Guid CustomerId,
    Guid? InvoiceId = null,
    Guid? OrderId = null,
    DateOnly? ReturnDate = null,
    string? Reason = null,
    IReadOnlyList<CreateSalesReturnLineRequest>? Lines = null);

public sealed record SalesReturnLineSummary(
    int LineNumber,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    decimal ReturnedQty,
    decimal UnitPrice,
    decimal UnitCost,
    decimal LineTotal,
    string? Reason);

public sealed record SalesReturnSummary(
    Guid ReturnId,
    string ReturnNumber,
    Guid CustomerId,
    string CustomerName,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid? OrderId,
    string? OrderNumber,
    DateOnly ReturnDate,
    decimal TotalAmount,
    decimal TotalCost,
    string Status,
    string? Reason,
    Guid? AuthorizedBy,
    DateTimeOffset? AuthorizedAt,
    Guid? GlJournalId,
    IReadOnlyList<SalesReturnLineSummary> Lines);

public sealed record CreateSalesCreditNoteRequest(
    string CreditNoteNumber,
    Guid CustomerId,
    decimal SubtotalAmount,
    decimal TaxAmount = 0,
    Guid? ReturnId = null,
    Guid? InvoiceId = null,
    DateOnly? CreditNoteDate = null,
    string? Reason = null);

public sealed record SalesCreditNoteSummary(
    Guid CreditNoteId,
    string CreditNoteNumber,
    Guid CustomerId,
    string CustomerName,
    Guid? ReturnId,
    Guid? InvoiceId,
    DateOnly CreditNoteDate,
    string CurrencyCode,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Status,
    Guid? GlJournalId,
    string? Reason,
    Guid? AuthorizedBy,
    DateTimeOffset? AuthorizedAt);

// ── Accounts Receivable Subledger & Aging ──────────────────────────────────────

public sealed record CustomerArAgingSummary(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string CurrencyCode,
    decimal CurrentBalance,        // 0-30 days
    decimal Days31To60,            // 31-60 days
    decimal Days61To90,            // 61-90 days
    decimal Over90Days,            // >90 days
    decimal TotalReceivable,
    decimal TotalCreditNotes,
    decimal TotalReceipts,
    decimal NetOutstanding,
    int OpenInvoiceCount);

public sealed record CustomerStatementItem(
    DateOnly TransactionDate,
    string TransactionType,        // "invoice", "receipt", "credit_note"
    string ReferenceNumber,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance);

public sealed record CustomerStatementSummary(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal OpeningBalance,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal ClosingBalance,
    IReadOnlyList<CustomerStatementItem> Items);
