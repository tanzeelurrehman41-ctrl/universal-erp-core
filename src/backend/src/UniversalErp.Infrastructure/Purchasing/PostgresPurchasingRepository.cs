using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Infrastructure.Purchasing;

/// <summary>
/// All queries are scoped by the RLS context set by the middleware:
///   SET LOCAL app.tenant_id = '…'; SET LOCAL app.company_id = '…';
/// No tenant/company parameter is passed explicitly.
/// </summary>
public sealed class PostgresPurchasingRepository(
    NpgsqlDataSource dataSource,
    ITenantContextAccessor tenantContext) : IPurchasingRepository
{
    private TenantContext Ctx => tenantContext.Current;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SetRls(NpgsqlCommand cmd, TenantContext ctx)
    {
        await using var setTenant = cmd.Connection!.CreateCommand();
        setTenant.CommandText =
            $"SET LOCAL app.tenant_id = '{ctx.TenantId}'; " +
            $"SET LOCAL app.company_id = '{ctx.CompanyId}';";
        await setTenant.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = await dataSource.OpenConnectionAsync(ct);
        return conn;
    }

    // ── Suppliers ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SupplierSummary>> ListSuppliersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT supplier_id, supplier_code, name, contact_name, email, phone,
                   currency_code, payment_terms_days, is_active
            FROM erp_purchasing.supplier
            ORDER BY name
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<SupplierSummary>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SupplierSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetBoolean(8)));
        }
        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<Guid> CreateSupplierAsync(CreateSupplierRequest req, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            INSERT INTO erp_purchasing.supplier
              (tenant_id, company_id, supplier_code, name, contact_name, email, phone,
               address, currency_code, payment_terms_days)
            VALUES
              (@tid, @cid, @code, @name, @contact, @email, @phone,
               @address, @currency, @terms)
            RETURNING supplier_id
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("code", req.SupplierCode.Trim());
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("contact", req.ContactName as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", req.Email as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", req.Phone as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", req.Address as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency", req.CurrencyCode.ToUpperInvariant());
        cmd.Parameters.AddWithValue("terms", req.PaymentTermsDays);

        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest req, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_purchasing.supplier
            SET name = @name, contact_name = @contact, email = @email,
                phone = @phone, address = @address, payment_terms_days = @terms,
                is_active = @active, updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND supplier_id = @id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", supplierId);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("contact", (object?)req.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)req.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)req.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", (object?)req.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("terms", req.PaymentTermsDays);
        cmd.Parameters.AddWithValue("active", req.IsActive);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    // ── Purchase Orders ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<PurchaseOrderSummary>> ListPurchaseOrdersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT
              po.purchase_order_id, po.po_number,
              po.supplier_id, s.name AS supplier_name,
              po.warehouse_id, w.name AS warehouse_name,
              po.order_status, po.currency_code,
              po.order_date, po.expected_date, po.note
            FROM erp_purchasing.purchase_order po
            JOIN erp_purchasing.supplier s
              ON s.tenant_id = po.tenant_id AND s.company_id = po.company_id AND s.supplier_id = po.supplier_id
            JOIN erp_inventory.warehouse w
              ON w.tenant_id = po.tenant_id AND w.company_id = po.company_id AND w.warehouse_id = po.warehouse_id
            ORDER BY po.created_at DESC
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var orders = new List<(PurchaseOrderSummary Header, Guid PoId)>();
        while (await reader.ReadAsync(ct))
        {
            orders.Add((new PurchaseOrderSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                []), reader.GetGuid(0)));
        }
        await reader.CloseAsync();

        // Load lines for all POs in one query.
        if (orders.Count == 0) { await tx.CommitAsync(ct); return []; }
        var poIds = orders.Select(o => o.PoId).ToArray();
        await using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = """
            SELECT
              l.purchase_order_id, l.line_number,
              l.variant_id, v.sku,
              l.ordered_qty, l.received_qty,
              l.unit_of_measure_id, u.code AS uom_code,
              l.unit_price, l.line_status
            FROM erp_purchasing.purchase_order_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.unit_of_measure_id
            WHERE l.purchase_order_id = ANY(@ids)
            ORDER BY l.purchase_order_id, l.line_number
            """;
        lineCmd.Parameters.AddWithValue("ids", poIds);
        await using var lineReader = await lineCmd.ExecuteReaderAsync(ct);
        var linesMap = new Dictionary<Guid, List<PurchaseOrderLineSummary>>();
        while (await lineReader.ReadAsync(ct))
        {
            var poId = lineReader.GetGuid(0);
            if (!linesMap.TryGetValue(poId, out var lineList)) { lineList = []; linesMap[poId] = lineList; }
            lineList.Add(new PurchaseOrderLineSummary(
                lineReader.GetInt32(1),
                lineReader.GetGuid(2),
                lineReader.GetString(3),
                lineReader.GetDecimal(4),
                lineReader.GetDecimal(5),
                lineReader.GetGuid(6),
                lineReader.GetString(7),
                lineReader.GetDecimal(8),
                lineReader.GetString(9)));
        }
        await lineReader.CloseAsync();

        await tx.CommitAsync(ct);
        return orders.Select(o => o.Header with
        {
            Lines = linesMap.TryGetValue(o.PoId, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<PurchaseOrderSummary?> GetPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken ct)
    {
        var all = await ListPurchaseOrdersAsync(ct);
        return all.FirstOrDefault(po => po.PurchaseOrderId == purchaseOrderId);
    }

    public async Task<Guid> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            INSERT INTO erp_purchasing.purchase_order
              (tenant_id, company_id, po_number, supplier_id, warehouse_id,
               currency_code, order_date, expected_date, note, created_by)
            VALUES
              (@tid, @cid, @pono, @sup, @wh,
               @cur, @odate, @edate, @note, @by)
            RETURNING purchase_order_id
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("pono", req.PoNumber.Trim());
        cmd.Parameters.AddWithValue("sup", req.SupplierId);
        cmd.Parameters.AddWithValue("wh", req.WarehouseId);
        cmd.Parameters.AddWithValue("cur", "PKR");
        cmd.Parameters.AddWithValue("odate", req.OrderDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("edate", req.ExpectedDate.HasValue
            ? (object)req.ExpectedDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("note", req.Note as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);

        var poId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;

        // Insert lines.
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var line = req.Lines[i];
            await using var lCmd = conn.CreateCommand();
            lCmd.CommandText = """
                INSERT INTO erp_purchasing.purchase_order_line
                  (tenant_id, company_id, purchase_order_id, line_number,
                   variant_id, ordered_qty, unit_of_measure_id, unit_price)
                VALUES (@tid, @cid, @po, @ln, @var, @qty, @uom, @price)
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("po", poId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", line.VariantId);
            lCmd.Parameters.AddWithValue("qty", line.OrderedQty);
            lCmd.Parameters.AddWithValue("uom", line.UnitOfMeasureId);
            lCmd.Parameters.AddWithValue("price", line.UnitPrice);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return poId;
    }

    public async Task ConfirmPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_purchasing.purchase_order
               SET order_status = 'confirmed', updated_at = now()
             WHERE purchase_order_id = @id AND order_status = 'draft'
            """;
        cmd.Parameters.AddWithValue("id", purchaseOrderId);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ── GRNs ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrnSummary>> ListGrnsAsync(Guid? purchaseOrderId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT
              g.grn_id, g.grn_number,
              g.purchase_order_id, po.po_number,
              g.supplier_id, s.name,
              g.warehouse_id, w.name,
              g.grn_status, g.received_date,
              g.supplier_reference, g.note, g.posted_at
            FROM erp_purchasing.goods_received_note g
            JOIN erp_purchasing.purchase_order po
              ON po.tenant_id = g.tenant_id AND po.company_id = g.company_id AND po.purchase_order_id = g.purchase_order_id
            JOIN erp_purchasing.supplier s
              ON s.tenant_id = g.tenant_id AND s.company_id = g.company_id AND s.supplier_id = g.supplier_id
            JOIN erp_inventory.warehouse w
              ON w.tenant_id = g.tenant_id AND w.company_id = g.company_id AND w.warehouse_id = g.warehouse_id
            WHERE (@poFilter::uuid IS NULL OR g.purchase_order_id = @poFilter::uuid)
            ORDER BY g.created_at DESC
            """;
        cmd.Parameters.Add("poFilter", NpgsqlTypes.NpgsqlDbType.Uuid).Value = purchaseOrderId.HasValue
            ? purchaseOrderId.Value
            : DBNull.Value;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var grns = new List<(GrnSummary Header, Guid GrnId)>();
        while (await reader.ReadAsync(ct))
        {
            grns.Add((new GrnSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                []), reader.GetGuid(0)));
        }
        await reader.CloseAsync();

        if (grns.Count == 0) { await tx.CommitAsync(ct); return []; }
        var grnIds = grns.Select(g => g.GrnId).ToArray();
        await using var lCmd = conn.CreateCommand();
        lCmd.CommandText = """
            SELECT
              l.grn_id, l.line_number, l.po_line_number,
              l.variant_id, v.sku,
              l.bin_id, b.code AS bin_code,
              l.received_qty,
              u.code AS uom_code,
              l.unit_price
            FROM erp_purchasing.goods_received_note_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.unit_of_measure_id
            WHERE l.grn_id = ANY(@ids)
            ORDER BY l.grn_id, l.line_number
            """;
        lCmd.Parameters.AddWithValue("ids", grnIds);
        await using var lReader = await lCmd.ExecuteReaderAsync(ct);
        var linesMap = new Dictionary<Guid, List<GrnLineSummary>>();
        while (await lReader.ReadAsync(ct))
        {
            var grnId = lReader.GetGuid(0);
            if (!linesMap.TryGetValue(grnId, out var ll)) { ll = []; linesMap[grnId] = ll; }
            ll.Add(new GrnLineSummary(
                lReader.GetInt32(1), lReader.GetInt32(2),
                lReader.GetGuid(3), lReader.GetString(4),
                lReader.GetGuid(5), lReader.GetString(6),
                lReader.GetDecimal(7), lReader.GetString(8),
                lReader.GetDecimal(9)));
        }
        await lReader.CloseAsync();

        await tx.CommitAsync(ct);
        return grns.Select(g => g.Header with
        {
            Lines = linesMap.TryGetValue(g.GrnId, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<GrnSummary?> GetGrnAsync(Guid grnId, CancellationToken ct)
    {
        var all = await ListGrnsAsync(null, ct);
        return all.FirstOrDefault(g => g.GrnId == grnId);
    }

    public async Task<Guid> PostGrnAsync(PostGrnRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        // Resolve supplier from PO (already validated by service layer).
        cmd.CommandText = "SELECT supplier_id, warehouse_id FROM erp_purchasing.purchase_order WHERE purchase_order_id = @id";
        cmd.Parameters.AddWithValue("id", req.PurchaseOrderId);
        Guid supplierId; Guid warehouseId;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new InvalidOperationException("Purchase order not found.");
            supplierId = r.GetGuid(0); warehouseId = r.GetGuid(1);
        }

        // Insert GRN header.
        await using var grnCmd = conn.CreateCommand();
        grnCmd.CommandText = """
            INSERT INTO erp_purchasing.goods_received_note
              (tenant_id, company_id, client_transaction_id, grn_number,
               purchase_order_id, supplier_id, warehouse_id,
               grn_status, received_date, supplier_reference, note, created_by, posted_at)
            VALUES
              (@tid, @cid, @ctid, @grn,
               @po, @sup, @wh,
               'posted', @rdate, @sref, @note, @by, now())
            RETURNING grn_id
            """;
        grnCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        grnCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        grnCmd.Parameters.AddWithValue("ctid", req.ClientTransactionId);
        grnCmd.Parameters.AddWithValue("grn", req.GrnNumber.Trim());
        grnCmd.Parameters.AddWithValue("po", req.PurchaseOrderId);
        grnCmd.Parameters.AddWithValue("sup", supplierId);
        grnCmd.Parameters.AddWithValue("wh", warehouseId);
        grnCmd.Parameters.AddWithValue("rdate", req.ReceivedDate.ToDateTime(TimeOnly.MinValue));
        grnCmd.Parameters.AddWithValue("sref", req.SupplierReference as object ?? DBNull.Value);
        grnCmd.Parameters.AddWithValue("note", req.Note as object ?? DBNull.Value);
        grnCmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? DBNull.Value : (object)createdBy);
        var grnId = (Guid)(await grnCmd.ExecuteScalarAsync(ct))!;

        // Insert GRN lines and update PO line received quantities.
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var line = req.Lines[i];
            await using var lCmd = conn.CreateCommand();
            lCmd.CommandText = """
                INSERT INTO erp_purchasing.goods_received_note_line
                  (tenant_id, company_id, grn_id, line_number,
                   po_line_number, purchase_order_id,
                   variant_id, bin_id, received_qty, unit_of_measure_id, unit_price)
                VALUES (@tid, @cid, @grn, @ln, @poln, @po, @var, @bin, @qty, @uom, @price)
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("grn", grnId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("poln", line.PoLineNumber);
            lCmd.Parameters.AddWithValue("po", req.PurchaseOrderId);
            lCmd.Parameters.AddWithValue("var", line.VariantId);
            lCmd.Parameters.AddWithValue("bin", line.BinId);
            lCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
            lCmd.Parameters.AddWithValue("uom", line.UnitOfMeasureId);
            lCmd.Parameters.AddWithValue("price", line.UnitPrice);
            await lCmd.ExecuteNonQueryAsync(ct);

            // Update PO line received quantity.
            await using var updCmd = conn.CreateCommand();
            updCmd.CommandText = """
                UPDATE erp_purchasing.purchase_order_line
                   SET received_qty = received_qty + @qty,
                       line_status = CASE
                         WHEN received_qty + @qty >= ordered_qty THEN 'received'
                         ELSE 'partially_received'
                       END
                 WHERE purchase_order_id = @po AND line_number = @ln
                """;
            updCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
            updCmd.Parameters.AddWithValue("po", req.PurchaseOrderId);
            updCmd.Parameters.AddWithValue("ln", line.PoLineNumber);
            await updCmd.ExecuteNonQueryAsync(ct);
        }

        // Update PO header status.
        await using var poUpdCmd = conn.CreateCommand();
        poUpdCmd.CommandText = """
            UPDATE erp_purchasing.purchase_order
               SET order_status = CASE
                 WHEN NOT EXISTS (
                   SELECT 1 FROM erp_purchasing.purchase_order_line
                    WHERE purchase_order_id = @po AND line_status NOT IN ('received','cancelled')
                 ) THEN 'received'
                 ELSE 'partially_received'
               END,
               updated_at = now()
             WHERE purchase_order_id = @po
            """;
        poUpdCmd.Parameters.AddWithValue("po", req.PurchaseOrderId);
        await poUpdCmd.ExecuteNonQueryAsync(ct);

        // Append outbox event for inventory to consume.
        var eventPayload = JsonSerializer.Serialize(new
        {
            grnId,
            purchaseOrderId = req.PurchaseOrderId,
            clientTransactionId = req.ClientTransactionId,
            warehouseId,
            lines = req.Lines.Select((l, idx) => new
            {
                lineNumber = idx + 1,
                l.VariantId,
                l.BinId,
                l.ReceivedQty,
                l.UnitOfMeasureId,
                l.UnitPrice,
            }).ToList(),
        });
        await using var evtCmd = conn.CreateCommand();
        evtCmd.CommandText = """
            INSERT INTO erp_purchasing.outbox_event
              (tenant_id, company_id, grn_id, event_type, payload)
            VALUES (@tid, @cid, @grn, 'grn.posted', @payload::jsonb)
            """;
        evtCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        evtCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        evtCmd.Parameters.AddWithValue("grn", grnId);
        evtCmd.Parameters.AddWithValue("payload", eventPayload);
        await evtCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return grnId;
    }
}
