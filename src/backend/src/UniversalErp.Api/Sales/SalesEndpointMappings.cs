using UniversalErp.Application.Sales;
using UniversalErp.Domain.Common;

namespace UniversalErp.Api.Sales;

public static class SalesEndpointMappings
{
    public static IEndpointRouteBuilder MapSalesEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var sales = endpoints.MapGroup("/api/v1/sales");

        // ── 1. Customers & Credit Limits ──────────────────────────────────────
        var listCustomers = sales.MapGet("/customers", async (
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListCustomersAsync(ct)));

        var getCustomer = sales.MapGet("/customers/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var customer = await svc.GetCustomerAsync(id, ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        });

        var createCustomer = sales.MapPost("/customers", async (
            CreateCustomerRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateCustomerAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var updateCustomer = sales.MapPut("/customers/{id:guid}", async (
            Guid id,
            UpdateCustomerRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.UpdateCustomerAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var getCreditExposure = sales.MapGet("/customers/{id:guid}/credit-exposure", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var exp = await svc.GetCustomerCreditExposureAsync(id, ct);
            return exp is null ? Results.NotFound() : Results.Ok(exp);
        });

        var checkCredit = sales.MapPost("/customers/{id:guid}/credit-check", async (
            Guid id,
            CreditCheckApiRequest? req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var amount = req?.NewOrderAmount ?? 0m;
            var result = await svc.CheckCustomerCreditAsync(id, amount, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── 2. Customer Quotations ────────────────────────────────────────────
        var listQuotations = sales.MapGet("/quotations", async (
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListQuotationsAsync(ct)));

        var getQuotation = sales.MapGet("/quotations/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var q = await svc.GetQuotationAsync(id, ct);
            return q is null ? Results.NotFound() : Results.Ok(q);
        });

        var createQuotation = sales.MapPost("/quotations", async (
            CreateSalesQuotationRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateQuotationAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var approveQuotation = sales.MapPost("/quotations/{id:guid}/approve", async (
            Guid id,
            ApproveSalesQuotationRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ApproveQuotationAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var convertQuotation = sales.MapPost("/quotations/{id:guid}/convert", async (
            Guid id,
            ConvertQuotationToOrderRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ConvertQuotationToOrderAsync(id, req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        // ── 3. Sales Orders ───────────────────────────────────────────────────
        var listOrders = sales.MapGet("/orders", async (
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListOrdersAsync(ct)));

        var getOrder = sales.MapGet("/orders/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var order = await svc.GetOrderAsync(id, ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        });

        var createOrder = sales.MapPost("/orders", async (
            CreateSalesOrderRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateOrderAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var confirmOrder = sales.MapPost("/orders/{id:guid}/confirm", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ConfirmOrderAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var approveOrder = sales.MapPost("/orders/{id:guid}/approve", async (
            Guid id,
            ApproveSalesOrderRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ApproveOrderAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var cancelOrder = sales.MapPost("/orders/{id:guid}/cancel", async (
            Guid id,
            CancelSalesOrderRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CancelOrderAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── 4. Warehouse Dispatches & Picking ──────────────────────────────────
        var listDispatches = sales.MapGet("/dispatches", async (
            Guid? orderId,
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListDispatchesAsync(orderId, ct)));

        var getDispatch = sales.MapGet("/dispatches/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var d = await svc.GetDispatchAsync(id, ct);
            return d is null ? Results.NotFound() : Results.Ok(d);
        });

        var createDispatch = sales.MapPost("/dispatches", async (
            CreateSalesDispatchRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateDispatchAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var confirmDispatch = sales.MapPost("/dispatches/{id:guid}/confirm", async (
            Guid id,
            ConfirmSalesDispatchRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ConfirmDispatchAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── 5. Sales Invoices ─────────────────────────────────────────────────
        var listInvoices = sales.MapGet("/invoices", async (
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListInvoicesAsync(ct)));

        var getInvoice = sales.MapGet("/invoices/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var invoice = await svc.GetInvoiceAsync(id, ct);
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        });

        var postInvoice = sales.MapPost("/invoices", async (
            HttpRequest httpRequest,
            PostSalesInvoiceRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var txHeader = httpRequest.Headers["Idempotency-Key"].ToString();
            var clientTxId = Guid.TryParse(txHeader, out var parsed) ? parsed : req.ClientTransactionId;
            var finalReq = clientTxId != Guid.Empty && req.ClientTransactionId == Guid.Empty
                ? req with { ClientTransactionId = clientTxId }
                : req;

            var result = await svc.PostInvoiceAsync(finalReq, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var postInvoiceFromDispatch = sales.MapPost("/invoices/from-dispatch", async (
            HttpRequest httpRequest,
            CreateInvoiceFromDispatchRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var txHeader = httpRequest.Headers["Idempotency-Key"].ToString();
            var clientTxId = Guid.TryParse(txHeader, out var parsed) ? parsed : req.ClientTransactionId;
            var finalReq = clientTxId != Guid.Empty && req.ClientTransactionId == Guid.Empty
                ? req with { ClientTransactionId = clientTxId }
                : req;

            var result = await svc.CreateInvoiceFromDispatchAsync(finalReq, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        // ── 6. Customer Receipts & Allocations ────────────────────────────────
        var listReceipts = sales.MapGet("/receipts", async (
            Guid? customerId,
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListReceiptsAsync(customerId, ct)));

        var getReceipt = sales.MapGet("/receipts/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var receipt = await svc.GetReceiptAsync(id, ct);
            return receipt is null ? Results.NotFound() : Results.Ok(receipt);
        });

        var createReceipt = sales.MapPost("/receipts", async (
            CreateCustomerReceiptRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateReceiptAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        // ── 7. Sales Returns & Credit Notes ───────────────────────────────────
        var listReturns = sales.MapGet("/returns", async (
            Guid? customerId,
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListReturnsAsync(customerId, ct)));

        var getReturn = sales.MapGet("/returns/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var ret = await svc.GetReturnAsync(id, ct);
            return ret is null ? Results.NotFound() : Results.Ok(ret);
        });

        var createReturn = sales.MapPost("/returns", async (
            CreateSalesReturnRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateReturnAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var approveReturn = sales.MapPost("/returns/{id:guid}/approve", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ApproveReturnAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var listCreditNotes = sales.MapGet("/credit-notes", async (
            Guid? customerId,
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListCreditNotesAsync(customerId, ct)));

        var getCreditNote = sales.MapGet("/credit-notes/{id:guid}", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var cn = await svc.GetCreditNoteAsync(id, ct);
            return cn is null ? Results.NotFound() : Results.Ok(cn);
        });

        var createCreditNote = sales.MapPost("/credit-notes", async (
            CreateSalesCreditNoteRequest req,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateCreditNoteAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var approveCreditNote = sales.MapPost("/credit-notes/{id:guid}/approve", async (
            Guid id,
            SalesService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ApproveCreditNoteAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── 8. Accounts Receivable & Statements ───────────────────────────────
        var getArAging = sales.MapGet("/receivables/aging", async (
            SalesService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListCustomerArAgingAsync(ct)));

        var getStatement = sales.MapGet("/customers/{id:guid}/statement", async (
            Guid id,
            DateOnly? fromDate,
            DateOnly? toDate,
            SalesService svc,
            CancellationToken ct) =>
        {
            var from = fromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
            var to = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var statement = await svc.GetCustomerStatementAsync(id, from, to, ct);
            return statement is null ? Results.NotFound() : Results.Ok(statement);
        });

        if (!isDevelopment)
        {
            listCustomers.RequireAuthorization("sales.customer.view");
            getCustomer.RequireAuthorization("sales.customer.view");
            createCustomer.RequireAuthorization("sales.customer.create");
            updateCustomer.RequireAuthorization("sales.customer.create");
            getCreditExposure.RequireAuthorization("sales.customer.view");
            checkCredit.RequireAuthorization("sales.customer.view");

            listQuotations.RequireAuthorization("sales.order.view");
            getQuotation.RequireAuthorization("sales.order.view");
            createQuotation.RequireAuthorization("sales.quotation.create");
            approveQuotation.RequireAuthorization("sales.quotation.approve");
            convertQuotation.RequireAuthorization("sales.order.create");

            listOrders.RequireAuthorization("sales.order.view");
            getOrder.RequireAuthorization("sales.order.view");
            createOrder.RequireAuthorization("sales.order.create");
            confirmOrder.RequireAuthorization("sales.order.confirm");
            approveOrder.RequireAuthorization("sales.order.approve");
            cancelOrder.RequireAuthorization("sales.order.create");

            listDispatches.RequireAuthorization("sales.dispatch.create");
            getDispatch.RequireAuthorization("sales.dispatch.create");
            createDispatch.RequireAuthorization("sales.dispatch.create");
            confirmDispatch.RequireAuthorization("sales.dispatch.confirm");

            listInvoices.RequireAuthorization("sales.invoice.view");
            getInvoice.RequireAuthorization("sales.invoice.view");
            postInvoice.RequireAuthorization("sales.invoice.post");
            postInvoiceFromDispatch.RequireAuthorization("sales.invoice.post");

            listReceipts.RequireAuthorization("sales.receipt.create");
            getReceipt.RequireAuthorization("sales.receipt.create");
            createReceipt.RequireAuthorization("sales.receipt.create");

            listReturns.RequireAuthorization("sales.return.create");
            getReturn.RequireAuthorization("sales.return.create");
            createReturn.RequireAuthorization("sales.return.create");
            approveReturn.RequireAuthorization("sales.return.approve");

            listCreditNotes.RequireAuthorization("sales.creditnote.create");
            getCreditNote.RequireAuthorization("sales.creditnote.create");
            createCreditNote.RequireAuthorization("sales.creditnote.create");
            approveCreditNote.RequireAuthorization("sales.creditnote.approve");

            getArAging.RequireAuthorization("sales.receivable.view");
            getStatement.RequireAuthorization("sales.receivable.view");
        }

        return endpoints;
    }

    private static IResult ToProblem<T>(Result<T> result)
    {
        var code = result.Error ?? "SALES_REQUEST_FAILED";
        var status = code switch
        {
            _ when code.StartsWith("DUPLICATE_", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal)   => StatusCodes.Status404NotFound,
            _ when code.EndsWith("_ALREADY_APPROVED", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ when code.EndsWith("_ALREADY_CONFIRMED", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ when code.EndsWith("_CANNOT_BE_CANCELLED", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            "ORDER_NOT_IN_DRAFT"                                           => StatusCodes.Status409Conflict,
            "ORDER_NOT_IN_DRAFT_OR_PENDING"                                => StatusCodes.Status409Conflict,
            "QUOTATION_NOT_PENDING"                                        => StatusCodes.Status409Conflict,
            "QUOTATION_NOT_APPROVED"                                       => StatusCodes.Status409Conflict,
            "INSUFFICIENT_STOCK"                                           => StatusCodes.Status422UnprocessableEntity,
            "INSUFFICIENT_ATP"                                             => StatusCodes.Status422UnprocessableEntity,
            "GL_POSTING_FAILED"                                            => StatusCodes.Status422UnprocessableEntity,
            "MISSING_VALUATION_DATA"                                       => StatusCodes.Status422UnprocessableEntity,
            "INSUFFICIENT_VALUATION_LAYERS"                                => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}

public sealed record CreditCheckApiRequest(decimal NewOrderAmount = 0m);
