using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Infrastructure.Inventory;

public sealed partial class PostgresInventoryWorkflowRepository
{
    // ── Stock Reservations & ATP ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StockReservationSummary>> ListReservationsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? variantId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        var sql = new StringBuilder("""
            SELECT r.reservation_id, r.client_transaction_id, r.variant_id, v.sku,
                   r.warehouse_id, w.code AS warehouse_code,
                   r.bin_id, b.code AS bin_code,
                   r.quantity, r.fulfilled_qty,
                   (r.quantity - r.fulfilled_qty) AS remaining_reserved_qty,
                   r.reservation_status, r.source_document_type, r.source_document_id,
                   r.expires_at, r.created_at, r.released_at, r.note
            FROM erp_inventory.stock_reservation r
            JOIN erp_inventory.product_variant v ON v.tenant_id = r.tenant_id AND v.variant_id = r.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = r.tenant_id AND w.company_id = r.company_id AND w.warehouse_id = r.warehouse_id
            LEFT JOIN erp_inventory.stock_bin b ON b.tenant_id = r.tenant_id AND b.company_id = r.company_id AND b.bin_id = r.bin_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId
            """);

        if (variantId.HasValue) sql.Append(" AND r.variant_id = @variantId");
        if (warehouseId.HasValue) sql.Append(" AND r.warehouse_id = @warehouseId");
        if (!string.IsNullOrEmpty(status)) sql.Append(" AND r.reservation_status = @status");
        sql.Append(" ORDER BY r.created_at DESC;");

        await using var cmd = new NpgsqlCommand(sql.ToString(), db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        if (variantId.HasValue) cmd.Parameters.AddWithValue("variantId", variantId.Value);
        if (warehouseId.HasValue) cmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("status", status);

        var list = new List<StockReservationSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StockReservationSummary(
                ReservationId: reader.GetGuid(0),
                ClientTransactionId: reader.GetGuid(1),
                VariantId: reader.GetGuid(2),
                Sku: reader.GetString(3),
                WarehouseId: reader.GetGuid(4),
                WarehouseCode: reader.GetString(5),
                BinId: reader.IsDBNull(6) ? null : reader.GetGuid(6),
                BinCode: reader.IsDBNull(7) ? null : reader.GetString(7),
                Quantity: reader.GetDecimal(8),
                FulfilledQty: reader.GetDecimal(9),
                RemainingReservedQty: reader.GetDecimal(10),
                ReservationStatus: reader.GetString(11),
                SourceDocumentType: reader.IsDBNull(12) ? "sales_order" : reader.GetString(12),
                SourceDocumentId: reader.IsDBNull(13) ? null : reader.GetGuid(13),
                ExpiresAt: reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(15),
                ReleasedAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                Note: reader.IsDBNull(17) ? null : reader.GetString(17)));
        }
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<Result<StockReservationSummary>> CreateReservationAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        CreateStockReservationRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('inventory.reservation.create:' || @key))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("key", transactionId.ToString());
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Check idempotency
            await using (var idemCmd = new NpgsqlCommand(
                "SELECT request_hash FROM erp_inventory.command_idempotency WHERE tenant_id = @tenantId AND idempotency_key = @key",
                db.Connection,
                db.Transaction))
            {
                idemCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                idemCmd.Parameters.AddWithValue("key", transactionId);
                var existingHash = (string?)await idemCmd.ExecuteScalarAsync(cancellationToken);
                if (existingHash is not null)
                {
                    if (!string.Equals(existingHash, requestHash, StringComparison.Ordinal))
                    {
                        return await RollbackWithFailureAsync<StockReservationSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                    }
                    var existing = await GetReservationByTransactionIdAsync(db, context.TenantId, context.CompanyId, transactionId, cancellationToken);
                    await db.Transaction.CommitAsync(cancellationToken);
                    return Result<StockReservationSummary>.Success(existing!);
                }
            }

            // Check ATP concurrency protection:
            decimal physicalOnHand = 0;
            var balanceSql = new StringBuilder("""
                SELECT COALESCE(SUM(on_hand_qty), 0)
                FROM (
                    SELECT on_hand_qty
                    FROM erp_inventory.stock_balance
                    WHERE tenant_id = @tenantId AND company_id = @companyId
                      AND variant_id = @variantId AND warehouse_id = @warehouseId
                      AND status_code = 'available'
                """);
            if (request.BinId.HasValue) balanceSql.Append(" AND bin_id = @binId");
            balanceSql.Append(" FOR UPDATE) s;");

            await using (var balCmd = new NpgsqlCommand(balanceSql.ToString(), db.Connection, db.Transaction))
            {
                balCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                balCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                balCmd.Parameters.AddWithValue("variantId", request.VariantId);
                balCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
                if (request.BinId.HasValue) balCmd.Parameters.AddWithValue("binId", request.BinId.Value);

                var balRes = await balCmd.ExecuteScalarAsync(cancellationToken);
                if (balRes is decimal d) physicalOnHand = d;
            }

            decimal activeReserved = 0;
            var resSql = new StringBuilder("""
                SELECT COALESCE(SUM(quantity - fulfilled_qty), 0)
                FROM erp_inventory.stock_reservation
                WHERE tenant_id = @tenantId AND company_id = @companyId
                  AND variant_id = @variantId AND warehouse_id = @warehouseId
                  AND reservation_status IN ('active', 'partially_fulfilled')
                """);
            if (request.BinId.HasValue) resSql.Append(" AND bin_id = @binId");

            await using (var resCmd = new NpgsqlCommand(resSql.ToString(), db.Connection, db.Transaction))
            {
                resCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                resCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                resCmd.Parameters.AddWithValue("variantId", request.VariantId);
                resCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
                if (request.BinId.HasValue) resCmd.Parameters.AddWithValue("binId", request.BinId.Value);

                var resVal = await resCmd.ExecuteScalarAsync(cancellationToken);
                if (resVal is decimal d) activeReserved = d;
            }

            var atp = Math.Max(0m, physicalOnHand - activeReserved);
            if (atp < request.Quantity)
            {
                return await RollbackWithFailureAsync<StockReservationSummary>(
                    db, $"INSUFFICIENT_AVAILABLE_TO_PROMISE: Requested {request.Quantity}, but Available-to-Promise is {atp} (Physical: {physicalOnHand}, Reserved: {activeReserved}).", cancellationToken);
            }

            var reservationId = Guid.NewGuid();
            await using (var insertRes = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_reservation
                    (tenant_id, company_id, reservation_id, client_transaction_id,
                     variant_id, warehouse_id, bin_id, status_code, quantity,
                     reservation_status, source_document_type, source_document_id,
                     expires_at, note, fulfilled_qty)
                VALUES
                    (@tenantId, @companyId, @resId, @txId,
                     @varId, @whId, @binId, 'available', @qty,
                     'active', @srcType, @srcId,
                     @expiresAt, @note, 0);
                """,
                db.Connection,
                db.Transaction))
            {
                insertRes.Parameters.AddWithValue("tenantId", context.TenantId);
                insertRes.Parameters.AddWithValue("companyId", context.CompanyId);
                insertRes.Parameters.AddWithValue("resId", reservationId);
                insertRes.Parameters.AddWithValue("txId", transactionId);
                insertRes.Parameters.AddWithValue("varId", request.VariantId);
                insertRes.Parameters.AddWithValue("whId", request.WarehouseId);
                AddNullableUuid(insertRes, "binId", request.BinId);
                insertRes.Parameters.AddWithValue("qty", request.Quantity);
                insertRes.Parameters.AddWithValue("srcType", request.SourceDocumentType);
                AddNullableUuid(insertRes, "srcId", request.SourceDocumentId);
                insertRes.Parameters.Add(new NpgsqlParameter("expiresAt", NpgsqlDbType.TimestampTz)
                {
                    Value = (object?)request.ExpiresAt ?? DBNull.Value
                });
                insertRes.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
                await insertRes.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var idemIns = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash)
                VALUES
                    (@tenantId, @companyId, @key, 'inventory.reservation.create', @hash);
                """,
                db.Connection,
                db.Transaction))
            {
                idemIns.Parameters.AddWithValue("tenantId", context.TenantId);
                idemIns.Parameters.AddWithValue("companyId", context.CompanyId);
                idemIns.Parameters.AddWithValue("key", transactionId);
                idemIns.Parameters.AddWithValue("hash", requestHash);
                await idemIns.ExecuteNonQueryAsync(cancellationToken);
            }

            var created = await GetReservationByTransactionIdAsync(db, context.TenantId, context.CompanyId, transactionId, cancellationToken);
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<StockReservationSummary>.Success(created!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<StockReservationSummary>(db, "DUPLICATE_RESERVATION", cancellationToken);
        }
    }

    public async Task<Result<StockReservationSummary>> ReleaseReservationAsync(
        TenantContext context,
        Guid reservationId,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string currentStatus;
        await using (var sel = new NpgsqlCommand(
            "SELECT reservation_status FROM erp_inventory.stock_reservation WHERE tenant_id = @tenantId AND company_id = @companyId AND reservation_id = @resId FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("resId", reservationId);
            var statusVal = (string?)await sel.ExecuteScalarAsync(cancellationToken);
            if (statusVal is null)
            {
                return await RollbackWithFailureAsync<StockReservationSummary>(db, "RESERVATION_NOT_FOUND", cancellationToken);
            }
            currentStatus = statusVal;
        }

        if (currentStatus is not ("active" or "partially_fulfilled"))
        {
            return await RollbackWithFailureAsync<StockReservationSummary>(db, $"CANNOT_RELEASE_RESERVATION: Current status is '{currentStatus}'.", cancellationToken);
        }

        await using (var upd = new NpgsqlCommand(
            """
            UPDATE erp_inventory.stock_reservation
            SET reservation_status = 'released',
                released_at = now(),
                note = CASE WHEN @reason IS NULL THEN note ELSE COALESCE(note || ' | Released: ' || @reason, 'Released: ' || @reason) END
            WHERE tenant_id = @tenantId AND company_id = @companyId AND reservation_id = @resId;
            """,
            db.Connection,
            db.Transaction))
        {
            upd.Parameters.AddWithValue("tenantId", context.TenantId);
            upd.Parameters.AddWithValue("companyId", context.CompanyId);
            upd.Parameters.AddWithValue("resId", reservationId);
            upd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        var summary = await GetReservationByIdAsync(db, context.TenantId, context.CompanyId, reservationId, cancellationToken);
        await db.Transaction.CommitAsync(cancellationToken);
        return Result<StockReservationSummary>.Success(summary!);
    }

    public async Task<IReadOnlyList<AvailableToPromiseSummary>> GetAtpAsync(
        Guid tenantId,
        Guid companyId,
        Guid? variantId,
        Guid? warehouseId,
        Guid? binId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);

        // 1. Physical on hand
        var balSql = new StringBuilder("""
            SELECT b.variant_id, v.sku, b.warehouse_id, w.code AS warehouse_code,
                   b.bin_id, sb.code AS bin_code,
                   COALESCE(SUM(b.on_hand_qty), 0) AS physical_on_hand
            FROM erp_inventory.stock_balance b
            JOIN erp_inventory.product_variant v ON v.tenant_id = b.tenant_id AND v.variant_id = b.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = b.tenant_id AND w.company_id = b.company_id AND w.warehouse_id = b.warehouse_id
            LEFT JOIN erp_inventory.stock_bin sb ON sb.tenant_id = b.tenant_id AND sb.company_id = b.company_id AND sb.bin_id = b.bin_id
            WHERE b.tenant_id = @tenantId AND b.company_id = @companyId AND b.status_code = 'available'
            """);
        if (variantId.HasValue) balSql.Append(" AND b.variant_id = @variantId");
        if (warehouseId.HasValue) balSql.Append(" AND b.warehouse_id = @warehouseId");
        if (binId.HasValue) balSql.Append(" AND b.bin_id = @binId");
        balSql.Append(" GROUP BY b.variant_id, v.sku, b.warehouse_id, w.code, b.bin_id, sb.code;");

        var map = new Dictionary<(Guid VariantId, Guid WarehouseId, Guid? BinId), (string Sku, string WhCode, string? BinCode, decimal Physical, decimal Reserved, decimal Valued)>();

        await using (var balCmd = new NpgsqlCommand(balSql.ToString(), db.Connection, db.Transaction))
        {
            balCmd.Parameters.AddWithValue("tenantId", tenantId);
            balCmd.Parameters.AddWithValue("companyId", companyId);
            if (variantId.HasValue) balCmd.Parameters.AddWithValue("variantId", variantId.Value);
            if (warehouseId.HasValue) balCmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (binId.HasValue) balCmd.Parameters.AddWithValue("binId", binId.Value);

            await using var reader = await balCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var vId = reader.GetGuid(0);
                var sku = reader.GetString(1);
                var whId = reader.GetGuid(2);
                var whCode = reader.GetString(3);
                var bId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
                var bCode = reader.IsDBNull(5) ? null : reader.GetString(5);
                var qty = reader.GetDecimal(6);

                map[(vId, whId, bId)] = (sku, whCode, bCode, qty, 0m, 0m);
            }
            await reader.CloseAsync();
        }

        // 2. Active reservations
        var resSql = new StringBuilder("""
            SELECT r.variant_id, r.warehouse_id, r.bin_id,
                   COALESCE(SUM(r.quantity - r.fulfilled_qty), 0) AS reserved_qty
            FROM erp_inventory.stock_reservation r
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId
              AND r.reservation_status IN ('active', 'partially_fulfilled')
            """);
        if (variantId.HasValue) resSql.Append(" AND r.variant_id = @variantId");
        if (warehouseId.HasValue) resSql.Append(" AND r.warehouse_id = @warehouseId");
        if (binId.HasValue) resSql.Append(" AND r.bin_id = @binId");
        resSql.Append(" GROUP BY r.variant_id, r.warehouse_id, r.bin_id;");

        await using (var resCmd = new NpgsqlCommand(resSql.ToString(), db.Connection, db.Transaction))
        {
            resCmd.Parameters.AddWithValue("tenantId", tenantId);
            resCmd.Parameters.AddWithValue("companyId", companyId);
            if (variantId.HasValue) resCmd.Parameters.AddWithValue("variantId", variantId.Value);
            if (warehouseId.HasValue) resCmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (binId.HasValue) resCmd.Parameters.AddWithValue("binId", binId.Value);

            await using var reader = await resCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (VariantId: reader.GetGuid(0), WarehouseId: reader.GetGuid(1), BinId: reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2));
                var resQty = reader.GetDecimal(3);
                if (map.TryGetValue(key, out var val))
                {
                    map[key] = val with { Reserved = resQty };
                }
            }
            await reader.CloseAsync();
        }

        // 3. Valued stock
        var valSql = new StringBuilder("""
            SELECT cl.variant_id, cl.warehouse_id, cl.bin_id,
                   COALESCE(SUM(cl.remaining_qty), 0) AS valued_qty
            FROM erp_inventory.inventory_cost_layer cl
            WHERE cl.tenant_id = @tenantId AND cl.company_id = @companyId
              AND cl.remaining_qty > 0
            """);
        if (variantId.HasValue) valSql.Append(" AND cl.variant_id = @variantId");
        if (warehouseId.HasValue) valSql.Append(" AND cl.warehouse_id = @warehouseId");
        if (binId.HasValue) valSql.Append(" AND cl.bin_id = @binId");
        valSql.Append(" GROUP BY cl.variant_id, cl.warehouse_id, cl.bin_id;");

        await using (var valCmd = new NpgsqlCommand(valSql.ToString(), db.Connection, db.Transaction))
        {
            valCmd.Parameters.AddWithValue("tenantId", tenantId);
            valCmd.Parameters.AddWithValue("companyId", companyId);
            if (variantId.HasValue) valCmd.Parameters.AddWithValue("variantId", variantId.Value);
            if (warehouseId.HasValue) valCmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (binId.HasValue) valCmd.Parameters.AddWithValue("binId", binId.Value);

            await using var reader = await valCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (VariantId: reader.GetGuid(0), WarehouseId: reader.GetGuid(1), BinId: reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2));
                var vQty = reader.GetDecimal(3);
                if (map.TryGetValue(key, out var val))
                {
                    map[key] = val with { Valued = vQty };
                }
            }
            await reader.CloseAsync();
        }

        var results = new List<AvailableToPromiseSummary>();
        foreach (var (k, v) in map)
        {
            var calc = AvailableToPromiseCalculator.Calculate(new AtpStockSlice(
                k.VariantId, k.WarehouseId, k.BinId, v.Physical, v.Reserved, v.Valued));

            results.Add(new AvailableToPromiseSummary(
                VariantId: k.VariantId,
                Sku: v.Sku,
                WarehouseId: k.WarehouseId,
                WarehouseCode: v.WhCode,
                BinId: k.BinId,
                BinCode: v.BinCode,
                PhysicalOnHand: calc.PhysicalOnHand,
                ActiveReservedQty: calc.ActiveReservedQty,
                AvailableToPromiseQty: calc.AvailableToPromiseQty,
                ValuedQty: calc.ValuedQty,
                SellableAtpQty: calc.SellableAtpQty));
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return results;
    }

    private static async Task<StockReservationSummary?> GetReservationByIdAsync(
        DbScope db, Guid tenantId, Guid companyId, Guid reservationId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT r.reservation_id, r.client_transaction_id, r.variant_id, v.sku,
                   r.warehouse_id, w.code AS warehouse_code,
                   r.bin_id, b.code AS bin_code,
                   r.quantity, r.fulfilled_qty,
                   (r.quantity - r.fulfilled_qty) AS remaining_reserved_qty,
                   r.reservation_status, r.source_document_type, r.source_document_id,
                   r.expires_at, r.created_at, r.released_at, r.note
            FROM erp_inventory.stock_reservation r
            JOIN erp_inventory.product_variant v ON v.tenant_id = r.tenant_id AND v.variant_id = r.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = r.tenant_id AND w.company_id = r.company_id AND w.warehouse_id = r.warehouse_id
            LEFT JOIN erp_inventory.stock_bin b ON b.tenant_id = r.tenant_id AND b.company_id = r.company_id AND b.bin_id = r.bin_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId AND r.reservation_id = @id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", reservationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new StockReservationSummary(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetGuid(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? "sales_order" : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.GetFieldValue<DateTimeOffset>(15),
            reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    private static async Task<StockReservationSummary?> GetReservationByTransactionIdAsync(
        DbScope db, Guid tenantId, Guid companyId, Guid transactionId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT r.reservation_id, r.client_transaction_id, r.variant_id, v.sku,
                   r.warehouse_id, w.code AS warehouse_code,
                   r.bin_id, b.code AS bin_code,
                   r.quantity, r.fulfilled_qty,
                   (r.quantity - r.fulfilled_qty) AS remaining_reserved_qty,
                   r.reservation_status, r.source_document_type, r.source_document_id,
                   r.expires_at, r.created_at, r.released_at, r.note
            FROM erp_inventory.stock_reservation r
            JOIN erp_inventory.product_variant v ON v.tenant_id = r.tenant_id AND v.variant_id = r.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = r.tenant_id AND w.company_id = r.company_id AND w.warehouse_id = r.warehouse_id
            LEFT JOIN erp_inventory.stock_bin b ON b.tenant_id = r.tenant_id AND b.company_id = r.company_id AND b.bin_id = r.bin_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId AND r.client_transaction_id = @txId;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("txId", transactionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new StockReservationSummary(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetGuid(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? "sales_order" : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.GetFieldValue<DateTimeOffset>(15),
            reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    // ── Inventory Picking & Dispatch ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StockPickListSummary>> ListPickListsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        var sql = new StringBuilder("""
            SELECT p.pick_list_id, p.pick_list_no, p.warehouse_id, w.code AS warehouse_code,
                   p.source_document_type, p.source_document_id, p.status, p.assigned_to,
                   p.note, p.created_at, p.dispatched_at
            FROM erp_inventory.stock_pick_list p
            JOIN erp_inventory.warehouse w ON w.tenant_id = p.tenant_id AND w.company_id = p.company_id AND w.warehouse_id = p.warehouse_id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId
            """);
        if (warehouseId.HasValue) sql.Append(" AND p.warehouse_id = @warehouseId");
        if (!string.IsNullOrEmpty(status)) sql.Append(" AND p.status = @status");
        sql.Append(" ORDER BY p.created_at DESC;");

        var pickLists = new List<StockPickListSummary>();
        var pickListIds = new List<Guid>();

        await using (var cmd = new NpgsqlCommand(sql.ToString(), db.Connection, db.Transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            if (warehouseId.HasValue) cmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("status", status);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                pickListIds.Add(id);
                pickLists.Add(new StockPickListSummary(
                    PickListId: id,
                    PickListNo: reader.GetString(1),
                    WarehouseId: reader.GetGuid(2),
                    WarehouseCode: reader.GetString(3),
                    SourceDocumentType: reader.GetString(4),
                    SourceDocumentId: reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    Status: reader.GetString(6),
                    AssignedTo: reader.IsDBNull(7) ? null : reader.GetGuid(7),
                    Note: reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(9),
                    DispatchedAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    Lines: []));
            }
            await reader.CloseAsync();
        }

        if (pickListIds.Count > 0)
        {
            var lines = await LoadPickLinesAsync(db, tenantId, companyId, pickListIds, cancellationToken);
            for (var i = 0; i < pickLists.Count; i++)
            {
                if (lines.TryGetValue(pickLists[i].PickListId, out var pLines))
                {
                    pickLists[i] = pickLists[i] with { Lines = pLines };
                }
            }
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return pickLists;
    }

    public async Task<Result<StockPickListSummary>> CreatePickListAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        CreatePickListRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('inventory.pick_list.create:' || @key))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("key", transactionId.ToString());
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var pickListId = Guid.NewGuid();
            var pickListNo = $"PL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

            await using (var insHead = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_pick_list
                    (tenant_id, company_id, pick_list_id, pick_list_no, warehouse_id,
                     source_document_type, source_document_id, status, note, created_at)
                VALUES
                    (@tenantId, @companyId, @id, @no, @whId,
                     @srcType, @srcId, 'draft', @note, now());
                """,
                db.Connection,
                db.Transaction))
            {
                insHead.Parameters.AddWithValue("tenantId", context.TenantId);
                insHead.Parameters.AddWithValue("companyId", context.CompanyId);
                insHead.Parameters.AddWithValue("id", pickListId);
                insHead.Parameters.AddWithValue("no", pickListNo);
                insHead.Parameters.AddWithValue("whId", request.WarehouseId);
                insHead.Parameters.AddWithValue("srcType", request.SourceDocumentType);
                AddNullableUuid(insHead, "srcId", request.SourceDocumentId);
                insHead.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
                await insHead.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var line in request.Lines)
            {
                await using var insLine = new NpgsqlCommand(
                    """
                    INSERT INTO erp_inventory.stock_pick_line
                        (tenant_id, company_id, pick_line_id, pick_list_id, reservation_id,
                         variant_id, bin_id, requested_qty, picked_qty, status, note)
                    VALUES
                        (@tenantId, @companyId, @lineId, @pickListId, @resId,
                         @varId, @binId, @reqQty, 0, 'pending', @note);
                    """,
                    db.Connection,
                    db.Transaction);
                insLine.Parameters.AddWithValue("tenantId", context.TenantId);
                insLine.Parameters.AddWithValue("companyId", context.CompanyId);
                insLine.Parameters.AddWithValue("lineId", Guid.NewGuid());
                insLine.Parameters.AddWithValue("pickListId", pickListId);
                AddNullableUuid(insLine, "resId", line.ReservationId);
                insLine.Parameters.AddWithValue("varId", line.VariantId);
                insLine.Parameters.AddWithValue("binId", line.BinId);
                insLine.Parameters.AddWithValue("reqQty", line.RequestedQty);
                insLine.Parameters.AddWithValue("note", (object?)line.Note ?? DBNull.Value);
                await insLine.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var idemIns = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash)
                VALUES
                    (@tenantId, @companyId, @key, 'inventory.pick_list.create', @hash);
                """,
                db.Connection,
                db.Transaction))
            {
                idemIns.Parameters.AddWithValue("tenantId", context.TenantId);
                idemIns.Parameters.AddWithValue("companyId", context.CompanyId);
                idemIns.Parameters.AddWithValue("key", transactionId);
                idemIns.Parameters.AddWithValue("hash", requestHash);
                await idemIns.ExecuteNonQueryAsync(cancellationToken);
            }

            var created = await GetPickListByIdAsync(db, context.TenantId, context.CompanyId, pickListId, cancellationToken);
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<StockPickListSummary>.Success(created!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<StockPickListSummary>(db, "DUPLICATE_PICK_LIST", cancellationToken);
        }
    }

    public async Task<Result<StockPickListSummary>> RecordPickLinesAsync(
        TenantContext context,
        Guid pickListId,
        IReadOnlyList<RecordPickLineRequest> lines,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string currentStatus;
        await using (var sel = new NpgsqlCommand(
            "SELECT status FROM erp_inventory.stock_pick_list WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("id", pickListId);
            var res = (string?)await sel.ExecuteScalarAsync(cancellationToken);
            if (res is null)
            {
                return await RollbackWithFailureAsync<StockPickListSummary>(db, "PICK_LIST_NOT_FOUND", cancellationToken);
            }
            currentStatus = res;
        }

        if (currentStatus is not ("draft" or "assigned" or "in_progress" or "picked"))
        {
            return await RollbackWithFailureAsync<StockPickListSummary>(db, $"CANNOT_RECORD_PICKS: Status is '{currentStatus}'.", cancellationToken);
        }

        foreach (var l in lines)
        {
            await using var upd = new NpgsqlCommand(
                """
                UPDATE erp_inventory.stock_pick_line
                SET picked_qty = @qty,
                    status = CASE WHEN @qty >= requested_qty THEN 'picked' ELSE 'short' END,
                    note = COALESCE(@note, note)
                WHERE tenant_id = @tenantId AND company_id = @companyId
                  AND pick_list_id = @pickListId AND pick_line_id = @lineId;
                """,
                db.Connection,
                db.Transaction);
            upd.Parameters.AddWithValue("tenantId", context.TenantId);
            upd.Parameters.AddWithValue("companyId", context.CompanyId);
            upd.Parameters.AddWithValue("pickListId", pickListId);
            upd.Parameters.AddWithValue("lineId", l.PickLineId);
            upd.Parameters.AddWithValue("qty", l.PickedQty);
            upd.Parameters.AddWithValue("note", (object?)l.Note ?? DBNull.Value);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Determine if entire pick list is picked
        bool allPicked = false;
        await using (var chk = new NpgsqlCommand(
            """
            SELECT COUNT(*) = 0
            FROM erp_inventory.stock_pick_line
            WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id
              AND (status != 'picked' OR picked_qty < requested_qty);
            """,
            db.Connection,
            db.Transaction))
        {
            chk.Parameters.AddWithValue("tenantId", context.TenantId);
            chk.Parameters.AddWithValue("companyId", context.CompanyId);
            chk.Parameters.AddWithValue("id", pickListId);
            allPicked = (bool)(await chk.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        var newStatus = allPicked ? "picked" : "in_progress";
        await using (var updHead = new NpgsqlCommand(
            "UPDATE erp_inventory.stock_pick_list SET status = @status WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id;",
            db.Connection,
            db.Transaction))
        {
            updHead.Parameters.AddWithValue("tenantId", context.TenantId);
            updHead.Parameters.AddWithValue("companyId", context.CompanyId);
            updHead.Parameters.AddWithValue("status", newStatus);
            updHead.Parameters.AddWithValue("id", pickListId);
            await updHead.ExecuteNonQueryAsync(cancellationToken);
        }

        var summary = await GetPickListByIdAsync(db, context.TenantId, context.CompanyId, pickListId, cancellationToken);
        await db.Transaction.CommitAsync(cancellationToken);
        return Result<StockPickListSummary>.Success(summary!);
    }

    public async Task<Result<DispatchPickListResult>> DispatchPickListAsync(
        TenantContext context,
        Guid pickListId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string pickListNo;
        Guid warehouseId;
        string currentStatus;

        await using (var sel = new NpgsqlCommand(
            "SELECT pick_list_no, warehouse_id, status FROM erp_inventory.stock_pick_list WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("id", pickListId);
            await using var r = await sel.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return await RollbackWithFailureAsync<DispatchPickListResult>(db, "PICK_LIST_NOT_FOUND", cancellationToken);
            }
            pickListNo = r.GetString(0);
            warehouseId = r.GetGuid(1);
            currentStatus = r.GetString(2);
            await r.CloseAsync();
        }

        if (currentStatus is not ("picked" or "in_progress"))
        {
            return await RollbackWithFailureAsync<DispatchPickListResult>(db, $"CANNOT_DISPATCH_PICK_LIST: Status is '{currentStatus}'.", cancellationToken);
        }

        // Load lines to dispatch
        var linesToDispatch = new List<(Guid LineId, Guid VariantId, Guid BinId, decimal PickedQty, Guid? ReservationId)>();
        await using (var selLines = new NpgsqlCommand(
            """
            SELECT pick_line_id, variant_id, bin_id, picked_qty, reservation_id
            FROM erp_inventory.stock_pick_line
            WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id AND picked_qty > 0;
            """,
            db.Connection,
            db.Transaction))
        {
            selLines.Parameters.AddWithValue("tenantId", context.TenantId);
            selLines.Parameters.AddWithValue("companyId", context.CompanyId);
            selLines.Parameters.AddWithValue("id", pickListId);
            await using var lr = await selLines.ExecuteReaderAsync(cancellationToken);
            while (await lr.ReadAsync(cancellationToken))
            {
                linesToDispatch.Add((
                    lr.GetGuid(0), lr.GetGuid(1), lr.GetGuid(2), lr.GetDecimal(3),
                    lr.IsDBNull(4) ? null : lr.GetGuid(4)));
            }
            await lr.CloseAsync();
        }

        if (linesToDispatch.Count == 0)
        {
            return await RollbackWithFailureAsync<DispatchPickListResult>(db, "NO_PICKED_ITEMS_TO_DISPATCH", cancellationToken);
        }

        // Create inventory movement: 'issue'
        var movementId = Guid.NewGuid();
        var dispatchedAt = DateTimeOffset.UtcNow;
        await using (var insMov = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type,
                 source_document_id, occurred_at, posted_at, created_by, note)
            VALUES
                (@tenantId, @companyId, @movId, @txId, 'issue', 'pick_list',
                 @pickListId, @now, @now, @userId, @note);
            """,
            db.Connection,
            db.Transaction))
        {
            insMov.Parameters.AddWithValue("tenantId", context.TenantId);
            insMov.Parameters.AddWithValue("companyId", context.CompanyId);
            insMov.Parameters.AddWithValue("movId", movementId);
            insMov.Parameters.AddWithValue("txId", Guid.NewGuid());
            insMov.Parameters.AddWithValue("pickListId", pickListId);
            insMov.Parameters.AddWithValue("now", dispatchedAt);
            insMov.Parameters.AddWithValue("userId", context.UserId);
            insMov.Parameters.AddWithValue("note", $"Pick list dispatch issue for {pickListNo}");
            await insMov.ExecuteNonQueryAsync(cancellationToken);
        }

        decimal totalDispatchedQty = 0;
        decimal totalFifoCost = 0;
        var allAllocations = new List<FifoLayerAllocation>();
        int legNumber = 1;

        foreach (var line in linesToDispatch)
        {
            totalDispatchedQty += line.PickedQty;

            // Fetch base unit of measure
            Guid baseUomId;
            await using (var getUom = new NpgsqlCommand(
                "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tenantId AND variant_id = @varId",
                db.Connection,
                db.Transaction))
            {
                getUom.Parameters.AddWithValue("tenantId", context.TenantId);
                getUom.Parameters.AddWithValue("varId", line.VariantId);
                var uomRes = await getUom.ExecuteScalarAsync(cancellationToken);
                baseUomId = (Guid)uomRes!;
            }

            // Record movement leg (negative)
            await using (var insLeg = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.inventory_movement_leg
                    (tenant_id, company_id, movement_id, leg_number, variant_id,
                     warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
                VALUES
                    (@tenantId, @companyId, @movId, @legNo, @varId,
                     @whId, @binId, 'available', @qtyDelta, @uomId);
                """,
                db.Connection,
                db.Transaction))
            {
                insLeg.Parameters.AddWithValue("tenantId", context.TenantId);
                insLeg.Parameters.AddWithValue("companyId", context.CompanyId);
                insLeg.Parameters.AddWithValue("movId", movementId);
                insLeg.Parameters.AddWithValue("legNo", legNumber++);
                insLeg.Parameters.AddWithValue("varId", line.VariantId);
                insLeg.Parameters.AddWithValue("whId", warehouseId);
                insLeg.Parameters.AddWithValue("binId", line.BinId);
                insLeg.Parameters.AddWithValue("qtyDelta", -line.PickedQty);
                insLeg.Parameters.AddWithValue("uomId", baseUomId);
                await insLeg.ExecuteNonQueryAsync(cancellationToken);
            }

            // Decrement physical stock balance
            await using (var updBal = new NpgsqlCommand(
                """
                UPDATE erp_inventory.stock_balance
                SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                WHERE tenant_id = @tenantId AND company_id = @companyId
                  AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                  AND status_code = 'available' AND on_hand_qty >= @qty;
                """,
                db.Connection,
                db.Transaction))
            {
                updBal.Parameters.AddWithValue("tenantId", context.TenantId);
                updBal.Parameters.AddWithValue("companyId", context.CompanyId);
                updBal.Parameters.AddWithValue("varId", line.VariantId);
                updBal.Parameters.AddWithValue("whId", warehouseId);
                updBal.Parameters.AddWithValue("binId", line.BinId);
                updBal.Parameters.AddWithValue("qty", line.PickedQty);
                var rows = await updBal.ExecuteNonQueryAsync(cancellationToken);
                if (rows != 1)
                {
                    return await RollbackWithFailureAsync<DispatchPickListResult>(
                        db, $"INSUFFICIENT_STOCK_BALANCE: Not enough stock in bin {line.BinId} to dispatch {line.PickedQty}.", cancellationToken);
                }
            }

            // FIFO layer consumption via FifoCostAllocator
            var openLayers = new List<FifoCostLayerSlice>();
            await using (var selLayers = new NpgsqlCommand(
                """
                SELECT layer_id, received_at, remaining_qty, unit_cost, warehouse_id, bin_id
                FROM erp_inventory.inventory_cost_layer
                WHERE tenant_id = @tenantId AND company_id = @companyId
                  AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                  AND remaining_qty > 0
                ORDER BY received_at ASC, layer_id ASC
                FOR UPDATE;
                """,
                db.Connection,
                db.Transaction))
            {
                selLayers.Parameters.AddWithValue("tenantId", context.TenantId);
                selLayers.Parameters.AddWithValue("companyId", context.CompanyId);
                selLayers.Parameters.AddWithValue("varId", line.VariantId);
                selLayers.Parameters.AddWithValue("whId", warehouseId);
                selLayers.Parameters.AddWithValue("binId", line.BinId);
                await using var lr = await selLayers.ExecuteReaderAsync(cancellationToken);
                while (await lr.ReadAsync(cancellationToken))
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

            var allocResult = FifoCostAllocator.Allocate(
                openLayers,
                line.PickedQty,
                locationDescription: $"warehouse {warehouseId}, bin {line.BinId}",
                expectedWarehouseId: warehouseId,
                expectedBinId: line.BinId);

            totalFifoCost += allocResult.TotalCost;
            foreach (var alloc in allocResult.Allocations)
            {
                allAllocations.Add(alloc);

                await using var updCostLayer = new NpgsqlCommand(
                    """
                    UPDATE erp_inventory.inventory_cost_layer
                    SET remaining_qty = remaining_qty - @consumed, updated_at = now()
                    WHERE tenant_id = @tenantId AND company_id = @companyId
                      AND layer_id = @layerId AND remaining_qty >= @consumed;
                    """,
                    db.Connection,
                    db.Transaction);
                updCostLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                updCostLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                updCostLayer.Parameters.AddWithValue("layerId", alloc.LayerId);
                updCostLayer.Parameters.AddWithValue("consumed", alloc.ConsumedQty);
                var layerRows = await updCostLayer.ExecuteNonQueryAsync(cancellationToken);
                if (layerRows != 1)
                {
                    return await RollbackWithFailureAsync<DispatchPickListResult>(
                        db, $"CONCURRENT_FIFO_LAYER_MODIFICATION: Layer {alloc.LayerId} could not be updated.", cancellationToken);
                }

                await using var insAlloc = new NpgsqlCommand(
                    """
                    INSERT INTO erp_inventory.inventory_cost_allocation
                        (tenant_id, company_id, allocation_id, layer_id, movement_id,
                         source_document_type, source_document_id, source_line_number,
                         variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                    VALUES
                        (@tenantId, @companyId, @allocId, @layerId, @movId,
                         'pick_dispatch', @pickListId, @ln,
                         @varId, @qty, @cost, @extCost, 'PKR');
                    """,
                    db.Connection,
                    db.Transaction);
                insAlloc.Parameters.AddWithValue("tenantId", context.TenantId);
                insAlloc.Parameters.AddWithValue("companyId", context.CompanyId);
                insAlloc.Parameters.AddWithValue("allocId", Guid.NewGuid());
                insAlloc.Parameters.AddWithValue("layerId", alloc.LayerId);
                insAlloc.Parameters.AddWithValue("movId", movementId);
                insAlloc.Parameters.AddWithValue("pickListId", pickListId);
                insAlloc.Parameters.AddWithValue("ln", legNumber);
                insAlloc.Parameters.AddWithValue("varId", line.VariantId);
                insAlloc.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                insAlloc.Parameters.AddWithValue("cost", alloc.UnitCost);
                insAlloc.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                await insAlloc.ExecuteNonQueryAsync(cancellationToken);
            }

            // Update reservation fulfillment if linked
            if (line.ReservationId.HasValue)
            {
                await using var updRes = new NpgsqlCommand(
                    """
                    UPDATE erp_inventory.stock_reservation
                    SET fulfilled_qty = fulfilled_qty + @picked,
                        reservation_status = CASE WHEN fulfilled_qty + @picked >= quantity THEN 'fulfilled' ELSE 'partially_fulfilled' END
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND reservation_id = @resId;
                    """,
                    db.Connection,
                    db.Transaction);
                updRes.Parameters.AddWithValue("tenantId", context.TenantId);
                updRes.Parameters.AddWithValue("companyId", context.CompanyId);
                updRes.Parameters.AddWithValue("picked", line.PickedQty);
                updRes.Parameters.AddWithValue("resId", line.ReservationId.Value);
                await updRes.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // Mark pick list as dispatched
        await using (var updList = new NpgsqlCommand(
            "UPDATE erp_inventory.stock_pick_list SET status = 'dispatched', dispatched_at = @now WHERE tenant_id = @tenantId AND company_id = @companyId AND pick_list_id = @id;",
            db.Connection,
            db.Transaction))
        {
            updList.Parameters.AddWithValue("tenantId", context.TenantId);
            updList.Parameters.AddWithValue("companyId", context.CompanyId);
            updList.Parameters.AddWithValue("now", dispatchedAt);
            updList.Parameters.AddWithValue("id", pickListId);
            await updList.ExecuteNonQueryAsync(cancellationToken);
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return Result<DispatchPickListResult>.Success(new DispatchPickListResult(
            PickListId: pickListId,
            PickListNo: pickListNo,
            MovementId: movementId,
            TotalDispatchedQty: totalDispatchedQty,
            TotalFifoCost: totalFifoCost,
            CostAllocations: allAllocations,
            DispatchedAt: dispatchedAt));
    }

    private static async Task<Dictionary<Guid, List<StockPickLineSummary>>> LoadPickLinesAsync(
        DbScope db, Guid tenantId, Guid companyId, IReadOnlyList<Guid> pickListIds, CancellationToken ct)
    {
        var dict = new Dictionary<Guid, List<StockPickLineSummary>>();
        await using var cmd = new NpgsqlCommand("""
            SELECT l.pick_line_id, l.pick_list_id, l.reservation_id, l.variant_id, v.sku,
                   l.bin_id, b.code AS bin_code, l.requested_qty, l.picked_qty, l.status, l.note
            FROM erp_inventory.stock_pick_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.pick_list_id = ANY(@ids)
            ORDER BY l.pick_line_id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("ids", pickListIds.ToArray());

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var pId = r.GetGuid(1);
            if (!dict.TryGetValue(pId, out var lines))
            {
                lines = [];
                dict[pId] = lines;
            }
            lines.Add(new StockPickLineSummary(
                PickLineId: r.GetGuid(0),
                PickListId: pId,
                ReservationId: r.IsDBNull(2) ? null : r.GetGuid(2),
                VariantId: r.GetGuid(3),
                Sku: r.GetString(4),
                BinId: r.GetGuid(5),
                BinCode: r.GetString(6),
                RequestedQty: r.GetDecimal(7),
                PickedQty: r.GetDecimal(8),
                Status: r.GetString(9),
                Note: r.IsDBNull(10) ? null : r.GetString(10)));
        }
        await r.CloseAsync();
        return dict;
    }

    private static async Task<StockPickListSummary?> GetPickListByIdAsync(
        DbScope db, Guid tenantId, Guid companyId, Guid pickListId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT p.pick_list_id, p.pick_list_no, p.warehouse_id, w.code AS warehouse_code,
                   p.source_document_type, p.source_document_id, p.status, p.assigned_to,
                   p.note, p.created_at, p.dispatched_at
            FROM erp_inventory.stock_pick_list p
            JOIN erp_inventory.warehouse w ON w.tenant_id = p.tenant_id AND w.company_id = p.company_id AND w.warehouse_id = p.warehouse_id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId AND p.pick_list_id = @id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", pickListId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var header = new StockPickListSummary(
            PickListId: r.GetGuid(0),
            PickListNo: r.GetString(1),
            WarehouseId: r.GetGuid(2),
            WarehouseCode: r.GetString(3),
            SourceDocumentType: r.GetString(4),
            SourceDocumentId: r.IsDBNull(5) ? null : r.GetGuid(5),
            Status: r.GetString(6),
            AssignedTo: r.IsDBNull(7) ? null : r.GetGuid(7),
            Note: r.IsDBNull(8) ? null : r.GetString(8),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(9),
            DispatchedAt: r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
            Lines: []);
        await r.CloseAsync();

        var linesMap = await LoadPickLinesAsync(db, tenantId, companyId, [pickListId], ct);
        if (linesMap.TryGetValue(pickListId, out var lines))
        {
            header = header with { Lines = lines };
        }
        return header;
    }

    // ── Stock Counts & Cycle Counting ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<StockCountSessionSummary>> ListCountSessionsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        var sql = new StringBuilder("""
            SELECT s.session_id, s.count_number, s.warehouse_id, w.code AS warehouse_code,
                   s.count_type, s.status, s.initiated_by, s.approved_by,
                   s.created_at, s.completed_at, s.approved_at, s.note
            FROM erp_inventory.stock_count_session s
            JOIN erp_inventory.warehouse w ON w.tenant_id = s.tenant_id AND w.company_id = s.company_id AND w.warehouse_id = s.warehouse_id
            WHERE s.tenant_id = @tenantId AND s.company_id = @companyId
            """);
        if (warehouseId.HasValue) sql.Append(" AND s.warehouse_id = @warehouseId");
        if (!string.IsNullOrEmpty(status)) sql.Append(" AND s.status = @status");
        sql.Append(" ORDER BY s.created_at DESC;");

        var sessions = new List<StockCountSessionSummary>();
        var sessionIds = new List<Guid>();

        await using (var cmd = new NpgsqlCommand(sql.ToString(), db.Connection, db.Transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            if (warehouseId.HasValue) cmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("status", status);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                sessionIds.Add(id);
                sessions.Add(new StockCountSessionSummary(
                    SessionId: id,
                    CountNumber: reader.GetString(1),
                    WarehouseId: reader.GetGuid(2),
                    WarehouseCode: reader.GetString(3),
                    CountType: reader.GetString(4),
                    Status: reader.GetString(5),
                    InitiatedBy: reader.GetGuid(6),
                    ApprovedBy: reader.IsDBNull(7) ? null : reader.GetGuid(7),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(8),
                    CompletedAt: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                    ApprovedAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    Note: reader.IsDBNull(11) ? null : reader.GetString(11),
                    Lines: []));
            }
            await reader.CloseAsync();
        }

        if (sessionIds.Count > 0)
        {
            var lines = await LoadCountLinesAsync(db, tenantId, companyId, sessionIds, cancellationToken);
            for (var i = 0; i < sessions.Count; i++)
            {
                if (lines.TryGetValue(sessions[i].SessionId, out var sLines))
                {
                    sessions[i] = sessions[i] with { Lines = sLines };
                }
            }
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return sessions;
    }

    public async Task<Result<StockCountSessionSummary>> CreateCountSessionAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        CreateCountSessionRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('inventory.count_session.create:' || @key))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("key", transactionId.ToString());
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var sessionId = Guid.NewGuid();
            var countNumber = $"CNT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

            await using (var insHead = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_count_session
                    (tenant_id, company_id, session_id, count_number, warehouse_id,
                     count_type, status, initiated_by, note, created_at)
                VALUES
                    (@tenantId, @companyId, @id, @no, @whId,
                     @countType, 'draft', @initBy, @note, now());
                """,
                db.Connection,
                db.Transaction))
            {
                insHead.Parameters.AddWithValue("tenantId", context.TenantId);
                insHead.Parameters.AddWithValue("companyId", context.CompanyId);
                insHead.Parameters.AddWithValue("id", sessionId);
                insHead.Parameters.AddWithValue("no", countNumber);
                insHead.Parameters.AddWithValue("whId", request.WarehouseId);
                insHead.Parameters.AddWithValue("countType", request.CountType);
                insHead.Parameters.AddWithValue("initBy", context.UserId);
                insHead.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
                await insHead.ExecuteNonQueryAsync(cancellationToken);
            }

            // Populate snapshot lines from stock_balance in this warehouse
            await using (var insSnap = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_count_line
                    (tenant_id, company_id, line_id, session_id, variant_id, bin_id, snapshot_qty, unit_cost)
                SELECT b.tenant_id, b.company_id, gen_random_uuid(), @sessionId, b.variant_id, b.bin_id,
                       b.on_hand_qty,
                       COALESCE((
                           SELECT cl.unit_cost
                           FROM erp_inventory.inventory_cost_layer cl
                           WHERE cl.tenant_id = b.tenant_id AND cl.company_id = b.company_id
                             AND cl.variant_id = b.variant_id AND cl.warehouse_id = b.warehouse_id
                             AND cl.bin_id = b.bin_id AND cl.remaining_qty > 0
                           ORDER BY cl.received_at DESC, cl.layer_id DESC
                           LIMIT 1
                       ), 0)
                FROM erp_inventory.stock_balance b
                WHERE b.tenant_id = @tenantId AND b.company_id = @companyId
                  AND b.warehouse_id = @whId AND b.status_code = 'available';
                """,
                db.Connection,
                db.Transaction))
            {
                insSnap.Parameters.AddWithValue("tenantId", context.TenantId);
                insSnap.Parameters.AddWithValue("companyId", context.CompanyId);
                insSnap.Parameters.AddWithValue("sessionId", sessionId);
                insSnap.Parameters.AddWithValue("whId", request.WarehouseId);
                await insSnap.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var idemIns = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash)
                VALUES
                    (@tenantId, @companyId, @key, 'inventory.count_session.create', @hash);
                """,
                db.Connection,
                db.Transaction))
            {
                idemIns.Parameters.AddWithValue("tenantId", context.TenantId);
                idemIns.Parameters.AddWithValue("companyId", context.CompanyId);
                idemIns.Parameters.AddWithValue("key", transactionId);
                idemIns.Parameters.AddWithValue("hash", requestHash);
                await idemIns.ExecuteNonQueryAsync(cancellationToken);
            }

            var created = await GetCountSessionByIdAsync(db, context.TenantId, context.CompanyId, sessionId, cancellationToken);
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<StockCountSessionSummary>.Success(created!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<StockCountSessionSummary>(db, "DUPLICATE_COUNT_SESSION", cancellationToken);
        }
    }

    public async Task<Result<StockCountSessionSummary>> RecordCountLinesAsync(
        TenantContext context,
        Guid sessionId,
        IReadOnlyList<RecordCountLineRequest> lines,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string currentStatus;
        await using (var sel = new NpgsqlCommand(
            "SELECT status FROM erp_inventory.stock_count_session WHERE tenant_id = @tenantId AND company_id = @companyId AND session_id = @id FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("id", sessionId);
            var res = (string?)await sel.ExecuteScalarAsync(cancellationToken);
            if (res is null)
            {
                return await RollbackWithFailureAsync<StockCountSessionSummary>(db, "COUNT_SESSION_NOT_FOUND", cancellationToken);
            }
            currentStatus = res;
        }

        if (currentStatus is not ("draft" or "in_progress" or "completed"))
        {
            return await RollbackWithFailureAsync<StockCountSessionSummary>(db, $"CANNOT_RECORD_COUNTS: Status is '{currentStatus}'.", cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var l in lines)
        {
            await using var upsertLine = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_count_line
                    (tenant_id, company_id, line_id, session_id, variant_id, bin_id,
                     snapshot_qty, counted_qty, variance_qty, unit_cost, counted_by, counted_at, note)
                VALUES
                    (@tenantId, @companyId, gen_random_uuid(), @sessionId, @varId, @binId,
                     0, @countedQty, @countedQty, 0, @userId, @now, @note)
                ON CONFLICT (tenant_id, company_id, session_id, variant_id, bin_id)
                DO UPDATE SET
                    counted_qty = EXCLUDED.counted_qty,
                    variance_qty = EXCLUDED.counted_qty - erp_inventory.stock_count_line.snapshot_qty,
                    counted_by = EXCLUDED.counted_by,
                    counted_at = EXCLUDED.counted_at,
                    note = COALESCE(EXCLUDED.note, erp_inventory.stock_count_line.note);
                """,
                db.Connection,
                db.Transaction);
            upsertLine.Parameters.AddWithValue("tenantId", context.TenantId);
            upsertLine.Parameters.AddWithValue("companyId", context.CompanyId);
            upsertLine.Parameters.AddWithValue("sessionId", sessionId);
            upsertLine.Parameters.AddWithValue("varId", l.VariantId);
            upsertLine.Parameters.AddWithValue("binId", l.BinId);
            upsertLine.Parameters.AddWithValue("countedQty", l.CountedQty);
            upsertLine.Parameters.AddWithValue("userId", context.UserId);
            upsertLine.Parameters.AddWithValue("now", now);
            upsertLine.Parameters.AddWithValue("note", (object?)l.Note ?? DBNull.Value);
            await upsertLine.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updHead = new NpgsqlCommand(
            "UPDATE erp_inventory.stock_count_session SET status = 'completed', completed_at = @now WHERE tenant_id = @tenantId AND company_id = @companyId AND session_id = @id;",
            db.Connection,
            db.Transaction))
        {
            updHead.Parameters.AddWithValue("tenantId", context.TenantId);
            updHead.Parameters.AddWithValue("companyId", context.CompanyId);
            updHead.Parameters.AddWithValue("now", now);
            updHead.Parameters.AddWithValue("id", sessionId);
            await updHead.ExecuteNonQueryAsync(cancellationToken);
        }

        var summary = await GetCountSessionByIdAsync(db, context.TenantId, context.CompanyId, sessionId, cancellationToken);
        await db.Transaction.CommitAsync(cancellationToken);
        return Result<StockCountSessionSummary>.Success(summary!);
    }

    public async Task<Result<ApproveCountSessionResult>> ApproveCountSessionAsync(
        TenantContext context,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string countNumber;
        Guid warehouseId;
        string currentStatus;

        await using (var sel = new NpgsqlCommand(
            "SELECT count_number, warehouse_id, status FROM erp_inventory.stock_count_session WHERE tenant_id = @tenantId AND company_id = @companyId AND session_id = @id FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("id", sessionId);
            await using var r = await sel.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return await RollbackWithFailureAsync<ApproveCountSessionResult>(db, "COUNT_SESSION_NOT_FOUND", cancellationToken);
            }
            countNumber = r.GetString(0);
            warehouseId = r.GetGuid(1);
            currentStatus = r.GetString(2);
            await r.CloseAsync();
        }

        if (currentStatus is not ("completed" or "in_progress"))
        {
            return await RollbackWithFailureAsync<ApproveCountSessionResult>(db, $"CANNOT_APPROVE_COUNT_SESSION: Status is '{currentStatus}'.", cancellationToken);
        }

        var lines = new List<(Guid LineId, Guid VariantId, Guid BinId, decimal SnapshotQty, decimal CountedQty, decimal VarianceQty, decimal UnitCost)>();
        await using (var selLines = new NpgsqlCommand(
            """
            SELECT line_id, variant_id, bin_id, snapshot_qty, counted_qty, variance_qty, unit_cost
            FROM erp_inventory.stock_count_line
            WHERE tenant_id = @tenantId AND company_id = @companyId AND session_id = @id AND counted_qty IS NOT NULL;
            """,
            db.Connection,
            db.Transaction))
        {
            selLines.Parameters.AddWithValue("tenantId", context.TenantId);
            selLines.Parameters.AddWithValue("companyId", context.CompanyId);
            selLines.Parameters.AddWithValue("id", sessionId);
            await using var lr = await selLines.ExecuteReaderAsync(cancellationToken);
            while (await lr.ReadAsync(cancellationToken))
            {
                lines.Add((
                    lr.GetGuid(0), lr.GetGuid(1), lr.GetGuid(2),
                    lr.GetDecimal(3), lr.GetDecimal(4), lr.GetDecimal(5), lr.GetDecimal(6)));
            }
            await lr.CloseAsync();
        }

        var linesWithVariance = lines.Where(l => l.VarianceQty != 0).ToList();
        Guid? adjustmentMovementId = null;
        decimal totalNetVarianceQty = 0;
        decimal totalNetVarianceCost = 0;
        var approvedAt = DateTimeOffset.UtcNow;

        if (linesWithVariance.Count > 0)
        {
            adjustmentMovementId = Guid.NewGuid();
            await using (var insMov = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type,
                     source_document_id, occurred_at, posted_at, created_by, note)
                VALUES
                    (@tenantId, @companyId, @movId, @txId, 'adjustment', 'stock_count',
                     @sessionId, @now, @now, @userId, @note);
                """,
                db.Connection,
                db.Transaction))
            {
                insMov.Parameters.AddWithValue("tenantId", context.TenantId);
                insMov.Parameters.AddWithValue("companyId", context.CompanyId);
                insMov.Parameters.AddWithValue("movId", adjustmentMovementId.Value);
                insMov.Parameters.AddWithValue("txId", Guid.NewGuid());
                insMov.Parameters.AddWithValue("sessionId", sessionId);
                insMov.Parameters.AddWithValue("now", approvedAt);
                insMov.Parameters.AddWithValue("userId", context.UserId);
                insMov.Parameters.AddWithValue("note", $"Physical stock reconciliation for session {countNumber}");
                await insMov.ExecuteNonQueryAsync(cancellationToken);
            }

            int legNo = 1;
            foreach (var line in linesWithVariance)
            {
                totalNetVarianceQty += line.VarianceQty;

                // Base UOM
                Guid baseUomId;
                await using (var getUom = new NpgsqlCommand(
                    "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tenantId AND variant_id = @varId",
                    db.Connection,
                    db.Transaction))
                {
                    getUom.Parameters.AddWithValue("tenantId", context.TenantId);
                    getUom.Parameters.AddWithValue("varId", line.VariantId);
                    baseUomId = (Guid)(await getUom.ExecuteScalarAsync(cancellationToken))!;
                }

                // Leg
                await using (var insLeg = new NpgsqlCommand(
                    """
                    INSERT INTO erp_inventory.inventory_movement_leg
                        (tenant_id, company_id, movement_id, leg_number, variant_id,
                         warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
                    VALUES
                        (@tenantId, @companyId, @movId, @legNo, @varId,
                         @whId, @binId, 'available', @qtyDelta, @uomId);
                    """,
                    db.Connection,
                    db.Transaction))
                {
                    insLeg.Parameters.AddWithValue("tenantId", context.TenantId);
                    insLeg.Parameters.AddWithValue("companyId", context.CompanyId);
                    insLeg.Parameters.AddWithValue("movId", adjustmentMovementId.Value);
                    insLeg.Parameters.AddWithValue("legNo", legNo++);
                    insLeg.Parameters.AddWithValue("varId", line.VariantId);
                    insLeg.Parameters.AddWithValue("whId", warehouseId);
                    insLeg.Parameters.AddWithValue("binId", line.BinId);
                    insLeg.Parameters.AddWithValue("qtyDelta", line.VarianceQty);
                    insLeg.Parameters.AddWithValue("uomId", baseUomId);
                    await insLeg.ExecuteNonQueryAsync(cancellationToken);
                }

                if (line.VarianceQty > 0)
                {
                    // Stock Gain
                    var cost = line.UnitCost > 0 ? line.UnitCost : 0m;
                    totalNetVarianceCost += line.VarianceQty * cost;

                    // Update balance
                    await using (var upsertBal = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.stock_balance
                            (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                        VALUES
                            (@tenantId, @companyId, @varId, @whId, @binId, 'available', @qty, gen_random_uuid())
                        ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                        DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        upsertBal.Parameters.AddWithValue("tenantId", context.TenantId);
                        upsertBal.Parameters.AddWithValue("companyId", context.CompanyId);
                        upsertBal.Parameters.AddWithValue("varId", line.VariantId);
                        upsertBal.Parameters.AddWithValue("whId", warehouseId);
                        upsertBal.Parameters.AddWithValue("binId", line.BinId);
                        upsertBal.Parameters.AddWithValue("qty", line.VarianceQty);
                        await upsertBal.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Create new FIFO cost layer
                    await using (var insLayer = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.inventory_cost_layer
                            (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id,
                             status_code, source_document_type, source_document_id, source_movement_id,
                             received_at, original_qty, remaining_qty, unit_cost, currency_code)
                        VALUES
                            (@tenantId, @companyId, gen_random_uuid(), @varId, @whId, @binId,
                             'available', 'stock_count', @sessionId, @movId,
                             @now, @qty, @qty, @cost, 'PKR');
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        insLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                        insLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                        insLayer.Parameters.AddWithValue("varId", line.VariantId);
                        insLayer.Parameters.AddWithValue("whId", warehouseId);
                        insLayer.Parameters.AddWithValue("binId", line.BinId);
                        insLayer.Parameters.AddWithValue("sessionId", sessionId);
                        insLayer.Parameters.AddWithValue("movId", adjustmentMovementId.Value);
                        insLayer.Parameters.AddWithValue("now", approvedAt);
                        insLayer.Parameters.AddWithValue("qty", line.VarianceQty);
                        insLayer.Parameters.AddWithValue("cost", cost);
                        await insLayer.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
                else
                {
                    // Stock Loss: variance is negative
                    var absQty = Math.Abs(line.VarianceQty);

                    // Update balance
                    await using (var decBal = new NpgsqlCommand(
                        """
                        UPDATE erp_inventory.stock_balance
                        SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                        WHERE tenant_id = @tenantId AND company_id = @companyId
                          AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                          AND status_code = 'available' AND on_hand_qty >= @qty;
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        decBal.Parameters.AddWithValue("tenantId", context.TenantId);
                        decBal.Parameters.AddWithValue("companyId", context.CompanyId);
                        decBal.Parameters.AddWithValue("varId", line.VariantId);
                        decBal.Parameters.AddWithValue("whId", warehouseId);
                        decBal.Parameters.AddWithValue("binId", line.BinId);
                        decBal.Parameters.AddWithValue("qty", absQty);
                        await decBal.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Consume FIFO layers
                    var openLayers = new List<FifoCostLayerSlice>();
                    await using (var selLayers = new NpgsqlCommand(
                        """
                        SELECT layer_id, received_at, remaining_qty, unit_cost, warehouse_id, bin_id
                        FROM erp_inventory.inventory_cost_layer
                        WHERE tenant_id = @tenantId AND company_id = @companyId
                          AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                          AND remaining_qty > 0
                        ORDER BY received_at ASC, layer_id ASC
                        FOR UPDATE;
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        selLayers.Parameters.AddWithValue("tenantId", context.TenantId);
                        selLayers.Parameters.AddWithValue("companyId", context.CompanyId);
                        selLayers.Parameters.AddWithValue("varId", line.VariantId);
                        selLayers.Parameters.AddWithValue("whId", warehouseId);
                        selLayers.Parameters.AddWithValue("binId", line.BinId);
                        await using var lr = await selLayers.ExecuteReaderAsync(cancellationToken);
                        while (await lr.ReadAsync(cancellationToken))
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

                    if (openLayers.Count > 0 && openLayers.Sum(l => l.RemainingQty) >= absQty)
                    {
                        var allocResult = FifoCostAllocator.Allocate(
                            openLayers,
                            absQty,
                            locationDescription: $"warehouse {warehouseId}, bin {line.BinId}",
                            expectedWarehouseId: warehouseId,
                            expectedBinId: line.BinId);

                        totalNetVarianceCost -= allocResult.TotalCost;
                        foreach (var alloc in allocResult.Allocations)
                        {
                            await using var updCostLayer = new NpgsqlCommand(
                                """
                                UPDATE erp_inventory.inventory_cost_layer
                                SET remaining_qty = remaining_qty - @consumed, updated_at = now()
                                WHERE tenant_id = @tenantId AND company_id = @companyId
                                  AND layer_id = @layerId;
                                """,
                                db.Connection,
                                db.Transaction);
                            updCostLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                            updCostLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                            updCostLayer.Parameters.AddWithValue("layerId", alloc.LayerId);
                            updCostLayer.Parameters.AddWithValue("consumed", alloc.ConsumedQty);
                            await updCostLayer.ExecuteNonQueryAsync(cancellationToken);

                            await using var insAlloc = new NpgsqlCommand(
                                """
                                INSERT INTO erp_inventory.inventory_cost_allocation
                                    (tenant_id, company_id, allocation_id, layer_id, movement_id,
                                     source_document_type, source_document_id, source_line_number,
                                     variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                                VALUES
                                    (@tenantId, @companyId, gen_random_uuid(), @layerId, @movId,
                                     'stock_count_loss', @sessionId, @ln,
                                     @varId, @qty, @cost, @extCost, 'PKR');
                                """,
                                db.Connection,
                                db.Transaction);
                            insAlloc.Parameters.AddWithValue("tenantId", context.TenantId);
                            insAlloc.Parameters.AddWithValue("companyId", context.CompanyId);
                            insAlloc.Parameters.AddWithValue("layerId", alloc.LayerId);
                            insAlloc.Parameters.AddWithValue("movId", adjustmentMovementId.Value);
                            insAlloc.Parameters.AddWithValue("sessionId", sessionId);
                            insAlloc.Parameters.AddWithValue("ln", legNo);
                            insAlloc.Parameters.AddWithValue("varId", line.VariantId);
                            insAlloc.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                            insAlloc.Parameters.AddWithValue("cost", alloc.UnitCost);
                            insAlloc.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                            await insAlloc.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                }

                // Mark line reconciled
                await using var markLine = new NpgsqlCommand(
                    """
                    UPDATE erp_inventory.stock_count_line
                    SET is_reconciled = true, reconciled_movement_id = @movId
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND line_id = @lineId;
                    """,
                    db.Connection,
                    db.Transaction);
                markLine.Parameters.AddWithValue("tenantId", context.TenantId);
                markLine.Parameters.AddWithValue("companyId", context.CompanyId);
                markLine.Parameters.AddWithValue("movId", adjustmentMovementId.Value);
                markLine.Parameters.AddWithValue("lineId", line.LineId);
                await markLine.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // Update session header
        await using (var updSession = new NpgsqlCommand(
            """
            UPDATE erp_inventory.stock_count_session
            SET status = 'approved', approved_by = @userId, approved_at = @now
            WHERE tenant_id = @tenantId AND company_id = @companyId AND session_id = @id;
            """,
            db.Connection,
            db.Transaction))
        {
            updSession.Parameters.AddWithValue("tenantId", context.TenantId);
            updSession.Parameters.AddWithValue("companyId", context.CompanyId);
            updSession.Parameters.AddWithValue("userId", context.UserId);
            updSession.Parameters.AddWithValue("now", approvedAt);
            updSession.Parameters.AddWithValue("id", sessionId);
            await updSession.ExecuteNonQueryAsync(cancellationToken);
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return Result<ApproveCountSessionResult>.Success(new ApproveCountSessionResult(
            SessionId: sessionId,
            CountNumber: countNumber,
            TotalReconciledLines: linesWithVariance.Count,
            TotalNetVarianceQty: totalNetVarianceQty,
            TotalNetVarianceCost: totalNetVarianceCost,
            AdjustmentMovementId: adjustmentMovementId,
            ApprovedAt: approvedAt));
    }

    private static async Task<Dictionary<Guid, List<StockCountLineSummary>>> LoadCountLinesAsync(
        DbScope db, Guid tenantId, Guid companyId, IReadOnlyList<Guid> sessionIds, CancellationToken ct)
    {
        var dict = new Dictionary<Guid, List<StockCountLineSummary>>();
        await using var cmd = new NpgsqlCommand("""
            SELECT l.line_id, l.session_id, l.variant_id, v.sku,
                   l.bin_id, b.code AS bin_code,
                   l.snapshot_qty, l.counted_qty, l.variance_qty, l.unit_cost,
                   (l.variance_qty * l.unit_cost) AS variance_extended_cost,
                   l.is_reconciled, l.reconciled_movement_id, l.counted_by, l.counted_at, l.note
            FROM erp_inventory.stock_count_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.session_id = ANY(@ids)
            ORDER BY l.line_id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("ids", sessionIds.ToArray());

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var sId = r.GetGuid(1);
            if (!dict.TryGetValue(sId, out var lines))
            {
                lines = [];
                dict[sId] = lines;
            }
            lines.Add(new StockCountLineSummary(
                LineId: r.GetGuid(0),
                SessionId: sId,
                VariantId: r.GetGuid(2),
                Sku: r.GetString(3),
                BinId: r.GetGuid(4),
                BinCode: r.GetString(5),
                SnapshotQty: r.GetDecimal(6),
                CountedQty: r.IsDBNull(7) ? null : r.GetDecimal(7),
                VarianceQty: r.IsDBNull(8) ? null : r.GetDecimal(8),
                UnitCost: r.GetDecimal(9),
                VarianceExtendedCost: r.IsDBNull(10) ? null : r.GetDecimal(10),
                IsReconciled: r.GetBoolean(11),
                ReconciledMovementId: r.IsDBNull(12) ? null : r.GetGuid(12),
                CountedBy: r.IsDBNull(13) ? null : r.GetGuid(13),
                CountedAt: r.IsDBNull(14) ? null : r.GetFieldValue<DateTimeOffset>(14),
                Note: r.IsDBNull(15) ? null : r.GetString(15)));
        }
        await r.CloseAsync();
        return dict;
    }

    private static async Task<StockCountSessionSummary?> GetCountSessionByIdAsync(
        DbScope db, Guid tenantId, Guid companyId, Guid sessionId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT s.session_id, s.count_number, s.warehouse_id, w.code AS warehouse_code,
                   s.count_type, s.status, s.initiated_by, s.approved_by,
                   s.created_at, s.completed_at, s.approved_at, s.note
            FROM erp_inventory.stock_count_session s
            JOIN erp_inventory.warehouse w ON w.tenant_id = s.tenant_id AND w.company_id = s.company_id AND w.warehouse_id = s.warehouse_id
            WHERE s.tenant_id = @tenantId AND s.company_id = @companyId AND s.session_id = @id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", sessionId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var header = new StockCountSessionSummary(
            SessionId: r.GetGuid(0),
            CountNumber: r.GetString(1),
            WarehouseId: r.GetGuid(2),
            WarehouseCode: r.GetString(3),
            CountType: r.GetString(4),
            Status: r.GetString(5),
            InitiatedBy: r.GetGuid(6),
            ApprovedBy: r.IsDBNull(7) ? null : r.GetGuid(7),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(8),
            CompletedAt: r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
            ApprovedAt: r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
            Note: r.IsDBNull(11) ? null : r.GetString(11),
            Lines: []);
        await r.CloseAsync();

        var linesMap = await LoadCountLinesAsync(db, tenantId, companyId, [sessionId], ct);
        if (linesMap.TryGetValue(sessionId, out var lines))
        {
            header = header with { Lines = lines };
        }
        return header;
    }

    // ── Stock Returns ───────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StockReturnSummary>> ListStockReturnsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        var sql = new StringBuilder("""
            SELECT r.return_id, r.return_number, r.return_type, r.source_document_type,
                   r.source_document_id, r.warehouse_id, w.code AS warehouse_code,
                   r.status, r.created_by, r.approved_by, r.created_at, r.processed_at, r.note
            FROM erp_inventory.stock_return r
            JOIN erp_inventory.warehouse w ON w.tenant_id = r.tenant_id AND w.company_id = r.company_id AND w.warehouse_id = r.warehouse_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId
            """);
        if (warehouseId.HasValue) sql.Append(" AND r.warehouse_id = @warehouseId");
        if (!string.IsNullOrEmpty(status)) sql.Append(" AND r.status = @status");
        sql.Append(" ORDER BY r.created_at DESC;");

        var returns = new List<StockReturnSummary>();
        var returnIds = new List<Guid>();

        await using (var cmd = new NpgsqlCommand(sql.ToString(), db.Connection, db.Transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            if (warehouseId.HasValue) cmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
            if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("status", status);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                returnIds.Add(id);
                returns.Add(new StockReturnSummary(
                    ReturnId: id,
                    ReturnNumber: reader.GetString(1),
                    ReturnType: reader.GetString(2),
                    SourceDocumentType: reader.GetString(3),
                    SourceDocumentId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    WarehouseId: reader.GetGuid(5),
                    WarehouseCode: reader.GetString(6),
                    Status: reader.GetString(7),
                    CreatedBy: reader.GetGuid(8),
                    ApprovedBy: reader.IsDBNull(9) ? null : reader.GetGuid(9),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(10),
                    ProcessedAt: reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                    Note: reader.IsDBNull(12) ? null : reader.GetString(12),
                    Lines: []));
            }
            await reader.CloseAsync();
        }

        if (returnIds.Count > 0)
        {
            var lines = await LoadReturnLinesAsync(db, tenantId, companyId, returnIds, cancellationToken);
            for (var i = 0; i < returns.Count; i++)
            {
                if (lines.TryGetValue(returns[i].ReturnId, out var rLines))
                {
                    returns[i] = returns[i] with { Lines = rLines };
                }
            }
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return returns;
    }

    public async Task<Result<StockReturnSummary>> CreateStockReturnAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        CreateStockReturnRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('inventory.stock_return.create:' || @key))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("key", transactionId.ToString());
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var returnId = Guid.NewGuid();
            var returnNumber = $"RET-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

            await using (var insHead = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_return
                    (tenant_id, company_id, return_id, return_number, return_type,
                     source_document_type, source_document_id, warehouse_id, status,
                     created_by, note, created_at)
                VALUES
                    (@tenantId, @companyId, @id, @no, @retType,
                     @srcType, @srcId, @whId, 'draft',
                     @userId, @note, now());
                """,
                db.Connection,
                db.Transaction))
            {
                insHead.Parameters.AddWithValue("tenantId", context.TenantId);
                insHead.Parameters.AddWithValue("companyId", context.CompanyId);
                insHead.Parameters.AddWithValue("id", returnId);
                insHead.Parameters.AddWithValue("no", returnNumber);
                insHead.Parameters.AddWithValue("retType", request.ReturnType);
                insHead.Parameters.AddWithValue("srcType", request.SourceDocumentType);
                AddNullableUuid(insHead, "srcId", request.SourceDocumentId);
                insHead.Parameters.AddWithValue("whId", request.WarehouseId);
                insHead.Parameters.AddWithValue("userId", context.UserId);
                insHead.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
                await insHead.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var l in request.Lines)
            {
                await using var insLine = new NpgsqlCommand(
                    """
                    INSERT INTO erp_inventory.stock_return_line
                        (tenant_id, company_id, line_id, return_id, variant_id, bin_id,
                         quantity, unit_cost, disposition, note)
                    VALUES
                        (@tenantId, @companyId, gen_random_uuid(), @retId, @varId, @binId,
                         @qty, @cost, @disp, @note);
                    """,
                    db.Connection,
                    db.Transaction);
                insLine.Parameters.AddWithValue("tenantId", context.TenantId);
                insLine.Parameters.AddWithValue("companyId", context.CompanyId);
                insLine.Parameters.AddWithValue("retId", returnId);
                insLine.Parameters.AddWithValue("varId", l.VariantId);
                insLine.Parameters.AddWithValue("binId", l.BinId);
                insLine.Parameters.AddWithValue("qty", l.Quantity);
                insLine.Parameters.AddWithValue("cost", l.UnitCost);
                insLine.Parameters.AddWithValue("disp", l.Disposition);
                insLine.Parameters.AddWithValue("note", (object?)l.Note ?? DBNull.Value);
                await insLine.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var idemIns = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash)
                VALUES
                    (@tenantId, @companyId, @key, 'inventory.stock_return.create', @hash);
                """,
                db.Connection,
                db.Transaction))
            {
                idemIns.Parameters.AddWithValue("tenantId", context.TenantId);
                idemIns.Parameters.AddWithValue("companyId", context.CompanyId);
                idemIns.Parameters.AddWithValue("key", transactionId);
                idemIns.Parameters.AddWithValue("hash", requestHash);
                await idemIns.ExecuteNonQueryAsync(cancellationToken);
            }

            var created = await GetStockReturnByIdAsync(db, context.TenantId, context.CompanyId, returnId, cancellationToken);
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<StockReturnSummary>.Success(created!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<StockReturnSummary>(db, "DUPLICATE_STOCK_RETURN", cancellationToken);
        }
    }

    public async Task<Result<PostStockReturnResult>> ProcessStockReturnAsync(
        TenantContext context,
        Guid returnId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        string returnNumber;
        string returnType;
        Guid warehouseId;
        string currentStatus;

        await using (var sel = new NpgsqlCommand(
            "SELECT return_number, return_type, warehouse_id, status FROM erp_inventory.stock_return WHERE tenant_id = @tenantId AND company_id = @companyId AND return_id = @id FOR UPDATE;",
            db.Connection,
            db.Transaction))
        {
            sel.Parameters.AddWithValue("tenantId", context.TenantId);
            sel.Parameters.AddWithValue("companyId", context.CompanyId);
            sel.Parameters.AddWithValue("id", returnId);
            await using var r = await sel.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return await RollbackWithFailureAsync<PostStockReturnResult>(db, "STOCK_RETURN_NOT_FOUND", cancellationToken);
            }
            returnNumber = r.GetString(0);
            returnType = r.GetString(1);
            warehouseId = r.GetGuid(2);
            currentStatus = r.GetString(3);
            await r.CloseAsync();
        }

        if (currentStatus is not ("draft" or "approved"))
        {
            return await RollbackWithFailureAsync<PostStockReturnResult>(db, $"CANNOT_PROCESS_STOCK_RETURN: Status is '{currentStatus}'.", cancellationToken);
        }

        var lines = new List<(Guid LineId, Guid VariantId, Guid BinId, decimal Quantity, decimal UnitCost, string Disposition)>();
        await using (var selLines = new NpgsqlCommand(
            "SELECT line_id, variant_id, bin_id, quantity, unit_cost, disposition FROM erp_inventory.stock_return_line WHERE tenant_id = @tenantId AND company_id = @companyId AND return_id = @id;",
            db.Connection,
            db.Transaction))
        {
            selLines.Parameters.AddWithValue("tenantId", context.TenantId);
            selLines.Parameters.AddWithValue("companyId", context.CompanyId);
            selLines.Parameters.AddWithValue("id", returnId);
            await using var lr = await selLines.ExecuteReaderAsync(cancellationToken);
            while (await lr.ReadAsync(cancellationToken))
            {
                lines.Add((lr.GetGuid(0), lr.GetGuid(1), lr.GetGuid(2), lr.GetDecimal(3), lr.GetDecimal(4), lr.GetString(5)));
            }
            await lr.CloseAsync();
        }

        if (lines.Count == 0)
        {
            return await RollbackWithFailureAsync<PostStockReturnResult>(db, "NO_LINES_TO_PROCESS", cancellationToken);
        }

        var movementId = Guid.NewGuid();
        var processedAt = DateTimeOffset.UtcNow;
        var movementType = returnType == "customer_return" ? "receipt" : "issue";

        await using (var insMov = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type,
                 source_document_id, occurred_at, posted_at, created_by, note)
            VALUES
                (@tenantId, @companyId, @movId, @txId, @movType, @retType,
                 @returnId, @now, @now, @userId, @note);
            """,
            db.Connection,
            db.Transaction))
        {
            insMov.Parameters.AddWithValue("tenantId", context.TenantId);
            insMov.Parameters.AddWithValue("companyId", context.CompanyId);
            insMov.Parameters.AddWithValue("movId", movementId);
            insMov.Parameters.AddWithValue("txId", Guid.NewGuid());
            insMov.Parameters.AddWithValue("movType", movementType);
            insMov.Parameters.AddWithValue("retType", returnType);
            insMov.Parameters.AddWithValue("returnId", returnId);
            insMov.Parameters.AddWithValue("now", processedAt);
            insMov.Parameters.AddWithValue("userId", context.UserId);
            insMov.Parameters.AddWithValue("note", $"{returnType} processing for {returnNumber}");
            await insMov.ExecuteNonQueryAsync(cancellationToken);
        }

        decimal totalReturnedQty = 0;
        decimal totalValuationImpact = 0;
        int legNo = 1;

        foreach (var line in lines)
        {
            totalReturnedQty += line.Quantity;
            totalValuationImpact += line.Quantity * line.UnitCost;

            // Base UOM
            Guid baseUomId;
            await using (var getUom = new NpgsqlCommand(
                "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tenantId AND variant_id = @varId",
                db.Connection,
                db.Transaction))
            {
                getUom.Parameters.AddWithValue("tenantId", context.TenantId);
                getUom.Parameters.AddWithValue("varId", line.VariantId);
                baseUomId = (Guid)(await getUom.ExecuteScalarAsync(cancellationToken))!;
            }

            var delta = returnType == "customer_return" ? line.Quantity : -line.Quantity;

            // Leg
            await using (var insLeg = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.inventory_movement_leg
                    (tenant_id, company_id, movement_id, leg_number, variant_id,
                     warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
                VALUES
                    (@tenantId, @companyId, @movId, @legNo, @varId,
                     @whId, @binId, 'available', @delta, @uomId);
                """,
                db.Connection,
                db.Transaction))
            {
                insLeg.Parameters.AddWithValue("tenantId", context.TenantId);
                insLeg.Parameters.AddWithValue("companyId", context.CompanyId);
                insLeg.Parameters.AddWithValue("movId", movementId);
                insLeg.Parameters.AddWithValue("legNo", legNo++);
                insLeg.Parameters.AddWithValue("varId", line.VariantId);
                insLeg.Parameters.AddWithValue("whId", warehouseId);
                insLeg.Parameters.AddWithValue("binId", line.BinId);
                insLeg.Parameters.AddWithValue("delta", delta);
                insLeg.Parameters.AddWithValue("uomId", baseUomId);
                await insLeg.ExecuteNonQueryAsync(cancellationToken);
            }

            if (returnType == "customer_return")
            {
                // Increment physical stock
                await using (var upsertBal = new NpgsqlCommand(
                    """
                    INSERT INTO erp_inventory.stock_balance
                        (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                    VALUES
                        (@tenantId, @companyId, @varId, @whId, @binId, 'available', @qty, gen_random_uuid())
                    ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                    DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
                    """,
                    db.Connection,
                    db.Transaction))
                {
                    upsertBal.Parameters.AddWithValue("tenantId", context.TenantId);
                    upsertBal.Parameters.AddWithValue("companyId", context.CompanyId);
                    upsertBal.Parameters.AddWithValue("varId", line.VariantId);
                    upsertBal.Parameters.AddWithValue("whId", warehouseId);
                    upsertBal.Parameters.AddWithValue("binId", line.BinId);
                    upsertBal.Parameters.AddWithValue("qty", line.Quantity);
                    await upsertBal.ExecuteNonQueryAsync(cancellationToken);
                }

                // If available or quarantine, create new FIFO layer
                if (line.Disposition is "available" or "quarantine")
                {
                    await using var insLayer = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.inventory_cost_layer
                            (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id,
                             status_code, source_document_type, source_document_id, source_movement_id,
                             received_at, original_qty, remaining_qty, unit_cost, currency_code)
                        VALUES
                            (@tenantId, @companyId, gen_random_uuid(), @varId, @whId, @binId,
                             'available', 'customer_return', @returnId, @movId,
                             @now, @qty, @qty, @cost, 'PKR');
                        """,
                        db.Connection,
                        db.Transaction);
                    insLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                    insLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                    insLayer.Parameters.AddWithValue("varId", line.VariantId);
                    insLayer.Parameters.AddWithValue("whId", warehouseId);
                    insLayer.Parameters.AddWithValue("binId", line.BinId);
                    insLayer.Parameters.AddWithValue("returnId", returnId);
                    insLayer.Parameters.AddWithValue("movId", movementId);
                    insLayer.Parameters.AddWithValue("now", processedAt);
                    insLayer.Parameters.AddWithValue("qty", line.Quantity);
                    insLayer.Parameters.AddWithValue("cost", line.UnitCost);
                    await insLayer.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                // Supplier return: decrement physical stock
                await using (var decBal = new NpgsqlCommand(
                    """
                    UPDATE erp_inventory.stock_balance
                    SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                    WHERE tenant_id = @tenantId AND company_id = @companyId
                      AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                      AND status_code = 'available' AND on_hand_qty >= @qty;
                    """,
                    db.Connection,
                    db.Transaction))
                {
                    decBal.Parameters.AddWithValue("tenantId", context.TenantId);
                    decBal.Parameters.AddWithValue("companyId", context.CompanyId);
                    decBal.Parameters.AddWithValue("varId", line.VariantId);
                    decBal.Parameters.AddWithValue("whId", warehouseId);
                    decBal.Parameters.AddWithValue("binId", line.BinId);
                    decBal.Parameters.AddWithValue("qty", line.Quantity);
                    var rows = await decBal.ExecuteNonQueryAsync(cancellationToken);
                    if (rows != 1)
                    {
                        return await RollbackWithFailureAsync<PostStockReturnResult>(
                            db, $"INSUFFICIENT_STOCK_BALANCE: Not enough stock in bin {line.BinId} for supplier return of {line.Quantity}.", cancellationToken);
                    }
                }

                // Consume FIFO layer
                var openLayers = new List<FifoCostLayerSlice>();
                await using (var selLayers = new NpgsqlCommand(
                    """
                    SELECT layer_id, received_at, remaining_qty, unit_cost, warehouse_id, bin_id
                    FROM erp_inventory.inventory_cost_layer
                    WHERE tenant_id = @tenantId AND company_id = @companyId
                      AND variant_id = @varId AND warehouse_id = @whId AND bin_id = @binId
                      AND remaining_qty > 0
                    ORDER BY received_at ASC, layer_id ASC
                    FOR UPDATE;
                    """,
                    db.Connection,
                    db.Transaction))
                {
                    selLayers.Parameters.AddWithValue("tenantId", context.TenantId);
                    selLayers.Parameters.AddWithValue("companyId", context.CompanyId);
                    selLayers.Parameters.AddWithValue("varId", line.VariantId);
                    selLayers.Parameters.AddWithValue("whId", warehouseId);
                    selLayers.Parameters.AddWithValue("binId", line.BinId);
                    await using var lr = await selLayers.ExecuteReaderAsync(cancellationToken);
                    while (await lr.ReadAsync(cancellationToken))
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

                var allocResult = FifoCostAllocator.Allocate(
                    openLayers,
                    line.Quantity,
                    locationDescription: $"warehouse {warehouseId}, bin {line.BinId}",
                    expectedWarehouseId: warehouseId,
                    expectedBinId: line.BinId);

                foreach (var alloc in allocResult.Allocations)
                {
                    await using var updCostLayer = new NpgsqlCommand(
                        """
                        UPDATE erp_inventory.inventory_cost_layer
                        SET remaining_qty = remaining_qty - @consumed, updated_at = now()
                        WHERE tenant_id = @tenantId AND company_id = @companyId
                          AND layer_id = @layerId;
                        """,
                        db.Connection,
                        db.Transaction);
                    updCostLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                    updCostLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                    updCostLayer.Parameters.AddWithValue("layerId", alloc.LayerId);
                    updCostLayer.Parameters.AddWithValue("consumed", alloc.ConsumedQty);
                    await updCostLayer.ExecuteNonQueryAsync(cancellationToken);

                    await using var insAlloc = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.inventory_cost_allocation
                            (tenant_id, company_id, allocation_id, layer_id, movement_id,
                             source_document_type, source_document_id, source_line_number,
                             variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                        VALUES
                            (@tenantId, @companyId, gen_random_uuid(), @layerId, @movId,
                             'supplier_return', @returnId, @ln,
                             @varId, @qty, @cost, @extCost, 'PKR');
                        """,
                        db.Connection,
                        db.Transaction);
                    insAlloc.Parameters.AddWithValue("tenantId", context.TenantId);
                    insAlloc.Parameters.AddWithValue("companyId", context.CompanyId);
                    insAlloc.Parameters.AddWithValue("layerId", alloc.LayerId);
                    insAlloc.Parameters.AddWithValue("movId", movementId);
                    insAlloc.Parameters.AddWithValue("returnId", returnId);
                    insAlloc.Parameters.AddWithValue("ln", legNo);
                    insAlloc.Parameters.AddWithValue("varId", line.VariantId);
                    insAlloc.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                    insAlloc.Parameters.AddWithValue("cost", alloc.UnitCost);
                    insAlloc.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                    await insAlloc.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Link movement to return line
            await using var updLineMov = new NpgsqlCommand(
                "UPDATE erp_inventory.stock_return_line SET movement_id = @movId WHERE tenant_id = @tenantId AND company_id = @companyId AND line_id = @lineId;",
                db.Connection,
                db.Transaction);
            updLineMov.Parameters.AddWithValue("tenantId", context.TenantId);
            updLineMov.Parameters.AddWithValue("companyId", context.CompanyId);
            updLineMov.Parameters.AddWithValue("movId", movementId);
            updLineMov.Parameters.AddWithValue("lineId", line.LineId);
            await updLineMov.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update return header
        await using (var updRet = new NpgsqlCommand(
            "UPDATE erp_inventory.stock_return SET status = 'processed', processed_at = @now WHERE tenant_id = @tenantId AND company_id = @companyId AND return_id = @id;",
            db.Connection,
            db.Transaction))
        {
            updRet.Parameters.AddWithValue("tenantId", context.TenantId);
            updRet.Parameters.AddWithValue("companyId", context.CompanyId);
            updRet.Parameters.AddWithValue("now", processedAt);
            updRet.Parameters.AddWithValue("id", returnId);
            await updRet.ExecuteNonQueryAsync(cancellationToken);
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return Result<PostStockReturnResult>.Success(new PostStockReturnResult(
            ReturnId: returnId,
            ReturnNumber: returnNumber,
            MovementId: movementId,
            TotalReturnedQty: totalReturnedQty,
            TotalValuationImpact: totalValuationImpact,
            ProcessedAt: processedAt));
    }

    private static async Task<Dictionary<Guid, List<StockReturnLineSummary>>> LoadReturnLinesAsync(
        DbScope db, Guid tenantId, Guid companyId, IReadOnlyList<Guid> returnIds, CancellationToken ct)
    {
        var dict = new Dictionary<Guid, List<StockReturnLineSummary>>();
        await using var cmd = new NpgsqlCommand("""
            SELECT l.line_id, l.return_id, l.variant_id, v.sku,
                   l.bin_id, b.code AS bin_code,
                   l.quantity, l.unit_cost, (l.quantity * l.unit_cost) AS ext_cost,
                   l.disposition, l.movement_id, l.note
            FROM erp_inventory.stock_return_line l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.return_id = ANY(@ids)
            ORDER BY l.line_id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("ids", returnIds.ToArray());

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var retId = r.GetGuid(1);
            if (!dict.TryGetValue(retId, out var lines))
            {
                lines = [];
                dict[retId] = lines;
            }
            lines.Add(new StockReturnLineSummary(
                LineId: r.GetGuid(0),
                ReturnId: retId,
                VariantId: r.GetGuid(2),
                Sku: r.GetString(3),
                BinId: r.GetGuid(4),
                BinCode: r.GetString(5),
                Quantity: r.GetDecimal(6),
                UnitCost: r.GetDecimal(7),
                ExtendedCost: r.GetDecimal(8),
                Disposition: r.GetString(9),
                MovementId: r.IsDBNull(10) ? null : r.GetGuid(10),
                Note: r.IsDBNull(11) ? null : r.GetString(11)));
        }
        await r.CloseAsync();
        return dict;
    }

    private static async Task<StockReturnSummary?> GetStockReturnByIdAsync(
        DbScope db, Guid tenantId, Guid companyId, Guid returnId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT r.return_id, r.return_number, r.return_type, r.source_document_type,
                   r.source_document_id, r.warehouse_id, w.code AS warehouse_code,
                   r.status, r.created_by, r.approved_by, r.created_at, r.processed_at, r.note
            FROM erp_inventory.stock_return r
            JOIN erp_inventory.warehouse w ON w.tenant_id = r.tenant_id AND w.company_id = r.company_id AND w.warehouse_id = r.warehouse_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId AND r.return_id = @id;
            """, db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", returnId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var header = new StockReturnSummary(
            ReturnId: r.GetGuid(0),
            ReturnNumber: r.GetString(1),
            ReturnType: r.GetString(2),
            SourceDocumentType: r.GetString(3),
            SourceDocumentId: r.IsDBNull(4) ? null : r.GetGuid(4),
            WarehouseId: r.GetGuid(5),
            WarehouseCode: r.GetString(6),
            Status: r.GetString(7),
            CreatedBy: r.GetGuid(8),
            ApprovedBy: r.IsDBNull(9) ? null : r.GetGuid(9),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(10),
            ProcessedAt: r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
            Note: r.IsDBNull(12) ? null : r.GetString(12),
            Lines: []);
        await r.CloseAsync();

        var linesMap = await LoadReturnLinesAsync(db, tenantId, companyId, [returnId], ct);
        if (linesMap.TryGetValue(returnId, out var lines))
        {
            header = header with { Lines = lines };
        }
        return header;
    }

    // ── Inventory Movement History & Valuation Reports ──────────────────────────────────

    public async Task<IReadOnlyList<BinMovementHistoryItem>> GetBinMovementHistoryAsync(
        Guid tenantId,
        Guid companyId,
        Guid? warehouseId,
        Guid? binId,
        Guid? variantId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        var sql = new StringBuilder("""
            SELECT m.movement_id, l.leg_number, l.variant_id, v.sku,
                   l.warehouse_id, w.code AS warehouse_code,
                   l.bin_id, b.code AS bin_code,
                   m.movement_type, m.source_document_type, m.source_document_id,
                   l.quantity_delta, l.status_code,
                   m.occurred_at, m.posted_at, m.note
            FROM erp_inventory.inventory_movement_leg l
            JOIN erp_inventory.inventory_movement m ON m.tenant_id = l.tenant_id AND m.company_id = l.company_id AND m.movement_id = l.movement_id
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = l.tenant_id AND w.company_id = l.company_id AND w.warehouse_id = l.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId
            """);

        if (warehouseId.HasValue) sql.Append(" AND l.warehouse_id = @warehouseId");
        if (binId.HasValue) sql.Append(" AND l.bin_id = @binId");
        if (variantId.HasValue) sql.Append(" AND l.variant_id = @variantId");
        sql.Append(" ORDER BY m.occurred_at DESC, l.leg_number ASC LIMIT @limit;");

        await using var cmd = new NpgsqlCommand(sql.ToString(), db.Connection, db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        if (warehouseId.HasValue) cmd.Parameters.AddWithValue("warehouseId", warehouseId.Value);
        if (binId.HasValue) cmd.Parameters.AddWithValue("binId", binId.Value);
        if (variantId.HasValue) cmd.Parameters.AddWithValue("variantId", variantId.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        var history = new List<BinMovementHistoryItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new BinMovementHistoryItem(
                MovementId: reader.GetGuid(0),
                LegNumber: reader.GetInt32(1),
                VariantId: reader.GetGuid(2),
                Sku: reader.GetString(3),
                WarehouseId: reader.GetGuid(4),
                WarehouseCode: reader.GetString(5),
                BinId: reader.GetGuid(6),
                BinCode: reader.GetString(7),
                MovementType: reader.GetString(8),
                SourceDocumentType: reader.IsDBNull(9) ? null : reader.GetString(9),
                SourceDocumentId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
                QuantityDelta: reader.GetDecimal(11),
                StatusCode: reader.GetString(12),
                OccurredAt: reader.GetFieldValue<DateTimeOffset>(13),
                PostedAt: reader.GetFieldValue<DateTimeOffset>(14),
                Note: reader.IsDBNull(15) ? null : reader.GetString(15)));
        }
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return history;
    }

    public async Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);

        // 1. Load active layers
        var layers = new List<InventoryValuationLayerReportItem>();
        decimal totalValuedStock = 0;
        decimal totalValuationPkr = 0;

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT cl.layer_id, cl.parent_layer_id, cl.variant_id, v.sku,
                   cl.warehouse_id, w.code AS warehouse_code,
                   cl.bin_id, b.code AS bin_code,
                   cl.source_document_type, cl.source_document_id,
                   cl.received_at, cl.original_qty, cl.remaining_qty,
                   cl.unit_cost, (cl.remaining_qty * cl.unit_cost) AS ext_val,
                   cl.currency_code, cl.status_code
            FROM erp_inventory.inventory_cost_layer cl
            JOIN erp_inventory.product_variant v ON v.tenant_id = cl.tenant_id AND v.variant_id = cl.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = cl.tenant_id AND w.company_id = cl.company_id AND w.warehouse_id = cl.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = cl.tenant_id AND b.company_id = cl.company_id AND b.bin_id = cl.bin_id
            WHERE cl.tenant_id = @tenantId AND cl.company_id = @companyId AND cl.remaining_qty > 0
            ORDER BY cl.received_at ASC, cl.layer_id ASC;
            """,
            db.Connection,
            db.Transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var remQty = r.GetDecimal(12);
                var unitCost = r.GetDecimal(13);
                var extVal = r.GetDecimal(14);
                totalValuedStock += remQty;
                totalValuationPkr += extVal;

                layers.Add(new InventoryValuationLayerReportItem(
                    LayerId: r.GetGuid(0),
                    ParentLayerId: r.IsDBNull(1) ? null : r.GetGuid(1),
                    VariantId: r.GetGuid(2),
                    Sku: r.GetString(3),
                    WarehouseId: r.GetGuid(4),
                    WarehouseCode: r.GetString(5),
                    BinId: r.GetGuid(6),
                    BinCode: r.GetString(7),
                    SourceDocumentType: r.GetString(8),
                    SourceDocumentId: r.IsDBNull(9) ? null : r.GetGuid(9),
                    ReceivedAt: r.GetFieldValue<DateTimeOffset>(10),
                    OriginalQty: r.GetDecimal(11),
                    RemainingQty: remQty,
                    UnitCost: unitCost,
                    ExtendedValuation: extVal,
                    CurrencyCode: r.GetString(15),
                    StatusCode: r.GetString(16)));
            }
            await r.CloseAsync();
        }

        // 2. Physical stock
        decimal totalPhysicalStock = 0;
        await using (var physCmd = new NpgsqlCommand(
            "SELECT COALESCE(SUM(on_hand_qty), 0) FROM erp_inventory.stock_balance WHERE tenant_id = @tenantId AND company_id = @companyId AND status_code = 'available';",
            db.Connection,
            db.Transaction))
        {
            physCmd.Parameters.AddWithValue("tenantId", tenantId);
            physCmd.Parameters.AddWithValue("companyId", companyId);
            totalPhysicalStock = (decimal)(await physCmd.ExecuteScalarAsync(cancellationToken) ?? 0m);
        }

        // 3. GL Account 1200 Balance
        decimal gl1200Balance = 0;
        await using (var glCmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0)
            FROM finance.journal_lines jl
            JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
            JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE jl.tenant_id = @tenantId AND jl.company_id = @companyId
              AND a.account_code = '1200' AND j.status = 'posted';
            """,
            db.Connection,
            db.Transaction))
        {
            glCmd.Parameters.AddWithValue("tenantId", tenantId);
            glCmd.Parameters.AddWithValue("companyId", companyId);
            gl1200Balance = (decimal)(await glCmd.ExecuteScalarAsync(cancellationToken) ?? 0m);
        }

        decimal unvaluedStock = Math.Max(0m, totalPhysicalStock - totalValuedStock);
        decimal variance = totalValuationPkr - gl1200Balance;
        bool isReconciled = variance == 0m;

        await db.Transaction.CommitAsync(cancellationToken);
        return new InventoryValuationReportSummary(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalActiveLayers: layers.Count,
            TotalPhysicalStock: totalPhysicalStock,
            TotalValuedStock: totalValuedStock,
            TotalUnvaluedStock: unvaluedStock,
            TotalValuationPkr: totalValuationPkr,
            GeneralLedger1200BalancePkr: gl1200Balance,
            ReconciliationVariancePkr: variance,
            IsReconciled: isReconciled,
            Layers: layers);
    }
}
