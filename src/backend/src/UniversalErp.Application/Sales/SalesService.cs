using System.Text.RegularExpressions;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Sales;

public sealed class SalesService(
    ISalesRepository repository,
    ITenantContextAccessor tenantContext)
{
    private static readonly Regex CodePattern = new(
        @"\A[A-Z0-9][A-Z0-9._/-]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> ValidCustomerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "retail", "wholesale", "distributor", "corporate"
    };

    private static readonly HashSet<string> ValidPaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash", "credit", "bank_transfer", "card", "mobile_wallet"
    };

    // ── Customers ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<CustomerSummary>> ListCustomersAsync(CancellationToken cancellationToken) =>
        repository.ListCustomersAsync(cancellationToken);

    public Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        repository.GetCustomerAsync(customerId, cancellationToken);

    public async Task<Result<CustomerSummary>> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.CustomerCode?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var type = request.CustomerType?.Trim().ToLowerInvariant() ?? "retail";

        if (string.IsNullOrEmpty(code) || !CodePattern.IsMatch(code))
            return Result<CustomerSummary>.Failure("INVALID_CUSTOMER_CODE");
        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 200)
            return Result<CustomerSummary>.Failure("INVALID_CUSTOMER_NAME");
        if (!ValidCustomerTypes.Contains(type))
            return Result<CustomerSummary>.Failure("INVALID_CUSTOMER_TYPE");
        if (request.CreditLimit < 0)
            return Result<CustomerSummary>.Failure("INVALID_CREDIT_LIMIT");
        if (request.PaymentTermsDays < 0)
            return Result<CustomerSummary>.Failure("INVALID_PAYMENT_TERMS");

        try
        {
            var normalized = request with { CustomerCode = code, Name = name, CustomerType = type };
            var id = await repository.CreateCustomerAsync(normalized, cancellationToken);
            var customer = await repository.GetCustomerAsync(id, cancellationToken);
            return customer is null
                ? Result<CustomerSummary>.Failure("CUSTOMER_NOT_FOUND")
                : Result<CustomerSummary>.Success(customer);
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<CustomerSummary>.Failure("DUPLICATE_CUSTOMER_CODE");
        }
    }

    public async Task<Result<CustomerSummary>> UpdateCustomerAsync(
        Guid customerId,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var type = request.CustomerType?.Trim().ToLowerInvariant() ?? "retail";

        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 200)
            return Result<CustomerSummary>.Failure("INVALID_CUSTOMER_NAME");
        if (!ValidCustomerTypes.Contains(type))
            return Result<CustomerSummary>.Failure("INVALID_CUSTOMER_TYPE");
        if (request.CreditLimit < 0)
            return Result<CustomerSummary>.Failure("INVALID_CREDIT_LIMIT");
        if (request.PaymentTermsDays < 0)
            return Result<CustomerSummary>.Failure("INVALID_PAYMENT_TERMS");

        var normalized = request with { Name = name, CustomerType = type };
        var ok = await repository.UpdateCustomerAsync(customerId, normalized, cancellationToken);
        if (!ok) return Result<CustomerSummary>.Failure("CUSTOMER_NOT_FOUND");

        var updated = await repository.GetCustomerAsync(customerId, cancellationToken);
        return updated is not null
            ? Result<CustomerSummary>.Success(updated)
            : Result<CustomerSummary>.Failure("CUSTOMER_NOT_FOUND");
    }

    // ── Sales Orders ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<SalesOrderSummary>> ListOrdersAsync(CancellationToken cancellationToken) =>
        repository.ListOrdersAsync(cancellationToken);

    public Task<SalesOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        repository.GetOrderAsync(orderId, cancellationToken);

    public async Task<Result<SalesOrderSummary>> CreateOrderAsync(
        CreateSalesOrderRequest request,
        CancellationToken cancellationToken)
    {
        var orderNo = request.OrderNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(orderNo) || !CodePattern.IsMatch(orderNo))
            return Result<SalesOrderSummary>.Failure("INVALID_ORDER_NUMBER");
        if (request.CustomerId == Guid.Empty)
            return Result<SalesOrderSummary>.Failure("INVALID_CUSTOMER_ID");
        if (request.WarehouseId == Guid.Empty)
            return Result<SalesOrderSummary>.Failure("INVALID_WAREHOUSE_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<SalesOrderSummary>.Failure("ORDER_REQUIRES_LINES");
        if (request.Lines.Any(l => l.OrderedQty <= 0 || l.UnitPrice < 0 || l.VariantId == Guid.Empty || l.BinId == Guid.Empty))
            return Result<SalesOrderSummary>.Failure("INVALID_ORDER_LINE");

        try
        {
            var userId = tenantContext.Current.UserId;
            var normalized = request with { OrderNumber = orderNo };
            var orderId = await repository.CreateOrderAsync(normalized, userId, cancellationToken);
            var order = await repository.GetOrderAsync(orderId, cancellationToken);
            return order is null
                ? Result<SalesOrderSummary>.Failure("SALES_ORDER_NOT_FOUND")
                : Result<SalesOrderSummary>.Success(order);
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesOrderSummary>.Failure("DUPLICATE_ORDER_NUMBER");
        }
    }

    public async Task<Result<SalesOrderSummary>> ConfirmOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetOrderAsync(orderId, cancellationToken);
        if (order is null) return Result<SalesOrderSummary>.Failure("SALES_ORDER_NOT_FOUND");
        if (order.OrderStatus != "draft") return Result<SalesOrderSummary>.Failure("ORDER_NOT_IN_DRAFT");

        await repository.ConfirmOrderAsync(orderId, cancellationToken);
        var confirmed = await repository.GetOrderAsync(orderId, cancellationToken);
        return confirmed is null
            ? Result<SalesOrderSummary>.Failure("SALES_ORDER_NOT_FOUND")
            : Result<SalesOrderSummary>.Success(confirmed);
    }

    // ── Sales Invoices & POS Receipts ───────────────────────────────────────

    public Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken) =>
        repository.ListInvoicesAsync(cancellationToken);

    public Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        repository.GetInvoiceAsync(invoiceId, cancellationToken);

    public async Task<Result<SalesInvoiceSummary>> PostInvoiceAsync(
        PostSalesInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientTransactionId == Guid.Empty)
            return Result<SalesInvoiceSummary>.Failure("INVALID_CLIENT_TRANSACTION_ID");

        var invoiceNo = request.InvoiceNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(invoiceNo) || !CodePattern.IsMatch(invoiceNo))
            return Result<SalesInvoiceSummary>.Failure("INVALID_INVOICE_NUMBER");

        if (request.CustomerId == Guid.Empty)
            return Result<SalesInvoiceSummary>.Failure("INVALID_CUSTOMER_ID");

        var paymentMethod = request.PaymentMethod?.Trim().ToLowerInvariant() ?? "cash";
        if (!ValidPaymentMethods.Contains(paymentMethod))
            return Result<SalesInvoiceSummary>.Failure("INVALID_PAYMENT_METHOD");

        if (request.Lines is null || request.Lines.Count == 0)
            return Result<SalesInvoiceSummary>.Failure("INVOICE_REQUIRES_LINES");

        if (request.DiscountAmount < 0)
            return Result<SalesInvoiceSummary>.Failure("INVALID_DISCOUNT_AMOUNT");

        foreach (var line in request.Lines)
        {
            if (line.VariantId == Guid.Empty || line.WarehouseId == Guid.Empty || line.BinId == Guid.Empty || line.Quantity <= 0)
                return Result<SalesInvoiceSummary>.Failure("INVALID_INVOICE_LINE");
            if (line.UnitPrice <= 0)
                return Result<SalesInvoiceSummary>.Failure("INVALID_SELLING_PRICE");
            if (line.TaxRate < 0)
                return Result<SalesInvoiceSummary>.Failure("INVALID_TAX_RATE");
            if (line.UnitCost < 0)
                return Result<SalesInvoiceSummary>.Failure("INVALID_UNIT_COST");
        }

        var variantIds = request.Lines.Select(l => l.VariantId).Distinct().ToArray();
        var variantRequiresCogs = await repository.GetVariantRequiresCogsAsync(variantIds, cancellationToken);
        foreach (var line in request.Lines)
        {
            var requiresCogs = !variantRequiresCogs.TryGetValue(line.VariantId, out var tracked) || tracked;
            if (requiresCogs && line.UnitCost <= 0)
                return Result<SalesInvoiceSummary>.Failure("UNIT_COST_REQUIRED");
        }

        decimal subtotal = 0m;
        decimal tax = 0m;
        foreach (var line in request.Lines)
        {
            var lineSubtotal = line.Quantity * line.UnitPrice;
            subtotal += lineSubtotal;
            tax += lineSubtotal * line.TaxRate;
        }
        var netRevenue = subtotal - request.DiscountAmount;
        var total = subtotal + tax - request.DiscountAmount;
        if (subtotal <= 0m || netRevenue <= 0m || total <= 0m)
            return Result<SalesInvoiceSummary>.Failure("INVALID_INVOICE_TOTAL");

        try
        {
            var userId = tenantContext.Current.UserId;
            var normalized = request with { InvoiceNumber = invoiceNo, PaymentMethod = paymentMethod };
            var invoiceId = await repository.PostInvoiceAsync(normalized, userId, cancellationToken);
            var invoice = await repository.GetInvoiceAsync(invoiceId, cancellationToken);
            return invoice is null
                ? Result<SalesInvoiceSummary>.Failure("SALES_INVOICE_NOT_FOUND")
                : Result<SalesInvoiceSummary>.Success(invoice);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("INSUFFICIENT_STOCK", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("INSUFFICIENT_STOCK");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_SELLING_PRICE", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("INVALID_SELLING_PRICE");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("UNIT_COST_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("UNIT_COST_REQUIRED");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_INVOICE_TOTAL", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("INVALID_INVOICE_TOTAL");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GL_POSTING_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("GL_POSTING_FAILED");
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotency: return existing invoice only if the same ClientTransactionId is replayed.
            var all = await repository.ListInvoicesAsync(cancellationToken);
            var existingByTx = all.FirstOrDefault(i => i.ClientTransactionId == request.ClientTransactionId);
            if (existingByTx is not null)
                return Result<SalesInvoiceSummary>.Success(existingByTx);

            var existingByNo = all.FirstOrDefault(i => i.InvoiceNumber.Equals(invoiceNo, StringComparison.OrdinalIgnoreCase));
            return existingByNo is not null
                ? Result<SalesInvoiceSummary>.Failure("DUPLICATE_INVOICE_NUMBER")
                : Result<SalesInvoiceSummary>.Failure("DUPLICATE_INVOICE_TRANSACTION");
        }
    }
}
