using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Inventory;

public sealed class PostgresInventoryWorkflowRepository(NpgsqlDataSource dataSource)
    : IInventoryWorkflowRepository
{
    public async Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT category_id, code, name, parent_category_id FROM erp_inventory.product_category WHERE tenant_id = @tenantId ORDER BY code",
            db.Connection,
            db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        var items = new List<ProductCategorySummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ProductCategorySummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        }
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<ProductCategorySummary>> CreateCategoryAsync(
        Guid tenantId,
        CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        try
        {
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.product_category
                    (tenant_id, category_id, code, name, parent_category_id)
                VALUES
                    (@tenantId, @id, @code, @name, @parent)
                RETURNING category_id, code, name, parent_category_id
                """,
                db.Connection,
                db.Transaction);
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("code", request.Code!);
            command.Parameters.AddWithValue("name", request.Name!);
            command.Parameters.AddWithValue("parent", request.ParentCategoryId.HasValue ? (object)request.ParentCategoryId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var category = new ProductCategorySummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3));
            await reader.CloseAsync();
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<ProductCategorySummary>.Success(category);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<ProductCategorySummary>(db, "DUPLICATE_CATEGORY_CODE", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(
        Guid tenantId,
        Guid companyId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        const string movSql = """
            SELECT m.movement_id, m.movement_type, m.source_document_type, m.source_document_id,
                   m.occurred_at, m.note
            FROM erp_inventory.inventory_movement m
            WHERE m.tenant_id = @tenantId AND m.company_id = @companyId
            ORDER BY m.occurred_at DESC
            LIMIT @limit;
            """;

        var movements = new List<InventoryMovementSummary>();
        var movementIds = new List<Guid>();

        await using (var cmd = new NpgsqlCommand(movSql, db.Connection, db.Transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var mId = reader.GetGuid(0);
                movementIds.Add(mId);
                movements.Add(new InventoryMovementSummary(
                    mId,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    []));
            }
            await reader.CloseAsync();
        }

        if (movementIds.Count == 0)
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return [];
        }

        const string legsSql = """
            SELECT l.movement_id, l.leg_number, l.variant_id, v.sku,
                   l.warehouse_id, w.code, l.bin_id, b.code,
                   l.status_code, l.quantity_delta, l.base_uom_id, u.code
            FROM erp_inventory.inventory_movement_leg l
            JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
            JOIN erp_inventory.warehouse w ON w.tenant_id = l.tenant_id AND w.company_id = l.company_id AND w.warehouse_id = l.warehouse_id
            JOIN erp_inventory.stock_bin b ON b.tenant_id = l.tenant_id AND b.company_id = l.company_id AND b.bin_id = l.bin_id
            JOIN erp_inventory.unit_of_measure u ON u.tenant_id = l.tenant_id AND u.uom_id = l.base_uom_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.movement_id = ANY(@mIds)
            ORDER BY l.movement_id, l.leg_number;
            """;

        var legsMap = new Dictionary<Guid, List<InventoryMovementLegSummary>>();
        await using (var legCmd = new NpgsqlCommand(legsSql, db.Connection, db.Transaction))
        {
            legCmd.Parameters.AddWithValue("tenantId", tenantId);
            legCmd.Parameters.AddWithValue("companyId", companyId);
            legCmd.Parameters.AddWithValue("mIds", movementIds.ToArray());

            await using var legReader = await legCmd.ExecuteReaderAsync(cancellationToken);
            while (await legReader.ReadAsync(cancellationToken))
            {
                var mId = legReader.GetGuid(0);
                if (!legsMap.TryGetValue(mId, out var list))
                {
                    list = [];
                    legsMap[mId] = list;
                }
                list.Add(new InventoryMovementLegSummary(
                    legReader.GetInt32(1),
                    legReader.GetGuid(2),
                    legReader.GetString(3),
                    legReader.GetGuid(4),
                    legReader.GetString(5),
                    legReader.GetGuid(6),
                    legReader.GetString(7),
                    legReader.GetString(8),
                    legReader.GetDecimal(9),
                    legReader.GetGuid(10),
                    legReader.GetString(11)));
            }
            await legReader.CloseAsync();
        }

        await db.Transaction.CommitAsync(cancellationToken);

        return movements.Select(m => m with
        {
            Legs = legsMap.TryGetValue(m.MovementId, out var l) ? l : []
        }).ToList();
    }

    public async Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT uom_id, code, name, dimension, decimal_places FROM erp_inventory.unit_of_measure WHERE tenant_id = @tenantId ORDER BY code",
            db.Connection,
            db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        var items = new List<UnitOfMeasureSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadUnit(reader));
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(
        Guid tenantId,
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places) VALUES (@tenantId, @id, @code, @name, @dimension, @places) RETURNING uom_id, code, name, dimension, decimal_places",
                db.Connection,
                db.Transaction);
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("code", request.Code!);
            command.Parameters.AddWithValue("name", request.Name!);
            command.Parameters.AddWithValue("dimension", request.Dimension!);
            command.Parameters.AddWithValue("places", request.DecimalPlaces);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var unit = ReadUnit(reader);
            await reader.CloseAsync();
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<UnitOfMeasureSummary>.Success(unit);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<UnitOfMeasureSummary>(db, "DUPLICATE_UOM_CODE", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        await using var command = new NpgsqlCommand(VariantSelect + " WHERE variant.tenant_id = @tenantId ORDER BY variant.sku", db.Connection, db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        var items = new List<ProductVariantSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadVariant(reader));
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<ProductVariantSummary>> CreateVariantAsync(
        Guid tenantId,
        CreateProductVariantRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, null, cancellationToken);
        await using (var validate = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM erp_inventory.product AS product JOIN erp_inventory.unit_of_measure AS uom ON uom.tenant_id = product.tenant_id WHERE product.tenant_id = @tenantId AND product.product_id = @productId AND product.product_type = 'stock' AND product.is_active AND uom.uom_id = @uomId)",
            db.Connection,
            db.Transaction))
        {
            validate.Parameters.AddWithValue("tenantId", tenantId);
            validate.Parameters.AddWithValue("productId", request.ProductId);
            validate.Parameters.AddWithValue("uomId", request.BaseUnitOfMeasureId);
            if (await validate.ExecuteScalarAsync(cancellationToken) as bool? != true)
            {
                return await RollbackWithFailureAsync<ProductVariantSummary>(db, "INVALID_VARIANT_REFERENCE", cancellationToken);
            }
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, tracking_mode, costing_method) VALUES (@tenantId, @id, @productId, @sku, @name, @uomId, @tracking, @costing)",
                db.Connection,
                db.Transaction);
            insert.Parameters.AddWithValue("tenantId", tenantId);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("productId", request.ProductId);
            insert.Parameters.AddWithValue("sku", request.Sku!);
            insert.Parameters.AddWithValue("name", (object?)request.VariantName ?? DBNull.Value);
            insert.Parameters.AddWithValue("uomId", request.BaseUnitOfMeasureId);
            insert.Parameters.AddWithValue("tracking", request.TrackingMode!);
            insert.Parameters.AddWithValue("costing", request.CostingMethod!);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            var item = await FindVariantAsync(db.Connection, db.Transaction, tenantId, request.Sku!, cancellationToken);
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<ProductVariantSummary>.Success(item!);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<ProductVariantSummary>(db, "DUPLICATE_VARIANT_SKU", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT warehouse_id, code, name, is_active FROM erp_inventory.warehouse WHERE tenant_id = @tenantId AND company_id = @companyId ORDER BY code",
            db.Connection,
            db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);
        var items = new List<WarehouseSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadWarehouse(reader));
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<WarehouseSummary>> CreateWarehouseAsync(
        Guid tenantId,
        Guid companyId,
        CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name) VALUES (@tenantId, @companyId, @id, @code, @name) RETURNING warehouse_id, code, name, is_active",
                db.Connection,
                db.Transaction);
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("companyId", companyId);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("code", request.Code!);
            command.Parameters.AddWithValue("name", request.Name!);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var warehouse = ReadWarehouse(reader);
            await reader.CloseAsync();
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<WarehouseSummary>.Success(warehouse);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<WarehouseSummary>(db, "DUPLICATE_WAREHOUSE_CODE", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT bin_id, warehouse_id, code, bin_type, is_active FROM erp_inventory.stock_bin WHERE tenant_id = @tenantId AND company_id = @companyId AND (@warehouseId IS NULL OR warehouse_id = @warehouseId) ORDER BY warehouse_id, code",
            db.Connection,
            db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);
        AddNullableUuid(command, "warehouseId", warehouseId);
        var items = new List<StockBinSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadBin(reader));
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<StockBinSummary>> CreateBinAsync(
        Guid tenantId,
        Guid companyId,
        Guid warehouseId,
        CreateStockBinRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);
        await using (var validate = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM erp_inventory.warehouse WHERE tenant_id = @tenantId AND company_id = @companyId AND warehouse_id = @warehouseId AND is_active)",
            db.Connection,
            db.Transaction))
        {
            validate.Parameters.AddWithValue("tenantId", tenantId);
            validate.Parameters.AddWithValue("companyId", companyId);
            validate.Parameters.AddWithValue("warehouseId", warehouseId);
            if (await validate.ExecuteScalarAsync(cancellationToken) as bool? != true)
            {
                return await RollbackWithFailureAsync<StockBinSummary>(db, "WAREHOUSE_NOT_FOUND", cancellationToken);
            }
        }

        try
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type) VALUES (@tenantId, @companyId, @warehouseId, @id, @code, @type) RETURNING bin_id, warehouse_id, code, bin_type, is_active",
                db.Connection,
                db.Transaction);
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("companyId", companyId);
            command.Parameters.AddWithValue("warehouseId", warehouseId);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("code", request.Code!);
            command.Parameters.AddWithValue("type", request.BinType!);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var bin = ReadBin(reader);
            await reader.CloseAsync();
            await db.Transaction.CommitAsync(cancellationToken);
            return Result<StockBinSummary>.Success(bin);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<StockBinSummary>(db, "DUPLICATE_BIN_CODE", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(
        Guid tenantId,
        Guid companyId,
        Guid? variantId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(tenantId, companyId, cancellationToken);

        bool hasCostLayersTable;
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT to_regclass('erp_inventory.inventory_cost_layer') IS NOT NULL;",
            db.Connection,
            db.Transaction))
        {
            var res = await checkCmd.ExecuteScalarAsync(cancellationToken);
            hasCostLayersTable = res is bool b && b;
        }

        var sql = hasCostLayersTable
            ? """
              SELECT balance.variant_id, balance.warehouse_id, balance.bin_id, balance.status_code, balance.on_hand_qty, variant.base_uom_id, COALESCE(vl.valued_qty, 0) AS valued_qty
              FROM erp_inventory.stock_balance AS balance
              JOIN erp_inventory.product_variant AS variant ON variant.tenant_id = balance.tenant_id AND variant.variant_id = balance.variant_id
              LEFT JOIN (
                  SELECT variant_id, warehouse_id, bin_id, sum(remaining_qty) AS valued_qty
                  FROM erp_inventory.inventory_cost_layer
                  WHERE tenant_id = @tenantId AND company_id = @companyId
                    AND status_code = 'available' AND remaining_qty > 0 AND unit_cost > 0
                  GROUP BY variant_id, warehouse_id, bin_id
              ) AS vl ON vl.variant_id = balance.variant_id AND vl.warehouse_id = balance.warehouse_id AND vl.bin_id = balance.bin_id
              WHERE balance.tenant_id = @tenantId AND balance.company_id = @companyId
                AND (@variantId IS NULL OR balance.variant_id = @variantId)
                AND (@warehouseId IS NULL OR balance.warehouse_id = @warehouseId)
              ORDER BY balance.variant_id, balance.warehouse_id, balance.bin_id, balance.status_code
              """
            : """
              SELECT balance.variant_id, balance.warehouse_id, balance.bin_id, balance.status_code, balance.on_hand_qty, variant.base_uom_id, NULL::numeric AS valued_qty
              FROM erp_inventory.stock_balance AS balance
              JOIN erp_inventory.product_variant AS variant ON variant.tenant_id = balance.tenant_id AND variant.variant_id = balance.variant_id
              WHERE balance.tenant_id = @tenantId AND balance.company_id = @companyId
                AND (@variantId IS NULL OR balance.variant_id = @variantId)
                AND (@warehouseId IS NULL OR balance.warehouse_id = @warehouseId)
              ORDER BY balance.variant_id, balance.warehouse_id, balance.bin_id, balance.status_code
              """;

        await using var command = new NpgsqlCommand(sql, db.Connection, db.Transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);
        AddNullableUuid(command, "variantId", variantId);
        AddNullableUuid(command, "warehouseId", warehouseId);
        var items = new List<InventoryAvailabilitySummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal? valQty = reader.IsDBNull(6) ? null : reader.GetDecimal(6);
            items.Add(new InventoryAvailabilitySummary(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetGuid(5), valQty));
        }
        await reader.CloseAsync();
        await db.Transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<Result<InventoryReceiptSummary>> PostReceiptAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        PostInventoryReceiptRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('cmd_idemp:' || @tenantId::text || ':' || @key::text))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                lockCmd.Parameters.AddWithValue("key", transactionId);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var reserve = new NpgsqlCommand(
                "INSERT INTO erp_inventory.command_idempotency (tenant_id, company_id, idempotency_key, command_code, request_hash) VALUES (@tenantId, @companyId, @key, 'inventory.receipt.post', @hash) ON CONFLICT (tenant_id, idempotency_key) DO NOTHING RETURNING idempotency_key",
                db.Connection,
                db.Transaction))
            {
                reserve.Parameters.AddWithValue("tenantId", context.TenantId);
                reserve.Parameters.AddWithValue("companyId", context.CompanyId);
                reserve.Parameters.AddWithValue("key", transactionId);
                reserve.Parameters.AddWithValue("hash", requestHash);
                if (await reserve.ExecuteScalarAsync(cancellationToken) is null)
                {
                    var replay = await FindReceiptByKeyAsync(db, context, transactionId, cancellationToken);
                    if (replay is null)
                    {
                        return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                    }
                    var saved = replay.Value;
                    if (saved.RequestHash != requestHash || saved.CommandCode != "inventory.receipt.post")
                    {
                        return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                    }
                    if (saved.Receipt is null)
                    {
                        return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                    }
                    await db.Transaction.CommitAsync(cancellationToken);
                    return Result<InventoryReceiptSummary>.Success(saved.Receipt);
                }
            }

            Guid baseUomId;
            await using (var validateVariant = new NpgsqlCommand(
                "SELECT variant.base_uom_id FROM erp_inventory.product_variant AS variant JOIN erp_inventory.product AS product ON product.tenant_id = variant.tenant_id AND product.product_id = variant.product_id WHERE variant.tenant_id = @tenantId AND variant.variant_id = @variantId AND variant.is_active AND product.is_active AND product.product_type = 'stock' AND variant.tracking_mode = 'none'",
                db.Connection,
                db.Transaction))
            {
                validateVariant.Parameters.AddWithValue("tenantId", context.TenantId);
                validateVariant.Parameters.AddWithValue("variantId", request.VariantId);
                var result = await validateVariant.ExecuteScalarAsync(cancellationToken);
                if (result is not Guid foundUom)
                {
                    return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "RECEIPT_VARIANT_NOT_SUPPORTED", cancellationToken);
                }
                baseUomId = foundUom;
            }

            if (baseUomId != request.UnitOfMeasureId)
            {
                return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "RECEIPT_UNIT_MUST_MATCH_BASE_UNIT", cancellationToken);
            }

            await using (var validateLocation = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM erp_inventory.warehouse AS warehouse JOIN erp_inventory.stock_bin AS bin ON bin.tenant_id = warehouse.tenant_id AND bin.company_id = warehouse.company_id AND bin.warehouse_id = warehouse.warehouse_id WHERE warehouse.tenant_id = @tenantId AND warehouse.company_id = @companyId AND warehouse.warehouse_id = @warehouseId AND warehouse.is_active AND bin.bin_id = @binId AND bin.is_active AND bin.bin_type IN ('storage','receiving'))",
                db.Connection,
                db.Transaction))
            {
                validateLocation.Parameters.AddWithValue("tenantId", context.TenantId);
                validateLocation.Parameters.AddWithValue("companyId", context.CompanyId);
                validateLocation.Parameters.AddWithValue("warehouseId", request.WarehouseId);
                validateLocation.Parameters.AddWithValue("binId", request.BinId);
                if (await validateLocation.ExecuteScalarAsync(cancellationToken) as bool? != true)
                {
                    return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "RECEIPT_LOCATION_NOT_SUPPORTED", cancellationToken);
                }
            }

            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var movementId = Guid.NewGuid();
            DateTimeOffset postedAt;
            await using (var movement = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, occurred_at, created_by, note) VALUES (@tenantId, @companyId, @movementId, @transactionId, 'receipt', @sourceType, @sourceId, @occurredAt, @createdBy, @note) RETURNING posted_at",
                db.Connection,
                db.Transaction))
            {
                movement.Parameters.AddWithValue("tenantId", context.TenantId);
                movement.Parameters.AddWithValue("companyId", context.CompanyId);
                movement.Parameters.AddWithValue("movementId", movementId);
                movement.Parameters.AddWithValue("transactionId", transactionId);
                movement.Parameters.AddWithValue("sourceType", (object?)request.SourceDocumentType ?? DBNull.Value);
                movement.Parameters.AddWithValue("sourceId", (object?)request.SourceDocumentId ?? DBNull.Value);
                movement.Parameters.AddWithValue("occurredAt", occurredAt);
                movement.Parameters.AddWithValue("createdBy", context.UserId);
                movement.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
                var postedAtValue = await movement.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The stock receipt was not posted.");
                postedAt = postedAtValue switch
                {
                    DateTimeOffset timestamp => timestamp.ToUniversalTime(),
                    DateTime timestamp => new DateTimeOffset(timestamp.ToUniversalTime()),
                    _ => throw new InvalidOperationException("PostgreSQL returned an invalid posting time."),
                };
            }

            await using (var leg = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement_leg (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id) VALUES (@tenantId, @companyId, @movementId, 1, @variantId, @warehouseId, @binId, 'available', @quantity, @uomId)",
                db.Connection,
                db.Transaction))
            {
                leg.Parameters.AddWithValue("tenantId", context.TenantId);
                leg.Parameters.AddWithValue("companyId", context.CompanyId);
                leg.Parameters.AddWithValue("movementId", movementId);
                leg.Parameters.AddWithValue("variantId", request.VariantId);
                leg.Parameters.AddWithValue("warehouseId", request.WarehouseId);
                leg.Parameters.AddWithValue("binId", request.BinId);
                leg.Parameters.AddWithValue("quantity", request.Quantity);
                leg.Parameters.AddWithValue("uomId", baseUomId);
                await leg.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var balance = new NpgsqlCommand(
                "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@tenantId, @companyId, @variantId, @warehouseId, @binId, 'available', @quantity, @balanceId) ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid))) DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now()",
                db.Connection,
                db.Transaction))
            {
                balance.Parameters.AddWithValue("tenantId", context.TenantId);
                balance.Parameters.AddWithValue("companyId", context.CompanyId);
                balance.Parameters.AddWithValue("variantId", request.VariantId);
                balance.Parameters.AddWithValue("warehouseId", request.WarehouseId);
                balance.Parameters.AddWithValue("binId", request.BinId);
                balance.Parameters.AddWithValue("quantity", request.Quantity);
                balance.Parameters.AddWithValue("balanceId", Guid.NewGuid());
                await balance.ExecuteNonQueryAsync(cancellationToken);
            }

            var eventPayload = JsonSerializer.Serialize(new
            {
                transactionId,
                movementId,
                request.VariantId,
                request.WarehouseId,
                request.BinId,
                request.Quantity,
                unitOfMeasureId = baseUomId,
                occurredAt,
                postedAt,
            });
            await using (var outbox = new NpgsqlCommand(
                "INSERT INTO erp_inventory.outbox_event (tenant_id, company_id, event_id, aggregate_type, aggregate_id, event_type, payload, occurred_at) VALUES (@tenantId, @companyId, @eventId, 'inventory_movement', @movementId, 'inventory.receipt.posted', @payload, @occurredAt)",
                db.Connection,
                db.Transaction))
            {
                outbox.Parameters.AddWithValue("tenantId", context.TenantId);
                outbox.Parameters.AddWithValue("companyId", context.CompanyId);
                outbox.Parameters.AddWithValue("eventId", Guid.NewGuid());
                outbox.Parameters.AddWithValue("movementId", movementId);
                outbox.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = eventPayload;
                outbox.Parameters.AddWithValue("occurredAt", occurredAt);
                await outbox.ExecuteNonQueryAsync(cancellationToken);
            }

            await CompanyChangeLogWriter.AppendAsync(
                db.Connection,
                db.Transaction,
                context.TenantId,
                context.CompanyId,
                "inventory_movement",
                movementId,
                1,
                "upsert",
                eventPayload,
                cancellationToken);

            await using (var saveResult = new NpgsqlCommand(
                "UPDATE erp_inventory.command_idempotency SET resource_id = @movementId WHERE tenant_id = @tenantId AND idempotency_key = @key",
                db.Connection,
                db.Transaction))
            {
                saveResult.Parameters.AddWithValue("tenantId", context.TenantId);
                saveResult.Parameters.AddWithValue("key", transactionId);
                saveResult.Parameters.AddWithValue("movementId", movementId);
                await saveResult.ExecuteNonQueryAsync(cancellationToken);
            }

            await db.Transaction.CommitAsync(cancellationToken);
            return Result<InventoryReceiptSummary>.Success(new InventoryReceiptSummary(
                transactionId, movementId, request.VariantId, request.WarehouseId, request.BinId,
                request.Quantity, baseUomId, occurredAt, postedAt));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<InventoryReceiptSummary>(db, "INVENTORY_TRANSACTION_CONFLICT", cancellationToken);
        }
    }

    public async Task<Result<InventoryTransferSummary>> PostTransferAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        PostInventoryTransferRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            // 1. Idempotency reservation with advisory lock
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('cmd_idemp:' || @tenantId::text || ':' || @key::text))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                lockCmd.Parameters.AddWithValue("key", transactionId);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var reserve = new NpgsqlCommand(
                "INSERT INTO erp_inventory.command_idempotency (tenant_id, company_id, idempotency_key, command_code, request_hash) VALUES (@tenantId, @companyId, @key, 'inventory.transfer.post', @hash) ON CONFLICT (tenant_id, idempotency_key) DO NOTHING RETURNING idempotency_key",
                db.Connection,
                db.Transaction))
            {
                reserve.Parameters.AddWithValue("tenantId", context.TenantId);
                reserve.Parameters.AddWithValue("companyId", context.CompanyId);
                reserve.Parameters.AddWithValue("key", transactionId);
                reserve.Parameters.AddWithValue("hash", requestHash);
                if (await reserve.ExecuteScalarAsync(cancellationToken) is null)
                {
                    return await RollbackWithFailureAsync<InventoryTransferSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                }
            }

            // 2. Validate Variant
            Guid baseUomId;
            await using (var valVar = new NpgsqlCommand(
                "SELECT variant.base_uom_id FROM erp_inventory.product_variant AS variant JOIN erp_inventory.product AS product ON product.tenant_id = variant.tenant_id AND product.product_id = variant.product_id WHERE variant.tenant_id = @tenantId AND variant.variant_id = @variantId AND variant.is_active AND product.is_active",
                db.Connection,
                db.Transaction))
            {
                valVar.Parameters.AddWithValue("tenantId", context.TenantId);
                valVar.Parameters.AddWithValue("variantId", request.VariantId);
                var uomResult = await valVar.ExecuteScalarAsync(cancellationToken);
                if (uomResult is not Guid foundUom)
                {
                    return await RollbackWithFailureAsync<InventoryTransferSummary>(db, "VARIANT_NOT_FOUND", cancellationToken);
                }
                baseUomId = foundUom;
            }

            // 3. Check source stock balance
            decimal sourceOnHand = 0;
            await using (var checkStock = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @tenantId AND company_id = @companyId AND variant_id = @variantId AND warehouse_id = @whId AND bin_id = @binId AND status_code = 'available'",
                db.Connection,
                db.Transaction))
            {
                checkStock.Parameters.AddWithValue("tenantId", context.TenantId);
                checkStock.Parameters.AddWithValue("companyId", context.CompanyId);
                checkStock.Parameters.AddWithValue("variantId", request.VariantId);
                checkStock.Parameters.AddWithValue("whId", request.SourceWarehouseId);
                checkStock.Parameters.AddWithValue("binId", request.SourceBinId);
                var balanceVal = await checkStock.ExecuteScalarAsync(cancellationToken);
                if (balanceVal is decimal onHand)
                {
                    sourceOnHand = onHand;
                }
            }

            if (sourceOnHand < request.Quantity)
            {
                return await RollbackWithFailureAsync<InventoryTransferSummary>(db, "INSUFFICIENT_STOCK", cancellationToken);
            }

            // 4. Validate destination bin exists
            await using (var valDest = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM erp_inventory.stock_bin WHERE tenant_id = @tenantId AND company_id = @companyId AND warehouse_id = @whId AND bin_id = @binId AND is_active)",
                db.Connection,
                db.Transaction))
            {
                valDest.Parameters.AddWithValue("tenantId", context.TenantId);
                valDest.Parameters.AddWithValue("companyId", context.CompanyId);
                valDest.Parameters.AddWithValue("whId", request.DestinationWarehouseId);
                valDest.Parameters.AddWithValue("binId", request.DestinationBinId);
                if (await valDest.ExecuteScalarAsync(cancellationToken) as bool? != true)
                {
                    return await RollbackWithFailureAsync<InventoryTransferSummary>(db, "DESTINATION_LOCATION_NOT_FOUND", cancellationToken);
                }
            }

            var movementId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            DateTimeOffset postedAt;

            // 5. Insert transfer movement
            await using (var movementCmd = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement (tenant_id, company_id, movement_id, client_transaction_id, movement_type, occurred_at, created_by, note) VALUES (@tenantId, @companyId, @movementId, @transactionId, 'transfer', @occurredAt, @createdBy, @note) RETURNING posted_at",
                db.Connection,
                db.Transaction))
            {
                movementCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                movementCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                movementCmd.Parameters.AddWithValue("movementId", movementId);
                movementCmd.Parameters.AddWithValue("transactionId", transactionId);
                movementCmd.Parameters.AddWithValue("occurredAt", occurredAt);
                movementCmd.Parameters.AddWithValue("createdBy", context.UserId);
                movementCmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);

                var postedVal = await movementCmd.ExecuteScalarAsync(cancellationToken);
                postedAt = postedVal is DateTimeOffset dt ? dt.ToUniversalTime() : DateTimeOffset.UtcNow;
            }

            // 6. Insert Leg 1 (Source: negative delta)
            await using (var leg1 = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement_leg (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id) VALUES (@tenantId, @companyId, @movementId, 1, @variantId, @whId, @binId, 'available', @qtyDelta, @uomId)",
                db.Connection,
                db.Transaction))
            {
                leg1.Parameters.AddWithValue("tenantId", context.TenantId);
                leg1.Parameters.AddWithValue("companyId", context.CompanyId);
                leg1.Parameters.AddWithValue("movementId", movementId);
                leg1.Parameters.AddWithValue("variantId", request.VariantId);
                leg1.Parameters.AddWithValue("whId", request.SourceWarehouseId);
                leg1.Parameters.AddWithValue("binId", request.SourceBinId);
                leg1.Parameters.AddWithValue("qtyDelta", -request.Quantity);
                leg1.Parameters.AddWithValue("uomId", baseUomId);
                await leg1.ExecuteNonQueryAsync(cancellationToken);
            }

            // 7. Insert Leg 2 (Destination: positive delta)
            await using (var leg2 = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement_leg (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id) VALUES (@tenantId, @companyId, @movementId, 2, @variantId, @whId, @binId, 'available', @qtyDelta, @uomId)",
                db.Connection,
                db.Transaction))
            {
                leg2.Parameters.AddWithValue("tenantId", context.TenantId);
                leg2.Parameters.AddWithValue("companyId", context.CompanyId);
                leg2.Parameters.AddWithValue("movementId", movementId);
                leg2.Parameters.AddWithValue("variantId", request.VariantId);
                leg2.Parameters.AddWithValue("whId", request.DestinationWarehouseId);
                leg2.Parameters.AddWithValue("binId", request.DestinationBinId);
                leg2.Parameters.AddWithValue("qtyDelta", request.Quantity);
                leg2.Parameters.AddWithValue("uomId", baseUomId);
                await leg2.ExecuteNonQueryAsync(cancellationToken);
            }

            // 8. Update source balance
            await using (var updateSource = new NpgsqlCommand(
                "UPDATE erp_inventory.stock_balance SET on_hand_qty = on_hand_qty - @qty, updated_at = now() WHERE tenant_id = @tenantId AND company_id = @companyId AND variant_id = @variantId AND warehouse_id = @whId AND bin_id = @binId AND status_code = 'available'",
                db.Connection,
                db.Transaction))
            {
                updateSource.Parameters.AddWithValue("tenantId", context.TenantId);
                updateSource.Parameters.AddWithValue("companyId", context.CompanyId);
                updateSource.Parameters.AddWithValue("variantId", request.VariantId);
                updateSource.Parameters.AddWithValue("whId", request.SourceWarehouseId);
                updateSource.Parameters.AddWithValue("binId", request.SourceBinId);
                updateSource.Parameters.AddWithValue("qty", request.Quantity);
                await updateSource.ExecuteNonQueryAsync(cancellationToken);
            }

            // 9. Upsert destination balance
            await using (var upsertDest = new NpgsqlCommand(
                "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@tenantId, @companyId, @variantId, @whId, @binId, 'available', @qty, @balanceId) ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid))) DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now()",
                db.Connection,
                db.Transaction))
            {
                upsertDest.Parameters.AddWithValue("tenantId", context.TenantId);
                upsertDest.Parameters.AddWithValue("companyId", context.CompanyId);
                upsertDest.Parameters.AddWithValue("variantId", request.VariantId);
                upsertDest.Parameters.AddWithValue("whId", request.DestinationWarehouseId);
                upsertDest.Parameters.AddWithValue("binId", request.DestinationBinId);
                upsertDest.Parameters.AddWithValue("qty", request.Quantity);
                upsertDest.Parameters.AddWithValue("balanceId", Guid.NewGuid());
                await upsertDest.ExecuteNonQueryAsync(cancellationToken);
            }

            // 9b. Transfer FIFO Cost Layers from source bin to destination bin (preserving chronology, unit cost, and original GRN source)
            bool costLayerTableExists;
            await using (var checkLayersTable = new NpgsqlCommand(
                "SELECT to_regclass('erp_inventory.inventory_cost_layer') IS NOT NULL;",
                db.Connection,
                db.Transaction))
            {
                var checkRes = await checkLayersTable.ExecuteScalarAsync(cancellationToken);
                costLayerTableExists = checkRes is bool b && b;
            }

            if (costLayerTableExists)
            {
                var openLayers = new List<(
                    Guid LayerId,
                    string SourceDocType,
                    Guid? SourceDocId,
                    int? SourceLineNo,
                    DateTimeOffset ReceivedAt,
                    decimal RemainingQty,
                    decimal UnitCost,
                    string CurrencyCode)>();

                await using (var selectLayers = new NpgsqlCommand(
                    """
                    SELECT layer_id, source_document_type, source_document_id, source_line_number,
                           received_at, remaining_qty, unit_cost, currency_code
                    FROM erp_inventory.inventory_cost_layer
                    WHERE tenant_id = @tenantId
                      AND company_id = @companyId
                      AND variant_id = @variantId
                      AND warehouse_id = @srcWhId
                      AND bin_id = @srcBinId
                      AND status_code = 'available'
                      AND remaining_qty > 0
                    ORDER BY received_at ASC, created_at ASC, layer_id ASC
                    FOR UPDATE;
                    """,
                    db.Connection,
                    db.Transaction))
                {
                    selectLayers.Parameters.AddWithValue("tenantId", context.TenantId);
                    selectLayers.Parameters.AddWithValue("companyId", context.CompanyId);
                    selectLayers.Parameters.AddWithValue("variantId", request.VariantId);
                    selectLayers.Parameters.AddWithValue("srcWhId", request.SourceWarehouseId);
                    selectLayers.Parameters.AddWithValue("srcBinId", request.SourceBinId);

                    await using var layerReader = await selectLayers.ExecuteReaderAsync(cancellationToken);
                    while (await layerReader.ReadAsync(cancellationToken))
                    {
                        openLayers.Add((
                            LayerId: layerReader.GetGuid(0),
                            SourceDocType: layerReader.GetString(1),
                            SourceDocId: layerReader.IsDBNull(2) ? null : layerReader.GetGuid(2),
                            SourceLineNo: layerReader.IsDBNull(3) ? null : layerReader.GetInt32(3),
                            ReceivedAt: layerReader.GetFieldValue<DateTimeOffset>(4),
                            RemainingQty: layerReader.GetDecimal(5),
                            UnitCost: layerReader.GetDecimal(6),
                            CurrencyCode: layerReader.GetString(7)));
                    }
                    await layerReader.CloseAsync();
                }

                decimal qtyLeftToTransfer = request.Quantity;
                int transferAllocLine = 1;

                foreach (var srcLayer in openLayers)
                {
                    if (qtyLeftToTransfer <= 0) break;

                    var takeQty = Math.Min(srcLayer.RemainingQty, qtyLeftToTransfer);
                    if (takeQty <= 0) continue;

                    // 1. Decrement remaining_qty on source layer
                    await using (var decSourceLayer = new NpgsqlCommand(
                        """
                        UPDATE erp_inventory.inventory_cost_layer
                        SET remaining_qty = remaining_qty - @qty,
                            updated_at = now()
                        WHERE tenant_id = @tenantId
                          AND company_id = @companyId
                          AND layer_id = @layerId
                          AND remaining_qty >= @qty;
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        decSourceLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                        decSourceLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                        decSourceLayer.Parameters.AddWithValue("layerId", srcLayer.LayerId);
                        decSourceLayer.Parameters.AddWithValue("qty", takeQty);
                        var updatedCount = await decSourceLayer.ExecuteNonQueryAsync(cancellationToken);
                        if (updatedCount != 1)
                        {
                            throw new InvalidOperationException($"INSUFFICIENT_VALUATION_LAYERS: Concurrent modification on layer {srcLayer.LayerId}.");
                        }
                    }

                    // 2. Insert audit trail into inventory_cost_allocation
                    await using (var insAlloc = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.inventory_cost_allocation
                            (tenant_id, company_id, allocation_id, layer_id, movement_id,
                             source_document_type, source_document_id, source_line_number,
                             variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                        VALUES
                            (@tenantId, @companyId, @allocId, @layerId, @movementId,
                             'inventory_transfer', @movementId, @lineNo,
                             @variantId, @consumedQty, @unitCost, @extendedCost, @currency);
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        insAlloc.Parameters.AddWithValue("tenantId", context.TenantId);
                        insAlloc.Parameters.AddWithValue("companyId", context.CompanyId);
                        insAlloc.Parameters.AddWithValue("allocId", Guid.NewGuid());
                        insAlloc.Parameters.AddWithValue("layerId", srcLayer.LayerId);
                        insAlloc.Parameters.AddWithValue("movementId", movementId);
                        insAlloc.Parameters.AddWithValue("lineNo", transferAllocLine++);
                        insAlloc.Parameters.AddWithValue("variantId", request.VariantId);
                        insAlloc.Parameters.AddWithValue("consumedQty", takeQty);
                        insAlloc.Parameters.AddWithValue("unitCost", srcLayer.UnitCost);
                        insAlloc.Parameters.AddWithValue("extendedCost", takeQty * srcLayer.UnitCost);
                        insAlloc.Parameters.AddWithValue("currency", srcLayer.CurrencyCode);
                        await insAlloc.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // 3. Create destination child layer in inventory_cost_layer
                    await using (var insChildLayer = new NpgsqlCommand(
                        """
                        INSERT INTO erp_inventory.inventory_cost_layer
                            (tenant_id, company_id, layer_id, parent_layer_id, variant_id,
                             warehouse_id, bin_id, status_code, source_document_type,
                             source_document_id, source_line_number, source_movement_id,
                             received_at, original_qty, remaining_qty, unit_cost, currency_code)
                        VALUES
                            (@tenantId, @companyId, @childLayerId, @parentLayerId, @variantId,
                             @destWhId, @destBinId, 'available', @srcDocType,
                             @srcDocId, @srcLineNo, @movId,
                             @receivedAt, @origQty, @remainingQty, @unitCost, @currency);
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        insChildLayer.Parameters.AddWithValue("tenantId", context.TenantId);
                        insChildLayer.Parameters.AddWithValue("companyId", context.CompanyId);
                        insChildLayer.Parameters.AddWithValue("childLayerId", Guid.NewGuid());
                        insChildLayer.Parameters.AddWithValue("parentLayerId", srcLayer.LayerId);
                        insChildLayer.Parameters.AddWithValue("variantId", request.VariantId);
                        insChildLayer.Parameters.AddWithValue("destWhId", request.DestinationWarehouseId);
                        insChildLayer.Parameters.AddWithValue("destBinId", request.DestinationBinId);
                        insChildLayer.Parameters.AddWithValue("srcDocType", srcLayer.SourceDocType);
                        insChildLayer.Parameters.AddWithValue("srcDocId", (object?)srcLayer.SourceDocId ?? DBNull.Value);
                        insChildLayer.Parameters.AddWithValue("srcLineNo", (object?)srcLayer.SourceLineNo ?? DBNull.Value);
                        insChildLayer.Parameters.AddWithValue("movId", movementId);
                        insChildLayer.Parameters.AddWithValue("receivedAt", srcLayer.ReceivedAt);
                        insChildLayer.Parameters.AddWithValue("origQty", takeQty);
                        insChildLayer.Parameters.AddWithValue("remainingQty", takeQty);
                        insChildLayer.Parameters.AddWithValue("unitCost", srcLayer.UnitCost);
                        insChildLayer.Parameters.AddWithValue("currency", srcLayer.CurrencyCode);
                        await insChildLayer.ExecuteNonQueryAsync(cancellationToken);
                    }

                    qtyLeftToTransfer -= takeQty;
                }
            }

            // 10. Outbox event
            var eventPayload = JsonSerializer.Serialize(new
            {
                transactionId,
                movementId,
                request.VariantId,
                request.SourceWarehouseId,
                request.SourceBinId,
                request.DestinationWarehouseId,
                request.DestinationBinId,
                request.Quantity,
                unitOfMeasureId = baseUomId,
                occurredAt,
                postedAt,
            });
            await using (var outbox = new NpgsqlCommand(
                "INSERT INTO erp_inventory.outbox_event (tenant_id, company_id, event_id, aggregate_type, aggregate_id, event_type, payload, occurred_at) VALUES (@tenantId, @companyId, @eventId, 'inventory_movement', @movementId, 'inventory.transfer.posted', @payload, @occurredAt)",
                db.Connection,
                db.Transaction))
            {
                outbox.Parameters.AddWithValue("tenantId", context.TenantId);
                outbox.Parameters.AddWithValue("companyId", context.CompanyId);
                outbox.Parameters.AddWithValue("eventId", Guid.NewGuid());
                outbox.Parameters.AddWithValue("movementId", movementId);
                outbox.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = eventPayload;
                outbox.Parameters.AddWithValue("occurredAt", occurredAt);
                await outbox.ExecuteNonQueryAsync(cancellationToken);
            }

            // 11. Update idempotency record
            await using (var saveResult = new NpgsqlCommand(
                "UPDATE erp_inventory.command_idempotency SET resource_id = @movementId WHERE tenant_id = @tenantId AND idempotency_key = @key",
                db.Connection,
                db.Transaction))
            {
                saveResult.Parameters.AddWithValue("tenantId", context.TenantId);
                saveResult.Parameters.AddWithValue("key", transactionId);
                saveResult.Parameters.AddWithValue("movementId", movementId);
                await saveResult.ExecuteNonQueryAsync(cancellationToken);
            }

            // 12. Write to sync change_log so offline clients can download this transfer.
            await CompanyChangeLogWriter.AppendAsync(
                db.Connection,
                db.Transaction,
                context.TenantId,
                context.CompanyId,
                "inventory_movement",
                movementId,
                1,
                "upsert",
                eventPayload,
                cancellationToken);

            await db.Transaction.CommitAsync(cancellationToken);
            return Result<InventoryTransferSummary>.Success(new InventoryTransferSummary(
                transactionId, movementId, request.VariantId, request.SourceWarehouseId, request.SourceBinId,
                request.DestinationWarehouseId, request.DestinationBinId, request.Quantity, baseUomId, postedAt));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<InventoryTransferSummary>(db, "INVENTORY_TRANSACTION_CONFLICT", cancellationToken);
        }
    }

    public async Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(
        TenantContext context,
        Guid transactionId,
        string requestHash,
        PostInventoryAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        try
        {
            // 1. Idempotency reservation with advisory lock
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('cmd_idemp:' || @tenantId::text || ':' || @key::text))",
                db.Connection,
                db.Transaction))
            {
                lockCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                lockCmd.Parameters.AddWithValue("key", transactionId);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var reserve = new NpgsqlCommand(
                "INSERT INTO erp_inventory.command_idempotency (tenant_id, company_id, idempotency_key, command_code, request_hash) VALUES (@tenantId, @companyId, @key, 'inventory.adjustment.post', @hash) ON CONFLICT (tenant_id, idempotency_key) DO NOTHING RETURNING idempotency_key",
                db.Connection,
                db.Transaction))
            {
                reserve.Parameters.AddWithValue("tenantId", context.TenantId);
                reserve.Parameters.AddWithValue("companyId", context.CompanyId);
                reserve.Parameters.AddWithValue("key", transactionId);
                reserve.Parameters.AddWithValue("hash", requestHash);
                if (await reserve.ExecuteScalarAsync(cancellationToken) is null)
                {
                    return await RollbackWithFailureAsync<InventoryAdjustmentSummary>(db, "IDEMPOTENCY_KEY_REUSED", cancellationToken);
                }
            }

            // 2. Validate Variant
            Guid baseUomId;
            await using (var valVar = new NpgsqlCommand(
                "SELECT variant.base_uom_id FROM erp_inventory.product_variant AS variant JOIN erp_inventory.product AS product ON product.tenant_id = variant.tenant_id AND product.product_id = variant.product_id WHERE variant.tenant_id = @tenantId AND variant.variant_id = @variantId AND variant.is_active AND product.is_active",
                db.Connection,
                db.Transaction))
            {
                valVar.Parameters.AddWithValue("tenantId", context.TenantId);
                valVar.Parameters.AddWithValue("variantId", request.VariantId);
                var uomResult = await valVar.ExecuteScalarAsync(cancellationToken);
                if (uomResult is not Guid foundUom)
                {
                    return await RollbackWithFailureAsync<InventoryAdjustmentSummary>(db, "VARIANT_NOT_FOUND", cancellationToken);
                }
                baseUomId = foundUom;
            }

            // 3. If negative adjustment, verify available stock
            if (request.QuantityDelta < 0)
            {
                decimal currentStock = 0;
                await using (var checkStock = new NpgsqlCommand(
                    "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @tenantId AND company_id = @companyId AND variant_id = @variantId AND warehouse_id = @whId AND bin_id = @binId AND status_code = 'available'",
                    db.Connection,
                    db.Transaction))
                {
                    checkStock.Parameters.AddWithValue("tenantId", context.TenantId);
                    checkStock.Parameters.AddWithValue("companyId", context.CompanyId);
                    checkStock.Parameters.AddWithValue("variantId", request.VariantId);
                    checkStock.Parameters.AddWithValue("whId", request.WarehouseId);
                    checkStock.Parameters.AddWithValue("binId", request.BinId);
                    var balanceVal = await checkStock.ExecuteScalarAsync(cancellationToken);
                    if (balanceVal is decimal onHand)
                    {
                        currentStock = onHand;
                    }
                }

                if (currentStock + request.QuantityDelta < 0)
                {
                    return await RollbackWithFailureAsync<InventoryAdjustmentSummary>(db, "INSUFFICIENT_STOCK", cancellationToken);
                }
            }

            var movementId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            DateTimeOffset postedAt;

            // 4. Insert adjustment movement
            var note = $"Reason: {request.ReasonCode}. {request.Note}".Trim();
            await using (var movementCmd = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement (tenant_id, company_id, movement_id, client_transaction_id, movement_type, occurred_at, created_by, note) VALUES (@tenantId, @companyId, @movementId, @transactionId, 'adjustment', @occurredAt, @createdBy, @note) RETURNING posted_at",
                db.Connection,
                db.Transaction))
            {
                movementCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                movementCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                movementCmd.Parameters.AddWithValue("movementId", movementId);
                movementCmd.Parameters.AddWithValue("transactionId", transactionId);
                movementCmd.Parameters.AddWithValue("occurredAt", occurredAt);
                movementCmd.Parameters.AddWithValue("createdBy", context.UserId);
                movementCmd.Parameters.AddWithValue("note", note);

                var postedVal = await movementCmd.ExecuteScalarAsync(cancellationToken);
                postedAt = postedVal is DateTimeOffset dt ? dt.ToUniversalTime() : DateTimeOffset.UtcNow;
            }

            // 5. Insert Leg
            await using (var leg = new NpgsqlCommand(
                "INSERT INTO erp_inventory.inventory_movement_leg (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id) VALUES (@tenantId, @companyId, @movementId, 1, @variantId, @whId, @binId, 'available', @qtyDelta, @uomId)",
                db.Connection,
                db.Transaction))
            {
                leg.Parameters.AddWithValue("tenantId", context.TenantId);
                leg.Parameters.AddWithValue("companyId", context.CompanyId);
                leg.Parameters.AddWithValue("movementId", movementId);
                leg.Parameters.AddWithValue("variantId", request.VariantId);
                leg.Parameters.AddWithValue("whId", request.WarehouseId);
                leg.Parameters.AddWithValue("binId", request.BinId);
                leg.Parameters.AddWithValue("qtyDelta", request.QuantityDelta);
                leg.Parameters.AddWithValue("uomId", baseUomId);
                await leg.ExecuteNonQueryAsync(cancellationToken);
            }

            // 6. Upsert balance
            await using (var upsertBalance = new NpgsqlCommand(
                "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@tenantId, @companyId, @variantId, @whId, @binId, 'available', @qtyDelta, @balanceId) ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid))) DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now()",
                db.Connection,
                db.Transaction))
            {
                upsertBalance.Parameters.AddWithValue("tenantId", context.TenantId);
                upsertBalance.Parameters.AddWithValue("companyId", context.CompanyId);
                upsertBalance.Parameters.AddWithValue("variantId", request.VariantId);
                upsertBalance.Parameters.AddWithValue("whId", request.WarehouseId);
                upsertBalance.Parameters.AddWithValue("binId", request.BinId);
                upsertBalance.Parameters.AddWithValue("qtyDelta", request.QuantityDelta);
                upsertBalance.Parameters.AddWithValue("balanceId", Guid.NewGuid());
                await upsertBalance.ExecuteNonQueryAsync(cancellationToken);
            }

            // 7. Outbox event
            var eventPayload = JsonSerializer.Serialize(new
            {
                transactionId,
                movementId,
                request.VariantId,
                request.WarehouseId,
                request.BinId,
                request.QuantityDelta,
                unitOfMeasureId = baseUomId,
                request.ReasonCode,
                occurredAt,
                postedAt,
            });
            await using (var outbox = new NpgsqlCommand(
                "INSERT INTO erp_inventory.outbox_event (tenant_id, company_id, event_id, aggregate_type, aggregate_id, event_type, payload, occurred_at) VALUES (@tenantId, @companyId, @eventId, 'inventory_movement', @movementId, 'inventory.adjustment.posted', @payload, @occurredAt)",
                db.Connection,
                db.Transaction))
            {
                outbox.Parameters.AddWithValue("tenantId", context.TenantId);
                outbox.Parameters.AddWithValue("companyId", context.CompanyId);
                outbox.Parameters.AddWithValue("eventId", Guid.NewGuid());
                outbox.Parameters.AddWithValue("movementId", movementId);
                outbox.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = eventPayload;
                outbox.Parameters.AddWithValue("occurredAt", occurredAt);
                await outbox.ExecuteNonQueryAsync(cancellationToken);
            }

            // 8. Update idempotency
            await using (var saveResult = new NpgsqlCommand(
                "UPDATE erp_inventory.command_idempotency SET resource_id = @movementId WHERE tenant_id = @tenantId AND idempotency_key = @key",
                db.Connection,
                db.Transaction))
            {
                saveResult.Parameters.AddWithValue("tenantId", context.TenantId);
                saveResult.Parameters.AddWithValue("key", transactionId);
                saveResult.Parameters.AddWithValue("movementId", movementId);
                await saveResult.ExecuteNonQueryAsync(cancellationToken);
            }

            // 9. Write to sync change_log so offline clients can download this adjustment.
            await CompanyChangeLogWriter.AppendAsync(
                db.Connection,
                db.Transaction,
                context.TenantId,
                context.CompanyId,
                "inventory_movement",
                movementId,
                1,
                "upsert",
                eventPayload,
                cancellationToken);

            await db.Transaction.CommitAsync(cancellationToken);
            return Result<InventoryAdjustmentSummary>.Success(new InventoryAdjustmentSummary(
                transactionId, movementId, request.VariantId, request.WarehouseId, request.BinId,
                request.QuantityDelta, baseUomId, request.ReasonCode, postedAt));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return await RollbackWithFailureAsync<InventoryAdjustmentSummary>(db, "INVENTORY_TRANSACTION_CONFLICT", cancellationToken);
        }
    }

    private static readonly string VariantSelect = """
        SELECT variant.variant_id, variant.product_id, product.product_code, product.name,
               variant.sku, variant.variant_name, variant.base_uom_id, uom.code,
               variant.tracking_mode, variant.costing_method, variant.is_active
        FROM erp_inventory.product_variant AS variant
        JOIN erp_inventory.product AS product
          ON product.tenant_id = variant.tenant_id AND product.product_id = variant.product_id
        JOIN erp_inventory.unit_of_measure AS uom
          ON uom.tenant_id = variant.tenant_id AND uom.uom_id = variant.base_uom_id
        """;

    private static async Task<ProductVariantSummary?> FindVariantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        string sku,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(VariantSelect + " WHERE variant.tenant_id = @tenantId AND variant.sku = @sku", connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("sku", sku);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVariant(reader) : null;
    }

    private static async Task<(string CommandCode, string RequestHash, InventoryReceiptSummary? Receipt)?> FindReceiptByKeyAsync(
        DbScope db,
        TenantContext context,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        Guid? movementId;
        string commandCode;
        string requestHash;
        await using (var command = new NpgsqlCommand(
            "SELECT command_code, request_hash, resource_id FROM erp_inventory.command_idempotency WHERE tenant_id = @tenantId AND idempotency_key = @key FOR UPDATE",
            db.Connection,
            db.Transaction))
        {
            command.Parameters.AddWithValue("tenantId", context.TenantId);
            command.Parameters.AddWithValue("key", transactionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            commandCode = reader.GetString(0);
            requestHash = reader.GetString(1).Trim();
            movementId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        }

        if (movementId is null) return (commandCode, requestHash, null);
        await using var findMovement = new NpgsqlCommand(
            "SELECT movement.movement_id, movement.occurred_at, movement.posted_at, leg.variant_id, leg.warehouse_id, leg.bin_id, leg.quantity_delta, leg.base_uom_id FROM erp_inventory.inventory_movement AS movement JOIN erp_inventory.inventory_movement_leg AS leg ON leg.tenant_id = movement.tenant_id AND leg.company_id = movement.company_id AND leg.movement_id = movement.movement_id AND leg.leg_number = 1 WHERE movement.tenant_id = @tenantId AND movement.company_id = @companyId AND movement.movement_id = @movementId AND movement.client_transaction_id = @transactionId",
            db.Connection,
            db.Transaction);
        findMovement.Parameters.AddWithValue("tenantId", context.TenantId);
        findMovement.Parameters.AddWithValue("companyId", context.CompanyId);
        findMovement.Parameters.AddWithValue("movementId", movementId.Value);
        findMovement.Parameters.AddWithValue("transactionId", transactionId);
        await using var movementReader = await findMovement.ExecuteReaderAsync(cancellationToken);
        if (!await movementReader.ReadAsync(cancellationToken)) return (commandCode, requestHash, null);
        var receipt = new InventoryReceiptSummary(
            transactionId,
            movementReader.GetGuid(0),
            movementReader.GetGuid(3),
            movementReader.GetGuid(4),
            movementReader.GetGuid(5),
            movementReader.GetDecimal(6),
            movementReader.GetGuid(7),
            movementReader.GetFieldValue<DateTimeOffset>(1),
            movementReader.GetFieldValue<DateTimeOffset>(2));
        return (commandCode, requestHash, receipt);
    }

    private async Task<DbScope> OpenScopeAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
                connection,
                transaction);
            command.Parameters.AddWithValue("tenantId", tenantId.ToString());
            command.Parameters.AddWithValue("companyId", companyId?.ToString() ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new DbScope(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<Result<T>> RollbackWithFailureAsync<T>(
        DbScope db,
        string error,
        CancellationToken cancellationToken)
    {
        await db.Transaction.RollbackAsync(cancellationToken);
        return Result<T>.Failure(error);
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static UnitOfMeasureSummary ReadUnit(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt16(4));

    private static ProductVariantSummary ReadVariant(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetGuid(6), reader.GetString(7),
        reader.GetString(8), reader.GetString(9), reader.GetBoolean(10));

    private static WarehouseSummary ReadWarehouse(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));

    private static StockBinSummary ReadBin(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4));

    private sealed class DbScope(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        public NpgsqlTransaction Transaction { get; } = transaction;

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
