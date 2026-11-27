using Npgsql;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sales;
using UniversalErp.Infrastructure.Finance;

namespace UniversalErp.Infrastructure.Sales;

public sealed partial class PostgresSalesRepository
{
    // ── 1. Customer Quotations ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<SalesQuotationSummary>> ListQuotationsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT q.quotation_id, q.quotation_number, q.customer_id, c.name AS customer_name,
                   q.warehouse_id, w.name AS warehouse_name, q.quotation_date, q.valid_until,
                   q.status, q.currency_code, q.subtotal_amount, q.tax_amount,
                   q.discount_amount, q.total_amount, q.note, q.approved_by, q.approved_at,
                   q.converted_order_id
            FROM erp_sales.sales_quotation q
            JOIN erp_sales.customer c ON c.tenant_id = q.tenant_id AND c.company_id = q.company_id AND c.customer_id = q.customer_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = q.tenant_id AND w.company_id = q.company_id AND w.warehouse_id = q.warehouse_id
            ORDER BY q.quotation_date DESC, q.created_at DESC;
            """;

        var quotations = new List<SalesQuotationSummary>();
        var quotationIds = new List<Guid>();

        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var qId = r.GetGuid(0);
                quotationIds.Add(qId);
                quotations.Add(new SalesQuotationSummary(
                    qId,
                    r.GetString(1),
                    r.GetGuid(2),
                    r.GetString(3),
                    r.GetGuid(4),
                    r.GetString(5),
                    DateOnly.FromDateTime(r.GetDateTime(6)),
                    DateOnly.FromDateTime(r.GetDateTime(7)),
                    r.GetString(8),
                    r.GetString(9),
                    r.GetDecimal(10),
                    r.GetDecimal(11),
                    r.GetDecimal(12),
                    r.GetDecimal(13),
                    r.IsDBNull(14) ? null : r.GetString(14),
                    r.IsDBNull(15) ? null : r.GetGuid(15),
                    r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16),
                    r.IsDBNull(17) ? null : r.GetGuid(17),
                    []));
            }
            await r.CloseAsync();
        }

        if (quotations.Count == 0)
        {
            await tx.CommitAsync(ct);
            return quotations;
        }

        // Fetch lines
        await using var lCmd = conn.CreateCommand();
        lCmd.Transaction = tx;
        lCmd.CommandText = """
            SELECT l.quotation_id, l.line_number, l.variant_id, v.sku, l.bin_id, b.code AS bin_code,
                   l.requested_qty, l.unit_of_measure_id, u.code AS uom_code, l.unit_price,
                   l.tax_rate, l.discount_percent, l.line_total
            FROM erp_sales.sales_quotation_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.unit_of_measure_id
            WHERE l.quotation_id = ANY(@ids)
            ORDER BY l.quotation_id, l.line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", quotationIds.ToArray());

        var linesMap = new Dictionary<Guid, List<SalesQuotationLineSummary>>();
        await using (var lr = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lr.ReadAsync(ct))
            {
                var qId = lr.GetGuid(0);
                if (!linesMap.TryGetValue(qId, out var list))
                {
                    list = [];
                    linesMap[qId] = list;
                }
                list.Add(new SalesQuotationLineSummary(
                    lr.GetInt32(1),
                    lr.GetGuid(2),
                    lr.GetString(3),
                    lr.GetGuid(4),
                    lr.GetString(5),
                    lr.GetDecimal(6),
                    lr.GetGuid(7),
                    lr.GetString(8),
                    lr.GetDecimal(9),
                    lr.GetDecimal(10),
                    lr.GetDecimal(11),
                    lr.GetDecimal(12)));
            }
            await lr.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return quotations.Select(q => q with
        {
            Lines = linesMap.TryGetValue(q.QuotationId, out var lines) ? lines : []
        }).ToList();
    }

    public async Task<SalesQuotationSummary?> GetQuotationAsync(Guid quotationId, CancellationToken ct)
    {
        var all = await ListQuotationsAsync(ct);
        return all.FirstOrDefault(q => q.QuotationId == quotationId);
    }

    public async Task<Guid> CreateQuotationAsync(CreateSalesQuotationRequest request, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var lines = request.Lines ?? [];
        if (lines.Count == 0) throw new InvalidOperationException("QUOTATION_REQUIRES_LINES");

        decimal subtotal = 0;
        decimal tax = 0;
        decimal discount = 0;

        foreach (var l in lines)
        {
            var lineGross = l.RequestedQty * l.UnitPrice;
            var lineDisc = lineGross * (l.DiscountPercent / 100m);
            var lineNet = lineGross - lineDisc;
            var lineTax = lineNet * l.TaxRate;

            subtotal += lineGross;
            discount += lineDisc;
            tax += lineTax;
        }

        var total = subtotal - discount + tax;
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var quotationId = Guid.NewGuid();
        var qDate = request.QuotationDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var validUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue) ?? qDate.AddDays(15);

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_quotation
                (tenant_id, company_id, quotation_id, quotation_number, customer_id, warehouse_id,
                 quotation_date, valid_until, status, currency_code, subtotal_amount, tax_amount,
                 discount_amount, total_amount, note, created_by)
            VALUES
                (@tid, @cid, @qid, @qno, @cust, @wh,
                 @qdate, @vdate, 'draft', @cur, @sub, @tax,
                 @disc, @tot, @note, @by);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("qid", quotationId);
        cmd.Parameters.AddWithValue("qno", request.QuotationNumber.Trim());
        cmd.Parameters.AddWithValue("cust", request.CustomerId);
        cmd.Parameters.AddWithValue("wh", request.WarehouseId);
        cmd.Parameters.AddWithValue("qdate", qDate);
        cmd.Parameters.AddWithValue("vdate", validUntil);
        cmd.Parameters.AddWithValue("cur", functionalCurrency);
        cmd.Parameters.AddWithValue("sub", subtotal);
        cmd.Parameters.AddWithValue("tax", tax);
        cmd.Parameters.AddWithValue("disc", discount);
        cmd.Parameters.AddWithValue("tot", total);
        cmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var lineGross = l.RequestedQty * l.UnitPrice;
            var lineDisc = lineGross * (l.DiscountPercent / 100m);
            var lineNet = lineGross - lineDisc;
            var lineTax = lineNet * l.TaxRate;
            var lineTotal = lineNet + lineTax;

            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_quotation_line
                    (tenant_id, company_id, quotation_id, line_number, variant_id, bin_id,
                     requested_qty, unit_of_measure_id, unit_price, tax_rate, discount_percent, line_total)
                VALUES
                    (@tid, @cid, @qid, @ln, @var, @bin,
                     @qty, @uom, @price, @tax, @disc, @tot);
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("qid", quotationId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.RequestedQty);
            lCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
            lCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lCmd.Parameters.AddWithValue("tax", l.TaxRate);
            lCmd.Parameters.AddWithValue("disc", l.DiscountPercent);
            lCmd.Parameters.AddWithValue("tot", lineTotal);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return quotationId;
    }

    public async Task<bool> ApproveQuotationAsync(Guid quotationId, Guid approvedBy, string? note, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_sales.sales_quotation
            SET status = 'approved',
                approved_by = @by,
                approved_at = now(),
                note = CASE WHEN @note IS NOT NULL THEN COALESCE(note || ' | ', '') || @note ELSE note END,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND quotation_id = @qid AND status IN ('draft', 'pending_approval');
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("qid", quotationId);
        cmd.Parameters.AddWithValue("by", approvedBy == Guid.Empty ? Ctx.UserId : approvedBy);
        cmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<Guid> ConvertQuotationToOrderAsync(Guid quotationId, ConvertQuotationToOrderRequest request, Guid createdBy, CancellationToken ct)
    {
        var quotation = await GetQuotationAsync(quotationId, ct);
        if (quotation is null) throw new InvalidOperationException("QUOTATION_NOT_FOUND");
        if (quotation.Status != "approved") throw new InvalidOperationException("QUOTATION_NOT_APPROVED: Only approved quotations can be converted to sales orders.");

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var orderId = Guid.NewGuid();
        var orderDate = request.OrderDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var functionalCurrency = !string.IsNullOrWhiteSpace(quotation.CurrencyCode)
            ? quotation.CurrencyCode
            : await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_order
                (tenant_id, company_id, order_id, order_number, customer_id, warehouse_id,
                 order_type, order_status, currency_code, order_date, subtotal_amount, tax_amount,
                 discount_amount, total_amount, note, quotation_id, created_by)
            VALUES
                (@tid, @cid, @oid, @ono, @cust, @wh,
                 @type, 'draft', @cur, @odate, @sub, @tax,
                 @disc, @tot, @note, @qid, @by);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("oid", orderId);
        cmd.Parameters.AddWithValue("ono", request.OrderNumber.Trim());
        cmd.Parameters.AddWithValue("cust", quotation.CustomerId);
        cmd.Parameters.AddWithValue("wh", quotation.WarehouseId);
        cmd.Parameters.AddWithValue("type", request.OrderType);
        cmd.Parameters.AddWithValue("cur", functionalCurrency);
        cmd.Parameters.AddWithValue("odate", orderDate);
        cmd.Parameters.AddWithValue("sub", quotation.SubtotalAmount);
        cmd.Parameters.AddWithValue("tax", quotation.TaxAmount);
        cmd.Parameters.AddWithValue("disc", quotation.DiscountAmount);
        cmd.Parameters.AddWithValue("tot", quotation.TotalAmount);
        cmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("qid", quotationId);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? Ctx.UserId : createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < quotation.Lines.Count; i++)
        {
            var l = quotation.Lines[i];
            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_order_line
                    (tenant_id, company_id, order_id, line_number, variant_id, bin_id,
                     ordered_qty, fulfilled_qty, unit_of_measure_id, unit_price, tax_rate, line_total, line_status)
                VALUES
                    (@tid, @cid, @oid, @ln, @var, @bin,
                     @qty, 0, @uom, @price, @tax, @tot, 'pending');
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("oid", orderId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.RequestedQty);
            lCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
            lCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lCmd.Parameters.AddWithValue("tax", l.TaxRate);
            lCmd.Parameters.AddWithValue("tot", l.LineTotal);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        // Mark quotation as converted
        await using var qUpd = conn.CreateCommand();
        qUpd.Transaction = tx;
        qUpd.CommandText = "UPDATE erp_sales.sales_quotation SET status = 'converted', converted_order_id = @oid, updated_at = now() WHERE quotation_id = @qid;";
        qUpd.Parameters.AddWithValue("oid", orderId);
        qUpd.Parameters.AddWithValue("qid", quotationId);
        await qUpd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return orderId;
    }

    // ── 2. Order Approvals & Cancellations ──────────────────────────────────────

    public async Task<bool> ApproveOrderAsync(Guid orderId, Guid approvedBy, string? note, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_sales.sales_order
            SET order_status = 'approved',
                approved_by = @by,
                approved_at = now(),
                note = CASE WHEN @note IS NOT NULL THEN COALESCE(note || ' | ', '') || @note ELSE note END,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND order_id = @oid AND order_status IN ('draft', 'pending_approval');
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("oid", orderId);
        cmd.Parameters.AddWithValue("by", approvedBy == Guid.Empty ? Ctx.UserId : approvedBy);
        cmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<bool> CancelOrderAsync(Guid orderId, string reason, Guid cancelledBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_sales.sales_order
            SET order_status = 'cancelled',
                cancellation_reason = @reason,
                cancelled_at = now(),
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND order_id = @oid AND order_status NOT IN ('fulfilled', 'cancelled');
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("oid", orderId);
        cmd.Parameters.AddWithValue("reason", reason);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    // ── 3. Customer Credit Limit & Exposure ─────────────────────────────────────

    public async Task<CustomerCreditExposureSummary?> GetCustomerCreditExposureAsync(Guid customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT c.customer_id, c.customer_code, c.name, c.credit_limit, c.outstanding_balance,
                   c.payment_terms_days,
                   COALESCE((
                       SELECT SUM(so.total_amount)
                       FROM erp_sales.sales_order so
                       WHERE so.tenant_id = c.tenant_id AND so.company_id = c.company_id AND so.customer_id = c.customer_id
                         AND so.order_status IN ('approved', 'confirmed', 'partially_fulfilled')
                   ), 0) AS uninvoiced_orders_total
            FROM erp_sales.customer c
            WHERE c.tenant_id = @tid AND c.company_id = @cid AND c.customer_id = @cust;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("cust", customerId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var id = r.GetGuid(0);
        var code = r.GetString(1);
        var name = r.GetString(2);
        var limit = r.GetDecimal(3);
        var outBal = r.GetDecimal(4);
        var terms = r.GetInt32(5);
        var uninvOrders = r.GetDecimal(6);

        var exposure = outBal + uninvOrders;
        var available = Math.Max(0m, limit - exposure);
        var isExceeded = limit > 0m && exposure > limit;

        return new CustomerCreditExposureSummary(
            id, code, name, limit, outBal, uninvOrders, exposure, available, isExceeded, terms);
    }

    // ── 4. Warehouse Dispatches & Goods Issue ───────────────────────────────────

    public async Task<IReadOnlyList<SalesDispatchSummary>> ListDispatchesAsync(Guid? orderId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT d.dispatch_id, d.dispatch_number, d.order_id, so.order_number,
                   d.customer_id, c.name AS customer_name, d.warehouse_id, w.name AS warehouse_name,
                   d.dispatch_date, d.carrier, d.tracking_number, d.status, d.note,
                   d.gl_journal_id, d.movement_id, d.dispatched_by, d.dispatched_at, d.delivered_at
            FROM erp_sales.sales_dispatch d
            JOIN erp_sales.sales_order so ON so.tenant_id = d.tenant_id AND so.company_id = d.company_id AND so.order_id = d.order_id
            JOIN erp_sales.customer c ON c.tenant_id = d.tenant_id AND c.company_id = d.company_id AND c.customer_id = d.customer_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = d.tenant_id AND w.company_id = d.company_id AND w.warehouse_id = d.warehouse_id
            WHERE (@oid::uuid IS NULL OR d.order_id = @oid)
            ORDER BY d.dispatch_date DESC, d.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("oid", (object?)orderId ?? DBNull.Value);

        var dispatches = new List<SalesDispatchSummary>();
        var dispatchIds = new List<Guid>();

        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var dId = r.GetGuid(0);
                dispatchIds.Add(dId);
                dispatches.Add(new SalesDispatchSummary(
                    dId,
                    r.GetString(1),
                    r.GetGuid(2),
                    r.GetString(3),
                    r.GetGuid(4),
                    r.GetString(5),
                    r.GetGuid(6),
                    r.GetString(7),
                    DateOnly.FromDateTime(r.GetDateTime(8)),
                    r.IsDBNull(9) ? null : r.GetString(9),
                    r.IsDBNull(10) ? null : r.GetString(10),
                    r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetString(12),
                    r.IsDBNull(13) ? null : r.GetGuid(13),
                    r.IsDBNull(14) ? null : r.GetGuid(14),
                    r.IsDBNull(15) ? null : r.GetGuid(15),
                    r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16),
                    r.IsDBNull(17) ? null : r.GetFieldValue<DateTimeOffset>(17),
                    []));
            }
            await r.CloseAsync();
        }

        if (dispatches.Count == 0)
        {
            await tx.CommitAsync(ct);
            return dispatches;
        }

        // Fetch dispatch lines
        await using var lCmd = conn.CreateCommand();
        lCmd.Transaction = tx;
        lCmd.CommandText = """
            SELECT dl.dispatch_id, dl.line_number, dl.order_line_number, dl.variant_id, v.sku,
                   dl.bin_id, b.code AS bin_code, dl.dispatched_qty, dl.unit_of_measure_id,
                   u.code AS uom_code, dl.unit_cost, dl.total_cost
            FROM erp_sales.sales_dispatch_line dl
            JOIN erp_inventory.product_variant v ON v.tenant_id = dl.tenant_id AND v.variant_id = dl.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = dl.tenant_id AND b.company_id = dl.company_id AND b.bin_id = dl.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = dl.tenant_id AND u.uom_id = dl.unit_of_measure_id
            WHERE dl.dispatch_id = ANY(@ids)
            ORDER BY dl.dispatch_id, dl.line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", dispatchIds.ToArray());

        var linesMap = new Dictionary<Guid, List<SalesDispatchLineSummary>>();
        await using (var lr = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lr.ReadAsync(ct))
            {
                var dId = lr.GetGuid(0);
                if (!linesMap.TryGetValue(dId, out var list))
                {
                    list = [];
                    linesMap[dId] = list;
                }
                list.Add(new SalesDispatchLineSummary(
                    lr.GetInt32(1),
                    lr.GetInt32(2),
                    lr.GetGuid(3),
                    lr.GetString(4),
                    lr.GetGuid(5),
                    lr.GetString(6),
                    lr.GetDecimal(7),
                    lr.GetGuid(8),
                    lr.GetString(9),
                    lr.GetDecimal(10),
                    lr.GetDecimal(11)));
            }
            await lr.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return dispatches.Select(d => d with
        {
            Lines = linesMap.TryGetValue(d.DispatchId, out var lines) ? lines : []
        }).ToList();
    }

    public async Task<SalesDispatchSummary?> GetDispatchAsync(Guid dispatchId, CancellationToken ct)
    {
        var all = await ListDispatchesAsync(null, ct);
        return all.FirstOrDefault(d => d.DispatchId == dispatchId);
    }

    public async Task<Guid> CreateDispatchAsync(CreateSalesDispatchRequest request, Guid createdBy, CancellationToken ct)
    {
        var order = await GetOrderAsync(request.OrderId, ct);
        if (order is null) throw new InvalidOperationException("ORDER_NOT_FOUND");
        if (order.OrderStatus is not ("approved" or "confirmed" or "partially_fulfilled"))
        {
            throw new InvalidOperationException("ORDER_NOT_CONFIRMED: Cannot dispatch unconfirmed order.");
        }

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var dispatchId = Guid.NewGuid();
        var dDate = request.DispatchDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var lines = request.Lines ?? [];

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_dispatch
                (tenant_id, company_id, dispatch_id, dispatch_number, order_id, customer_id, warehouse_id,
                 dispatch_date, carrier, tracking_number, status, note, created_at, updated_at)
            VALUES
                (@tid, @cid, @did, @dno, @oid, @cust, @wh,
                 @ddate, @carrier, @track, 'draft', @note, now(), now());
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("did", dispatchId);
        cmd.Parameters.AddWithValue("dno", request.DispatchNumber.Trim());
        cmd.Parameters.AddWithValue("oid", request.OrderId);
        cmd.Parameters.AddWithValue("cust", order.CustomerId);
        cmd.Parameters.AddWithValue("wh", order.WarehouseId);
        cmd.Parameters.AddWithValue("ddate", dDate);
        cmd.Parameters.AddWithValue("carrier", (object?)request.Carrier ?? DBNull.Value);
        cmd.Parameters.AddWithValue("track", (object?)request.TrackingNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_dispatch_line
                    (tenant_id, company_id, dispatch_id, line_number, order_line_number,
                     variant_id, bin_id, dispatched_qty, unit_of_measure_id, unit_cost, total_cost)
                VALUES
                    (@tid, @cid, @did, @ln, @oln,
                     @var, @bin, @qty, @uom, 0, 0);
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("did", dispatchId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("oln", l.OrderLineNumber);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.DispatchedQty);
            lCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return dispatchId;
    }

    public async Task<bool> ConfirmDispatchAsync(Guid dispatchId, Guid dispatchedBy, bool markDelivered, string? note, CancellationToken ct)
    {
        var dispatch = await GetDispatchAsync(dispatchId, ct);
        if (dispatch is null) throw new InvalidOperationException("DISPATCH_NOT_FOUND");
        if (dispatch.Status != "draft") return true;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        // 1. Stock sufficiency verification & FIFO Cost Layer Allocation
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var movementId = Guid.NewGuid();
        var clientTxId = Guid.NewGuid();
        var totalCost = 0m;

        // Insert inventory movement header
        await using (var movCmd = conn.CreateCommand())
        {
            movCmd.Transaction = tx;
            movCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                     source_document_type, source_document_id, occurred_at, created_by, note)
                VALUES
                    (@tid, @cid, @mid, @txid, 'issue',
                     'sales_dispatch', @did, now(), @by, @note);
                """;
            movCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            movCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            movCmd.Parameters.AddWithValue("mid", movementId);
            movCmd.Parameters.AddWithValue("txid", clientTxId);
            movCmd.Parameters.AddWithValue("did", dispatchId);
            movCmd.Parameters.AddWithValue("by", dispatchedBy == Guid.Empty ? Ctx.UserId : dispatchedBy);
            movCmd.Parameters.AddWithValue("note", $"Goods dispatch for {dispatch.DispatchNumber}");
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < dispatch.Lines.Count; i++)
        {
            var l = dispatch.Lines[i];
            var lineNumber = i + 1;

            // Fetch and lock open FIFO layers for this variant, warehouse, bin
            var openLayers = new List<FifoCostLayerSlice>();
            await using (var layerSel = conn.CreateCommand())
            {
                layerSel.Transaction = tx;
                layerSel.CommandText = """
                    SELECT layer_id, received_at, remaining_qty, unit_cost, warehouse_id, bin_id
                    FROM erp_inventory.inventory_cost_layer
                    WHERE tenant_id = @tid
                      AND company_id = @cid
                      AND variant_id = @var
                      AND warehouse_id = @wh
                      AND bin_id = @bin
                      AND status_code = 'available'
                      AND remaining_qty > 0
                      AND unit_cost > 0
                    ORDER BY received_at ASC, created_at ASC, layer_id ASC
                    FOR UPDATE;
                    """;
                layerSel.Parameters.AddWithValue("tid", Ctx.TenantId);
                layerSel.Parameters.AddWithValue("cid", Ctx.CompanyId);
                layerSel.Parameters.AddWithValue("var", l.VariantId);
                layerSel.Parameters.AddWithValue("wh", dispatch.WarehouseId);
                layerSel.Parameters.AddWithValue("bin", l.BinId);
                await using var lr = await layerSel.ExecuteReaderAsync(ct);
                while (await lr.ReadAsync(ct))
                {
                    openLayers.Add(new FifoCostLayerSlice(
                        LayerId: lr.GetGuid(0),
                        ReceivedAt: lr.GetFieldValue<DateTimeOffset>(1),
                        RemainingQty: lr.GetDecimal(2),
                        UnitCost: lr.GetDecimal(3),
                        WarehouseId: lr.GetGuid(4),
                        BinId: lr.GetGuid(5)));
                }
                await lr.CloseAsync();
            }

            var allocationResult = FifoCostAllocator.Allocate(
                openLayers,
                l.DispatchedQty,
                locationDescription: $"warehouse {dispatch.WarehouseId}, bin {l.BinId}",
                expectedWarehouseId: dispatch.WarehouseId,
                expectedBinId: l.BinId);

            var lineUnitCost = allocationResult.EffectiveUnitCost;
            totalCost += allocationResult.TotalCost;

            foreach (var alloc in allocationResult.Allocations)
            {
                // Decrement layer remaining_qty
                await using var updLayer = conn.CreateCommand();
                updLayer.Transaction = tx;
                updLayer.CommandText = """
                    UPDATE erp_inventory.inventory_cost_layer
                    SET remaining_qty = remaining_qty - @consumed, updated_at = now()
                    WHERE tenant_id = @tid AND company_id = @cid AND layer_id = @layerId AND remaining_qty >= @consumed;
                    """;
                updLayer.Parameters.AddWithValue("tid", Ctx.TenantId);
                updLayer.Parameters.AddWithValue("cid", Ctx.CompanyId);
                updLayer.Parameters.AddWithValue("layerId", alloc.LayerId);
                updLayer.Parameters.AddWithValue("consumed", alloc.ConsumedQty);
                var rows = await updLayer.ExecuteNonQueryAsync(ct);
                if (rows != 1) throw new InvalidOperationException($"INSUFFICIENT_VALUATION_LAYERS: Concurrent layer modification on layer {alloc.LayerId}.");

                // Insert cost allocation
                await using var insAlloc = conn.CreateCommand();
                insAlloc.Transaction = tx;
                insAlloc.CommandText = """
                    INSERT INTO erp_inventory.inventory_cost_allocation
                        (tenant_id, company_id, allocation_id, layer_id, movement_id,
                         source_document_type, source_document_id, source_line_number,
                         variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                    VALUES
                        (@tid, @cid, @allocId, @layerId, @movId,
                         'sales_dispatch', @did, @ln,
                         @var, @qty, @cost, @extCost, @cur);
                    """;
                insAlloc.Parameters.AddWithValue("tid", Ctx.TenantId);
                insAlloc.Parameters.AddWithValue("cid", Ctx.CompanyId);
                insAlloc.Parameters.AddWithValue("allocId", Guid.NewGuid());
                insAlloc.Parameters.AddWithValue("layerId", alloc.LayerId);
                insAlloc.Parameters.AddWithValue("movId", movementId);
                insAlloc.Parameters.AddWithValue("did", dispatchId);
                insAlloc.Parameters.AddWithValue("ln", lineNumber);
                insAlloc.Parameters.AddWithValue("var", l.VariantId);
                insAlloc.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                insAlloc.Parameters.AddWithValue("cost", alloc.UnitCost);
                insAlloc.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                insAlloc.Parameters.AddWithValue("cur", functionalCurrency);
                await insAlloc.ExecuteNonQueryAsync(ct);
            }

            // Valuation entry
            await using var valCmd = conn.CreateCommand();
            valCmd.Transaction = tx;
            valCmd.CommandText = """
                INSERT INTO erp_inventory.valuation_entry
                    (tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
                     entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at)
                VALUES
                    (@tid, @cid, @valId, @movId, @var,
                     'issue_cost', @qty, @unitCost, @extCost, @cur, now());
                """;
            valCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            valCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            valCmd.Parameters.AddWithValue("valId", Guid.NewGuid());
            valCmd.Parameters.AddWithValue("movId", movementId);
            valCmd.Parameters.AddWithValue("var", l.VariantId);
            valCmd.Parameters.AddWithValue("qty", -l.DispatchedQty);
            valCmd.Parameters.AddWithValue("unitCost", lineUnitCost);
            valCmd.Parameters.AddWithValue("extCost", -allocationResult.TotalCost);
            valCmd.Parameters.AddWithValue("cur", functionalCurrency);
            await valCmd.ExecuteNonQueryAsync(ct);

            // Movement leg & decrement stock_balance
            await using var legCmd = conn.CreateCommand();
            legCmd.Transaction = tx;
            legCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_movement_leg
                    (tenant_id, company_id, movement_id, leg_number, variant_id,
                     warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
                VALUES
                    (@tid, @cid, @mid, @ln, @var,
                     @wh, @bin, 'available', @delta, @uom);

                UPDATE erp_inventory.stock_balance
                SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                  AND warehouse_id = @wh AND bin_id = @bin AND status_code = 'available';
                """;
            legCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            legCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            legCmd.Parameters.AddWithValue("mid", movementId);
            legCmd.Parameters.AddWithValue("ln", lineNumber);
            legCmd.Parameters.AddWithValue("var", l.VariantId);
            legCmd.Parameters.AddWithValue("wh", dispatch.WarehouseId);
            legCmd.Parameters.AddWithValue("bin", l.BinId);
            legCmd.Parameters.AddWithValue("delta", -l.DispatchedQty);
            legCmd.Parameters.AddWithValue("qty", l.DispatchedQty);
            legCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
            await legCmd.ExecuteNonQueryAsync(ct);

            // Update dispatch line costs
            await using var updDl = conn.CreateCommand();
            updDl.Transaction = tx;
            updDl.CommandText = "UPDATE erp_sales.sales_dispatch_line SET unit_cost = @cost, total_cost = @tcost WHERE dispatch_id = @did AND line_number = @ln;";
            updDl.Parameters.AddWithValue("cost", lineUnitCost);
            updDl.Parameters.AddWithValue("tcost", allocationResult.TotalCost);
            updDl.Parameters.AddWithValue("did", dispatchId);
            updDl.Parameters.AddWithValue("ln", lineNumber);
            await updDl.ExecuteNonQueryAsync(ct);

            // Update sales order line fulfilled_qty
            await using var solCmd = conn.CreateCommand();
            solCmd.Transaction = tx;
            solCmd.CommandText = """
                UPDATE erp_sales.sales_order_line
                SET fulfilled_qty = fulfilled_qty + @qty,
                    line_status = CASE WHEN fulfilled_qty + @qty >= ordered_qty THEN 'fulfilled' ELSE line_status END
                WHERE tenant_id = @tid AND company_id = @cid AND order_id = @oid AND line_number = @oln;
                """;
            solCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            solCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            solCmd.Parameters.AddWithValue("oid", dispatch.OrderId);
            solCmd.Parameters.AddWithValue("oln", l.OrderLineNumber);
            solCmd.Parameters.AddWithValue("qty", l.DispatchedQty);
            await solCmd.ExecuteNonQueryAsync(ct);
        }

        // Update overall Sales Order status
        await using var soStatusCmd = conn.CreateCommand();
        soStatusCmd.Transaction = tx;
        soStatusCmd.CommandText = """
            UPDATE erp_sales.sales_order
            SET order_status = CASE
                WHEN NOT EXISTS (SELECT 1 FROM erp_sales.sales_order_line WHERE tenant_id = @tid AND company_id = @cid AND order_id = @oid AND fulfilled_qty < ordered_qty)
                    THEN 'fulfilled'
                ELSE 'partially_fulfilled'
            END,
            updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND order_id = @oid;
            """;
        soStatusCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        soStatusCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        soStatusCmd.Parameters.AddWithValue("oid", dispatch.OrderId);
        await soStatusCmd.ExecuteNonQueryAsync(ct);

        // 2. Post COGS Double-Entry GL Journal: Dr 5010 (COGS) / Cr 1200 (Inventory Asset)
        Guid? glJournalId = null;
        if (totalCost > 0m)
        {
            // Resolve accounts 5010 and 1200
            Guid cogsAccount = Guid.Empty;
            Guid invAccount = Guid.Empty;
            await using (var accCmd = conn.CreateCommand())
            {
                accCmd.Transaction = tx;
                accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('5010', '1200');";
                accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                await using var accR = await accCmd.ExecuteReaderAsync(ct);
                while (await accR.ReadAsync(ct))
                {
                    var id = accR.GetGuid(0);
                    var code = accR.GetString(1);
                    if (code == "5010") cogsAccount = id;
                    else if (code == "1200") invAccount = id;
                }
                await accR.CloseAsync();
            }

            // Resolve open fiscal period
            Guid activePeriodId;
            await using (var pCmd = conn.CreateCommand())
            {
                pCmd.Transaction = tx;
                pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
                pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                var pVal = await pCmd.ExecuteScalarAsync(ct);
                if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post dispatch COGS journal.");
                activePeriodId = pid;
            }

            var journalId = Guid.NewGuid();
            var journalNo = $"JRN-DISP-{dispatch.DispatchNumber}";
            var idempotencyKey = $"sales-dispatch-cogs-{dispatchId:N}";

            await using (var jourCmd = conn.CreateCommand())
            {
                jourCmd.Transaction = tx;
                jourCmd.CommandText = """
                    INSERT INTO finance.journals
                        (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                         description, functional_currency, source_module, source_type, source_id,
                         idempotency_key, status, created_by, posted_by, posted_at)
                    VALUES
                        (@id, @tid, @cid, @no, @date, @pid,
                         @desc, @funcCurr, 'sales', 'sales_dispatch', @sid,
                         @key, 'posted', @by, @by, now())
                    ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                    """;
                jourCmd.Parameters.AddWithValue("id", journalId);
                jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                jourCmd.Parameters.AddWithValue("no", journalNo);
                jourCmd.Parameters.AddWithValue("date", dispatch.DispatchDate.ToDateTime(TimeOnly.MinValue));
                jourCmd.Parameters.AddWithValue("pid", activePeriodId);
                jourCmd.Parameters.AddWithValue("desc", $"COGS for Dispatch {dispatch.DispatchNumber}");
                jourCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
                jourCmd.Parameters.AddWithValue("sid", dispatchId.ToString());
                jourCmd.Parameters.AddWithValue("key", idempotencyKey);
                jourCmd.Parameters.AddWithValue("by", dispatchedBy == Guid.Empty ? Ctx.UserId : dispatchedBy);
                await jourCmd.ExecuteNonQueryAsync(ct);
            }

            // Dr 5010 COGS
            await using (var l1 = conn.CreateCommand())
            {
                l1.Transaction = tx;
                l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, 'Cost of Goods Sold', @cur, @amt, 1.0, @amt, 0);";
                l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", cogsAccount); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", totalCost);
                await l1.ExecuteNonQueryAsync(ct);
            }

            // Cr 1200 Inventory Asset
            await using (var l2 = conn.CreateCommand())
            {
                l2.Transaction = tx;
                l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, 'Inventory Asset Relief', @cur, @amt, 1.0, 0, @amt);";
                l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", journalId); l2.Parameters.AddWithValue("aid", invAccount); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", totalCost);
                await l2.ExecuteNonQueryAsync(ct);
            }

            glJournalId = journalId;
        }

        // 3. Update dispatch header status
        var newStatus = markDelivered ? "delivered" : "dispatched";
        await using (var updD = conn.CreateCommand())
        {
            updD.Transaction = tx;
            updD.CommandText = """
                UPDATE erp_sales.sales_dispatch
                SET status = @st,
                    dispatched_by = @by,
                    dispatched_at = now(),
                    delivered_at = CASE WHEN @delivered THEN now() ELSE delivered_at END,
                    gl_journal_id = @jid,
                    movement_id = @mid,
                    updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND dispatch_id = @did;
                """;
            updD.Parameters.AddWithValue("tid", Ctx.TenantId);
            updD.Parameters.AddWithValue("cid", Ctx.CompanyId);
            updD.Parameters.AddWithValue("did", dispatchId);
            updD.Parameters.AddWithValue("st", newStatus);
            updD.Parameters.AddWithValue("by", dispatchedBy == Guid.Empty ? Ctx.UserId : dispatchedBy);
            updD.Parameters.AddWithValue("delivered", markDelivered);
            updD.Parameters.AddWithValue("jid", (object?)glJournalId ?? DBNull.Value);
            updD.Parameters.AddWithValue("mid", movementId);
            await updD.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    // ── 5. Invoicing from Dispatch Note ────────────────────────────────────────

    public async Task<Guid> CreateInvoiceFromDispatchAsync(CreateInvoiceFromDispatchRequest req, Guid createdBy, CancellationToken ct)
    {
        var dispatch = await GetDispatchAsync(req.DispatchId, ct);
        if (dispatch is null) throw new InvalidOperationException("DISPATCH_NOT_FOUND");
        if (dispatch.Status is not ("dispatched" or "delivered"))
        {
            throw new InvalidOperationException("DISPATCH_NOT_CONFIRMED: Cannot invoice unconfirmed dispatch.");
        }

        var order = await GetOrderAsync(dispatch.OrderId, ct);
        if (order is null) throw new InvalidOperationException("ORDER_NOT_FOUND");

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var invoiceId = Guid.NewGuid();
        var invDate = req.InvoiceDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var dueDate = req.DueDate?.ToDateTime(TimeOnly.MinValue) ?? invDate.AddDays(30);

        decimal subtotal = 0;
        decimal tax = 0;

        // Build lines matching dispatch quantities with order prices
        var invLines = new List<(int LineNumber, int OrderLineNumber, Guid VariantId, Guid BinId, decimal Qty, Guid UomId, decimal UnitPrice, decimal UnitCost, decimal TaxRate, decimal LineTotal)>();

        for (var i = 0; i < dispatch.Lines.Count; i++)
        {
            var dl = dispatch.Lines[i];
            var ol = order.Lines.FirstOrDefault(x => x.LineNumber == dl.OrderLineNumber);
            var unitPrice = ol?.UnitPrice ?? 0m;
            var taxRate = ol?.TaxRate ?? 0m;
            var lineSub = dl.DispatchedQty * unitPrice;
            var lineTax = lineSub * taxRate;
            var lineTot = lineSub + lineTax;

            subtotal += lineSub;
            tax += lineTax;

            invLines.Add((i + 1, dl.OrderLineNumber, dl.VariantId, dl.BinId, dl.DispatchedQty, dl.UnitOfMeasureId, unitPrice, dl.UnitCost, taxRate, lineTot));
        }

        var total = subtotal + tax - req.DiscountAmount;
        var paid = req.PaymentMethod == "credit" ? 0m : total;

        // Insert invoice header: CRITICAL INVARIANT: DO NOT relieve stock or post COGS again!
        cmd.CommandText = """
            INSERT INTO erp_sales.sales_invoice
                (tenant_id, company_id, invoice_id, invoice_number, client_transaction_id,
                 order_id, customer_id, payment_method, invoice_status, invoice_date, due_date,
                 subtotal_amount, tax_amount, discount_amount, total_amount, paid_amount,
                 note, dispatch_id, created_by, posted_at)
            VALUES
                (@tid, @cid, @iid, @ino, @txid,
                 @oid, @cust, @pay, 'posted', @idate, @ddate,
                 @sub, @tax, @disc, @tot, @paid,
                 @note, @did, @by, now())
            RETURNING invoice_id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("iid", invoiceId);
        cmd.Parameters.AddWithValue("ino", req.InvoiceNumber.Trim());
        cmd.Parameters.AddWithValue("txid", req.ClientTransactionId);
        cmd.Parameters.AddWithValue("oid", dispatch.OrderId);
        cmd.Parameters.AddWithValue("cust", dispatch.CustomerId);
        cmd.Parameters.AddWithValue("pay", req.PaymentMethod);
        cmd.Parameters.AddWithValue("idate", invDate);
        cmd.Parameters.AddWithValue("ddate", dueDate);
        cmd.Parameters.AddWithValue("sub", subtotal);
        cmd.Parameters.AddWithValue("tax", tax);
        cmd.Parameters.AddWithValue("disc", req.DiscountAmount);
        cmd.Parameters.AddWithValue("tot", total);
        cmd.Parameters.AddWithValue("paid", paid);
        cmd.Parameters.AddWithValue("note", (object?)req.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("did", req.DispatchId);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        foreach (var l in invLines)
        {
            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_invoice_line
                    (tenant_id, company_id, invoice_id, line_number, order_line_number,
                     variant_id, warehouse_id, bin_id, quantity, unit_of_measure_id,
                     unit_price, unit_cost, tax_rate, line_total)
                VALUES
                    (@tid, @cid, @iid, @ln, @oln,
                     @var, @wh, @bin, @qty, @uom,
                     @price, @cost, @tax, @tot);
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("iid", invoiceId);
            lCmd.Parameters.AddWithValue("ln", l.LineNumber);
            lCmd.Parameters.AddWithValue("oln", l.OrderLineNumber);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("wh", dispatch.WarehouseId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.Qty);
            lCmd.Parameters.AddWithValue("uom", l.UomId);
            lCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lCmd.Parameters.AddWithValue("cost", l.UnitCost);
            lCmd.Parameters.AddWithValue("tax", l.TaxRate);
            lCmd.Parameters.AddWithValue("tot", l.LineTotal);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        // Update customer balance if credit sale
        if (req.PaymentMethod == "credit")
        {
            await using var custCmd = conn.CreateCommand();
            custCmd.Transaction = tx;
            custCmd.CommandText = "UPDATE erp_sales.customer SET outstanding_balance = outstanding_balance + @tot, updated_at = now() WHERE tenant_id = @tid AND company_id = @cid AND customer_id = @cust;";
            custCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            custCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            custCmd.Parameters.AddWithValue("tot", total);
            custCmd.Parameters.AddWithValue("cust", dispatch.CustomerId);
            await custCmd.ExecuteNonQueryAsync(ct);
        }

        // Post Revenue and Tax Journal
        Guid? arAccount = null;
        Guid? cashAccount = null;
        Guid? bankAccount = null;
        Guid? salesRevenueAccount = null;
        Guid? salesTaxAccount = null;

        await using (var accCmd = conn.CreateCommand())
        {
            accCmd.Transaction = tx;
            accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('1010', '1020', '1100', '2100', '4010');";
            accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            await using var accR = await accCmd.ExecuteReaderAsync(ct);
            while (await accR.ReadAsync(ct))
            {
                var id = accR.GetGuid(0);
                var code = accR.GetString(1);
                switch (code)
                {
                    case "1010": cashAccount = id; break;
                    case "1020": bankAccount = id; break;
                    case "1100": arAccount = id; break;
                    case "2100": salesTaxAccount = id; break;
                    case "4010": salesRevenueAccount = id; break;
                }
            }
            await accR.CloseAsync();
        }

        var debitAccountId = req.PaymentMethod switch
        {
            "credit" => arAccount ?? cashAccount,
            "bank_transfer" or "card" => bankAccount ?? cashAccount,
            _ => cashAccount,
        };

        // Open fiscal period
        Guid activePeriodId;
        await using (var pCmd = conn.CreateCommand())
        {
            pCmd.Transaction = tx;
            pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
            pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            var pVal = await pCmd.ExecuteScalarAsync(ct);
            if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post sales revenue journal.");
            activePeriodId = pid;
        }

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-INV-{req.InvoiceNumber}";
        var idempotencyKey = $"sales-inv-rev-{invoiceId:N}";

        await using (var jourCmd = conn.CreateCommand())
        {
            jourCmd.Transaction = tx;
            jourCmd.CommandText = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @pid,
                     @desc, @funcCurr, 'sales', 'sales_invoice', @sid,
                     @key, 'posted', @by, @by, now())
                ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                """;
            jourCmd.Parameters.AddWithValue("id", journalId);
            jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            jourCmd.Parameters.AddWithValue("no", journalNo);
            jourCmd.Parameters.AddWithValue("date", invDate);
            jourCmd.Parameters.AddWithValue("pid", activePeriodId);
            jourCmd.Parameters.AddWithValue("desc", $"Sales Revenue for Invoice {req.InvoiceNumber}");
            jourCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
            jourCmd.Parameters.AddWithValue("sid", invoiceId.ToString());
            jourCmd.Parameters.AddWithValue("key", idempotencyKey);
            jourCmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? Ctx.UserId : createdBy);
            await jourCmd.ExecuteNonQueryAsync(ct);
        }

        // Dr AR / Cash
        await using (var l1 = conn.CreateCommand())
        {
            l1.Transaction = tx;
            l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, 'Accounts Receivable / Cash', @cur, @amt, 1.0, @amt, 0);";
            l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", debitAccountId); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", total);
            await l1.ExecuteNonQueryAsync(ct);
        }

        // Cr Sales Revenue
        var netRevenue = subtotal - req.DiscountAmount;
        await using (var l2 = conn.CreateCommand())
        {
            l2.Transaction = tx;
            l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, 'Sales Revenue', @cur, @amt, 1.0, 0, @amt);";
            l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", journalId); l2.Parameters.AddWithValue("aid", salesRevenueAccount); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", netRevenue);
            await l2.ExecuteNonQueryAsync(ct);
        }

        // Cr Sales Tax (if applicable)
        if (tax > 0m)
        {
            await using var l3 = conn.CreateCommand();
            l3.Transaction = tx;
            l3.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 3, @aid, 'Sales Tax Output', @cur, @amt, 1.0, 0, @amt);";
            l3.Parameters.AddWithValue("tid", Ctx.TenantId); l3.Parameters.AddWithValue("cid", Ctx.CompanyId); l3.Parameters.AddWithValue("jid", journalId); l3.Parameters.AddWithValue("aid", salesTaxAccount); l3.Parameters.AddWithValue("cur", functionalCurrency); l3.Parameters.AddWithValue("amt", tax);
            await l3.ExecuteNonQueryAsync(ct);
        }

        // Save gl_journal_id
        await using (var updInv = conn.CreateCommand())
        {
            updInv.Transaction = tx;
            updInv.CommandText = "UPDATE erp_sales.sales_invoice SET gl_journal_id = @jid WHERE invoice_id = @iid;";
            updInv.Parameters.AddWithValue("jid", journalId);
            updInv.Parameters.AddWithValue("iid", invoiceId);
            await updInv.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return invoiceId;
    }

    // ── 6. Customer Receipts & Payment Allocations ─────────────────────────────

    public async Task<IReadOnlyList<CustomerReceiptSummary>> ListReceiptsAsync(Guid? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT r.receipt_id, r.receipt_number, r.customer_id, c.name AS customer_name,
                   r.receipt_date, r.payment_method, r.amount, r.unallocated_amount,
                   r.reference_number, r.status, r.gl_journal_id, r.note,
                   r.received_by, r.created_at
            FROM erp_sales.customer_receipt r
            JOIN erp_sales.customer c ON c.tenant_id = r.tenant_id AND c.company_id = r.company_id AND c.customer_id = r.customer_id
            WHERE (@cust::uuid IS NULL OR r.customer_id = @cust)
            ORDER BY r.receipt_date DESC, r.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("cust", (object?)customerId ?? DBNull.Value);

        var receipts = new List<CustomerReceiptSummary>();
        var receiptIds = new List<Guid>();

        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var rId = r.GetGuid(0);
                receiptIds.Add(rId);
                receipts.Add(new CustomerReceiptSummary(
                    rId,
                    r.GetString(1),
                    r.GetGuid(2),
                    r.GetString(3),
                    DateOnly.FromDateTime(r.GetDateTime(4)),
                    r.GetString(5),
                    r.GetDecimal(6),
                    r.GetDecimal(7),
                    r.IsDBNull(8) ? null : r.GetString(8),
                    r.GetString(9),
                    r.IsDBNull(10) ? null : r.GetGuid(10),
                    r.IsDBNull(11) ? null : r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetGuid(12),
                    r.GetFieldValue<DateTimeOffset>(13),
                    []));
            }
            await r.CloseAsync();
        }

        if (receipts.Count == 0)
        {
            await tx.CommitAsync(ct);
            return receipts;
        }

        // Fetch allocations
        await using var aCmd = conn.CreateCommand();
        aCmd.Transaction = tx;
        aCmd.CommandText = """
            SELECT a.allocation_id, a.receipt_id, a.invoice_id, inv.invoice_number,
                   a.allocated_amount, a.allocated_at
            FROM erp_sales.customer_receipt_allocation a
            JOIN erp_sales.sales_invoice inv ON inv.tenant_id = a.tenant_id AND inv.company_id = a.company_id AND inv.invoice_id = a.invoice_id
            WHERE a.receipt_id = ANY(@ids)
            ORDER BY a.allocated_at ASC;
            """;
        aCmd.Parameters.AddWithValue("ids", receiptIds.ToArray());

        var allocMap = new Dictionary<Guid, List<CustomerReceiptAllocationSummary>>();
        await using (var ar = await aCmd.ExecuteReaderAsync(ct))
        {
            while (await ar.ReadAsync(ct))
            {
                var rId = ar.GetGuid(1);
                if (!allocMap.TryGetValue(rId, out var list))
                {
                    list = [];
                    allocMap[rId] = list;
                }
                list.Add(new CustomerReceiptAllocationSummary(
                    ar.GetGuid(0),
                    rId,
                    ar.GetGuid(2),
                    ar.GetString(3),
                    ar.GetDecimal(4),
                    ar.GetFieldValue<DateTimeOffset>(5)));
            }
            await ar.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return receipts.Select(rc => rc with
        {
            Allocations = allocMap.TryGetValue(rc.ReceiptId, out var allocs) ? allocs : []
        }).ToList();
    }

    public async Task<CustomerReceiptSummary?> GetReceiptAsync(Guid receiptId, CancellationToken ct)
    {
        var all = await ListReceiptsAsync(null, ct);
        return all.FirstOrDefault(r => r.ReceiptId == receiptId);
    }

    public async Task<Guid> CreateReceiptAsync(CreateCustomerReceiptRequest request, Guid receivedBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var receiptId = Guid.NewGuid();
        var rDate = request.ReceiptDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var allocations = request.Allocations ?? [];
        var totalAllocated = allocations.Sum(a => a.AllocatedAmount);

        if (totalAllocated > request.Amount)
        {
            throw new InvalidOperationException("ALLOCATION_EXCEEDS_RECEIPT_AMOUNT: Total allocated amount cannot exceed receipt amount.");
        }

        var unallocated = request.Amount - totalAllocated;

        cmd.CommandText = """
            INSERT INTO erp_sales.customer_receipt
                (tenant_id, company_id, receipt_id, receipt_number, customer_id, receipt_date,
                 payment_method, amount, unallocated_amount, reference_number, status, note, received_by)
            VALUES
                (@tid, @cid, @rid, @rno, @cust, @rdate,
                 @pay, @amt, @unalloc, @ref, 'posted', @note, @by);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("rid", receiptId);
        cmd.Parameters.AddWithValue("rno", request.ReceiptNumber.Trim());
        cmd.Parameters.AddWithValue("cust", request.CustomerId);
        cmd.Parameters.AddWithValue("rdate", rDate);
        cmd.Parameters.AddWithValue("pay", request.PaymentMethod);
        cmd.Parameters.AddWithValue("amt", request.Amount);
        cmd.Parameters.AddWithValue("unalloc", unallocated);
        cmd.Parameters.AddWithValue("ref", (object?)request.ReferenceNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", receivedBy == Guid.Empty ? Ctx.UserId : receivedBy);
        await cmd.ExecuteNonQueryAsync(ct);

        // Process allocations against open invoices
        foreach (var alloc in allocations)
        {
            await using var aCmd = conn.CreateCommand();
            aCmd.Transaction = tx;
            aCmd.CommandText = """
                INSERT INTO erp_sales.customer_receipt_allocation
                    (tenant_id, company_id, allocation_id, receipt_id, invoice_id, allocated_amount, allocated_at)
                VALUES
                    (@tid, @cid, @aid, @rid, @iid, @amt, now());

                UPDATE erp_sales.sales_invoice
                SET paid_amount = paid_amount + @amt,
                    invoice_status = CASE WHEN paid_amount + @amt >= total_amount THEN 'paid' ELSE 'partially_paid' END
                WHERE tenant_id = @tid AND company_id = @cid AND invoice_id = @iid;
                """;
            aCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            aCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            aCmd.Parameters.AddWithValue("aid", Guid.NewGuid());
            aCmd.Parameters.AddWithValue("rid", receiptId);
            aCmd.Parameters.AddWithValue("iid", alloc.InvoiceId);
            aCmd.Parameters.AddWithValue("amt", alloc.AllocatedAmount);
            await aCmd.ExecuteNonQueryAsync(ct);
        }

        // Decrement customer outstanding balance
        await using var custUpd = conn.CreateCommand();
        custUpd.Transaction = tx;
        custUpd.CommandText = """
            UPDATE erp_sales.customer
            SET outstanding_balance = GREATEST(0, outstanding_balance - @amt), updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND customer_id = @cust;
            """;
        custUpd.Parameters.AddWithValue("tid", Ctx.TenantId);
        custUpd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        custUpd.Parameters.AddWithValue("amt", request.Amount);
        custUpd.Parameters.AddWithValue("cust", request.CustomerId);
        await custUpd.ExecuteNonQueryAsync(ct);

        // Post Double-Entry Financial Journal: Dr Cash/Bank / Cr AR
        Guid? cashAccount = null;
        Guid? bankAccount = null;
        Guid? arAccount = null;

        await using (var accCmd = conn.CreateCommand())
        {
            accCmd.Transaction = tx;
            accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('1010', '1020', '1100');";
            accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            await using var accR = await accCmd.ExecuteReaderAsync(ct);
            while (await accR.ReadAsync(ct))
            {
                var id = accR.GetGuid(0);
                var code = accR.GetString(1);
                if (code == "1010") cashAccount = id;
                else if (code == "1020") bankAccount = id;
                else if (code == "1100") arAccount = id;
            }
            await accR.CloseAsync();
        }

        var debitAccountId = request.PaymentMethod == "cash" ? (cashAccount ?? bankAccount) : (bankAccount ?? cashAccount);

        Guid activePeriodId;
        await using (var pCmd = conn.CreateCommand())
        {
            pCmd.Transaction = tx;
            pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
            pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            var pVal = await pCmd.ExecuteScalarAsync(ct);
            if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post customer receipt journal.");
            activePeriodId = pid;
        }

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-RCPT-{request.ReceiptNumber}";
        var idempotencyKey = $"customer-receipt-{receiptId:N}";

        await using (var jourCmd = conn.CreateCommand())
        {
            jourCmd.Transaction = tx;
            jourCmd.CommandText = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @pid,
                     @desc, @funcCurr, 'sales', 'customer_receipt', @sid,
                     @key, 'posted', @by, @by, now())
                ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                """;
            jourCmd.Parameters.AddWithValue("id", journalId);
            jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            jourCmd.Parameters.AddWithValue("no", journalNo);
            jourCmd.Parameters.AddWithValue("date", rDate);
            jourCmd.Parameters.AddWithValue("pid", activePeriodId);
            jourCmd.Parameters.AddWithValue("desc", $"Customer Payment Receipt {request.ReceiptNumber}");
            jourCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
            jourCmd.Parameters.AddWithValue("sid", receiptId.ToString());
            jourCmd.Parameters.AddWithValue("key", idempotencyKey);
            jourCmd.Parameters.AddWithValue("by", receivedBy == Guid.Empty ? Ctx.UserId : receivedBy);
            await jourCmd.ExecuteNonQueryAsync(ct);
        }

        // Dr Cash / Bank
        await using (var l1 = conn.CreateCommand())
        {
            l1.Transaction = tx;
            l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, 'Cash / Bank Receipt', @cur, @amt, 1.0, @amt, 0);";
            l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", debitAccountId); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", request.Amount);
            await l1.ExecuteNonQueryAsync(ct);
        }

        // Cr Accounts Receivable (1100)
        await using (var l2 = conn.CreateCommand())
        {
            l2.Transaction = tx;
            l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, 'Customer Account Receivable Credit', @cur, @amt, 1.0, 0, @amt);";
            l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", journalId); l2.Parameters.AddWithValue("aid", arAccount); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", request.Amount);
            await l2.ExecuteNonQueryAsync(ct);
        }

        // Update receipt header with gl_journal_id
        await using (var updR = conn.CreateCommand())
        {
            updR.Transaction = tx;
            updR.CommandText = "UPDATE erp_sales.customer_receipt SET gl_journal_id = @jid WHERE receipt_id = @rid;";
            updR.Parameters.AddWithValue("jid", journalId);
            updR.Parameters.AddWithValue("rid", receiptId);
            await updR.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return receiptId;
    }

    // ── 7. Sales Returns & Customer Credit Notes ───────────────────────────────

    public async Task<IReadOnlyList<SalesReturnSummary>> ListReturnsAsync(Guid? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT r.return_id, r.return_number, r.customer_id, c.name AS customer_name,
                   r.invoice_id, inv.invoice_number, r.order_id, so.order_number,
                   r.return_date, r.total_amount, r.total_cost, r.status, r.reason,
                   r.authorized_by, r.authorized_at, r.gl_journal_id
            FROM erp_sales.sales_return r
            JOIN erp_sales.customer c ON c.tenant_id = r.tenant_id AND c.company_id = r.company_id AND c.customer_id = r.customer_id
            LEFT JOIN erp_sales.sales_invoice inv ON inv.tenant_id = r.tenant_id AND inv.company_id = r.company_id AND inv.invoice_id = r.invoice_id
            LEFT JOIN erp_sales.sales_order so ON so.tenant_id = r.tenant_id AND so.company_id = r.company_id AND so.order_id = r.order_id
            WHERE (@cust::uuid IS NULL OR r.customer_id = @cust)
            ORDER BY r.return_date DESC, r.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("cust", (object?)customerId ?? DBNull.Value);

        var returns = new List<SalesReturnSummary>();
        var returnIds = new List<Guid>();

        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var rId = r.GetGuid(0);
                returnIds.Add(rId);
                returns.Add(new SalesReturnSummary(
                    rId,
                    r.GetString(1),
                    r.GetGuid(2),
                    r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetGuid(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetGuid(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    DateOnly.FromDateTime(r.GetDateTime(8)),
                    r.GetDecimal(9),
                    r.GetDecimal(10),
                    r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetString(12),
                    r.IsDBNull(13) ? null : r.GetGuid(13),
                    r.IsDBNull(14) ? null : r.GetFieldValue<DateTimeOffset>(14),
                    r.IsDBNull(15) ? null : r.GetGuid(15),
                    []));
            }
            await r.CloseAsync();
        }

        if (returns.Count == 0)
        {
            await tx.CommitAsync(ct);
            return returns;
        }

        await using var lCmd = conn.CreateCommand();
        lCmd.Transaction = tx;
        lCmd.CommandText = """
            SELECT rl.return_id, rl.line_number, rl.variant_id, v.sku, rl.warehouse_id, w.code AS wh_code,
                   rl.bin_id, b.code AS bin_code, rl.returned_qty, rl.unit_price, rl.unit_cost,
                   rl.line_total, rl.reason
            FROM erp_sales.sales_return_line rl
            JOIN erp_inventory.product_variant v ON v.tenant_id = rl.tenant_id AND v.variant_id = rl.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = rl.tenant_id AND w.company_id = rl.company_id AND w.warehouse_id = rl.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = rl.tenant_id AND b.company_id = rl.company_id AND b.bin_id = rl.bin_id
            WHERE rl.return_id = ANY(@ids)
            ORDER BY rl.return_id, rl.line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", returnIds.ToArray());

        var linesMap = new Dictionary<Guid, List<SalesReturnLineSummary>>();
        await using (var lr = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lr.ReadAsync(ct))
            {
                var rId = lr.GetGuid(0);
                if (!linesMap.TryGetValue(rId, out var list))
                {
                    list = [];
                    linesMap[rId] = list;
                }
                list.Add(new SalesReturnLineSummary(
                    lr.GetInt32(1),
                    lr.GetGuid(2),
                    lr.GetString(3),
                    lr.GetGuid(4),
                    lr.GetString(5),
                    lr.GetGuid(6),
                    lr.GetString(7),
                    lr.GetDecimal(8),
                    lr.GetDecimal(9),
                    lr.GetDecimal(10),
                    lr.GetDecimal(11),
                    lr.IsDBNull(12) ? null : lr.GetString(12)));
            }
            await lr.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return returns.Select(rt => rt with
        {
            Lines = linesMap.TryGetValue(rt.ReturnId, out var lines) ? lines : []
        }).ToList();
    }

    public async Task<SalesReturnSummary?> GetReturnAsync(Guid returnId, CancellationToken ct)
    {
        var all = await ListReturnsAsync(null, ct);
        return all.FirstOrDefault(r => r.ReturnId == returnId);
    }

    public async Task<Guid> CreateReturnAsync(CreateSalesReturnRequest request, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var returnId = Guid.NewGuid();
        var rDate = request.ReturnDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var lines = request.Lines ?? [];
        if (lines.Count == 0) throw new InvalidOperationException("RETURN_REQUIRES_LINES");

        decimal totalAmount = lines.Sum(l => l.ReturnedQty * l.UnitPrice);
        decimal totalCost = lines.Sum(l => l.ReturnedQty * l.UnitCost);

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_return
                (tenant_id, company_id, return_id, return_number, customer_id, invoice_id, order_id,
                 return_date, total_amount, total_cost, status, reason)
            VALUES
                (@tid, @cid, @rid, @rno, @cust, @inv, @oid,
                 @rdate, @amt, @cost, 'draft', @reason);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("rid", returnId);
        cmd.Parameters.AddWithValue("rno", request.ReturnNumber.Trim());
        cmd.Parameters.AddWithValue("cust", request.CustomerId);
        cmd.Parameters.AddWithValue("inv", (object?)request.InvoiceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("oid", (object?)request.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rdate", rDate);
        cmd.Parameters.AddWithValue("amt", totalAmount);
        cmd.Parameters.AddWithValue("cost", totalCost);
        cmd.Parameters.AddWithValue("reason", (object?)request.Reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var lineTot = l.ReturnedQty * l.UnitPrice;
            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_return_line
                    (tenant_id, company_id, return_id, line_number, variant_id, warehouse_id,
                     bin_id, returned_qty, unit_price, unit_cost, line_total, reason)
                VALUES
                    (@tid, @cid, @rid, @ln, @var, @wh,
                     @bin, @qty, @price, @cost, @tot, @reason);
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("rid", returnId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.ReturnedQty);
            lCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lCmd.Parameters.AddWithValue("cost", l.UnitCost);
            lCmd.Parameters.AddWithValue("tot", lineTot);
            lCmd.Parameters.AddWithValue("reason", (object?)l.Reason ?? DBNull.Value);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return returnId;
    }

    public async Task<bool> ApproveReturnAsync(Guid returnId, Guid authorizedBy, CancellationToken ct)
    {
        var ret = await GetReturnAsync(returnId, ct);
        if (ret is null) throw new InvalidOperationException("RETURN_NOT_FOUND");
        if (ret.Status != "draft") return true;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var totalCost = 0m;
        var movementId = Guid.NewGuid();

        // 1. Create movement header for goods return
        await using (var movCmd = conn.CreateCommand())
        {
            movCmd.Transaction = tx;
            movCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                     source_document_type, source_document_id, occurred_at, created_by, note)
                VALUES
                    (@tid, @cid, @mid, gen_random_uuid(), 'receipt',
                     'sales_return', @rid, now(), @by, @note);
                """;
            movCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            movCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            movCmd.Parameters.AddWithValue("mid", movementId);
            movCmd.Parameters.AddWithValue("rid", returnId);
            movCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            movCmd.Parameters.AddWithValue("note", $"Customer sales return {ret.ReturnNumber}");
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < ret.Lines.Count; i++)
        {
            var l = ret.Lines[i];
            var ln = i + 1;
            var effectiveCost = l.UnitCost > 0m ? l.UnitCost : l.UnitPrice;
            var lineCost = l.ReturnedQty * effectiveCost;
            totalCost += lineCost;

            // Restock: increment stock_balance
            await using var balCmd = conn.CreateCommand();
            balCmd.Transaction = tx;
            balCmd.CommandText = """
                INSERT INTO erp_inventory.stock_balance
                    (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                VALUES
                    (@tid, @cid, @var, @wh, @bin, 'available', @qty, gen_random_uuid())
                ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
                """;
            balCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            balCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            balCmd.Parameters.AddWithValue("var", l.VariantId);
            balCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            balCmd.Parameters.AddWithValue("bin", l.BinId);
            balCmd.Parameters.AddWithValue("qty", l.ReturnedQty);
            await balCmd.ExecuteNonQueryAsync(ct);

            // Create new FIFO Cost Layer for returned stock
            await using var layerCmd = conn.CreateCommand();
            layerCmd.Transaction = tx;
            layerCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
                     source_document_type, source_document_id, source_line_number, source_movement_id,
                     received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tid, @cid, gen_random_uuid(), @var, @wh, @bin, 'available',
                     'sales_return', @rid, @ln, @mid,
                     now(), @qty, @qty, @cost, @cur);
                """;
            layerCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            layerCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            layerCmd.Parameters.AddWithValue("var", l.VariantId);
            layerCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            layerCmd.Parameters.AddWithValue("bin", l.BinId);
            layerCmd.Parameters.AddWithValue("rid", returnId);
            layerCmd.Parameters.AddWithValue("ln", ln);
            layerCmd.Parameters.AddWithValue("mid", movementId);
            layerCmd.Parameters.AddWithValue("qty", l.ReturnedQty);
            layerCmd.Parameters.AddWithValue("cost", effectiveCost);
            layerCmd.Parameters.AddWithValue("cur", functionalCurrency);
            await layerCmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Post COGS Reversal Journal: Dr 1200 (Inventory Asset) / Cr 5010 (COGS)
        Guid? glJournalId = null;
        if (totalCost > 0m)
        {
            Guid cogsAccount = Guid.Empty;
            Guid invAccount = Guid.Empty;
            await using (var accCmd = conn.CreateCommand())
            {
                accCmd.Transaction = tx;
                accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('5010', '1200');";
                accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                await using var accR = await accCmd.ExecuteReaderAsync(ct);
                while (await accR.ReadAsync(ct))
                {
                    var id = accR.GetGuid(0);
                    var code = accR.GetString(1);
                    if (code == "5010") cogsAccount = id;
                    else if (code == "1200") invAccount = id;
                }
                await accR.CloseAsync();
            }

            Guid activePeriodId;
            await using (var pCmd = conn.CreateCommand())
            {
                pCmd.Transaction = tx;
                pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
                pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                var pVal = await pCmd.ExecuteScalarAsync(ct);
                if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post return COGS journal.");
                activePeriodId = pid;
            }

            var journalId = Guid.NewGuid();
            var journalNo = $"JRN-RET-{ret.ReturnNumber}";
            var idempotencyKey = $"sales-return-cogs-{returnId:N}";

            await using (var jourCmd = conn.CreateCommand())
            {
                jourCmd.Transaction = tx;
                jourCmd.CommandText = """
                    INSERT INTO finance.journals
                        (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                         description, functional_currency, source_module, source_type, source_id,
                         idempotency_key, status, created_by, posted_by, posted_at)
                    VALUES
                        (@id, @tid, @cid, @no, @date, @pid,
                         @desc, @funcCurr, 'sales', 'sales_return', @sid,
                         @key, 'posted', @by, @by, now())
                    ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                    """;
                jourCmd.Parameters.AddWithValue("id", journalId);
                jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                jourCmd.Parameters.AddWithValue("no", journalNo);
                jourCmd.Parameters.AddWithValue("date", ret.ReturnDate.ToDateTime(TimeOnly.MinValue));
                jourCmd.Parameters.AddWithValue("pid", activePeriodId);
                jourCmd.Parameters.AddWithValue("desc", $"COGS Reversal for Return {ret.ReturnNumber}");
                jourCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
                jourCmd.Parameters.AddWithValue("sid", returnId.ToString());
                jourCmd.Parameters.AddWithValue("key", idempotencyKey);
                jourCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
                await jourCmd.ExecuteNonQueryAsync(ct);
            }

            // Dr 1200 Inventory Asset
            await using (var l1 = conn.CreateCommand())
            {
                l1.Transaction = tx;
                l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, 'Inventory Asset Restock', @cur, @amt, 1.0, @amt, 0);";
                l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", invAccount); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", totalCost);
                await l1.ExecuteNonQueryAsync(ct);
            }

            // Cr 5010 COGS
            await using (var l2 = conn.CreateCommand())
            {
                l2.Transaction = tx;
                l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, 'Cost of Goods Sold Reversal', @cur, @amt, 1.0, 0, @amt);";
                l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", journalId); l2.Parameters.AddWithValue("aid", cogsAccount); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", totalCost);
                await l2.ExecuteNonQueryAsync(ct);
            }

            glJournalId = journalId;
        }

        // 3. Mark return approved
        await using (var updR = conn.CreateCommand())
        {
            updR.Transaction = tx;
            updR.CommandText = """
                UPDATE erp_sales.sales_return
                SET status = 'approved',
                    authorized_by = @by,
                    authorized_at = now(),
                    gl_journal_id = @jid,
                    total_cost = @cost,
                    updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND return_id = @rid;
                """;
            updR.Parameters.AddWithValue("tid", Ctx.TenantId);
            updR.Parameters.AddWithValue("cid", Ctx.CompanyId);
            updR.Parameters.AddWithValue("rid", returnId);
            updR.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            updR.Parameters.AddWithValue("jid", (object?)glJournalId ?? DBNull.Value);
            updR.Parameters.AddWithValue("cost", totalCost);
            await updR.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SalesCreditNoteSummary>> ListCreditNotesAsync(Guid? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT cn.credit_note_id, cn.credit_note_number, cn.customer_id, c.name AS customer_name,
                   cn.return_id, cn.invoice_id, cn.credit_note_date, cn.currency_code,
                   cn.subtotal_amount, cn.tax_amount, cn.total_amount, cn.status,
                   cn.gl_journal_id, cn.reason, cn.authorized_by, cn.authorized_at
            FROM erp_sales.sales_credit_note cn
            JOIN erp_sales.customer c ON c.tenant_id = cn.tenant_id AND c.company_id = cn.company_id AND c.customer_id = cn.customer_id
            WHERE (@cust::uuid IS NULL OR cn.customer_id = @cust)
            ORDER BY cn.credit_note_date DESC, cn.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("cust", (object?)customerId ?? DBNull.Value);

        var creditNotes = new List<SalesCreditNoteSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            creditNotes.Add(new SalesCreditNoteSummary(
                r.GetGuid(0),
                r.GetString(1),
                r.GetGuid(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetGuid(4),
                r.IsDBNull(5) ? null : r.GetGuid(5),
                DateOnly.FromDateTime(r.GetDateTime(6)),
                r.GetString(7),
                r.GetDecimal(8),
                r.GetDecimal(9),
                r.GetDecimal(10),
                r.GetString(11),
                r.IsDBNull(12) ? null : r.GetGuid(12),
                r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? null : r.GetGuid(14),
                r.IsDBNull(15) ? null : r.GetFieldValue<DateTimeOffset>(15)));
        }
        await r.CloseAsync();
        await tx.CommitAsync(ct);
        return creditNotes;
    }

    public async Task<SalesCreditNoteSummary?> GetCreditNoteAsync(Guid creditNoteId, CancellationToken ct)
    {
        var all = await ListCreditNotesAsync(null, ct);
        return all.FirstOrDefault(cn => cn.CreditNoteId == creditNoteId);
    }

    public async Task<Guid> CreateCreditNoteAsync(CreateSalesCreditNoteRequest request, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var creditNoteId = Guid.NewGuid();
        var cnDate = request.CreditNoteDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
        var totalAmount = request.SubtotalAmount + request.TaxAmount;

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_credit_note
                (tenant_id, company_id, credit_note_id, credit_note_number, customer_id, return_id,
                 invoice_id, credit_note_date, currency_code, subtotal_amount, tax_amount, total_amount,
                 status, reason)
            VALUES
                (@tid, @cid, @cnid, @cnno, @cust, @rid,
                 @iid, @cndate, @cur, @sub, @tax, @tot,
                 'draft', @reason);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("cnid", creditNoteId);
        cmd.Parameters.AddWithValue("cnno", request.CreditNoteNumber.Trim());
        cmd.Parameters.AddWithValue("cust", request.CustomerId);
        cmd.Parameters.AddWithValue("rid", (object?)request.ReturnId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("iid", (object?)request.InvoiceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cndate", cnDate);
        cmd.Parameters.AddWithValue("cur", functionalCurrency);
        cmd.Parameters.AddWithValue("sub", request.SubtotalAmount);
        cmd.Parameters.AddWithValue("tax", request.TaxAmount);
        cmd.Parameters.AddWithValue("tot", totalAmount);
        cmd.Parameters.AddWithValue("reason", (object?)request.Reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return creditNoteId;
    }

    public async Task<bool> ApproveCreditNoteAsync(Guid creditNoteId, Guid authorizedBy, CancellationToken ct)
    {
        var cn = await GetCreditNoteAsync(creditNoteId, ct);
        if (cn is null) throw new InvalidOperationException("CREDIT_NOTE_NOT_FOUND");
        if (cn.Status != "draft") return true;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        // 1. Post Revenue and Tax Reversal Journal: Dr 4010, Dr 2100 / Cr 1100
        Guid? arAccount = null;
        Guid? salesRevenueAccount = null;
        Guid? salesTaxAccount = null;

        await using (var accCmd = conn.CreateCommand())
        {
            accCmd.Transaction = tx;
            accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('1100', '2100', '4010');";
            accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            await using var accR = await accCmd.ExecuteReaderAsync(ct);
            while (await accR.ReadAsync(ct))
            {
                var id = accR.GetGuid(0);
                var code = accR.GetString(1);
                if (code == "1100") arAccount = id;
                else if (code == "2100") salesTaxAccount = id;
                else if (code == "4010") salesRevenueAccount = id;
            }
            await accR.CloseAsync();
        }

        Guid activePeriodId;
        await using (var pCmd = conn.CreateCommand())
        {
            pCmd.Transaction = tx;
            pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
            pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            var pVal = await pCmd.ExecuteScalarAsync(ct);
            if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post credit note journal.");
            activePeriodId = pid;
        }

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-CN-{cn.CreditNoteNumber}";
        var idempotencyKey = $"sales-credit-note-{creditNoteId:N}";

        await using (var jourCmd = conn.CreateCommand())
        {
            jourCmd.Transaction = tx;
            jourCmd.CommandText = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @pid,
                     @desc, @funcCurr, 'sales', 'sales_credit_note', @sid,
                     @key, 'posted', @by, @by, now())
                ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                """;
            jourCmd.Parameters.AddWithValue("id", journalId);
            jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            jourCmd.Parameters.AddWithValue("no", journalNo);
            jourCmd.Parameters.AddWithValue("date", cn.CreditNoteDate.ToDateTime(TimeOnly.MinValue));
            jourCmd.Parameters.AddWithValue("pid", activePeriodId);
            jourCmd.Parameters.AddWithValue("desc", $"Credit Note {cn.CreditNoteNumber}");
            jourCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
            jourCmd.Parameters.AddWithValue("sid", creditNoteId.ToString());
            jourCmd.Parameters.AddWithValue("key", idempotencyKey);
            jourCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            await jourCmd.ExecuteNonQueryAsync(ct);
        }

        // Dr 4010 Sales Revenue Reversal
        await using (var l1 = conn.CreateCommand())
        {
            l1.Transaction = tx;
            l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, 'Sales Revenue Reversal', @cur, @amt, 1.0, @amt, 0);";
            l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", salesRevenueAccount); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", cn.SubtotalAmount);
            await l1.ExecuteNonQueryAsync(ct);
        }

        // Dr 2100 Sales Tax Reversal (if applicable)
        var lineIndex = 2;
        if (cn.TaxAmount > 0m)
        {
            await using var lTax = conn.CreateCommand();
            lTax.Transaction = tx;
            lTax.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, @ln, @aid, 'Sales Tax Reversal', @cur, @amt, 1.0, @amt, 0);";
            lTax.Parameters.AddWithValue("tid", Ctx.TenantId); lTax.Parameters.AddWithValue("cid", Ctx.CompanyId); lTax.Parameters.AddWithValue("jid", journalId); lTax.Parameters.AddWithValue("ln", lineIndex++); lTax.Parameters.AddWithValue("aid", salesTaxAccount); lTax.Parameters.AddWithValue("cur", functionalCurrency); lTax.Parameters.AddWithValue("amt", cn.TaxAmount);
            await lTax.ExecuteNonQueryAsync(ct);
        }

        // Cr 1100 Accounts Receivable (Recovery / Relief)
        await using (var lAr = conn.CreateCommand())
        {
            lAr.Transaction = tx;
            lAr.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, @ln, @aid, 'Accounts Receivable Credit Recovery', @cur, @amt, 1.0, 0, @amt);";
            lAr.Parameters.AddWithValue("tid", Ctx.TenantId); lAr.Parameters.AddWithValue("cid", Ctx.CompanyId); lAr.Parameters.AddWithValue("jid", journalId); lAr.Parameters.AddWithValue("ln", lineIndex); lAr.Parameters.AddWithValue("aid", arAccount); lAr.Parameters.AddWithValue("cur", functionalCurrency); lAr.Parameters.AddWithValue("amt", cn.TotalAmount);
            await lAr.ExecuteNonQueryAsync(ct);
        }

        // 2. Decrement customer outstanding balance
        await using (var custCmd = conn.CreateCommand())
        {
            custCmd.Transaction = tx;
            custCmd.CommandText = "UPDATE erp_sales.customer SET outstanding_balance = GREATEST(0, outstanding_balance - @amt), updated_at = now() WHERE tenant_id = @tid AND company_id = @cid AND customer_id = @cust;";
            custCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            custCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            custCmd.Parameters.AddWithValue("amt", cn.TotalAmount);
            custCmd.Parameters.AddWithValue("cust", cn.CustomerId);
            await custCmd.ExecuteNonQueryAsync(ct);
        }

        // 3. Mark credit note approved
        await using (var updCn = conn.CreateCommand())
        {
            updCn.Transaction = tx;
            updCn.CommandText = """
                UPDATE erp_sales.sales_credit_note
                SET status = 'approved',
                    authorized_by = @by,
                    authorized_at = now(),
                    gl_journal_id = @jid,
                    updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND credit_note_id = @cnid;
                """;
            updCn.Parameters.AddWithValue("tid", Ctx.TenantId);
            updCn.Parameters.AddWithValue("cid", Ctx.CompanyId);
            updCn.Parameters.AddWithValue("cnid", creditNoteId);
            updCn.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            updCn.Parameters.AddWithValue("jid", journalId);
            await updCn.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    // ── 8. Accounts Receivable Subledger & Statements ──────────────────────────

    public async Task<IReadOnlyList<CustomerArAgingSummary>> ListCustomerArAgingAsync(CancellationToken ct)
    {
        var customers = await ListCustomersAsync(ct);
        var invoices = await ListInvoicesAsync(ct);
        var creditNotes = await ListCreditNotesAsync(null, ct);
        var receipts = await ListReceiptsAsync(null, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<CustomerArAgingSummary>();

        foreach (var c in customers)
        {
            var custInvoices = invoices
                .Where(i => i.CustomerId == c.CustomerId && i.PaymentMethod == "credit" && i.InvoiceStatus != "reversed" && i.InvoiceStatus != "cancelled")
                .Select(i => new ArAgingEngine.InvoiceAgingSlice(
                    i.InvoiceId,
                    i.InvoiceNumber,
                    i.InvoiceDate,
                    i.InvoiceDate.AddDays(c.PaymentTermsDays > 0 ? c.PaymentTermsDays : 30),
                    i.TotalAmount,
                    i.PaidAmount))
                .ToList();

            var totalCn = creditNotes
                .Where(cn => cn.CustomerId == c.CustomerId && cn.Status == "approved")
                .Sum(cn => cn.TotalAmount);

            var totalRcpt = receipts
                .Where(rc => rc.CustomerId == c.CustomerId && rc.Status == "posted")
                .Sum(rc => rc.Amount);

            var aging = ArAgingEngine.CalculateCustomerAging(
                c.CustomerId,
                c.CustomerCode,
                c.Name,
                c.CurrencyCode,
                custInvoices,
                totalCn,
                totalRcpt,
                today);

            results.Add(aging);
        }

        return results;
    }

    public async Task<CustomerStatementSummary?> GetCustomerStatementAsync(Guid customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var customer = await GetCustomerAsync(customerId, ct);
        if (customer is null) return null;

        var invoices = (await ListInvoicesAsync(ct))
            .Where(i => i.CustomerId == customerId && i.PaymentMethod == "credit" && i.InvoiceStatus != "reversed" && i.InvoiceStatus != "cancelled");

        var receipts = (await ListReceiptsAsync(customerId, ct))
            .Where(r => r.Status == "posted");

        var creditNotes = (await ListCreditNotesAsync(customerId, ct))
            .Where(cn => cn.Status == "approved");

        // Calculate opening balance prior to fromDate
        decimal openingDebits = invoices.Where(i => i.InvoiceDate < fromDate).Sum(i => i.TotalAmount);
        decimal openingCredits = receipts.Where(r => r.ReceiptDate < fromDate).Sum(r => r.Amount)
                               + creditNotes.Where(cn => cn.CreditNoteDate < fromDate).Sum(cn => cn.TotalAmount);
        decimal openingBalance = Math.Max(0m, openingDebits - openingCredits);

        var transactions = new List<(DateOnly TxDate, string TxType, string RefNo, string? Desc, decimal Debit, decimal Credit)>();

        foreach (var inv in invoices.Where(i => i.InvoiceDate >= fromDate && i.InvoiceDate <= toDate))
        {
            transactions.Add((inv.InvoiceDate, "invoice", inv.InvoiceNumber, $"Sales Invoice #{inv.InvoiceNumber}", inv.TotalAmount, 0m));
        }

        foreach (var r in receipts.Where(r => r.ReceiptDate >= fromDate && r.ReceiptDate <= toDate))
        {
            transactions.Add((r.ReceiptDate, "receipt", r.ReceiptNumber, $"Customer Receipt #{r.ReceiptNumber} ({r.PaymentMethod})", 0m, r.Amount));
        }

        foreach (var cn in creditNotes.Where(cn => cn.CreditNoteDate >= fromDate && cn.CreditNoteDate <= toDate))
        {
            transactions.Add((cn.CreditNoteDate, "credit_note", cn.CreditNoteNumber, $"Credit Note #{cn.CreditNoteNumber} ({cn.Reason})", 0m, cn.TotalAmount));
        }

        // Sort chronologically
        var sorted = transactions.OrderBy(t => t.TxDate).ThenBy(t => t.TxType == "invoice" ? 0 : 1).ToList();

        decimal running = openingBalance;
        decimal totalDebits = 0m;
        decimal totalCredits = 0m;
        var items = new List<CustomerStatementItem>();

        foreach (var tx in sorted)
        {
            running += tx.Debit - tx.Credit;
            totalDebits += tx.Debit;
            totalCredits += tx.Credit;

            items.Add(new CustomerStatementItem(
                tx.TxDate,
                tx.TxType,
                tx.RefNo,
                tx.Desc,
                tx.Debit,
                tx.Credit,
                Math.Max(0m, running)));
        }

        return new CustomerStatementSummary(
            customer.CustomerId,
            customer.CustomerCode,
            customer.Name,
            fromDate,
            toDate,
            openingBalance,
            totalDebits,
            totalCredits,
            Math.Max(0m, running),
            items);
    }
}
