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
    decimal UnitCost,
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
    IReadOnlyList<SalesInvoiceLineSummary> Lines);

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
    Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken);
    Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken);
}
