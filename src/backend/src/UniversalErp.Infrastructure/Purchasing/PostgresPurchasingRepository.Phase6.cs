using System.Data.Common;
using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Purchasing;
using UniversalErp.Infrastructure.Finance;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Purchasing;

public sealed partial class PostgresPurchasingRepository
{
    // ── Tolerance Policy ────────────────────────────────────────────────────────

    public async Task<PurchasingTolerancePolicySummary> GetTolerancePolicyAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT policy_id, price_tolerance_percent, quantity_tolerance_percent,
                   auto_approve_within_tolerance, updated_at
            FROM erp_purchasing.purchasing_tolerance_policy
            WHERE tenant_id = @tid AND company_id = @cid
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var res = new PurchasingTolerancePolicySummary(
                reader.GetGuid(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetBoolean(3),
                reader.GetFieldValue<DateTimeOffset>(4));
            await reader.CloseAsync();
            await tx.CommitAsync(ct);
            return res;
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);

        // Default policy if not configured yet
        return new PurchasingTolerancePolicySummary(Guid.Empty, 5.0m, 5.0m, true, DateTimeOffset.UtcNow);
    }

    public async Task<bool> UpdateTolerancePolicyAsync(UpdatePurchasingTolerancePolicyRequest req, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            INSERT INTO erp_purchasing.purchasing_tolerance_policy
                (tenant_id, company_id, price_tolerance_percent, quantity_tolerance_percent,
                 auto_approve_within_tolerance, updated_at)
            VALUES
                (@tid, @cid, @price, @qty, @auto, now())
            ON CONFLICT (tenant_id, company_id)
            DO UPDATE SET
                price_tolerance_percent = EXCLUDED.price_tolerance_percent,
                quantity_tolerance_percent = EXCLUDED.quantity_tolerance_percent,
                auto_approve_within_tolerance = EXCLUDED.auto_approve_within_tolerance,
                updated_at = now();
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("price", req.PriceTolerancePercent);
        cmd.Parameters.AddWithValue("qty", req.QuantityTolerancePercent);
        cmd.Parameters.AddWithValue("auto", req.AutoApproveWithinTolerance);

        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    // ── Purchase Order Approvals & Cancellation ──────────────────────────────────

    public async Task<bool> ApprovePurchaseOrderAsync(Guid purchaseOrderId, Guid approvedBy, string? note, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_purchasing.purchase_order
            SET order_status = 'approved',
                approved_by = @by,
                approved_at = now(),
                note = CASE WHEN @note IS NOT NULL THEN COALESCE(note || ' | ', '') || @note ELSE note END,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND purchase_order_id = @id
              AND order_status IN ('draft', 'pending_approval');
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", purchaseOrderId);
        cmd.Parameters.AddWithValue("by", approvedBy == Guid.Empty ? DBNull.Value : (object)approvedBy);
        cmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<bool> CancelPurchaseOrderAsync(Guid purchaseOrderId, string reason, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            UPDATE erp_purchasing.purchase_order
            SET order_status = 'cancelled',
                note = COALESCE(note || ' | Cancelled: ', 'Cancelled: ') || @reason,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND purchase_order_id = @id
              AND order_status IN ('draft', 'pending_approval', 'approved', 'confirmed');
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", purchaseOrderId);
        cmd.Parameters.AddWithValue("reason", reason);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    // ── Quality Inspection & Quarantine Release ──────────────────────────────────

    public async Task<IReadOnlyList<PurchaseInspectionSummary>> ListInspectionsAsync(Guid? grnId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT pi.inspection_id, pi.inspection_number, pi.grn_id, g.grn_number,
                   pi.inspector_id, pi.inspected_at, pi.overall_status, pi.notes
            FROM erp_purchasing.purchase_inspection pi
            JOIN erp_purchasing.goods_received_note g
              ON g.tenant_id = pi.tenant_id AND g.company_id = pi.company_id AND g.grn_id = pi.grn_id
            WHERE (@grn::uuid IS NULL OR pi.grn_id = @grn::uuid)
            ORDER BY pi.inspected_at DESC;
            """;
        cmd.Parameters.Add("grn", NpgsqlTypes.NpgsqlDbType.Uuid).Value = grnId.HasValue ? grnId.Value : DBNull.Value;

        var inspections = new List<(PurchaseInspectionSummary Header, Guid Id)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                inspections.Add((new PurchaseInspectionSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    []), reader.GetGuid(0)));
            }
        }

        if (inspections.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var inspIds = inspections.Select(i => i.Id).ToArray();
        await using var lCmd = conn.CreateCommand();
        lCmd.CommandText = """
            SELECT pil.inspection_id, pil.inspection_line_id, pil.grn_line_number, pil.variant_id, v.sku,
                   pil.inspected_qty, pil.accepted_qty, pil.quarantined_qty, pil.rejected_qty,
                   pil.rejection_reason, pil.disposition_notes
            FROM erp_purchasing.purchase_inspection_line pil
            JOIN erp_inventory.product_variant v
              ON v.tenant_id = pil.tenant_id AND v.variant_id = pil.variant_id
            WHERE pil.inspection_id = ANY(@ids)
            ORDER BY pil.inspection_id, pil.grn_line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", inspIds);

        var linesMap = new Dictionary<Guid, List<PurchaseInspectionLineSummary>>();
        await using (var lReader = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                var id = lReader.GetGuid(0);
                if (!linesMap.TryGetValue(id, out var list)) { list = []; linesMap[id] = list; }
                list.Add(new PurchaseInspectionLineSummary(
                    lReader.GetGuid(1),
                    lReader.GetInt16(2),
                    lReader.GetGuid(3),
                    lReader.GetString(4),
                    lReader.GetDecimal(5),
                    lReader.GetDecimal(6),
                    lReader.GetDecimal(7),
                    lReader.GetDecimal(8),
                    lReader.IsDBNull(9) ? null : lReader.GetString(9),
                    lReader.IsDBNull(10) ? null : lReader.GetString(10)));
            }
        }

        await tx.CommitAsync(ct);
        return inspections.Select(i => i.Header with
        {
            Lines = linesMap.TryGetValue(i.Id, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<PurchaseInspectionSummary?> GetInspectionAsync(Guid inspectionId, CancellationToken ct)
    {
        var all = await ListInspectionsAsync(null, ct);
        return all.FirstOrDefault(i => i.InspectionId == inspectionId);
    }

    public async Task<Guid> CreateInspectionAsync(CreatePurchaseInspectionRequest req, Guid inspectorId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        // Fetch GRN to verify warehouse & status
        cmd.CommandText = "SELECT warehouse_id FROM erp_purchasing.goods_received_note WHERE tenant_id = @tid AND company_id = @cid AND grn_id = @gid";
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("gid", req.GrnId);
        var whIdObj = await cmd.ExecuteScalarAsync(ct);
        if (whIdObj is null) throw new InvalidOperationException("GRN_NOT_FOUND");
        var warehouseId = (Guid)whIdObj;

        // Calculate overall inspection status
        var totalRejected = req.Lines.Sum(l => l.RejectedQty);
        var totalQuarantined = req.Lines.Sum(l => l.QuarantinedQty);
        var totalAccepted = req.Lines.Sum(l => l.AcceptedQty);

        var overallStatus = (totalRejected == 0m && totalQuarantined == 0m)
            ? "passed"
            : (totalAccepted == 0m && totalQuarantined == 0m ? "rejected" : "partial");

        var inspId = Guid.NewGuid();
        await using var insCmd = conn.CreateCommand();
        insCmd.CommandText = """
            INSERT INTO erp_purchasing.purchase_inspection
                (tenant_id, company_id, inspection_id, inspection_number, grn_id,
                 inspector_id, inspected_at, overall_status, notes)
            VALUES
                (@tid, @cid, @id, @no, @gid, @insp, now(), @status, @notes);
            """;
        insCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        insCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        insCmd.Parameters.AddWithValue("id", inspId);
        insCmd.Parameters.AddWithValue("no", req.InspectionNumber.Trim());
        insCmd.Parameters.AddWithValue("gid", req.GrnId);
        insCmd.Parameters.AddWithValue("insp", inspectorId == Guid.Empty ? Ctx.UserId : inspectorId);
        insCmd.Parameters.AddWithValue("status", overallStatus);
        insCmd.Parameters.AddWithValue("notes", (object?)req.Notes ?? DBNull.Value);
        await insCmd.ExecuteNonQueryAsync(ct);

        // Insert lines & handle quarantine stock isolation
        foreach (var l in req.Lines)
        {
            var lineId = Guid.NewGuid();
            await using var lineCmd = conn.CreateCommand();
            lineCmd.CommandText = """
                INSERT INTO erp_purchasing.purchase_inspection_line
                    (tenant_id, company_id, inspection_line_id, inspection_id, grn_line_number,
                     variant_id, inspected_qty, accepted_qty, quarantined_qty, rejected_qty,
                     rejection_reason, disposition_notes)
                VALUES
                    (@tid, @cid, @lid, @id, @gln, @var, @iqty, @aqty, @qqty, @rqty, @rr, @dn);
                """;
            lineCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lineCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lineCmd.Parameters.AddWithValue("lid", lineId);
            lineCmd.Parameters.AddWithValue("id", inspId);
            lineCmd.Parameters.AddWithValue("gln", l.GrnLineNumber);
            lineCmd.Parameters.AddWithValue("var", l.VariantId);
            lineCmd.Parameters.AddWithValue("iqty", l.InspectedQty);
            lineCmd.Parameters.AddWithValue("aqty", l.AcceptedQty);
            lineCmd.Parameters.AddWithValue("qqty", l.QuarantinedQty);
            lineCmd.Parameters.AddWithValue("rqty", l.RejectedQty);
            lineCmd.Parameters.AddWithValue("rr", (object?)l.RejectionReason ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("dn", (object?)l.DispositionNotes ?? DBNull.Value);
            await lineCmd.ExecuteNonQueryAsync(ct);

            // Update GRN line quantities
            await using var updGrnCmd = conn.CreateCommand();
            updGrnCmd.CommandText = """
                UPDATE erp_purchasing.goods_received_note_line
                SET accepted_qty = accepted_qty + @aqty,
                    quarantined_qty = quarantined_qty + @qqty,
                    rejected_qty = rejected_qty + @rqty
                WHERE tenant_id = @tid AND company_id = @cid AND grn_id = @gid AND line_number = @gln;
                """;
            updGrnCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            updGrnCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            updGrnCmd.Parameters.AddWithValue("gid", req.GrnId);
            updGrnCmd.Parameters.AddWithValue("gln", l.GrnLineNumber);
            updGrnCmd.Parameters.AddWithValue("aqty", l.AcceptedQty);
            updGrnCmd.Parameters.AddWithValue("qqty", l.QuarantinedQty);
            updGrnCmd.Parameters.AddWithValue("rqty", l.RejectedQty);
            await updGrnCmd.ExecuteNonQueryAsync(ct);

            // If quarantined > 0, isolate stock: move from 'available' to 'quarantine' in the bin where GRN received it
            if (l.QuarantinedQty > 0m)
            {
                // Find GRN bin
                await using var binCmd = conn.CreateCommand();
                binCmd.CommandText = "SELECT bin_id FROM erp_purchasing.goods_received_note_line WHERE tenant_id = @tid AND company_id = @cid AND grn_id = @gid AND line_number = @gln";
                binCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                binCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                binCmd.Parameters.AddWithValue("gid", req.GrnId);
                binCmd.Parameters.AddWithValue("gln", l.GrnLineNumber);
                var binIdObj = await binCmd.ExecuteScalarAsync(ct);
                if (binIdObj is Guid binId)
                {
                    // Decrement available balance
                    await using var decCmd = conn.CreateCommand();
                    decCmd.CommandText = """
                        UPDATE erp_inventory.stock_balance
                        SET on_hand_qty = GREATEST(0, on_hand_qty - @qty), updated_at = now()
                        WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                          AND warehouse_id = @wh AND bin_id = @bin AND status_code = 'available';
                        """;
                    decCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                    decCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    decCmd.Parameters.AddWithValue("var", l.VariantId);
                    decCmd.Parameters.AddWithValue("wh", warehouseId);
                    decCmd.Parameters.AddWithValue("bin", binId);
                    decCmd.Parameters.AddWithValue("qty", l.QuarantinedQty);
                    await decCmd.ExecuteNonQueryAsync(ct);

                    // Increment quarantine balance
                    await using var incCmd = conn.CreateCommand();
                    incCmd.CommandText = """
                        INSERT INTO erp_inventory.stock_balance
                            (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                        VALUES
                            (@tid, @cid, @var, @wh, @bin, 'quarantine', @qty, gen_random_uuid())
                        ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                        DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
                        """;
                    incCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                    incCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    incCmd.Parameters.AddWithValue("var", l.VariantId);
                    incCmd.Parameters.AddWithValue("wh", warehouseId);
                    incCmd.Parameters.AddWithValue("bin", binId);
                    incCmd.Parameters.AddWithValue("qty", l.QuarantinedQty);
                    await incCmd.ExecuteNonQueryAsync(ct);

                    Guid baseUomId;
                    await using (var getUom = conn.CreateCommand())
                    {
                        getUom.CommandText = "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @var;";
                        getUom.Parameters.AddWithValue("tid", Ctx.TenantId);
                        getUom.Parameters.AddWithValue("var", l.VariantId);
                        baseUomId = (Guid)(await getUom.ExecuteScalarAsync(ct)
                            ?? throw new InvalidOperationException("PRODUCT_VARIANT_NOT_FOUND"));
                    }

                    var movementId = Guid.NewGuid();
                    var occurredAt = DateTimeOffset.UtcNow;
                    await using (var movement = conn.CreateCommand())
                    {
                        movement.CommandText = """
                            INSERT INTO erp_inventory.inventory_movement
                                (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                                 source_document_type, source_document_id, occurred_at, created_by, note)
                            VALUES
                                (@tid, @cid, @mid, @txid, 'transfer',
                                 'purchase_inspection', @sourceId, @occurred, @by, @note);
                            """;
                        movement.Parameters.AddWithValue("tid", Ctx.TenantId);
                        movement.Parameters.AddWithValue("cid", Ctx.CompanyId);
                        movement.Parameters.AddWithValue("mid", movementId);
                        movement.Parameters.AddWithValue("txid", Guid.NewGuid());
                        movement.Parameters.AddWithValue("sourceId", inspId);
                        movement.Parameters.AddWithValue("occurred", occurredAt);
                        movement.Parameters.AddWithValue("by", inspectorId == Guid.Empty ? Ctx.UserId : inspectorId);
                        movement.Parameters.AddWithValue("note", $"Quarantine isolation for inspection {req.InspectionNumber}");
                        await movement.ExecuteNonQueryAsync(ct);
                    }

                    await using (var legs = conn.CreateCommand())
                    {
                        legs.CommandText = """
                            INSERT INTO erp_inventory.inventory_movement_leg
                                (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id,
                                 bin_id, status_code, quantity_delta, base_uom_id)
                            VALUES
                                (@tid, @cid, @mid, 1, @var, @wh, @bin, 'available', @negativeQty, @uom),
                                (@tid, @cid, @mid, 2, @var, @wh, @bin, 'quarantine', @positiveQty, @uom);
                            """;
                        legs.Parameters.AddWithValue("tid", Ctx.TenantId);
                        legs.Parameters.AddWithValue("cid", Ctx.CompanyId);
                        legs.Parameters.AddWithValue("mid", movementId);
                        legs.Parameters.AddWithValue("var", l.VariantId);
                        legs.Parameters.AddWithValue("wh", warehouseId);
                        legs.Parameters.AddWithValue("bin", binId);
                        legs.Parameters.AddWithValue("negativeQty", -l.QuarantinedQty);
                        legs.Parameters.AddWithValue("positiveQty", l.QuarantinedQty);
                        legs.Parameters.AddWithValue("uom", baseUomId);
                        await legs.ExecuteNonQueryAsync(ct);
                    }

                    var quarantineFeedPayload = JsonSerializer.Serialize(new
                    {
                        movementId,
                        movementType = "status_transfer",
                        legs = new[]
                        {
                            new
                            {
                                variantId = l.VariantId,
                                warehouseId,
                                binId,
                                stockStatus = "available",
                                quantityDelta = -l.QuarantinedQty,
                                unitOfMeasureId = baseUomId,
                            },
                            new
                            {
                                variantId = l.VariantId,
                                warehouseId,
                                binId,
                                stockStatus = "quarantine",
                                quantityDelta = l.QuarantinedQty,
                                unitOfMeasureId = baseUomId,
                            },
                        },
                        occurredAt,
                        postedAt = occurredAt,
                    });
                    await CompanyChangeLogWriter.AppendAsync(
                        conn, tx, Ctx.TenantId, Ctx.CompanyId,
                        "inventory_movement", movementId, 1, "upsert", quarantineFeedPayload, ct);
                }
            }
        }

        await tx.CommitAsync(ct);
        return inspId;
    }

    public async Task<IReadOnlyList<QuarantineReleaseSummary>> ListQuarantineReleasesAsync(Guid? grnId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT qr.release_id, qr.release_number, qr.inspection_line_id, qr.grn_id,
                   qr.variant_id, v.sku, qr.source_bin_id, sb.code AS source_code,
                   qr.target_bin_id, tb.code AS target_code, qr.released_qty,
                   qr.release_destination, qr.authorized_by, qr.released_at, qr.notes
            FROM erp_purchasing.quarantine_release_record qr
            JOIN erp_inventory.product_variant v ON v.tenant_id = qr.tenant_id AND v.variant_id = qr.variant_id
            JOIN erp_inventory.stock_bin sb ON sb.tenant_id = qr.tenant_id AND sb.company_id = qr.company_id AND sb.bin_id = qr.source_bin_id
            JOIN erp_inventory.stock_bin tb ON tb.tenant_id = qr.tenant_id AND tb.company_id = qr.company_id AND tb.bin_id = qr.target_bin_id
            WHERE (@grn::uuid IS NULL OR qr.grn_id = @grn::uuid)
            ORDER BY qr.released_at DESC;
            """;
        cmd.Parameters.Add("grn", NpgsqlTypes.NpgsqlDbType.Uuid).Value = grnId.HasValue ? grnId.Value : DBNull.Value;

        var list = new List<QuarantineReleaseSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new QuarantineReleaseSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.GetGuid(8),
                reader.GetString(9),
                reader.GetDecimal(10),
                reader.GetString(11),
                reader.GetGuid(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<Guid> ReleaseQuarantineStockAsync(ReleaseQuarantineStockRequest req, Guid authorizedBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        // Verify quarantine stock balance
        cmd.CommandText = """
            SELECT sb.on_hand_qty, sb.warehouse_id
            FROM erp_inventory.stock_balance sb
            WHERE sb.tenant_id = @tid AND sb.company_id = @cid AND sb.variant_id = @var
              AND sb.bin_id = @bin AND sb.status_code = 'quarantine'
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("var", req.VariantId);
        cmd.Parameters.AddWithValue("bin", req.SourceBinId);

        decimal onHandQuarantine = 0m;
        Guid warehouseId = Guid.Empty;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                onHandQuarantine = reader.GetDecimal(0);
                warehouseId = reader.GetGuid(1);
            }
        }

        if (onHandQuarantine < req.ReleasedQty)
        {
            throw new InvalidOperationException($"INSUFFICIENT_QUARANTINE_STOCK: Available in quarantine {onHandQuarantine}, requested {req.ReleasedQty}");
        }

        var releaseId = Guid.NewGuid();
        await using var relCmd = conn.CreateCommand();
        relCmd.CommandText = """
            INSERT INTO erp_purchasing.quarantine_release_record
                (tenant_id, company_id, release_id, release_number, inspection_line_id,
                 grn_id, variant_id, source_bin_id, target_bin_id, released_qty,
                 release_destination, authorized_by, released_at, notes)
            VALUES
                (@tid, @cid, @id, @no, @inLine,
                 @gid, @var, @sBin, @tBin, @qty,
                 @dest, @by, now(), @notes);
            """;
        relCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        relCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        relCmd.Parameters.AddWithValue("id", releaseId);
        relCmd.Parameters.AddWithValue("no", req.ReleaseNumber.Trim());
        relCmd.Parameters.AddWithValue("inLine", req.InspectionLineId);
        relCmd.Parameters.AddWithValue("gid", req.GrnId);
        relCmd.Parameters.AddWithValue("var", req.VariantId);
        relCmd.Parameters.AddWithValue("sBin", req.SourceBinId);
        relCmd.Parameters.AddWithValue("tBin", req.TargetBinId);
        relCmd.Parameters.AddWithValue("qty", req.ReleasedQty);
        relCmd.Parameters.AddWithValue("dest", req.ReleaseDestination);
        relCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
        relCmd.Parameters.AddWithValue("notes", (object?)req.Notes ?? DBNull.Value);
        await relCmd.ExecuteNonQueryAsync(ct);

        // Deduct from quarantine
        await using var decCmd = conn.CreateCommand();
        decCmd.CommandText = """
            UPDATE erp_inventory.stock_balance
            SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
              AND bin_id = @bin AND status_code = 'quarantine';
            """;
        decCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        decCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        decCmd.Parameters.AddWithValue("var", req.VariantId);
        decCmd.Parameters.AddWithValue("bin", req.SourceBinId);
        decCmd.Parameters.AddWithValue("qty", req.ReleasedQty);
        await decCmd.ExecuteNonQueryAsync(ct);

        // If destination is available, add to target bin available stock (No duplicate valuation or layer created!)
        if (req.ReleaseDestination == "available")
        {
            await using var incCmd = conn.CreateCommand();
            incCmd.CommandText = """
                INSERT INTO erp_inventory.stock_balance
                    (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                VALUES
                    (@tid, @cid, @var, @wh, @bin, 'available', @qty, gen_random_uuid())
                ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
                """;
            incCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            incCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            incCmd.Parameters.AddWithValue("var", req.VariantId);
            incCmd.Parameters.AddWithValue("wh", warehouseId);
            incCmd.Parameters.AddWithValue("bin", req.TargetBinId);
            incCmd.Parameters.AddWithValue("qty", req.ReleasedQty);
            await incCmd.ExecuteNonQueryAsync(ct);
        }

        Guid baseUomId;
        await using (var getUom = conn.CreateCommand())
        {
            getUom.CommandText = "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @var;";
            getUom.Parameters.AddWithValue("tid", Ctx.TenantId);
            getUom.Parameters.AddWithValue("var", req.VariantId);
            baseUomId = (Guid)(await getUom.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("PRODUCT_VARIANT_NOT_FOUND"));
        }

        var movementId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        await using (var movement = conn.CreateCommand())
        {
            movement.CommandText = """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                     source_document_type, source_document_id, occurred_at, created_by, note)
                VALUES
                    (@tid, @cid, @mid, @txid, 'transfer',
                     'quarantine_release', @sourceId, @occurred, @by, @note);
                """;
            movement.Parameters.AddWithValue("tid", Ctx.TenantId);
            movement.Parameters.AddWithValue("cid", Ctx.CompanyId);
            movement.Parameters.AddWithValue("mid", movementId);
            movement.Parameters.AddWithValue("txid", Guid.NewGuid());
            movement.Parameters.AddWithValue("sourceId", releaseId);
            movement.Parameters.AddWithValue("occurred", occurredAt);
            movement.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            movement.Parameters.AddWithValue("note", $"Quarantine release {req.ReleaseNumber}");
            await movement.ExecuteNonQueryAsync(ct);
        }

        var releaseLegs = new List<object>
        {
            new
            {
                variantId = req.VariantId,
                warehouseId,
                binId = req.SourceBinId,
                stockStatus = "quarantine",
                quantityDelta = -req.ReleasedQty,
                unitOfMeasureId = baseUomId,
            },
        };
        await using (var legs = conn.CreateCommand())
        {
            legs.CommandText = """
                INSERT INTO erp_inventory.inventory_movement_leg
                    (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id,
                     bin_id, status_code, quantity_delta, base_uom_id)
                VALUES
                    (@tid, @cid, @mid, 1, @var, @wh, @sourceBin, 'quarantine', @negativeQty, @uom);
                """;
            legs.Parameters.AddWithValue("tid", Ctx.TenantId);
            legs.Parameters.AddWithValue("cid", Ctx.CompanyId);
            legs.Parameters.AddWithValue("mid", movementId);
            legs.Parameters.AddWithValue("var", req.VariantId);
            legs.Parameters.AddWithValue("wh", warehouseId);
            legs.Parameters.AddWithValue("sourceBin", req.SourceBinId);
            legs.Parameters.AddWithValue("negativeQty", -req.ReleasedQty);
            legs.Parameters.AddWithValue("uom", baseUomId);
            await legs.ExecuteNonQueryAsync(ct);
        }

        if (req.ReleaseDestination == "available")
        {
            await using var availableLeg = conn.CreateCommand();
            availableLeg.CommandText = """
                INSERT INTO erp_inventory.inventory_movement_leg
                    (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id,
                     bin_id, status_code, quantity_delta, base_uom_id)
                VALUES
                    (@tid, @cid, @mid, 2, @var, @wh, @targetBin, 'available', @positiveQty, @uom);
                """;
            availableLeg.Parameters.AddWithValue("tid", Ctx.TenantId);
            availableLeg.Parameters.AddWithValue("cid", Ctx.CompanyId);
            availableLeg.Parameters.AddWithValue("mid", movementId);
            availableLeg.Parameters.AddWithValue("var", req.VariantId);
            availableLeg.Parameters.AddWithValue("wh", warehouseId);
            availableLeg.Parameters.AddWithValue("targetBin", req.TargetBinId);
            availableLeg.Parameters.AddWithValue("positiveQty", req.ReleasedQty);
            availableLeg.Parameters.AddWithValue("uom", baseUomId);
            await availableLeg.ExecuteNonQueryAsync(ct);

            releaseLegs.Add(new
            {
                variantId = req.VariantId,
                warehouseId,
                binId = req.TargetBinId,
                stockStatus = "available",
                quantityDelta = req.ReleasedQty,
                unitOfMeasureId = baseUomId,
            });
        }

        var releaseFeedPayload = JsonSerializer.Serialize(new
        {
            movementId,
            movementType = "status_transfer",
            legs = releaseLegs,
            occurredAt,
            postedAt = occurredAt,
        });
        await CompanyChangeLogWriter.AppendAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId,
            "inventory_movement", movementId, 1, "upsert", releaseFeedPayload, ct);

        await tx.CommitAsync(ct);
        return releaseId;
    }

    // ── Supplier Invoices & 3-Way Matching ──────────────────────────────────────

    public async Task<IReadOnlyList<SupplierInvoiceSummary>> ListSupplierInvoicesAsync(Guid? supplierId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT si.invoice_id, si.invoice_number, si.supplier_id, s.name,
                   si.purchase_order_id, po.po_number, si.grn_id, g.grn_number,
                   si.invoice_date, si.due_date, si.currency_code, si.subtotal,
                   si.tax_amount, si.total_amount, si.matching_status, si.status,
                   si.payment_status, si.paid_amount, si.variance_amount, si.variance_reason,
                   si.gl_journal_id, si.approved_by, si.approved_at
            FROM erp_purchasing.supplier_invoice si
            JOIN erp_purchasing.supplier s ON s.tenant_id = si.tenant_id AND s.company_id = si.company_id AND s.supplier_id = si.supplier_id
            LEFT JOIN erp_purchasing.purchase_order po ON po.tenant_id = si.tenant_id AND po.company_id = si.company_id AND po.purchase_order_id = si.purchase_order_id
            LEFT JOIN erp_purchasing.goods_received_note g ON g.tenant_id = si.tenant_id AND g.company_id = si.company_id AND g.grn_id = si.grn_id
            WHERE (@sup::uuid IS NULL OR si.supplier_id = @sup::uuid)
            ORDER BY si.created_at DESC;
            """;
        cmd.Parameters.Add("sup", NpgsqlTypes.NpgsqlDbType.Uuid).Value = supplierId.HasValue ? supplierId.Value : DBNull.Value;

        var invoices = new List<(SupplierInvoiceSummary Header, Guid Id)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                invoices.Add((new SupplierInvoiceSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    DateOnly.FromDateTime(reader.GetDateTime(8)),
                    DateOnly.FromDateTime(reader.GetDateTime(9)),
                    reader.GetString(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetString(16),
                    reader.GetDecimal(17),
                    reader.GetDecimal(18),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.IsDBNull(20) ? null : reader.GetGuid(20),
                    reader.IsDBNull(21) ? null : reader.GetGuid(21),
                    reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset>(22),
                    []), reader.GetGuid(0)));
            }
        }

        if (invoices.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var invIds = invoices.Select(i => i.Id).ToArray();
        await using var lCmd = conn.CreateCommand();
        lCmd.CommandText = """
            SELECT sil.invoice_id, sil.invoice_line_id, sil.line_number, sil.po_line_number, sil.grn_line_number,
                   sil.variant_id, v.sku, sil.invoiced_qty, sil.unit_price, sil.line_total,
                   sil.matched_qty, sil.price_variance, sil.qty_variance, sil.matching_status
            FROM erp_purchasing.supplier_invoice_line sil
            JOIN erp_inventory.product_variant v ON v.tenant_id = sil.tenant_id AND v.variant_id = sil.variant_id
            WHERE sil.invoice_id = ANY(@ids)
            ORDER BY sil.invoice_id, sil.line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", invIds);

        var linesMap = new Dictionary<Guid, List<SupplierInvoiceLineSummary>>();
        await using (var lReader = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                var id = lReader.GetGuid(0);
                if (!linesMap.TryGetValue(id, out var list)) { list = []; linesMap[id] = list; }
                list.Add(new SupplierInvoiceLineSummary(
                    lReader.GetGuid(1),
                    lReader.GetInt16(2),
                    lReader.IsDBNull(3) ? null : lReader.GetInt16(3),
                    lReader.IsDBNull(4) ? null : lReader.GetInt16(4),
                    lReader.GetGuid(5),
                    lReader.GetString(6),
                    lReader.GetDecimal(7),
                    lReader.GetDecimal(8),
                    lReader.GetDecimal(9),
                    lReader.GetDecimal(10),
                    lReader.GetDecimal(11),
                    lReader.GetDecimal(12),
                    lReader.GetString(13)));
            }
        }

        await tx.CommitAsync(ct);
        return invoices.Select(i => i.Header with
        {
            Lines = linesMap.TryGetValue(i.Id, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<SupplierInvoiceSummary?> GetSupplierInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var all = await ListSupplierInvoicesAsync(null, ct);
        return all.FirstOrDefault(i => i.InvoiceId == invoiceId);
    }

    public async Task<Guid> CreateSupplierInvoiceAsync(CreateSupplierInvoiceRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        // Check for duplicate invoice number for the same supplier
        cmd.CommandText = """
            SELECT 1 FROM erp_purchasing.supplier_invoice
            WHERE tenant_id = @tid AND company_id = @cid AND supplier_id = @sup AND invoice_number = @no;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("sup", req.SupplierId);
        cmd.Parameters.AddWithValue("no", req.InvoiceNumber.Trim());
        if (await cmd.ExecuteScalarAsync(ct) is not null)
        {
            throw new InvalidOperationException($"DUPLICATE_INVOICE_NUMBER: Supplier invoice {req.InvoiceNumber} already exists for this supplier.");
        }

        var subtotal = req.Lines.Sum(l => l.InvoicedQty * l.UnitPrice);
        var total = subtotal + req.TaxAmount;
        var invoiceCurrency = string.IsNullOrWhiteSpace(req.CurrencyCode)
            ? await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct)
            : req.CurrencyCode.Trim().ToUpperInvariant();

        var invoiceId = Guid.NewGuid();
        await using var insCmd = conn.CreateCommand();
        insCmd.CommandText = """
            INSERT INTO erp_purchasing.supplier_invoice
                (tenant_id, company_id, invoice_id, invoice_number, supplier_id,
                 purchase_order_id, grn_id, invoice_date, due_date, currency_code,
                 subtotal, tax_amount, total_amount, matching_status, status, payment_status,
                 created_by)
            VALUES
                (@tid, @cid, @id, @no, @sup,
                 @po, @grn, @idate, @ddate, @cur,
                 @sub, @tax, @tot, 'unmatched', 'draft', 'unpaid',
                 @by);
            """;
        insCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        insCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        insCmd.Parameters.AddWithValue("id", invoiceId);
        insCmd.Parameters.AddWithValue("no", req.InvoiceNumber.Trim());
        insCmd.Parameters.AddWithValue("sup", req.SupplierId);
        insCmd.Parameters.AddWithValue("po", req.PurchaseOrderId.HasValue ? (object)req.PurchaseOrderId.Value : DBNull.Value);
        insCmd.Parameters.AddWithValue("grn", req.GrnId.HasValue ? (object)req.GrnId.Value : DBNull.Value);
        insCmd.Parameters.AddWithValue("idate", req.InvoiceDate.ToDateTime(TimeOnly.MinValue));
        insCmd.Parameters.AddWithValue("ddate", req.DueDate.ToDateTime(TimeOnly.MinValue));
        insCmd.Parameters.AddWithValue("cur", invoiceCurrency);
        insCmd.Parameters.AddWithValue("sub", subtotal);
        insCmd.Parameters.AddWithValue("tax", req.TaxAmount);
        insCmd.Parameters.AddWithValue("tot", total);
        insCmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? Ctx.UserId : createdBy);
        await insCmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < req.Lines.Count; i++)
        {
            var l = req.Lines[i];
            var lineId = Guid.NewGuid();
            var lineTot = l.InvoicedQty * l.UnitPrice;
            await using var lineCmd = conn.CreateCommand();
            lineCmd.CommandText = """
                INSERT INTO erp_purchasing.supplier_invoice_line
                    (tenant_id, company_id, invoice_line_id, invoice_id, line_number,
                     po_line_number, grn_line_number, variant_id, invoiced_qty, unit_price, line_total)
                VALUES
                    (@tid, @cid, @lid, @id, @ln,
                     @poln, @gln, @var, @qty, @price, @tot);
                """;
            lineCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lineCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lineCmd.Parameters.AddWithValue("lid", lineId);
            lineCmd.Parameters.AddWithValue("id", invoiceId);
            lineCmd.Parameters.AddWithValue("ln", i + 1);
            lineCmd.Parameters.AddWithValue("poln", l.PoLineNumber.HasValue ? (object)l.PoLineNumber.Value : DBNull.Value);
            lineCmd.Parameters.AddWithValue("gln", l.GrnLineNumber.HasValue ? (object)l.GrnLineNumber.Value : DBNull.Value);
            lineCmd.Parameters.AddWithValue("var", l.VariantId);
            lineCmd.Parameters.AddWithValue("qty", l.InvoicedQty);
            lineCmd.Parameters.AddWithValue("price", l.UnitPrice);
            lineCmd.Parameters.AddWithValue("tot", lineTot);
            await lineCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return invoiceId;
    }

    public async Task<ThreeWayMatchEvaluation> MatchSupplierInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await GetSupplierInvoiceAsync(invoiceId, ct);
        if (invoice is null) throw new InvalidOperationException("INVOICE_NOT_FOUND");

        PurchaseOrderSummary? po = null;
        if (invoice.PurchaseOrderId.HasValue)
        {
            po = await GetPurchaseOrderAsync(invoice.PurchaseOrderId.Value, ct);
        }

        GrnSummary? grn = null;
        if (invoice.GrnId.HasValue)
        {
            grn = await GetGrnAsync(invoice.GrnId.Value, ct);
        }

        var policy = await GetTolerancePolicyAsync(ct);

        var invoiceLinesReq = invoice.Lines.Select(l => new CreateSupplierInvoiceLineRequest(
            l.LineNumber,
            l.PoLineNumber,
            l.GrnLineNumber,
            l.VariantId,
            l.InvoicedQty,
            l.UnitPrice)).ToList();

        var evaluation = ThreeWayMatchingEngine.Evaluate(
            po,
            grn,
            invoiceLinesReq,
            policy.PriceTolerancePercent,
            policy.QuantityTolerancePercent,
            policy.AutoApproveWithinTolerance);

        // Update invoice and line records with match result
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        var headerMatchingStatus = evaluation.OverallStatus switch
        {
            "matched" => "matched",
            "matched_with_variance" => "matched",
            "variance_exceeded" => "variance_exceeded",
            _ => "unmatched"
        };

        cmd.CommandText = """
            UPDATE erp_purchasing.supplier_invoice
            SET matching_status = @mstatus,
                variance_amount = @var,
                variance_reason = @reason,
                status = CASE
                    WHEN @isMatched = true AND status = 'draft' THEN 'approved'
                    WHEN @mstatus = 'variance_exceeded' THEN 'on_hold'
                    ELSE status
                END,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND invoice_id = @id;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", invoiceId);
        cmd.Parameters.AddWithValue("mstatus", headerMatchingStatus);
        cmd.Parameters.AddWithValue("var", evaluation.TotalNetVariance);
        cmd.Parameters.AddWithValue("reason", evaluation.Discrepancies.Count > 0 ? string.Join(" | ", evaluation.Discrepancies) : DBNull.Value);
        cmd.Parameters.AddWithValue("isMatched", evaluation.IsMatched);
        await cmd.ExecuteNonQueryAsync(ct);

        // Update lines
        foreach (var lr in evaluation.LineResults)
        {
            var lineMatchingStatus = lr.Status switch
            {
                ThreeWayMatchLineStatus.ExactMatch => "matched",
                ThreeWayMatchLineStatus.WithinTolerance => "matched",
                ThreeWayMatchLineStatus.Unmatched => "unmatched",
                _ => "variance_exceeded"
            };

            await using var lCmd = conn.CreateCommand();
            lCmd.CommandText = """
                UPDATE erp_purchasing.supplier_invoice_line
                SET matched_qty = @mqty,
                    price_variance = @pvar,
                    qty_variance = @qvar,
                    matching_status = @mstatus
                WHERE tenant_id = @tid AND company_id = @cid AND invoice_id = @id AND line_number = @ln;
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("id", invoiceId);
            lCmd.Parameters.AddWithValue("ln", lr.LineNumber);
            lCmd.Parameters.AddWithValue("mqty", lr.ReceivedQty);
            lCmd.Parameters.AddWithValue("pvar", lr.PriceVariance);
            lCmd.Parameters.AddWithValue("qvar", lr.QuantityVariance);
            lCmd.Parameters.AddWithValue("mstatus", lineMatchingStatus);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return evaluation;
    }

    public async Task<bool> ApproveSupplierInvoiceAsync(
        Guid invoiceId,
        bool forceApprovalIfDisputed,
        string? resolutionNote,
        Guid approvedBy,
        CancellationToken ct)
    {
        var invoice = await GetSupplierInvoiceAsync(invoiceId, ct);
        if (invoice is null) throw new InvalidOperationException("INVOICE_NOT_FOUND");
        if (invoice.Status == "approved" || invoice.Status == "paid") return true; // Idempotent

        if (invoice.MatchingStatus == "variance_exceeded" && !forceApprovalIfDisputed)
        {
            throw new InvalidOperationException("INVOICE_VARIANCE_EXCEEDED: Cannot approve disputed invoice without force authorization.");
        }

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetRls(conn.CreateCommand(), Ctx);

        Guid? varianceJournalId = null;

        // CRITICAL ACCOUNTING INVARIANT:
        // GRN outbox event already posted:
        // Dr 1200 Inventory Asset for GRN valuation
        // Cr 2010 Accounts Payable for GRN valuation
        // Therefore, we DO NOT post Dr 1200 / Cr 2010 again!
        // ONLY if there is a price variance between Invoice and GRN do we post a variance journal adjusting AP 2010 vs COGS 5010!
        if (invoice.VarianceAmount != 0m)
        {
            // Fetch open fiscal period
            Guid activePeriodId;
            await using (var pCmd = conn.CreateCommand())
            {
                pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1";
                pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                var pRes = await pCmd.ExecuteScalarAsync(ct);
                if (pRes is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post variance journal without open period.");
                activePeriodId = pid;
            }

            // Fetch accounts 2010 and 5010
            Guid apAccountId = Guid.Empty;
            Guid cogsAccountId = Guid.Empty;
            await using (var accCmd = conn.CreateCommand())
            {
                accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('2010', '5010')";
                accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                await using var accR = await accCmd.ExecuteReaderAsync(ct);
                while (await accR.ReadAsync(ct))
                {
                    var id = accR.GetGuid(0);
                    var code = accR.GetString(1);
                    if (code == "2010") apAccountId = id;
                    else if (code == "5010") cogsAccountId = id;
                }
            }

            if (apAccountId != Guid.Empty && cogsAccountId != Guid.Empty)
            {
                var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
                    conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
                varianceJournalId = Guid.NewGuid();
                var jrnNo = $"JRN-INV-VAR-{invoice.InvoiceNumber}";
                var idempotencyKey = $"inv-var-{invoice.InvoiceId}";

                await using (var jCmd = conn.CreateCommand())
                {
                    jCmd.CommandText = """
                        INSERT INTO finance.journals
                            (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                             functional_currency, source_module, source_type, source_id, idempotency_key,
                             status, created_by, posted_by, posted_at)
                        VALUES
                            (@id, @tid, @cid, @no, @date, @pid, @desc,
                             @funcCurr, 'purchasing', 'supplier_invoice', @sid, @key,
                             'posted', @by, @by, now())
                        ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                        """;
                    jCmd.Parameters.AddWithValue("id", varianceJournalId);
                    jCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                    jCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    jCmd.Parameters.AddWithValue("no", jrnNo);
                    jCmd.Parameters.AddWithValue("date", invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue));
                    jCmd.Parameters.AddWithValue("pid", activePeriodId);
                    jCmd.Parameters.AddWithValue("desc", $"Invoice Price Variance for {invoice.InvoiceNumber}");
                    jCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
                    jCmd.Parameters.AddWithValue("sid", invoice.InvoiceId.ToString());
                    jCmd.Parameters.AddWithValue("key", idempotencyKey);
                    jCmd.Parameters.AddWithValue("by", approvedBy == Guid.Empty ? Ctx.UserId : approvedBy);
                    await jCmd.ExecuteNonQueryAsync(ct);
                }

                var absVar = Math.Abs(invoice.VarianceAmount);
                if (invoice.VarianceAmount > 0m)
                {
                    // Invoice price was higher than GRN: increase AP (Cr 2010), charge expense (Dr 5010)
                    await using var l1 = conn.CreateCommand();
                    l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, @desc, @cur, @amt, 1.0, @amt, 0);";
                    l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", varianceJournalId); l1.Parameters.AddWithValue("aid", cogsAccountId); l1.Parameters.AddWithValue("desc", "Purchase Price Variance"); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", absVar);
                    await l1.ExecuteNonQueryAsync(ct);

                    await using var l2 = conn.CreateCommand();
                    l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, @desc, @cur, @amt, 1.0, 0, @amt);";
                    l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", varianceJournalId); l2.Parameters.AddWithValue("aid", apAccountId); l2.Parameters.AddWithValue("desc", "Accounts Payable - Price Adjustment"); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", absVar);
                    await l2.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    // Invoice price was lower than GRN: decrease AP (Dr 2010), credit expense (Cr 5010)
                    await using var l1 = conn.CreateCommand();
                    l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, @desc, @cur, @amt, 1.0, @amt, 0);";
                    l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", varianceJournalId); l1.Parameters.AddWithValue("aid", apAccountId); l1.Parameters.AddWithValue("desc", "Accounts Payable - Credit Adjustment"); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", absVar);
                    await l1.ExecuteNonQueryAsync(ct);

                    await using var l2 = conn.CreateCommand();
                    l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, @desc, @cur, @amt, 1.0, 0, @amt);";
                    l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", varianceJournalId); l2.Parameters.AddWithValue("aid", cogsAccountId); l2.Parameters.AddWithValue("desc", "Purchase Price Variance Recovery"); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", absVar);
                    await l2.ExecuteNonQueryAsync(ct);
                }
            }
        }

        // Update invoice status to approved
        await using var updCmd = conn.CreateCommand();
        updCmd.CommandText = """
            UPDATE erp_purchasing.supplier_invoice
            SET status = 'approved',
                matching_status = CASE WHEN matching_status = 'variance_exceeded' THEN 'matched' ELSE matching_status END,
                approved_by = @by,
                approved_at = now(),
                gl_journal_id = COALESCE(@jid, gl_journal_id),
                variance_reason = CASE WHEN @note IS NOT NULL THEN COALESCE(variance_reason || ' | ', '') || @note ELSE variance_reason END,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND invoice_id = @id;
            """;
        updCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        updCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        updCmd.Parameters.AddWithValue("id", invoiceId);
        updCmd.Parameters.AddWithValue("by", approvedBy == Guid.Empty ? Ctx.UserId : approvedBy);
        updCmd.Parameters.AddWithValue("jid", varianceJournalId.HasValue ? (object)varianceJournalId.Value : DBNull.Value);
        updCmd.Parameters.AddWithValue("note", (object?)resolutionNote ?? DBNull.Value);
        await updCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return true;
    }

    // ── Purchase Returns & Supplier Debit Notes ──────────────────────────────────

    public async Task<IReadOnlyList<PurchaseReturnSummary>> ListPurchaseReturnsAsync(Guid? supplierId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT pr.return_id, pr.return_number, pr.supplier_id, s.name,
                   pr.purchase_order_id, po.po_number, pr.grn_id, g.grn_number,
                   pr.supplier_invoice_id, si.invoice_number, pr.status, pr.reason,
                   pr.authorized_by, pr.authorized_at, pr.created_at
            FROM erp_purchasing.purchase_return_record pr
            JOIN erp_purchasing.supplier s ON s.tenant_id = pr.tenant_id AND s.company_id = pr.company_id AND s.supplier_id = pr.supplier_id
            LEFT JOIN erp_purchasing.purchase_order po ON po.tenant_id = pr.tenant_id AND po.company_id = pr.company_id AND po.purchase_order_id = pr.purchase_order_id
            LEFT JOIN erp_purchasing.goods_received_note g ON g.tenant_id = pr.tenant_id AND g.company_id = pr.company_id AND g.grn_id = pr.grn_id
            LEFT JOIN erp_purchasing.supplier_invoice si ON si.tenant_id = pr.tenant_id AND si.company_id = pr.company_id AND si.invoice_id = pr.supplier_invoice_id
            WHERE (@sup::uuid IS NULL OR pr.supplier_id = @sup::uuid)
            ORDER BY pr.created_at DESC;
            """;
        cmd.Parameters.Add("sup", NpgsqlTypes.NpgsqlDbType.Uuid).Value = supplierId.HasValue ? supplierId.Value : DBNull.Value;

        var returns = new List<(PurchaseReturnSummary Header, Guid Id)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                returns.Add((new PurchaseReturnSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                    reader.GetFieldValue<DateTimeOffset>(14),
                    []), reader.GetGuid(0)));
            }
        }

        if (returns.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        var retIds = returns.Select(r => r.Id).ToArray();
        await using var lCmd = conn.CreateCommand();
        lCmd.CommandText = """
            SELECT prl.return_id, prl.return_line_id, prl.line_number, prl.variant_id, v.sku,
                   prl.warehouse_id, w.name, prl.bin_id, b.code, prl.return_qty,
                   prl.unit_cost, prl.total_cost, prl.from_stock_status, prl.restock_reason
            FROM erp_purchasing.purchase_return_line_record prl
            JOIN erp_inventory.product_variant v ON v.tenant_id = prl.tenant_id AND v.variant_id = prl.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = prl.tenant_id AND w.company_id = prl.company_id AND w.warehouse_id = prl.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = prl.tenant_id AND b.company_id = prl.company_id AND b.bin_id = prl.bin_id
            WHERE prl.return_id = ANY(@ids)
            ORDER BY prl.return_id, prl.line_number;
            """;
        lCmd.Parameters.AddWithValue("ids", retIds);

        var linesMap = new Dictionary<Guid, List<PurchaseReturnLineSummary>>();
        await using (var lReader = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                var id = lReader.GetGuid(0);
                if (!linesMap.TryGetValue(id, out var list)) { list = []; linesMap[id] = list; }
                list.Add(new PurchaseReturnLineSummary(
                    lReader.GetGuid(1),
                    lReader.GetInt16(2),
                    lReader.GetGuid(3),
                    lReader.GetString(4),
                    lReader.GetGuid(5),
                    lReader.GetString(6),
                    lReader.GetGuid(7),
                    lReader.GetString(8),
                    lReader.GetDecimal(9),
                    lReader.GetDecimal(10),
                    lReader.GetDecimal(11),
                    lReader.GetString(12),
                    lReader.IsDBNull(13) ? null : lReader.GetString(13)));
            }
        }

        await tx.CommitAsync(ct);
        return returns.Select(r => r.Header with
        {
            Lines = linesMap.TryGetValue(r.Id, out var ln) ? ln : []
        }).ToList();
    }

    public async Task<PurchaseReturnSummary?> GetPurchaseReturnAsync(Guid returnId, CancellationToken ct)
    {
        var all = await ListPurchaseReturnsAsync(null, ct);
        return all.FirstOrDefault(r => r.ReturnId == returnId);
    }

    public async Task<Guid> CreatePurchaseReturnAsync(CreatePurchaseReturnRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        var returnId = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO erp_purchasing.purchase_return_record
                (tenant_id, company_id, return_id, return_number, supplier_id,
                 purchase_order_id, grn_id, supplier_invoice_id, status, reason, created_by)
            VALUES
                (@tid, @cid, @id, @no, @sup,
                 @po, @grn, @inv, 'draft', @reason, @by);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", returnId);
        cmd.Parameters.AddWithValue("no", req.ReturnNumber.Trim());
        cmd.Parameters.AddWithValue("sup", req.SupplierId);
        cmd.Parameters.AddWithValue("po", req.PurchaseOrderId.HasValue ? (object)req.PurchaseOrderId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("grn", req.GrnId.HasValue ? (object)req.GrnId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("inv", req.SupplierInvoiceId.HasValue ? (object)req.SupplierInvoiceId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("reason", req.Reason.Trim());
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? Ctx.UserId : createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        for (var i = 0; i < req.Lines.Count; i++)
        {
            var l = req.Lines[i];
            var lineId = Guid.NewGuid();
            var totCost = l.ReturnQty * l.UnitCost;
            await using var lCmd = conn.CreateCommand();
            lCmd.CommandText = """
                INSERT INTO erp_purchasing.purchase_return_line_record
                    (tenant_id, company_id, return_line_id, return_id, line_number,
                     variant_id, warehouse_id, bin_id, return_qty, unit_cost, total_cost,
                     from_stock_status, restock_reason)
                VALUES
                    (@tid, @cid, @lid, @id, @ln,
                     @var, @wh, @bin, @qty, @cost, @tot,
                     @status, @reason);
                """;
            lCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            lCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            lCmd.Parameters.AddWithValue("lid", lineId);
            lCmd.Parameters.AddWithValue("id", returnId);
            lCmd.Parameters.AddWithValue("ln", i + 1);
            lCmd.Parameters.AddWithValue("var", l.VariantId);
            lCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            lCmd.Parameters.AddWithValue("bin", l.BinId);
            lCmd.Parameters.AddWithValue("qty", l.ReturnQty);
            lCmd.Parameters.AddWithValue("cost", l.UnitCost);
            lCmd.Parameters.AddWithValue("tot", totCost);
            lCmd.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(l.FromStockStatus) ? "available" : l.FromStockStatus);
            lCmd.Parameters.AddWithValue("reason", (object?)l.RestockReason ?? DBNull.Value);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return returnId;
    }

    public async Task<bool> ApprovePurchaseReturnAsync(Guid returnId, Guid authorizedBy, CancellationToken ct)
    {
        var ret = await GetPurchaseReturnAsync(returnId, ct);
        if (ret is null) throw new InvalidOperationException("PURCHASE_RETURN_NOT_FOUND");
        if (ret.Status == "approved" || ret.Status == "completed") return true;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetRls(conn.CreateCommand(), Ctx);

        var movementId = Guid.NewGuid();
        var clientTxId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var feedLegs = new List<object>();

        // Record inventory issue movement
        await using (var movCmd = conn.CreateCommand())
        {
            movCmd.CommandText = """
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type,
                     source_document_type, source_document_id, occurred_at, created_by, note)
                VALUES
                    (@tid, @cid, @mid, @txid, 'issue',
                     'purchase_return', @rid, @occurred, @by, @note);
                """;
            movCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            movCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            movCmd.Parameters.AddWithValue("mid", movementId);
            movCmd.Parameters.AddWithValue("txid", clientTxId);
            movCmd.Parameters.AddWithValue("rid", returnId);
            movCmd.Parameters.AddWithValue("occurred", occurredAt);
            movCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            movCmd.Parameters.AddWithValue("note", $"Purchase Return {ret.ReturnNumber}");
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        // Process lines: reduce stock balance and consume/close originating FIFO cost layer
        for (var i = 0; i < ret.Lines.Count; i++)
        {
            var l = ret.Lines[i];

            // Verify stock balance
            await using var chkCmd = conn.CreateCommand();
            chkCmd.CommandText = """
                SELECT on_hand_qty FROM erp_inventory.stock_balance
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                  AND warehouse_id = @wh AND bin_id = @bin AND status_code = @status
                FOR UPDATE;
                """;
            chkCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            chkCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            chkCmd.Parameters.AddWithValue("var", l.VariantId);
            chkCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            chkCmd.Parameters.AddWithValue("bin", l.BinId);
            chkCmd.Parameters.AddWithValue("status", l.FromStockStatus);

            var onHand = (decimal)(await chkCmd.ExecuteScalarAsync(ct) ?? 0m);
            if (onHand < l.ReturnQty)
            {
                throw new InvalidOperationException($"INSUFFICIENT_STOCK_FOR_RETURN: Available {onHand}, requested return {l.ReturnQty}");
            }

            // Deduct balance
            await using var decCmd = conn.CreateCommand();
            decCmd.CommandText = """
                UPDATE erp_inventory.stock_balance
                SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                  AND warehouse_id = @wh AND bin_id = @bin AND status_code = @status;
                """;
            decCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            decCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            decCmd.Parameters.AddWithValue("var", l.VariantId);
            decCmd.Parameters.AddWithValue("wh", l.WarehouseId);
            decCmd.Parameters.AddWithValue("bin", l.BinId);
            decCmd.Parameters.AddWithValue("status", l.FromStockStatus);
            decCmd.Parameters.AddWithValue("qty", l.ReturnQty);
            await decCmd.ExecuteNonQueryAsync(ct);

            Guid baseUomId;
            await using (var getUom = conn.CreateCommand())
            {
                getUom.CommandText = "SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @var;";
                getUom.Parameters.AddWithValue("tid", Ctx.TenantId);
                getUom.Parameters.AddWithValue("var", l.VariantId);
                baseUomId = (Guid)(await getUom.ExecuteScalarAsync(ct)
                    ?? throw new InvalidOperationException("PRODUCT_VARIANT_NOT_FOUND"));
            }

            await using (var leg = conn.CreateCommand())
            {
                leg.CommandText = """
                    INSERT INTO erp_inventory.inventory_movement_leg
                        (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id,
                         bin_id, status_code, quantity_delta, base_uom_id)
                    VALUES
                        (@tid, @cid, @mid, @ln, @var, @wh, @bin, @status, @delta, @uom);
                    """;
                leg.Parameters.AddWithValue("tid", Ctx.TenantId);
                leg.Parameters.AddWithValue("cid", Ctx.CompanyId);
                leg.Parameters.AddWithValue("mid", movementId);
                leg.Parameters.AddWithValue("ln", i + 1);
                leg.Parameters.AddWithValue("var", l.VariantId);
                leg.Parameters.AddWithValue("wh", l.WarehouseId);
                leg.Parameters.AddWithValue("bin", l.BinId);
                leg.Parameters.AddWithValue("status", l.FromStockStatus);
                leg.Parameters.AddWithValue("delta", -l.ReturnQty);
                leg.Parameters.AddWithValue("uom", baseUomId);
                await leg.ExecuteNonQueryAsync(ct);
            }

            feedLegs.Add(new
            {
                variantId = l.VariantId,
                warehouseId = l.WarehouseId,
                binId = l.BinId,
                stockStatus = l.FromStockStatus,
                quantityDelta = -l.ReturnQty,
                unitOfMeasureId = baseUomId,
            });

            // If from available, consume oldest open FIFO layer
            if (l.FromStockStatus == "available")
            {
                var remainingToDeduct = l.ReturnQty;
                await using var layerQuery = conn.CreateCommand();
                layerQuery.CommandText = """
                    SELECT layer_id, remaining_qty
                    FROM erp_inventory.inventory_cost_layer
                    WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var
                      AND warehouse_id = @wh AND bin_id = @bin AND remaining_qty > 0
                    ORDER BY received_at ASC, created_at ASC
                    FOR UPDATE;
                    """;
                layerQuery.Parameters.AddWithValue("tid", Ctx.TenantId);
                layerQuery.Parameters.AddWithValue("cid", Ctx.CompanyId);
                layerQuery.Parameters.AddWithValue("var", l.VariantId);
                layerQuery.Parameters.AddWithValue("wh", l.WarehouseId);
                layerQuery.Parameters.AddWithValue("bin", l.BinId);

                var openLayers = new List<(Guid LayerId, decimal RemQty)>();
                await using (var r = await layerQuery.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        openLayers.Add((r.GetGuid(0), r.GetDecimal(1)));
                    }
                }

                foreach (var layer in openLayers)
                {
                    if (remainingToDeduct <= 0m) break;
                    var take = Math.Min(layer.RemQty, remainingToDeduct);
                    remainingToDeduct -= take;

                    await using var updLayer = conn.CreateCommand();
                    updLayer.CommandText = """
                        UPDATE erp_inventory.inventory_cost_layer
                        SET remaining_qty = remaining_qty - @take,
                            updated_at = now()
                        WHERE tenant_id = @tid AND company_id = @cid AND layer_id = @lid;
                        """;
                    updLayer.Parameters.AddWithValue("tid", Ctx.TenantId);
                    updLayer.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    updLayer.Parameters.AddWithValue("take", take);
                    updLayer.Parameters.AddWithValue("lid", layer.LayerId);
                    await updLayer.ExecuteNonQueryAsync(ct);
                }
            }
        }

        // Mark return as approved/completed
        await using var retUpd = conn.CreateCommand();
        retUpd.CommandText = """
            UPDATE erp_purchasing.purchase_return_record
            SET status = 'approved',
                authorized_by = @by,
                authorized_at = now(),
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND return_id = @id;
            """;
        retUpd.Parameters.AddWithValue("tid", Ctx.TenantId);
        retUpd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        retUpd.Parameters.AddWithValue("id", returnId);
        retUpd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
        await retUpd.ExecuteNonQueryAsync(ct);

        var purchaseReturnFeedPayload = JsonSerializer.Serialize(new
        {
            movementId,
            movementType = "issue",
            legs = feedLegs,
            occurredAt,
            postedAt = occurredAt,
        });
        await CompanyChangeLogWriter.AppendAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId,
            "inventory_movement", movementId, 1, "upsert", purchaseReturnFeedPayload, ct);

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SupplierDebitNoteSummary>> ListSupplierDebitNotesAsync(Guid? supplierId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT dn.debit_note_id, dn.debit_note_number, dn.supplier_id, s.name,
                   dn.purchase_return_id, pr.return_number, dn.supplier_invoice_id, si.invoice_number,
                   dn.debit_note_date, dn.currency_code, dn.amount, dn.status,
                   dn.gl_journal_id, dn.authorized_by, dn.authorized_at, dn.notes
            FROM erp_purchasing.supplier_debit_note dn
            JOIN erp_purchasing.supplier s ON s.tenant_id = dn.tenant_id AND s.company_id = dn.company_id AND s.supplier_id = dn.supplier_id
            LEFT JOIN erp_purchasing.purchase_return_record pr ON pr.tenant_id = dn.tenant_id AND pr.company_id = dn.company_id AND pr.return_id = dn.purchase_return_id
            LEFT JOIN erp_purchasing.supplier_invoice si ON si.tenant_id = dn.tenant_id AND si.company_id = dn.company_id AND si.invoice_id = dn.supplier_invoice_id
            WHERE (@sup::uuid IS NULL OR dn.supplier_id = @sup::uuid)
            ORDER BY dn.debit_note_date DESC, dn.created_at DESC;
            """;
        cmd.Parameters.Add("sup", NpgsqlTypes.NpgsqlDbType.Uuid).Value = supplierId.HasValue ? supplierId.Value : DBNull.Value;

        var list = new List<SupplierDebitNoteSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SupplierDebitNoteSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.GetString(9),
                reader.GetDecimal(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<Guid> CreateSupplierDebitNoteAsync(CreateSupplierDebitNoteRequest req, Guid createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var dnId = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO erp_purchasing.supplier_debit_note
                (tenant_id, company_id, debit_note_id, debit_note_number, supplier_id,
                 purchase_return_id, supplier_invoice_id, debit_note_date, currency_code,
                 amount, status, notes, created_by)
            VALUES
                (@tid, @cid, @id, @no, @sup,
                 @pr, @inv, @date, @cur,
                 @amt, 'draft', @notes, @by);
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", dnId);
        cmd.Parameters.AddWithValue("no", req.DebitNoteNumber.Trim());
        cmd.Parameters.AddWithValue("sup", req.SupplierId);
        cmd.Parameters.AddWithValue("pr", req.PurchaseReturnId.HasValue ? (object)req.PurchaseReturnId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("inv", req.SupplierInvoiceId.HasValue ? (object)req.SupplierInvoiceId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("date", req.DebitNoteDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("cur", functionalCurrency);
        cmd.Parameters.AddWithValue("amt", req.Amount);
        cmd.Parameters.AddWithValue("notes", (object?)req.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("by", createdBy == Guid.Empty ? Ctx.UserId : createdBy);
        await cmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return dnId;
    }

    public async Task<bool> ApproveSupplierDebitNoteAsync(Guid debitNoteId, Guid authorizedBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetRls(conn.CreateCommand(), Ctx);

        // Fetch debit note
        await using var qCmd = conn.CreateCommand();
        qCmd.CommandText = "SELECT debit_note_number, supplier_id, amount, debit_note_date, status FROM erp_purchasing.supplier_debit_note WHERE tenant_id = @tid AND company_id = @cid AND debit_note_id = @id FOR UPDATE";
        qCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        qCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        qCmd.Parameters.AddWithValue("id", debitNoteId);

        string dnNo; decimal amount; DateOnly dnDate; string status;
        await using (var r = await qCmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new InvalidOperationException("DEBIT_NOTE_NOT_FOUND");
            dnNo = r.GetString(0);
            amount = r.GetDecimal(2);
            dnDate = DateOnly.FromDateTime(r.GetDateTime(3));
            status = r.GetString(4);
        }

        if (status == "approved" || status == "applied") return true;

        // Post financial GL journal: Dr 2010 (Accounts Payable), Cr 1200 (Inventory Asset)
        Guid activePeriodId;
        await using (var pCmd = conn.CreateCommand())
        {
            pCmd.CommandText = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tid AND company_id = @cid AND status = 'open' ORDER BY starts_on ASC LIMIT 1";
            pCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            pCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            var pRes = await pCmd.ExecuteScalarAsync(ct);
            if (pRes is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post debit note journal without open period.");
            activePeriodId = pid;
        }

        Guid apAccountId = Guid.Empty;
        Guid invAccountId = Guid.Empty;
        await using (var accCmd = conn.CreateCommand())
        {
            accCmd.CommandText = "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tid AND company_id = @cid AND account_code IN ('2010', '1200')";
            accCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            accCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            await using var accR = await accCmd.ExecuteReaderAsync(ct);
            while (await accR.ReadAsync(ct))
            {
                var id = accR.GetGuid(0);
                var code = accR.GetString(1);
                if (code == "2010") apAccountId = id;
                else if (code == "1200") invAccountId = id;
            }
        }

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var journalId = Guid.NewGuid();
        var jrnNo = $"JRN-DN-{dnNo}";
        var idempotencyKey = $"dn-gl-{debitNoteId}";

        await using (var jCmd = conn.CreateCommand())
        {
            jCmd.CommandText = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                     functional_currency, source_module, source_type, source_id, idempotency_key,
                     status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @pid, @desc,
                     @funcCurr, 'purchasing', 'supplier_debit_note', @sid, @key,
                     'posted', @by, @by, now())
                ON CONFLICT (tenant_id, company_id, idempotency_key) DO NOTHING;
                """;
            jCmd.Parameters.AddWithValue("id", journalId);
            jCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            jCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            jCmd.Parameters.AddWithValue("no", jrnNo);
            jCmd.Parameters.AddWithValue("date", dnDate.ToDateTime(TimeOnly.MinValue));
            jCmd.Parameters.AddWithValue("pid", activePeriodId);
            jCmd.Parameters.AddWithValue("desc", $"Supplier Debit Note {dnNo}");
            jCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
            jCmd.Parameters.AddWithValue("sid", debitNoteId.ToString());
            jCmd.Parameters.AddWithValue("key", idempotencyKey);
            jCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
            await jCmd.ExecuteNonQueryAsync(ct);
        }

        // Line 1: Debit 2010 (Accounts Payable)
        await using (var l1 = conn.CreateCommand())
        {
            l1.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 1, @aid, @desc, @cur, @amt, 1.0, @amt, 0);";
            l1.Parameters.AddWithValue("tid", Ctx.TenantId); l1.Parameters.AddWithValue("cid", Ctx.CompanyId); l1.Parameters.AddWithValue("jid", journalId); l1.Parameters.AddWithValue("aid", apAccountId); l1.Parameters.AddWithValue("desc", $"Debit Note AP Recovery - {dnNo}"); l1.Parameters.AddWithValue("cur", functionalCurrency); l1.Parameters.AddWithValue("amt", amount);
            await l1.ExecuteNonQueryAsync(ct);
        }

        // Line 2: Credit 1200 (Merchandise Inventory Asset)
        await using (var l2 = conn.CreateCommand())
        {
            l2.CommandText = "INSERT INTO finance.journal_lines (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional) VALUES (@tid, @cid, @jid, 2, @aid, @desc, @cur, @amt, 1.0, 0, @amt);";
            l2.Parameters.AddWithValue("tid", Ctx.TenantId); l2.Parameters.AddWithValue("cid", Ctx.CompanyId); l2.Parameters.AddWithValue("jid", journalId); l2.Parameters.AddWithValue("aid", invAccountId); l2.Parameters.AddWithValue("desc", $"Inventory Asset Reversal - {dnNo}"); l2.Parameters.AddWithValue("cur", functionalCurrency); l2.Parameters.AddWithValue("amt", amount);
            await l2.ExecuteNonQueryAsync(ct);
        }

        // Update debit note record
        await using var updCmd = conn.CreateCommand();
        updCmd.CommandText = """
            UPDATE erp_purchasing.supplier_debit_note
            SET status = 'approved',
                gl_journal_id = @jid,
                authorized_by = @by,
                authorized_at = now(),
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND debit_note_id = @id;
            """;
        updCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        updCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        updCmd.Parameters.AddWithValue("id", debitNoteId);
        updCmd.Parameters.AddWithValue("jid", journalId);
        updCmd.Parameters.AddWithValue("by", authorizedBy == Guid.Empty ? Ctx.UserId : authorizedBy);
        await updCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return true;
    }

    // ── Supplier Balances & AP Status ───────────────────────────────────────────

    public async Task<IReadOnlyList<SupplierBalanceSummary>> ListSupplierBalancesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetRls(cmd, Ctx);

        cmd.CommandText = """
            SELECT s.supplier_id, s.supplier_code, s.name, s.currency_code,
                   COALESCE(inv.total_invoiced, 0) AS total_invoiced,
                   COALESCE(dn.total_debit_notes, 0) AS total_debit_notes,
                   COALESCE(inv.total_paid, 0) AS payments_allocated,
                   COALESCE(inv.invoice_count, 0) AS active_invoice_count,
                   COALESCE(dn.debit_note_count, 0) AS open_debit_notes_count
            FROM erp_purchasing.supplier s
            LEFT JOIN (
                SELECT supplier_id,
                       SUM(total_amount) AS total_invoiced,
                       SUM(paid_amount) AS total_paid,
                       COUNT(*) AS invoice_count
                FROM erp_purchasing.supplier_invoice
                WHERE tenant_id = @tid AND company_id = @cid AND status IN ('approved', 'paid')
                GROUP BY supplier_id
            ) inv ON inv.supplier_id = s.supplier_id
            LEFT JOIN (
                SELECT supplier_id,
                       SUM(amount) AS total_debit_notes,
                       COUNT(*) AS debit_note_count
                FROM erp_purchasing.supplier_debit_note
                WHERE tenant_id = @tid AND company_id = @cid AND status IN ('approved', 'applied')
                GROUP BY supplier_id
            ) dn ON dn.supplier_id = s.supplier_id
            WHERE s.tenant_id = @tid AND s.company_id = @cid
            ORDER BY s.name;
            """;
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<SupplierBalanceSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var cur = reader.GetString(3);
            var invoiced = reader.GetDecimal(4);
            var debitNotes = reader.GetDecimal(5);
            var paid = reader.GetDecimal(6);
            var invCount = Convert.ToInt32(reader.GetInt64(7));
            var dnCount = Convert.ToInt32(reader.GetInt64(8));

            var netPayable = invoiced - debitNotes;
            var outstanding = netPayable - paid;
            var paymentStatus = outstanding <= 0m && invoiced > 0m
                ? "paid"
                : (paid > 0m ? "partially_paid" : "unpaid");

            list.Add(new SupplierBalanceSummary(
                id, code, name, cur,
                invoiced, debitNotes, netPayable, paid, outstanding,
                paymentStatus, invCount, dnCount));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<SupplierBalanceSummary?> GetSupplierBalanceAsync(Guid supplierId, CancellationToken ct)
    {
        var all = await ListSupplierBalancesAsync(ct);
        return all.FirstOrDefault(s => s.SupplierId == supplierId);
    }
}
