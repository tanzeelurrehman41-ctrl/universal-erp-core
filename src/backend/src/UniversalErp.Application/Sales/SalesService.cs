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
        if (order.OrderStatus is not ("draft" or "approved")) return Result<SalesOrderSummary>.Failure("ORDER_NOT_IN_DRAFT");

        await repository.ConfirmOrderAsync(orderId, cancellationToken);
        var confirmed = await repository.GetOrderAsync(orderId, cancellationToken);
        return confirmed is null
            ? Result<SalesOrderSummary>.Failure("SALES_ORDER_NOT_FOUND")
            : Result<SalesOrderSummary>.Success(confirmed);
    }

    // ── Sales Invoices & POS Receipts ───────────────────────────────────────

    public async Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken cancellationToken)
    {
        var invoices = await repository.ListInvoicesAsync(cancellationToken);
        var canViewCost = await repository.CanViewCostAsync(cancellationToken);
        return canViewCost ? invoices : invoices.Select(RedactCosts).ToList();
    }

    public async Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await repository.GetInvoiceAsync(invoiceId, cancellationToken);
        if (invoice is null) return null;
        var canViewCost = await repository.CanViewCostAsync(cancellationToken);
        return canViewCost ? invoice : RedactCosts(invoice);
    }

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
            // Strip any client-supplied UnitCost so server-side FIFO valuation always controls COGS.
            var sanitizedLines = request.Lines.Select(l => l with { UnitCost = 0m }).ToList();
            var normalized = request with
            {
                InvoiceNumber = invoiceNo,
                PaymentMethod = paymentMethod,
                Lines = sanitizedLines
            };
            var invoiceId = await repository.PostInvoiceAsync(normalized, userId, cancellationToken);
            var invoice = await GetInvoiceAsync(invoiceId, cancellationToken);
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
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("MISSING_VALUATION_DATA", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("MISSING_VALUATION_DATA");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INSUFFICIENT_VALUATION_LAYERS", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SalesInvoiceSummary>.Failure("INSUFFICIENT_VALUATION_LAYERS");
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
            var all = await ListInvoicesAsync(cancellationToken);
            var existingByTx = all.FirstOrDefault(i => i.ClientTransactionId == request.ClientTransactionId);
            if (existingByTx is not null)
                return Result<SalesInvoiceSummary>.Success(existingByTx);

            var existingByNo = all.FirstOrDefault(i => i.InvoiceNumber.Equals(invoiceNo, StringComparison.OrdinalIgnoreCase));
            return existingByNo is not null
                ? Result<SalesInvoiceSummary>.Failure("DUPLICATE_INVOICE_NUMBER")
                : Result<SalesInvoiceSummary>.Failure("DUPLICATE_INVOICE_TRANSACTION");
        }
    }

    private static SalesInvoiceSummary RedactCosts(SalesInvoiceSummary invoice) =>
        invoice with
        {
            Lines = invoice.Lines.Select(l => l with { UnitCost = null }).ToList()
        };

    // ── Phase 7 Enhancements ─────────────────────────────────────────────────

    // Quotations
    public Task<IReadOnlyList<SalesQuotationSummary>> ListQuotationsAsync(CancellationToken ct) =>
        repository.ListQuotationsAsync(ct);

    public Task<SalesQuotationSummary?> GetQuotationAsync(Guid quotationId, CancellationToken ct) =>
        repository.GetQuotationAsync(quotationId, ct);

    public async Task<Result<SalesQuotationSummary>> CreateQuotationAsync(
        CreateSalesQuotationRequest request, CancellationToken ct)
    {
        var qNo = request.QuotationNumber?.Trim();
        if (string.IsNullOrEmpty(qNo) || !CodePattern.IsMatch(qNo))
            return Result<SalesQuotationSummary>.Failure("INVALID_QUOTATION_NUMBER");
        if (request.CustomerId == Guid.Empty)
            return Result<SalesQuotationSummary>.Failure("INVALID_CUSTOMER_ID");
        if (request.WarehouseId == Guid.Empty)
            return Result<SalesQuotationSummary>.Failure("INVALID_WAREHOUSE_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<SalesQuotationSummary>.Failure("QUOTATION_REQUIRES_LINES");

        try
        {
            var userId = tenantContext.Current.UserId;
            var qId = await repository.CreateQuotationAsync(request, userId, ct);
            var q = await repository.GetQuotationAsync(qId, ct);
            return q is not null
                ? Result<SalesQuotationSummary>.Success(q)
                : Result<SalesQuotationSummary>.Failure("QUOTATION_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesQuotationSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesQuotationSummary>> ApproveQuotationAsync(
        Guid quotationId, ApproveSalesQuotationRequest request, CancellationToken ct)
    {
        var q = await repository.GetQuotationAsync(quotationId, ct);
        if (q is null) return Result<SalesQuotationSummary>.Failure("QUOTATION_NOT_FOUND");
        if (q.Status is not ("draft" or "pending_approval"))
            return Result<SalesQuotationSummary>.Failure("QUOTATION_NOT_PENDING");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.ApproveQuotationAsync(quotationId, userId, request.Note, ct);
            if (!ok) return Result<SalesQuotationSummary>.Failure("QUOTATION_APPROVAL_FAILED");

            var updated = await repository.GetQuotationAsync(quotationId, ct);
            return updated is not null
                ? Result<SalesQuotationSummary>.Success(updated)
                : Result<SalesQuotationSummary>.Failure("QUOTATION_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesQuotationSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesOrderSummary>> ConvertQuotationToOrderAsync(
        Guid quotationId, ConvertQuotationToOrderRequest request, CancellationToken ct)
    {
        var q = await repository.GetQuotationAsync(quotationId, ct);
        if (q is null) return Result<SalesOrderSummary>.Failure("QUOTATION_NOT_FOUND");
        if (q.Status != "approved")
            return Result<SalesOrderSummary>.Failure("QUOTATION_NOT_APPROVED");

        try
        {
            var userId = tenantContext.Current.UserId;
            var orderId = await repository.ConvertQuotationToOrderAsync(quotationId, request, userId, ct);
            var order = await repository.GetOrderAsync(orderId, ct);
            return order is not null
                ? Result<SalesOrderSummary>.Success(order)
                : Result<SalesOrderSummary>.Failure("ORDER_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesOrderSummary>.Failure(ex.Message);
        }
    }

    // Order Approvals & Cancellation
    public async Task<Result<SalesOrderSummary>> ApproveOrderAsync(
        Guid orderId, ApproveSalesOrderRequest request, CancellationToken ct)
    {
        var order = await repository.GetOrderAsync(orderId, ct);
        if (order is null) return Result<SalesOrderSummary>.Failure("ORDER_NOT_FOUND");
        if (order.OrderStatus is not ("draft" or "pending_approval"))
            return Result<SalesOrderSummary>.Failure("ORDER_NOT_IN_DRAFT_OR_PENDING");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.ApproveOrderAsync(orderId, userId, request.Note, ct);
            if (!ok) return Result<SalesOrderSummary>.Failure("ORDER_APPROVAL_FAILED");

            var updated = await repository.GetOrderAsync(orderId, ct);
            return updated is not null
                ? Result<SalesOrderSummary>.Success(updated)
                : Result<SalesOrderSummary>.Failure("ORDER_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesOrderSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesOrderSummary>> CancelOrderAsync(
        Guid orderId, CancelSalesOrderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<SalesOrderSummary>.Failure("CANCELLATION_REASON_REQUIRED");

        var order = await repository.GetOrderAsync(orderId, ct);
        if (order is null) return Result<SalesOrderSummary>.Failure("ORDER_NOT_FOUND");
        if (order.OrderStatus is "fulfilled" or "cancelled")
            return Result<SalesOrderSummary>.Failure("ORDER_CANNOT_BE_CANCELLED");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.CancelOrderAsync(orderId, request.Reason, userId, ct);
            if (!ok) return Result<SalesOrderSummary>.Failure("ORDER_CANCELLATION_FAILED");

            var updated = await repository.GetOrderAsync(orderId, ct);
            return updated is not null
                ? Result<SalesOrderSummary>.Success(updated)
                : Result<SalesOrderSummary>.Failure("ORDER_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesOrderSummary>.Failure(ex.Message);
        }
    }

    // Credit Exposure & Checks
    public Task<CustomerCreditExposureSummary?> GetCustomerCreditExposureAsync(Guid customerId, CancellationToken ct) =>
        repository.GetCustomerCreditExposureAsync(customerId, ct);

    public async Task<Result<CreditCheckResult>> CheckCustomerCreditAsync(
        Guid customerId, decimal newOrderAmount, CancellationToken ct)
    {
        var customer = await repository.GetCustomerAsync(customerId, ct);
        if (customer is null) return Result<CreditCheckResult>.Failure("CUSTOMER_NOT_FOUND");

        var exposure = await repository.GetCustomerCreditExposureAsync(customerId, ct);
        var uninvoicedOrders = exposure?.UninvoicedOrdersTotal ?? 0m;

        var check = CreditCheckEngine.Evaluate(
            customer.CreditLimit,
            customer.OutstandingBalance,
            uninvoicedOrders,
            newOrderAmount);

        return Result<CreditCheckResult>.Success(check);
    }

    // Dispatches
    public Task<IReadOnlyList<SalesDispatchSummary>> ListDispatchesAsync(Guid? orderId, CancellationToken ct) =>
        repository.ListDispatchesAsync(orderId, ct);

    public Task<SalesDispatchSummary?> GetDispatchAsync(Guid dispatchId, CancellationToken ct) =>
        repository.GetDispatchAsync(dispatchId, ct);

    public async Task<Result<SalesDispatchSummary>> CreateDispatchAsync(
        CreateSalesDispatchRequest request, CancellationToken ct)
    {
        var dNo = request.DispatchNumber?.Trim();
        if (string.IsNullOrEmpty(dNo) || !CodePattern.IsMatch(dNo))
            return Result<SalesDispatchSummary>.Failure("INVALID_DISPATCH_NUMBER");
        if (request.OrderId == Guid.Empty)
            return Result<SalesDispatchSummary>.Failure("INVALID_ORDER_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<SalesDispatchSummary>.Failure("DISPATCH_REQUIRES_LINES");

        try
        {
            var userId = tenantContext.Current.UserId;
            var dId = await repository.CreateDispatchAsync(request, userId, ct);
            var dispatch = await repository.GetDispatchAsync(dId, ct);
            return dispatch is not null
                ? Result<SalesDispatchSummary>.Success(dispatch)
                : Result<SalesDispatchSummary>.Failure("DISPATCH_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesDispatchSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesDispatchSummary>> ConfirmDispatchAsync(
        Guid dispatchId, ConfirmSalesDispatchRequest request, CancellationToken ct)
    {
        var d = await repository.GetDispatchAsync(dispatchId, ct);
        if (d is null) return Result<SalesDispatchSummary>.Failure("DISPATCH_NOT_FOUND");
        if (d.Status != "draft")
            return Result<SalesDispatchSummary>.Failure("DISPATCH_ALREADY_CONFIRMED");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.ConfirmDispatchAsync(dispatchId, userId, request.MarkDelivered, request.Note, ct);
            if (!ok) return Result<SalesDispatchSummary>.Failure("DISPATCH_CONFIRMATION_FAILED");

            var updated = await repository.GetDispatchAsync(dispatchId, ct);
            return updated is not null
                ? Result<SalesDispatchSummary>.Success(updated)
                : Result<SalesDispatchSummary>.Failure("DISPATCH_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesDispatchSummary>.Failure(ex.Message);
        }
    }

    // Invoices from Dispatch
    public async Task<Result<SalesInvoiceSummary>> CreateInvoiceFromDispatchAsync(
        CreateInvoiceFromDispatchRequest request, CancellationToken ct)
    {
        if (request.ClientTransactionId == Guid.Empty)
            return Result<SalesInvoiceSummary>.Failure("INVALID_CLIENT_TRANSACTION_ID");
        var invNo = request.InvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(invNo) || !CodePattern.IsMatch(invNo))
            return Result<SalesInvoiceSummary>.Failure("INVALID_INVOICE_NUMBER");
        if (request.DispatchId == Guid.Empty)
            return Result<SalesInvoiceSummary>.Failure("INVALID_DISPATCH_ID");

        try
        {
            var userId = tenantContext.Current.UserId;
            var invId = await repository.CreateInvoiceFromDispatchAsync(request, userId, ct);
            var invoice = await repository.GetInvoiceAsync(invId, ct);
            return invoice is not null
                ? Result<SalesInvoiceSummary>.Success(invoice)
                : Result<SalesInvoiceSummary>.Failure("SALES_INVOICE_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesInvoiceSummary>.Failure(ex.Message);
        }
    }

    // Customer Receipts
    public Task<IReadOnlyList<CustomerReceiptSummary>> ListReceiptsAsync(Guid? customerId, CancellationToken ct) =>
        repository.ListReceiptsAsync(customerId, ct);

    public Task<CustomerReceiptSummary?> GetReceiptAsync(Guid receiptId, CancellationToken ct) =>
        repository.GetReceiptAsync(receiptId, ct);

    public async Task<Result<CustomerReceiptSummary>> CreateReceiptAsync(
        CreateCustomerReceiptRequest request, CancellationToken ct)
    {
        var rNo = request.ReceiptNumber?.Trim();
        if (string.IsNullOrEmpty(rNo) || !CodePattern.IsMatch(rNo))
            return Result<CustomerReceiptSummary>.Failure("INVALID_RECEIPT_NUMBER");
        if (request.CustomerId == Guid.Empty)
            return Result<CustomerReceiptSummary>.Failure("INVALID_CUSTOMER_ID");
        if (request.Amount <= 0m)
            return Result<CustomerReceiptSummary>.Failure("INVALID_RECEIPT_AMOUNT");

        try
        {
            var userId = tenantContext.Current.UserId;
            var rId = await repository.CreateReceiptAsync(request, userId, ct);
            var receipt = await repository.GetReceiptAsync(rId, ct);
            return receipt is not null
                ? Result<CustomerReceiptSummary>.Success(receipt)
                : Result<CustomerReceiptSummary>.Failure("RECEIPT_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<CustomerReceiptSummary>.Failure(ex.Message);
        }
    }

    // Sales Returns & Credit Notes
    public Task<IReadOnlyList<SalesReturnSummary>> ListReturnsAsync(Guid? customerId, CancellationToken ct) =>
        repository.ListReturnsAsync(customerId, ct);

    public Task<SalesReturnSummary?> GetReturnAsync(Guid returnId, CancellationToken ct) =>
        repository.GetReturnAsync(returnId, ct);

    public async Task<Result<SalesReturnSummary>> CreateReturnAsync(
        CreateSalesReturnRequest request, CancellationToken ct)
    {
        var rNo = request.ReturnNumber?.Trim();
        if (string.IsNullOrEmpty(rNo) || !CodePattern.IsMatch(rNo))
            return Result<SalesReturnSummary>.Failure("INVALID_RETURN_NUMBER");
        if (request.CustomerId == Guid.Empty)
            return Result<SalesReturnSummary>.Failure("INVALID_CUSTOMER_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<SalesReturnSummary>.Failure("RETURN_REQUIRES_LINES");

        try
        {
            var userId = tenantContext.Current.UserId;
            var rId = await repository.CreateReturnAsync(request, userId, ct);
            var ret = await repository.GetReturnAsync(rId, ct);
            return ret is not null
                ? Result<SalesReturnSummary>.Success(ret)
                : Result<SalesReturnSummary>.Failure("RETURN_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesReturnSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesReturnSummary>> ApproveReturnAsync(Guid returnId, CancellationToken ct)
    {
        var ret = await repository.GetReturnAsync(returnId, ct);
        if (ret is null) return Result<SalesReturnSummary>.Failure("RETURN_NOT_FOUND");
        if (ret.Status != "draft")
            return Result<SalesReturnSummary>.Failure("RETURN_ALREADY_APPROVED");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.ApproveReturnAsync(returnId, userId, ct);
            if (!ok) return Result<SalesReturnSummary>.Failure("RETURN_APPROVAL_FAILED");

            var updated = await repository.GetReturnAsync(returnId, ct);
            return updated is not null
                ? Result<SalesReturnSummary>.Success(updated)
                : Result<SalesReturnSummary>.Failure("RETURN_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesReturnSummary>.Failure(ex.Message);
        }
    }

    public Task<IReadOnlyList<SalesCreditNoteSummary>> ListCreditNotesAsync(Guid? customerId, CancellationToken ct) =>
        repository.ListCreditNotesAsync(customerId, ct);

    public Task<SalesCreditNoteSummary?> GetCreditNoteAsync(Guid creditNoteId, CancellationToken ct) =>
        repository.GetCreditNoteAsync(creditNoteId, ct);

    public async Task<Result<SalesCreditNoteSummary>> CreateCreditNoteAsync(
        CreateSalesCreditNoteRequest request, CancellationToken ct)
    {
        var cnNo = request.CreditNoteNumber?.Trim();
        if (string.IsNullOrEmpty(cnNo) || !CodePattern.IsMatch(cnNo))
            return Result<SalesCreditNoteSummary>.Failure("INVALID_CREDIT_NOTE_NUMBER");
        if (request.CustomerId == Guid.Empty)
            return Result<SalesCreditNoteSummary>.Failure("INVALID_CUSTOMER_ID");
        if (request.SubtotalAmount <= 0m)
            return Result<SalesCreditNoteSummary>.Failure("INVALID_CREDIT_NOTE_AMOUNT");

        try
        {
            var userId = tenantContext.Current.UserId;
            var cnId = await repository.CreateCreditNoteAsync(request, userId, ct);
            var cn = await repository.GetCreditNoteAsync(cnId, ct);
            return cn is not null
                ? Result<SalesCreditNoteSummary>.Success(cn)
                : Result<SalesCreditNoteSummary>.Failure("CREDIT_NOTE_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesCreditNoteSummary>.Failure(ex.Message);
        }
    }

    public async Task<Result<SalesCreditNoteSummary>> ApproveCreditNoteAsync(Guid creditNoteId, CancellationToken ct)
    {
        var cn = await repository.GetCreditNoteAsync(creditNoteId, ct);
        if (cn is null) return Result<SalesCreditNoteSummary>.Failure("CREDIT_NOTE_NOT_FOUND");
        if (cn.Status != "draft")
            return Result<SalesCreditNoteSummary>.Failure("CREDIT_NOTE_ALREADY_APPROVED");

        try
        {
            var userId = tenantContext.Current.UserId;
            var ok = await repository.ApproveCreditNoteAsync(creditNoteId, userId, ct);
            if (!ok) return Result<SalesCreditNoteSummary>.Failure("CREDIT_NOTE_APPROVAL_FAILED");

            var updated = await repository.GetCreditNoteAsync(creditNoteId, ct);
            return updated is not null
                ? Result<SalesCreditNoteSummary>.Success(updated)
                : Result<SalesCreditNoteSummary>.Failure("CREDIT_NOTE_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return Result<SalesCreditNoteSummary>.Failure(ex.Message);
        }
    }

    // Accounts Receivable & Statements
    public Task<IReadOnlyList<CustomerArAgingSummary>> ListCustomerArAgingAsync(CancellationToken ct) =>
        repository.ListCustomerArAgingAsync(ct);

    public Task<CustomerStatementSummary?> GetCustomerStatementAsync(
        Guid customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        repository.GetCustomerStatementAsync(customerId, fromDate, toDate, ct);
}
