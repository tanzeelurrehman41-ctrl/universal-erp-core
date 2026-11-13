using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Sales;

public sealed class PostgresSalesRepository(
    NpgsqlDataSource dataSource,
    ITenantContextAccessor tenantContext) : ISalesRepository
{
    private TenantContext Ctx => tenantContext.Current;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct) =>
        await dataSource.OpenConnectionAsync(ct);

    private static async Task SetRls(NpgsqlCommand cmd, TenantContext ctx)
    {
        cmd.CommandText = "SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("t", ctx.TenantId.ToString());
        cmd.Parameters.AddWithValue("c", ctx.CompanyId.ToString());
        await cmd.ExecuteNonQueryAsync();
        cmd.Parameters.Clear();
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CustomerSummary>> ListCustomersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT customer_id, customer_code, name, customer_type, contact_name,
                   email, phone, address, ntn_number, strn_number, credit_limit,
                   outstanding_balance, currency_code, payment_terms_days, is_active
            FROM erp_sales.customer
            ORDER BY name;
            """;

        var list = new List<CustomerSummary>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CustomerSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetString(12),
                    reader.GetInt32(13),
                    reader.GetBoolean(14)));
            }
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var all = await ListCustomersAsync(ct);
        return all.FirstOrDefault(c => c.CustomerId == customerId);
    }

    public async Task<Guid> CreateCustomerAsync(CreateCustomerRequest req, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            INSERT INTO erp_sales.customer
                (tenant_id, company_id, customer_code, name, customer_type, contact_name,
                 email, phone, address, ntn_number, strn_number, credit_limit,
                 currency_code, payment_terms_days, is_active)
            VALUES
                (@tid, @cid, @code, @name, @type, @contact,
                 @email, @phone, @addr, @ntn, @strn, @credit,
                 @cur, @terms, true)
            RETURNING customer_id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("code", req.CustomerCode.Trim());
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("type", req.CustomerType);
        cmd.Parameters.AddWithValue("contact", (object?)req.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)req.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)req.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("addr", (object?)req.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ntn", (object?)req.NtnNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("strn", (object?)req.StrnNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("credit", req.CreditLimit);
        cmd.Parameters.AddWithValue("cur", req.CurrencyCode);
        cmd.Parameters.AddWithValue("terms", req.PaymentTermsDays);

        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest req, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_sales.customer
            SET name = @name, customer_type = @type, contact_name = @contact,
                email = @email, phone = @phone, address = @addr,
                ntn_number = @ntn, strn_number = @strn, credit_limit = @credit,
                payment_terms_days = @terms, is_active = @active, updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND customer_id = @id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", customerId);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("type", req.CustomerType);
        cmd.Parameters.AddWithValue("contact", (object?)req.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)req.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)req.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("addr", (object?)req.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ntn", (object?)req.NtnNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("strn", (object?)req.StrnNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("credit", req.CreditLimit);
        cmd.Parameters.AddWithValue("terms", req.PaymentTermsDays);
        cmd.Parameters.AddWithValue("active", req.IsActive);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    // ── Sales Orders ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SalesOrderSummary>> ListOrdersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT o.order_id, o.order_number, o.customer_id, c.name,
                   o.warehouse_id, w.name, o.order_type, o.order_status,
                   o.currency_code, o.order_date, o.subtotal_amount,
                   o.tax_amount, o.discount_amount, o.total_amount,
                   o.note, o.created_at
            FROM erp_sales.sales_order o
            JOIN erp_sales.customer c
              ON c.tenant_id = o.tenant_id AND c.company_id = o.company_id AND c.customer_id = o.customer_id
            JOIN erp_inventory.warehouse w
              ON w.tenant_id = o.tenant_id AND w.company_id = o.company_id AND w.warehouse_id = o.warehouse_id
            ORDER BY o.created_at DESC;
            """;

        var orders = new List<(SalesOrderSummary Header, Guid OrderId)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                orders.Add((new SalesOrderSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    DateOnly.FromDateTime(reader.GetDateTime(9)),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.GetFieldValue<DateTimeOffset>(15),
                    []), reader.GetGuid(0)));
            }
            await reader.CloseAsync();
        }

        if (orders.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var orderIds = orders.Select(o => o.OrderId).ToArray();
        await using var lineCmd = conn.CreateCommand();
        lineCmd.Transaction = tx;
        lineCmd.CommandText = """
            SELECT l.order_id, l.line_number, l.variant_id, v.sku,
                   l.bin_id, b.code, l.ordered_qty, l.fulfilled_qty,
                   l.unit_of_measure_id, u.code, l.unit_price,
                   l.tax_rate, l.line_total, l.line_status
            FROM erp_sales.sales_order_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.unit_of_measure_id
            WHERE l.order_id = ANY(@ids)
            ORDER BY l.order_id, l.line_number;
            """;
        lineCmd.Parameters.AddWithValue("ids", orderIds);

        var linesMap = new Dictionary<Guid, List<SalesOrderLineSummary>>();
        await using (var lReader = await lineCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                var oId = lReader.GetGuid(0);
                if (!linesMap.TryGetValue(oId, out var ll)) { ll = []; linesMap[oId] = ll; }
                ll.Add(new SalesOrderLineSummary(
                    lReader.GetInt32(1),
                    lReader.GetGuid(2),
                    lReader.GetString(3),
                    lReader.GetGuid(4),
                    lReader.GetString(5),
                    lReader.GetDecimal(6),
                    lReader.GetDecimal(7),
                    lReader.GetGuid(8),
                    lReader.GetString(9),
                    lReader.GetDecimal(10),
                    lReader.GetDecimal(11),
                    lReader.GetDecimal(12),
                    lReader.GetString(13)));
            }
            await lReader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return orders.Select(o => o.Header with
        {
            Lines = linesMap.TryGetValue(o.OrderId, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<SalesOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken ct)
    {
        var all = await ListOrdersAsync(ct);
        return all.FirstOrDefault(o => o.OrderId == orderId);
    }

    public async Task<Guid> CreateOrderAsync(CreateSalesOrderRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        decimal subtotal = 0;
        decimal tax = 0;
        var lines = req.Lines ?? [];
        foreach (var l in lines)
        {
            var lineSub = l.OrderedQty * l.UnitPrice;
            var lineTax = lineSub * l.TaxRate;
            subtotal += lineSub;
            tax += lineTax;
        }
        var total = subtotal + tax;

        cmd.CommandText = """
            INSERT INTO erp_sales.sales_order
                (tenant_id, company_id, order_number, customer_id, warehouse_id,
                 order_type, order_status, currency_code, order_date,
                 subtotal_amount, tax_amount, discount_amount, total_amount, note, created_by)
            VALUES
                (@tid, @cid, @no, @cust, @wh,
                 @type, 'draft', 'PKR', @date,
                 @sub, @tax, 0, @tot, @note, @by)
            RETURNING order_id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("no", req.OrderNumber.Trim());
        cmd.Parameters.AddWithValue("cust", req.CustomerId);
        cmd.Parameters.AddWithValue("wh", req.WarehouseId);
        cmd.Parameters.AddWithValue("type", req.OrderType);
        cmd.Parameters.AddWithValue("date", req.OrderDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date);
        cmd.Parameters.AddWithValue("sub", subtotal);
        cmd.Parameters.AddWithValue("tax", tax);
        cmd.Parameters.AddWithValue("tot", total);
        cmd.Parameters.AddWithValue("note", (object?)req.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);

        var orderId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var lTotal = (l.OrderedQty * l.UnitPrice) * (1 + l.TaxRate);
            await using var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = """
                INSERT INTO erp_sales.sales_order_line
                    (tenant_id, company_id, order_id, line_number, variant_id,
                     bin_id, ordered_qty, fulfilled_qty, unit_of_measure_id,
                     unit_price, tax_rate, line_total, line_status)
                VALUES
                    (@tid, @cid, @oId, @ln, @var,
                     @bin, @qty, 0, @uom,
                     @price, @tax, @tot, 'pending');
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("oId", orderId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.OrderedQty);
            lCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
            lCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lCmd.Parameters.AddWithValue("tax", l.TaxRate);
            lCmd.Parameters.AddWithValue("tot", lTotal);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return orderId;
    }

    public async Task ConfirmOrderAsync(Guid orderId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_sales.sales_order
            SET order_status = 'confirmed', updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND order_id = @id AND order_status = 'draft';
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", orderId);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ── Sales Invoices & POS Receipts ─────────────────────────────────────────

    public async Task<IReadOnlyList<SalesInvoiceSummary>> ListInvoicesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT i.invoice_id, i.invoice_number, i.client_transaction_id, i.order_id,
                   i.customer_id, c.name, i.payment_method, i.invoice_status,
                   i.invoice_date, i.subtotal_amount, i.tax_amount, i.discount_amount,
                   i.total_amount, i.paid_amount, i.note, i.posted_at
            FROM erp_sales.sales_invoice i
            JOIN erp_sales.customer c
              ON c.tenant_id = i.tenant_id AND c.company_id = i.company_id AND c.customer_id = i.customer_id
            ORDER BY i.posted_at DESC;
            """;

        var invoices = new List<(SalesInvoiceSummary Header, Guid InvoiceId)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                invoices.Add((new SalesInvoiceSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    DateOnly.FromDateTime(reader.GetDateTime(8)),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.GetFieldValue<DateTimeOffset>(15),
                    []), reader.GetGuid(0)));
            }
            await reader.CloseAsync();
        }

        if (invoices.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var invoiceIds = invoices.Select(i => i.InvoiceId).ToArray();
        await using var lineCmd = conn.CreateCommand();
        lineCmd.Transaction = tx;
        lineCmd.CommandText = """
            SELECT l.invoice_id, l.line_number, l.order_line_number,
                   l.variant_id, v.sku, l.warehouse_id, w.code,
                   l.bin_id, b.code, l.quantity, l.unit_of_measure_id,
                   u.code, l.unit_price, l.unit_cost, l.tax_rate, l.line_total
            FROM erp_sales.sales_invoice_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = l.tenant_id AND w.company_id = l.company_id AND w.warehouse_id = l.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.unit_of_measure_id
            WHERE l.invoice_id = ANY(@ids)
            ORDER BY l.invoice_id, l.line_number;
            """;
        lineCmd.Parameters.AddWithValue("ids", invoiceIds);

        var linesMap = new Dictionary<Guid, List<SalesInvoiceLineSummary>>();
        await using (var lReader = await lineCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                var invId = lReader.GetGuid(0);
                if (!linesMap.TryGetValue(invId, out var ll)) { ll = []; linesMap[invId] = ll; }
                ll.Add(new SalesInvoiceLineSummary(
                    lReader.GetInt32(1),
                    lReader.IsDBNull(2) ? null : lReader.GetInt32(2),
                    lReader.GetGuid(3),
                    lReader.GetString(4),
                    lReader.GetGuid(5),
                    lReader.GetString(6),
                    lReader.GetGuid(7),
                    lReader.GetString(8),
                    lReader.GetDecimal(9),
                    lReader.GetGuid(10),
                    lReader.GetString(11),
                    lReader.GetDecimal(12),
                    lReader.GetDecimal(13),
                    lReader.GetDecimal(14),
                    lReader.GetDecimal(15)));
            }
            await lReader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return invoices.Select(i => i.Header with
        {
            Lines = linesMap.TryGetValue(i.InvoiceId, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> GetVariantRequiresCogsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, bool>();
        if (variantIds.Count == 0) return result;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT v.variant_id, (p.product_type = 'stock') AS requires_cogs
            FROM erp_inventory.product_variant v
            JOIN erp_inventory.product p ON p.tenant_id = v.tenant_id AND p.product_id = v.product_id
            WHERE v.tenant_id = @tid AND v.variant_id = ANY(@ids);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("ids", variantIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetGuid(0)] = reader.GetBoolean(1);
        }
        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<bool> CanViewCostAsync(CancellationToken ct)
    {
        if (Ctx.UserId == Guid.Empty) return false;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT (
                identity.user_has_permission(@tid, @cid, @uid, 'inventory.valuation.view')
                OR identity.user_has_permission(@tid, @cid, @uid, 'finance.journal.view')
                OR identity.user_has_permission(@tid, @cid, @uid, 'finance.account.view')
                OR identity.user_has_permission(@tid, @cid, @uid, 'inventory.receipt.post')
                OR identity.user_has_permission(@tid, @cid, @uid, 'inventory.adjustment.create')
                OR identity.user_has_permission(@tid, @cid, @uid, 'purchasing.grn.view')
            );
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("uid", Ctx.UserId);

        var val = await cmd.ExecuteScalarAsync(ct);
        await tx.CommitAsync(ct);
        return val is bool b && b;
    }

    public async Task<SalesInvoiceSummary?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var all = await ListInvoicesAsync(ct);
        return all.FirstOrDefault(i => i.InvoiceId == invoiceId);
    }

    public async Task<Guid> PostInvoiceAsync(PostSalesInvoiceRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetRls(cmd, Ctx);

        var lines = req.Lines ?? [];
        if (lines.Count == 0)
            throw new InvalidOperationException("INVOICE_REQUIRES_LINES");
        if (req.DiscountAmount < 0m)
            throw new InvalidOperationException("INVALID_DISCOUNT_AMOUNT");

        // 0. Defense-in-depth validation of pricing and totals (client-supplied UnitCost is ignored)
        decimal subtotal = 0;
        decimal tax = 0;
        foreach (var l in lines)
        {
            if (l.Quantity <= 0m)
                throw new InvalidOperationException("INVALID_INVOICE_LINE: Quantity must be greater than zero.");
            if (l.UnitPrice <= 0m)
                throw new InvalidOperationException("INVALID_SELLING_PRICE: Selling price must be greater than zero.");
            if (l.TaxRate < 0m)
                throw new InvalidOperationException("INVALID_TAX_RATE: Tax rate cannot be negative.");

            var lineSub = l.Quantity * l.UnitPrice;
            var lineTax = lineSub * l.TaxRate;
            subtotal += lineSub;
            tax += lineTax;
        }

        var netRevenue = subtotal - req.DiscountAmount;
        var total = subtotal + tax - req.DiscountAmount;
        if (subtotal <= 0m || netRevenue <= 0m || total <= 0m)
            throw new InvalidOperationException("INVALID_INVOICE_TOTAL: Invoice total and net revenue must be greater than zero.");

        var variantCogsRequired = new Dictionary<Guid, bool>();
        await using (var varCmd = conn.CreateCommand())
        {
            varCmd.Transaction = tx;
            varCmd.CommandText = """
                SELECT v.variant_id, (p.product_type = 'stock') AS requires_cogs
                FROM erp_inventory.product_variant v
                JOIN erp_inventory.product p ON p.tenant_id = v.tenant_id AND p.product_id = v.product_id
                WHERE v.tenant_id = @tid AND v.variant_id = ANY(@ids);
                """;
            varCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            varCmd.Parameters.AddWithValue("ids", lines.Select(l => l.VariantId).Distinct().ToArray());
            await using var varReader = await varCmd.ExecuteReaderAsync(ct);
            while (await varReader.ReadAsync(ct))
            {
                variantCogsRequired[varReader.GetGuid(0)] = varReader.GetBoolean(1);
            }
            await varReader.CloseAsync();
        }

        var anyInventoryTracked = lines.Any(l =>
            !variantCogsRequired.TryGetValue(l.VariantId, out var tracked) || tracked);

        // 1. Verify aggregated stock sufficiency per (variant, warehouse, bin)
        var requestedByBucket = lines
            .GroupBy(l => (l.VariantId, l.WarehouseId, l.BinId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var bucket in requestedByBucket)
        {
            await using var chkCmd = conn.CreateCommand();
            chkCmd.Transaction = tx;
            chkCmd.CommandText = """
                SELECT on_hand_qty FROM erp_inventory.stock_balance
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                  AND warehouse_id = @wh AND bin_id = @bin AND status_code = 'available'
                FOR UPDATE;
                """;
            chkCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            chkCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            chkCmd.Parameters.AddWithValue("var", bucket.Key.VariantId);
            chkCmd.Parameters.AddWithValue("wh", bucket.Key.WarehouseId);
            chkCmd.Parameters.AddWithValue("bin", bucket.Key.BinId);

            var onHandVal = await chkCmd.ExecuteScalarAsync(ct);
            var onHand = onHandVal is decimal dec ? dec : 0m;
            if (onHand < bucket.Value)
            {
                throw new InvalidOperationException($"INSUFFICIENT_STOCK: Available {onHand}, Requested {bucket.Value}");
            }
        }

        // 2. Payment and Metadata Initialization
        var paid = req.PaymentMethod == "credit" ? 0m : total;
        var invoiceId = Guid.NewGuid();
        var invoiceDate = req.InvoiceDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;

        // 3. Insert Invoice Header FIRST (enforces idempotency on ClientTransactionId & InvoiceNumber before stock/FIFO mutations)
        cmd.CommandText = """
            INSERT INTO erp_sales.sales_invoice
                (tenant_id, company_id, invoice_id, invoice_number, client_transaction_id,
                 order_id, customer_id, payment_method, invoice_status, invoice_date,
                 subtotal_amount, tax_amount, discount_amount, total_amount, paid_amount,
                 note, created_by, posted_at)
            VALUES
                (@tid, @cid, @id, @no, @txId,
                 @oId, @cust, @pay, 'posted', @date,
                 @sub, @tax, @disc, @tot, @paid,
                 @note, @by, now())
            RETURNING invoice_id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", invoiceId);
        cmd.Parameters.AddWithValue("no", req.InvoiceNumber.Trim());
        cmd.Parameters.AddWithValue("txId", req.ClientTransactionId);
        cmd.Parameters.AddWithValue("oId", req.OrderId.HasValue ? (object)req.OrderId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("cust", req.CustomerId);
        cmd.Parameters.AddWithValue("pay", req.PaymentMethod);
        cmd.Parameters.AddWithValue("date", invoiceDate);
        cmd.Parameters.AddWithValue("sub", subtotal);
        cmd.Parameters.AddWithValue("tax", tax);
        cmd.Parameters.AddWithValue("disc", req.DiscountAmount);
        cmd.Parameters.AddWithValue("tot", total);
        cmd.Parameters.AddWithValue("paid", paid);
        cmd.Parameters.AddWithValue("note", (object?)req.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        // 4. Create Inventory Movement Header
        var movementId = Guid.NewGuid();
        await using (var movCmd = conn.CreateCommand())
        {
            movCmd.Transaction = tx;
            movCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                     source_document_type, source_document_id, occurred_at, created_by, note)
                VALUES
                    (@tid, @cid, @mId, @txId, 'issue',
                     'sales_invoice', @inv, now(), @by, @note);
                """;
            movCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            movCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            movCmd.Parameters.AddWithValue("mId", movementId);
            movCmd.Parameters.AddWithValue("txId", req.ClientTransactionId);
            movCmd.Parameters.AddWithValue("inv", invoiceId);
            movCmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
            movCmd.Parameters.AddWithValue("note", $"Sales dispatch for Invoice {req.InvoiceNumber}");
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        // 5. Server-Side FIFO Valuation Consumption, Invoice Lines, Movement Legs & Stock Relief
        decimal totalCost = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var lineNumber = i + 1;
            var requiresCogs = !variantCogsRequired.TryGetValue(l.VariantId, out var tracked) || tracked;
            var lineUnitCost = 0m;

            if (requiresCogs)
            {
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
                    layerSel.Parameters.AddWithValue("wh", l.WarehouseId);
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
                    l.Quantity,
                    locationDescription: $"warehouse {l.WarehouseId}, bin {l.BinId}",
                    expectedWarehouseId: l.WarehouseId,
                    expectedBinId: l.BinId);
                lineUnitCost = allocationResult.EffectiveUnitCost;
                totalCost += allocationResult.TotalCost;

                foreach (var alloc in allocationResult.Allocations)
                {
                    await using var updLayer = conn.CreateCommand();
                    updLayer.Transaction = tx;
                    updLayer.CommandText = """
                        UPDATE erp_inventory.inventory_cost_layer
                        SET remaining_qty = remaining_qty - @consumed,
                            updated_at = now()
                        WHERE tenant_id = @tid
                          AND company_id = @cid
                          AND layer_id = @layerId
                          AND remaining_qty >= @consumed;
                        """;
                    updLayer.Parameters.AddWithValue("tid", Ctx.TenantId);
                    updLayer.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    updLayer.Parameters.AddWithValue("layerId", alloc.LayerId);
                    updLayer.Parameters.AddWithValue("consumed", alloc.ConsumedQty);
                    var rowsUpdated = await updLayer.ExecuteNonQueryAsync(ct);
                    if (rowsUpdated != 1)
                    {
                        throw new InvalidOperationException($"INSUFFICIENT_VALUATION_LAYERS: Concurrent layer modification on layer {alloc.LayerId}.");
                    }

                    await using var insAlloc = conn.CreateCommand();
                    insAlloc.Transaction = tx;
                    insAlloc.CommandText = """
                        INSERT INTO erp_inventory.inventory_cost_allocation
                            (tenant_id, company_id, allocation_id, layer_id, movement_id,
                             source_document_type, source_document_id, source_line_number,
                             variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                        VALUES
                            (@tid, @cid, @allocId, @layerId, @movId,
                             'sales_invoice', @invId, @ln,
                             @var, @qty, @cost, @extCost, 'PKR');
                        """;
                    insAlloc.Parameters.AddWithValue("tid", Ctx.TenantId);
                    insAlloc.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    insAlloc.Parameters.AddWithValue("allocId", Guid.NewGuid());
                    insAlloc.Parameters.AddWithValue("layerId", alloc.LayerId);
                    insAlloc.Parameters.AddWithValue("movId", movementId);
                    insAlloc.Parameters.AddWithValue("invId", invoiceId);
                    insAlloc.Parameters.AddWithValue("ln", lineNumber);
                    insAlloc.Parameters.AddWithValue("var", l.VariantId);
                    insAlloc.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                    insAlloc.Parameters.AddWithValue("cost", alloc.UnitCost);
                    insAlloc.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                    await insAlloc.ExecuteNonQueryAsync(ct);
                }

                await using var valEntryCmd = conn.CreateCommand();
                valEntryCmd.Transaction = tx;
                valEntryCmd.CommandText = """
                    INSERT INTO erp_inventory.valuation_entry
                        (tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
                         entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at)
                    VALUES
                        (@tid, @cid, @valId, @movId, @var,
                         'issue_cost', @qty, @unitCost, @extCost, 'PKR', now());
                    """;
                valEntryCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                valEntryCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                valEntryCmd.Parameters.AddWithValue("valId", Guid.NewGuid());
                valEntryCmd.Parameters.AddWithValue("movId", movementId);
                valEntryCmd.Parameters.AddWithValue("var", l.VariantId);
                valEntryCmd.Parameters.AddWithValue("qty", -l.Quantity);
                valEntryCmd.Parameters.AddWithValue("unitCost", lineUnitCost);
                valEntryCmd.Parameters.AddWithValue("extCost", -allocationResult.TotalCost);
                await valEntryCmd.ExecuteNonQueryAsync(ct);
            }

            var lineTot = (l.Quantity * l.UnitPrice) * (1 + l.TaxRate);
            await using (var lCmd = conn.CreateCommand())
            {
                lCmd.Transaction = tx;
                lCmd.CommandText = """
                    INSERT INTO erp_sales.sales_invoice_line
                        (tenant_id, company_id, invoice_id, line_number, order_line_number,
                         variant_id, warehouse_id, bin_id, quantity, unit_of_measure_id,
                         unit_price, unit_cost, tax_rate, line_total)
                    VALUES
                        (@tid, @cid, @inv, @ln, @oln,
                         @var, @wh, @bin, @qty, @uom,
                         @price, @cost, @tax, @tot);
                    """;
                lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                lCmd.Parameters.AddWithValue("inv", invoiceId);
                lCmd.Parameters.AddWithValue("ln", lineNumber);
                lCmd.Parameters.AddWithValue("oln", l.OrderLineNumber.HasValue ? (object)l.OrderLineNumber.Value : DBNull.Value);
                lCmd.Parameters.AddWithValue("var", l.VariantId);
                lCmd.Parameters.AddWithValue("wh", l.WarehouseId);
                lCmd.Parameters.AddWithValue("bin", l.BinId);
                lCmd.Parameters.AddWithValue("qty", l.Quantity);
                lCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
                lCmd.Parameters.AddWithValue("price", l.UnitPrice);
                lCmd.Parameters.AddWithValue("cost", lineUnitCost);
                lCmd.Parameters.AddWithValue("tax", l.TaxRate);
                lCmd.Parameters.AddWithValue("tot", lineTot);
                await lCmd.ExecuteNonQueryAsync(ct);
            }

            await using (var legCmd = conn.CreateCommand())
            {
                legCmd.Transaction = tx;
                legCmd.CommandText = """
                    INSERT INTO erp_inventory.inventory_movement_leg
                        (tenant_id, company_id, movement_id, leg_number, variant_id,
                         warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
                    VALUES
                        (@tid, @cid, @mId, @ln, @var,
                         @wh, @bin, 'available', @qtyDelta, @uom);

                    UPDATE erp_inventory.stock_balance
                    SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                    WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                      AND warehouse_id = @wh AND bin_id = @bin AND status_code = 'available';
                    """;
                legCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                legCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                legCmd.Parameters.AddWithValue("mId", movementId);
                legCmd.Parameters.AddWithValue("ln", lineNumber);
                legCmd.Parameters.AddWithValue("var", l.VariantId);
                legCmd.Parameters.AddWithValue("wh", l.WarehouseId);
                legCmd.Parameters.AddWithValue("bin", l.BinId);
                legCmd.Parameters.AddWithValue("qtyDelta", -l.Quantity);
                legCmd.Parameters.AddWithValue("qty", l.Quantity);
                legCmd.Parameters.AddWithValue("uom", l.UnitOfMeasureId);
                await legCmd.ExecuteNonQueryAsync(ct);
            }
        }

        // 6. Update Customer Outstanding Balance if credit sale
        if (req.PaymentMethod == "credit")
        {
            await using var custUpdCmd = conn.CreateCommand();
            custUpdCmd.Transaction = tx;
            custUpdCmd.CommandText = """
                UPDATE erp_sales.customer
                SET outstanding_balance = outstanding_balance + @tot, updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND customer_id = @cust;
                """;
            custUpdCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            custUpdCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            custUpdCmd.Parameters.AddWithValue("tot", total);
            custUpdCmd.Parameters.AddWithValue("cust", req.CustomerId);
            await custUpdCmd.ExecuteNonQueryAsync(ct);
        }

        // 7. Automated Double-Entry General Ledger Posting
        Guid? openPeriodId = null;
        await using (var perCmd = conn.CreateCommand())
        {
            perCmd.Transaction = tx;
            perCmd.CommandText = """
                SELECT id FROM finance.fiscal_periods
                WHERE tenant_id = @tid AND company_id = @cid AND status = 'open'
                ORDER BY starts_on ASC LIMIT 1;
                """;
            perCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            perCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            var pVal = await perCmd.ExecuteScalarAsync(ct);
            if (pVal is Guid pId) openPeriodId = pId;
        }

        Guid? arAccount = null;
        Guid? cashAccount = null;
        Guid? bankAccount = null;
        Guid? salesRevenueAccount = null;
        Guid? salesTaxAccount = null;
        Guid? cogsAccount = null;
        Guid? inventoryAssetAccount = null;

        await using (var accCmd = conn.CreateCommand())
        {
            accCmd.Transaction = tx;
            accCmd.CommandText = """
                SELECT id, account_code FROM finance.accounts
                WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('1010', '1020', '1100', '1200', '2100', '4010', '5010');
                """;
            accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            await using var accReader = await accCmd.ExecuteReaderAsync(ct);
            while (await accReader.ReadAsync(ct))
            {
                var id = accReader.GetGuid(0);
                var code = accReader.GetString(1);
                switch (code)
                {
                    case "1010": cashAccount = id; break;
                    case "1020": bankAccount = id; break;
                    case "1100": arAccount = id; break;
                    case "1200": inventoryAssetAccount = id; break;
                    case "2100": salesTaxAccount = id; break;
                    case "4010": salesRevenueAccount = id; break;
                    case "5010": cogsAccount = id; break;
                }
            }
            await accReader.CloseAsync();
        }

        var debitAccountId = req.PaymentMethod switch
        {
            "credit" => arAccount ?? cashAccount,
            "card" or "bank_transfer" => bankAccount ?? cashAccount,
            _ => cashAccount,
        };

        if (!openPeriodId.HasValue)
            throw new InvalidOperationException("GL_POSTING_FAILED: No open fiscal period found for sales invoice GL posting.");
        if (!debitAccountId.HasValue)
            throw new InvalidOperationException("GL_POSTING_FAILED: Debit account (Cash/Bank/AR) is missing.");
        if (!salesRevenueAccount.HasValue)
            throw new InvalidOperationException("GL_POSTING_FAILED: Sales Revenue account (4010) is missing.");
        if (tax > 0 && !salesTaxAccount.HasValue)
            throw new InvalidOperationException("GL_POSTING_FAILED: Sales Tax Output account (2100) is missing.");
        if ((anyInventoryTracked || totalCost > 0) && (!cogsAccount.HasValue || !inventoryAssetAccount.HasValue))
            throw new InvalidOperationException("GL_POSTING_FAILED: COGS (5010) or Inventory Asset (1200) account is missing.");
        if (anyInventoryTracked && totalCost <= 0)
            throw new InvalidOperationException("GL_POSTING_FAILED: Positive COGS is required for inventory-tracked sales.");

        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-INV-{req.InvoiceNumber}";
        var glIdempotencyKey = $"sales-inv-gl-{invoiceId}";

        await using (var jourCmd = conn.CreateCommand())
        {
            jourCmd.Transaction = tx;
            jourCmd.CommandText = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @per,
                     @desc, 'PKR', 'sales', 'sales_invoice', @srcId,
                     @key, 'posted', @by, @by, now());
                """;
            jourCmd.Parameters.AddWithValue("id", journalId);
            jourCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            jourCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            jourCmd.Parameters.AddWithValue("no", journalNo);
            jourCmd.Parameters.AddWithValue("date", invoiceDate);
            jourCmd.Parameters.AddWithValue("per", openPeriodId.Value);
            jourCmd.Parameters.AddWithValue("desc", $"GL posting for Invoice {req.InvoiceNumber}");
            jourCmd.Parameters.AddWithValue("srcId", invoiceId.ToString());
            jourCmd.Parameters.AddWithValue("key", glIdempotencyKey);
            jourCmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
            await jourCmd.ExecuteNonQueryAsync(ct);
        }

        const string insLineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tid, @cid, @jId, @ln, @acc, @desc,
                 'PKR', @amt, 1.0, @deb, @cred);
            """;

        var lineNo = 1;
        decimal totalJournalDebits = 0m;
        decimal totalJournalCredits = 0m;

        async Task InsertJournalLineAsync(Guid accountId, string description, decimal debit, decimal credit)
        {
            await using var lineCmd = conn.CreateCommand();
            lineCmd.Transaction = tx;
            lineCmd.CommandText = insLineSql;
            lineCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lineCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lineCmd.Parameters.AddWithValue("jId", journalId);
            lineCmd.Parameters.AddWithValue("ln", lineNo++);
            lineCmd.Parameters.AddWithValue("acc", accountId);
            lineCmd.Parameters.AddWithValue("desc", description);
            lineCmd.Parameters.AddWithValue("amt", debit > 0 ? debit : credit);
            lineCmd.Parameters.AddWithValue("deb", debit);
            lineCmd.Parameters.AddWithValue("cred", credit);
            await lineCmd.ExecuteNonQueryAsync(ct);
            totalJournalDebits += debit;
            totalJournalCredits += credit;
        }

        // Line 1: Debit Cash or Bank or AR for Total
        await InsertJournalLineAsync(debitAccountId.Value, $"Payment/Receivable - {req.InvoiceNumber}", total, 0m);

        // Line 2: Credit Sales Revenue for Net Revenue (Subtotal - Discount)
        await InsertJournalLineAsync(salesRevenueAccount.Value, $"Sales Revenue - {req.InvoiceNumber}", 0m, netRevenue);

        // Line 3: Credit Sales Tax Payable (if tax > 0)
        if (tax > 0)
        {
            await InsertJournalLineAsync(salesTaxAccount!.Value, $"Sales Tax Output - {req.InvoiceNumber}", 0m, tax);
        }

        // Line 4 & 5: Cost of Goods Sold & Inventory Asset (when cost > 0)
        if (totalCost > 0)
        {
            await InsertJournalLineAsync(cogsAccount!.Value, $"COGS - {req.InvoiceNumber}", totalCost, 0m);
            await InsertJournalLineAsync(inventoryAssetAccount!.Value, $"Inventory relief - {req.InvoiceNumber}", 0m, totalCost);
        }

        if (totalJournalDebits <= 0m || totalJournalDebits != totalJournalCredits)
            throw new InvalidOperationException($"GL_POSTING_FAILED: Unbalanced GL journal (Debits {totalJournalDebits}, Credits {totalJournalCredits}).");

        // 8. Outbox Event
        var outboxPayload = JsonSerializer.Serialize(new
        {
            invoiceId,
            invoiceNumber = req.InvoiceNumber,
            clientTransactionId = req.ClientTransactionId,
            customerId = req.CustomerId,
            totalAmount = total,
            linesCount = lines.Count,
        });

        await using (var outCmd = conn.CreateCommand())
        {
            outCmd.Transaction = tx;
            outCmd.CommandText = """
                INSERT INTO erp_sales.outbox_event
                    (tenant_id, company_id, event_id, invoice_id, event_type, payload, published_at)
                VALUES
                    (@tid, @cid, @eId, @inv, 'sales.invoice.posted', @payload::jsonb, now());
                """;
            outCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            outCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            outCmd.Parameters.AddWithValue("eId", Guid.NewGuid());
            outCmd.Parameters.AddWithValue("inv", invoiceId);
            outCmd.Parameters.AddWithValue("payload", outboxPayload);
            await outCmd.ExecuteNonQueryAsync(ct);
        }

        // Write to sync change_log so offline clients can download this invoice.
        await CompanyChangeLogWriter.AppendAsync(
            conn,
            tx,
            Ctx.TenantId,
            Ctx.CompanyId,
            "sales_invoice",
            invoiceId,
            1,
            "upsert",
            outboxPayload,
            ct);

        await tx.CommitAsync(ct);
        return invoiceId;
    }
}
