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

        // ── Customers ─────────────────────────────────────────────────────────
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

        // ── Sales Orders ──────────────────────────────────────────────────────
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

        // ── Sales Invoices & POS ──────────────────────────────────────────────
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

        if (!isDevelopment)
        {
            listCustomers.RequireAuthorization("sales.customer.view");
            getCustomer.RequireAuthorization("sales.customer.view");
            createCustomer.RequireAuthorization("sales.customer.create");
            listOrders.RequireAuthorization("sales.order.view");
            getOrder.RequireAuthorization("sales.order.view");
            createOrder.RequireAuthorization("sales.order.create");
            confirmOrder.RequireAuthorization("sales.order.confirm");
            listInvoices.RequireAuthorization("sales.invoice.view");
            getInvoice.RequireAuthorization("sales.invoice.view");
            postInvoice.RequireAuthorization("sales.invoice.post");
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
            "ORDER_NOT_IN_DRAFT"                                           => StatusCodes.Status409Conflict,
            "INSUFFICIENT_STOCK"                                           => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
