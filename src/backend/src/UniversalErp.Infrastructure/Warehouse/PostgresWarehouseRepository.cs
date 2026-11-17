using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Warehouse;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Infrastructure.Warehouse;

public class PostgresWarehouseRepository : IWarehouseRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresWarehouseRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    private static async Task SetContextAsync(NpgsqlCommand cmd, TenantContext context)
    {
        cmd.CommandText = "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("tenantId", context.TenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", context.CompanyId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static DateTimeOffset ToDateTimeOffset(object? val) =>
        val switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => DateTimeOffset.UtcNow
        };

    private static DateTimeOffset? ToNullableDateTimeOffset(object? val) =>
        val switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => null
        };

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int index)
    {
        var val = reader.GetValue(index);
        return val switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => DateTimeOffset.UtcNow
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return null;
        var val = reader.GetValue(index);
        return val switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => null
        };
    }

    // ── 1. Zones ─────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<WarehouseZoneRecord>> GetZonesAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        var list = new List<WarehouseZoneRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, warehouse_id, zone_code, name,
                   zone_type, storage_type, default_putaway_strategy, is_active, created_at
            FROM warehouse.zones
            WHERE warehouse_id = @whId
            ORDER BY zone_code;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new WarehouseZoneRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetBoolean(9),
                ReadDateTimeOffset(reader, 10)));
        }
        return list;
    }

    public async Task<Result<WarehouseZoneRecord>> CreateZoneAsync(TenantContext context, Guid warehouseId, CreateZoneRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var zoneId = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO warehouse.zones
                (id, tenant_id, company_id, warehouse_id, zone_code, name, zone_type, storage_type, default_putaway_strategy, is_active)
            VALUES
                (@id, @tenantId, @companyId, @whId, @code, @name, @zoneType, @storageType, @strategy, true)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", zoneId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("code", req.ZoneCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("zoneType", req.ZoneType);
        cmd.Parameters.AddWithValue("storageType", req.StorageType ?? "ambient");
        cmd.Parameters.AddWithValue("strategy", req.DefaultPutawayStrategy ?? "nearest_empty");

        try
        {
            var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
            return Result<WarehouseZoneRecord>.Success(new WarehouseZoneRecord(
                zoneId, context.TenantId, context.CompanyId, warehouseId,
                req.ZoneCode.Trim().ToUpperInvariant(), req.Name.Trim(),
                req.ZoneType, req.StorageType ?? "ambient", req.DefaultPutawayStrategy ?? "nearest_empty",
                true, createdAt));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Result<WarehouseZoneRecord>.Failure("DUPLICATE_ZONE_CODE");
        }
    }

    public async Task<Result<WarehouseZoneRecord>> UpdateZoneAsync(TenantContext context, Guid zoneId, UpdateZoneRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            UPDATE warehouse.zones
            SET name = @name, zone_type = @zoneType, storage_type = @storageType,
                default_putaway_strategy = @strategy, is_active = @isActive
            WHERE id = @id
            RETURNING warehouse_id, zone_code, created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", zoneId);
        cmd.Parameters.AddWithValue("name", req.Name);
        cmd.Parameters.AddWithValue("zoneType", req.ZoneType);
        cmd.Parameters.AddWithValue("storageType", req.StorageType);
        cmd.Parameters.AddWithValue("strategy", req.DefaultPutawayStrategy);
        cmd.Parameters.AddWithValue("isActive", req.IsActive);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var whId = reader.GetGuid(0);
            var code = reader.GetString(1);
            var createdAt = ReadDateTimeOffset(reader, 2);
            return Result<WarehouseZoneRecord>.Success(new WarehouseZoneRecord(
                zoneId, context.TenantId, context.CompanyId, whId, code,
                req.Name, req.ZoneType, req.StorageType, req.DefaultPutawayStrategy, req.IsActive, createdAt));
        }
        return Result<WarehouseZoneRecord>.Failure("ZONE_NOT_FOUND");
    }

    // ── 2. Bin Profiles ───────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BinProfileRecord>> GetBinProfilesAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        var list = new List<BinProfileRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT b.bin_id, b.warehouse_id, b.code, b.bin_type, b.zone_id, z.zone_code,
                   b.aisle, b.rack, b.shelf, b.bin_position,
                   b.max_weight_kg, b.max_volume_cbm, b.max_units,
                   b.is_pick_face, b.storage_restriction, b.is_active,
                   COALESCE(sb.on_hand_units, 0) as current_units
            FROM erp_inventory.stock_bin b
            LEFT JOIN warehouse.zones z ON b.zone_id = z.id
            LEFT JOIN (
                SELECT bin_id, SUM(on_hand_qty) as on_hand_units
                FROM erp_inventory.stock_balance
                WHERE warehouse_id = @whId
                GROUP BY bin_id
            ) sb ON b.bin_id = sb.bin_id
            WHERE b.warehouse_id = @whId
            ORDER BY b.code;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var binId = reader.GetGuid(0);
            var whId = reader.GetGuid(1);
            var code = reader.GetString(2);
            var binType = reader.GetString(3);
            var zoneId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
            var zoneCode = reader.IsDBNull(5) ? null : reader.GetString(5);
            var aisle = reader.IsDBNull(6) ? null : reader.GetString(6);
            var rack = reader.IsDBNull(7) ? null : reader.GetString(7);
            var shelf = reader.IsDBNull(8) ? null : reader.GetString(8);
            var pos = reader.IsDBNull(9) ? null : reader.GetString(9);
            var maxWeight = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10);
            var maxVol = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11);
            var maxUnits = reader.IsDBNull(12) ? (decimal?)null : reader.GetDecimal(12);
            var isPickFace = reader.GetBoolean(13);
            var restriction = reader.GetString(14);
            var isActive = reader.GetBoolean(15);
            var currentUnits = reader.GetDecimal(16);

            var util = maxUnits.HasValue && maxUnits.Value > 0
                ? Math.Min(100m, Math.Round((currentUnits / maxUnits.Value) * 100m, 1))
                : 0m;

            list.Add(new BinProfileRecord(
                binId, whId, code, binType, zoneId, zoneCode, aisle, rack, shelf, pos,
                maxWeight, maxVol, maxUnits, isPickFace, restriction,
                currentUnits, currentUnits * 1.0m, currentUnits * 0.01m, util, isActive));
        }
        return list;
    }

    public async Task<Result<BinProfileRecord>> UpdateBinProfileAsync(TenantContext context, Guid binId, UpdateBinProfileRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            UPDATE erp_inventory.stock_bin
            SET zone_id = @zoneId, aisle = @aisle, rack = @rack, shelf = @shelf,
                bin_position = @pos, max_weight_kg = @maxWeight, max_volume_cbm = @maxVol,
                max_units = @maxUnits, is_pick_face = @isPickFace, storage_restriction = @restriction
            WHERE bin_id = @binId
            RETURNING warehouse_id, code, bin_type, is_active;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("binId", binId);
        cmd.Parameters.AddWithValue("zoneId", (object?)req.ZoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("aisle", (object?)req.Aisle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rack", (object?)req.Rack ?? DBNull.Value);
        cmd.Parameters.AddWithValue("shelf", (object?)req.Shelf ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pos", (object?)req.BinPosition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxWeight", (object?)req.MaxWeightKg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxVol", (object?)req.MaxVolumeCbm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxUnits", (object?)req.MaxUnits ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isPickFace", req.IsPickFace);
        cmd.Parameters.AddWithValue("restriction", req.StorageRestriction ?? "standard");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var whId = reader.GetGuid(0);
            var code = reader.GetString(1);
            var binType = reader.GetString(2);
            var isActive = reader.GetBoolean(3);

            return Result<BinProfileRecord>.Success(new BinProfileRecord(
                binId, whId, code, binType, req.ZoneId, null,
                req.Aisle, req.Rack, req.Shelf, req.BinPosition,
                req.MaxWeightKg, req.MaxVolumeCbm, req.MaxUnits,
                req.IsPickFace, req.StorageRestriction ?? "standard",
                0m, 0m, 0m, 0m, isActive));
        }
        return Result<BinProfileRecord>.Failure("BIN_NOT_FOUND");
    }

    // ── 3. Putaway Strategies & Directed Putaway ──────────────────────────────
    public async Task<IReadOnlyList<PutawayStrategyRecord>> GetPutawayStrategiesAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        var list = new List<PutawayStrategyRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, warehouse_id, product_variant_id,
                   product_category_id, preferred_zone_id, preferred_bin_id,
                   strategy_type, priority, is_active, created_at
            FROM warehouse.putaway_strategies
            WHERE warehouse_id = @whId
            ORDER BY priority;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PutawayStrategyRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetBoolean(10),
                ReadDateTimeOffset(reader, 11)));
        }
        return list;
    }

    public async Task<Result<PutawayStrategyRecord>> CreatePutawayStrategyAsync(TenantContext context, Guid warehouseId, CreatePutawayStrategyRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var id = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO warehouse.putaway_strategies
                (id, tenant_id, company_id, warehouse_id, product_variant_id, product_category_id,
                 preferred_zone_id, preferred_bin_id, strategy_type, priority, is_active)
            VALUES
                (@id, @tenantId, @companyId, @whId, @variantId, @catId, @zoneId, @binId, @strat, @priority, true)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("variantId", (object?)req.ProductVariantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("catId", (object?)req.ProductCategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("zoneId", (object?)req.PreferredZoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("binId", (object?)req.PreferredBinId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("strat", req.StrategyType);
        cmd.Parameters.AddWithValue("priority", req.Priority);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
        return Result<PutawayStrategyRecord>.Success(new PutawayStrategyRecord(
            id, context.TenantId, context.CompanyId, warehouseId,
            req.ProductVariantId, req.ProductCategoryId, req.PreferredZoneId, req.PreferredBinId,
            req.StrategyType, req.Priority, true, createdAt));
    }

    public async Task<IReadOnlyList<PutawayRecommendation>> GetPutawayRecommendationsAsync(TenantContext context, Guid warehouseId, Guid variantId, decimal quantity, CancellationToken ct)
    {
        var binProfiles = await GetBinProfilesAsync(context, warehouseId, ct);
        var strategies = await GetPutawayStrategiesAsync(context, warehouseId, ct);

        var candidateBins = binProfiles.Select(b => new CandidateBinInfo
        {
            BinId = b.BinId,
            BinCode = b.Code,
            ZoneId = b.ZoneId,
            ZoneCode = b.ZoneCode,
            ZoneType = b.BinType,
            ZoneStorageType = "ambient",
            BinStorageRestriction = b.StorageRestriction,
            IsPickFace = b.IsPickFace,
            CurrentUnits = b.CurrentUnits,
            CurrentWeightKg = b.CurrentWeightKg,
            CurrentVolumeCbm = b.CurrentVolumeCbm,
            MaxUnits = b.MaxUnits,
            MaxWeightKg = b.MaxWeightKg,
            MaxVolumeCbm = b.MaxVolumeCbm,
            IsActive = b.IsActive
        }).ToList();

        var variantReq = new PutawayVariantRequirements
        {
            VariantId = variantId,
            StorageRequirement = "ambient",
            UnitWeightKg = 1.0m,
            UnitVolumeCbm = 0.01m
        };

        return DirectedPutawayEngine.RecommendPutawayBins(variantReq, quantity, candidateBins, strategies);
    }

    public async Task<Result<PutawayTaskRecord>> CreatePutawayTaskAsync(TenantContext context, Guid warehouseId, CreatePutawayTaskRequest req, CancellationToken ct)
    {
        // Get recommendation if not provided
        var targetBinId = req.TargetBinId;
        if (!targetBinId.HasValue)
        {
            var recs = await GetPutawayRecommendationsAsync(context, warehouseId, req.ProductVariantId, req.Quantity, ct);
            if (recs.Count > 0)
                targetBinId = recs[0].BinId;
            else
                targetBinId = req.SourceBinId; // Fallback
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var taskId = Guid.NewGuid();
        var taskNum = $"PUT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        cmd.CommandText = """
            INSERT INTO warehouse.putaway_tasks
                (id, tenant_id, company_id, task_number, warehouse_id, source_type,
                 source_document_id, product_variant_id, source_bin_id, suggested_bin_id,
                 quantity, putaway_status, assigned_to)
            VALUES
                (@id, @tenantId, @companyId, @num, @whId, @srcType,
                 @srcDocId, @variantId, @srcBinId, @suggBinId,
                 @qty, 'pending', @assigned)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", taskId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("num", taskNum);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("srcType", req.SourceType);
        cmd.Parameters.AddWithValue("srcDocId", (object?)req.SourceDocumentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variantId", req.ProductVariantId);
        cmd.Parameters.AddWithValue("srcBinId", req.SourceBinId);
        cmd.Parameters.AddWithValue("suggBinId", targetBinId.Value);
        cmd.Parameters.AddWithValue("qty", req.Quantity);
        cmd.Parameters.AddWithValue("assigned", (object?)req.AssignedTo ?? DBNull.Value);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
        return Result<PutawayTaskRecord>.Success(new PutawayTaskRecord(
            taskId, context.TenantId, context.CompanyId, taskNum, warehouseId,
            req.SourceType, req.SourceDocumentId, req.ProductVariantId, req.SourceBinId,
            targetBinId.Value, null, req.Quantity, "pending", req.AssignedTo, null, null, createdAt));
    }

    public async Task<IReadOnlyList<PutawayTaskRecord>> GetPutawayTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct)
    {
        var list = new List<PutawayTaskRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, task_number, warehouse_id, source_type,
                   source_document_id, product_variant_id, source_bin_id, suggested_bin_id,
                   confirmed_bin_id, quantity, putaway_status, assigned_to, scanned_bin_barcode,
                   completed_at, created_at
            FROM warehouse.putaway_tasks
            WHERE warehouse_id = @whId AND (@status IS NULL OR putaway_status = @status)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PutawayTaskRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetGuid(7),
                reader.GetGuid(8),
                reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetDecimal(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                ReadNullableDateTimeOffset(reader, 15),
                ReadDateTimeOffset(reader, 16)));
        }
        return list;
    }

    public async Task<Result<PutawayTaskRecord>> ConfirmPutawayTaskAsync(TenantContext context, Guid taskId, ConfirmPutawayRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        // Fetch task
        cmd.CommandText = """
            SELECT warehouse_id, source_bin_id, product_variant_id, quantity, putaway_status, task_number, source_type, source_document_id, suggested_bin_id, created_at
            FROM warehouse.putaway_tasks
            WHERE id = @id;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", taskId);

        Guid whId, srcBinId, variantId, suggBinId;
        decimal qty;
        string status, taskNum, srcType;
        Guid? srcDocId;
        DateTimeOffset createdAt;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Result<PutawayTaskRecord>.Failure("TASK_NOT_FOUND");

            whId = reader.GetGuid(0);
            srcBinId = reader.GetGuid(1);
            variantId = reader.GetGuid(2);
            qty = reader.GetDecimal(3);
            status = reader.GetString(4);
            taskNum = reader.GetString(5);
            srcType = reader.GetString(6);
            srcDocId = reader.IsDBNull(7) ? null : reader.GetGuid(7);
            suggBinId = reader.GetGuid(8);
            createdAt = ReadDateTimeOffset(reader, 9);
        }

        if (status == "completed")
            return Result<PutawayTaskRecord>.Failure("TASK_ALREADY_COMPLETED");

        // Execute atomic Model A FIFO transfer between bins
        var transferResult = await ExecuteBinTransferInternalAsync(conn, context, new BinStockTransferRequest(
            variantId, whId, srcBinId, whId, req.ConfirmedBinId, qty, $"Putaway task {taskNum}"), ct);

        if (!transferResult.IsSuccess)
            return Result<PutawayTaskRecord>.Failure(transferResult.Error!);

        // Update putaway task
        cmd.CommandText = """
            UPDATE warehouse.putaway_tasks
            SET confirmed_bin_id = @confBin, putaway_status = 'completed',
                scanned_bin_barcode = @barcode, completed_at = now()
            WHERE id = @id
            RETURNING completed_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", taskId);
        cmd.Parameters.AddWithValue("confBin", req.ConfirmedBinId);
        cmd.Parameters.AddWithValue("barcode", (object?)req.ScannedBinBarcode ?? DBNull.Value);

        var completedAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        return Result<PutawayTaskRecord>.Success(new PutawayTaskRecord(
            taskId, context.TenantId, context.CompanyId, taskNum, whId,
            srcType, srcDocId, variantId, srcBinId, suggBinId,
            req.ConfirmedBinId, qty, "completed", null, req.ScannedBinBarcode, completedAt, createdAt));
    }

    // ── 4. Barcodes & Scanning ────────────────────────────────────────────────
    public async Task<IReadOnlyList<BarcodeDefinitionRecord>> GetBarcodesAsync(TenantContext context, string? entityType, CancellationToken ct)
    {
        var list = new List<BarcodeDefinitionRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, barcode_value, barcode_format,
                   entity_type, entity_id, gs1_gtin, gs1_lot, gs1_serial, gs1_expiry_date,
                   is_active, created_at
            FROM warehouse.barcode_definitions
            WHERE (@type::text IS NULL OR entity_type = @type)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.Clear();
        var typeParam = new NpgsqlParameter("type", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(entityType) ? DBNull.Value : entityType.Trim().ToLowerInvariant()
        };
        cmd.Parameters.Add(typeParam);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BarcodeDefinitionRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetBoolean(11),
                ReadDateTimeOffset(reader, 12)));
        }
        return list;
    }

    public async Task<Result<BarcodeDefinitionRecord>> CreateBarcodeAsync(TenantContext context, CreateBarcodeRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var id = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO warehouse.barcode_definitions
                (id, tenant_id, company_id, barcode_value, barcode_format, entity_type, entity_id,
                 gs1_gtin, gs1_lot, gs1_serial, gs1_expiry_date, is_active)
            VALUES
                (@id, @tenantId, @companyId, @val, @fmt, @type, @entId,
                 @gtin, @lot, @ser, @exp, true)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("val", req.BarcodeValue.Trim());
        cmd.Parameters.AddWithValue("fmt", req.BarcodeFormat);
        cmd.Parameters.AddWithValue("type", req.EntityType);
        cmd.Parameters.AddWithValue("entId", req.EntityId);
        cmd.Parameters.AddWithValue("gtin", (object?)req.Gs1Gtin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lot", (object?)req.Gs1Lot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ser", (object?)req.Gs1Serial ?? DBNull.Value);
        cmd.Parameters.AddWithValue("exp", (object?)req.Gs1ExpiryDate ?? DBNull.Value);

        try
        {
            var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
            return Result<BarcodeDefinitionRecord>.Success(new BarcodeDefinitionRecord(
                id, context.TenantId, context.CompanyId, req.BarcodeValue.Trim(), req.BarcodeFormat,
                req.EntityType, req.EntityId, req.Gs1Gtin, req.Gs1Lot, req.Gs1Serial, req.Gs1ExpiryDate, true, createdAt));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Result<BarcodeDefinitionRecord>.Failure("DUPLICATE_BARCODE_VALUE");
        }
    }

    public async Task<BarcodeScanResult> ProcessBarcodeScanAsync(TenantContext context, BarcodeScanRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var barcode = req.ScannedBarcode.Trim();
        var now = DateTimeOffset.UtcNow;

        // 1. Debounce / duplicate scan check against scan_audit_logs
        cmd.CommandText = """
            SELECT scanned_barcode, scanned_at
            FROM warehouse.scan_audit_logs
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY scanned_at DESC
            LIMIT 1;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);

        string? prevBarcode = null;
        DateTimeOffset? prevTime = null;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                prevBarcode = reader.GetString(0);
                prevTime = ReadDateTimeOffset(reader, 1);
            }
        }

        if (prevBarcode != null && prevTime.HasValue && BarcodeEngine.IsDuplicateScan(barcode, now, prevBarcode, prevTime.Value, 1500))
        {
            await LogScanAuditAsync(conn, context, barcode, "unknown", null, null, req.OperationContext, req.DeviceId, "duplicate", req.IdempotencyKey, "Debounce: duplicate scan ignored.", ct);
            return new BarcodeScanResult(false, barcode, "unknown", null, null, null, null, "duplicate", "Duplicate scan detected within debounce window.");
        }

        // 2. Lookup barcode_definitions
        cmd.CommandText = """
            SELECT entity_type, entity_id, barcode_format, gs1_gtin, gs1_lot, gs1_serial, gs1_expiry_date
            FROM warehouse.barcode_definitions
            WHERE tenant_id = @tenantId AND company_id = @companyId AND barcode_value = @val AND is_active = true;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("val", barcode);

        string? entityType = null;
        Guid? entityId = null;
        string? fmt = null;
        Dictionary<string, string>? meta = null;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                entityType = reader.GetString(0);
                entityId = reader.GetGuid(1);
                fmt = reader.GetString(2);
                meta = new Dictionary<string, string>();
                if (!reader.IsDBNull(3)) meta["gtin"] = reader.GetString(3);
                if (!reader.IsDBNull(4)) meta["lot"] = reader.GetString(4);
                if (!reader.IsDBNull(5)) meta["serial"] = reader.GetString(5);
                if (!reader.IsDBNull(6)) meta["expiry"] = reader.GetString(6);
            }
        }

        if (entityType != null && entityId.HasValue && fmt != null)
        {
            await LogScanAuditAsync(conn, context, barcode, fmt, entityType, entityId, req.OperationContext, req.DeviceId, "valid", req.IdempotencyKey, null, ct);
            return new BarcodeScanResult(true, barcode, fmt, entityType, entityId, barcode, $"Registered {entityType}", "valid", null, meta);
        }

        // 3. Fallback: Parse GS1 or standard prefixes
        if (barcode.StartsWith("(01)") || barcode.Length == 14 && barcode.All(char.IsDigit))
        {
            var parsed = BarcodeEngine.ParseGs1Barcode(barcode);
            var gs1Meta = new Dictionary<string, string>();
            if (parsed.Gtin != null) gs1Meta["gtin"] = parsed.Gtin;
            if (parsed.LotNumber != null) gs1Meta["lot"] = parsed.LotNumber;
            if (parsed.SerialNumber != null) gs1Meta["serial"] = parsed.SerialNumber;
            if (parsed.ExpiryDate != null) gs1Meta["expiry"] = parsed.ExpiryDate;

            await LogScanAuditAsync(conn, context, barcode, "gs1_128", "product_variant", null, req.OperationContext, req.DeviceId, "valid", req.IdempotencyKey, null, ct);
            return new BarcodeScanResult(true, barcode, "gs1_128", "product_variant", null, parsed.Gtin ?? barcode, "GS1 Barcode Scanned", "valid", null, gs1Meta);
        }

        var (stdType, rawId) = BarcodeEngine.ParseStandardBarcode(barcode);
        if (stdType != "unknown" && stdType != "unspecified")
        {
            await LogScanAuditAsync(conn, context, barcode, "code128", stdType, null, req.OperationContext, req.DeviceId, "valid", req.IdempotencyKey, null, ct);
            return new BarcodeScanResult(true, barcode, "code128", stdType, null, rawId, $"Recognized {stdType} prefix", "valid", null);
        }

        // Check if barcode matches stock_bin.code
        cmd.CommandText = "SELECT bin_id FROM erp_inventory.stock_bin WHERE tenant_id = @tenantId AND company_id = @companyId AND code = @code LIMIT 1;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("code", barcode);
        var foundBinId = await cmd.ExecuteScalarAsync(ct) as Guid?;
        if (foundBinId.HasValue)
        {
            await LogScanAuditAsync(conn, context, barcode, "code128", "stock_bin", foundBinId.Value, req.OperationContext, req.DeviceId, "valid", req.IdempotencyKey, null, ct);
            return new BarcodeScanResult(true, barcode, "code128", "stock_bin", foundBinId.Value, barcode, "Warehouse Bin Location", "valid", null);
        }

        // Unrecognized barcode
        await LogScanAuditAsync(conn, context, barcode, "unknown", null, null, req.OperationContext, req.DeviceId, "invalid", req.IdempotencyKey, "Barcode not found in registry", ct);
        return new BarcodeScanResult(false, barcode, "unknown", null, null, null, null, "invalid", "Barcode is not recognized in warehouse registry.");
    }

    private static async Task LogScanAuditAsync(
        NpgsqlConnection conn,
        TenantContext context,
        string barcode,
        string format,
        string? entityType,
        Guid? entityId,
        string operationContext,
        string? deviceId,
        string status,
        string? idempotencyKey,
        string? errorDetails,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO warehouse.scan_audit_logs
                (id, tenant_id, company_id, scanned_barcode, barcode_format, parsed_entity_type,
                 parsed_entity_id, operation_context, device_id, status, idempotency_key, error_details)
            VALUES
                (@id, @tenantId, @companyId, @code, @fmt, @type,
                 @entId, @opContext, @devId, @status, @idemKey, @err);
            """;
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("code", barcode);
        cmd.Parameters.AddWithValue("fmt", format);
        cmd.Parameters.AddWithValue("type", (object?)entityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("entId", (object?)entityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("opContext", operationContext);
        cmd.Parameters.AddWithValue("devId", (object?)deviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("idemKey", (object?)idempotencyKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("err", (object?)errorDetails ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<dynamic>> GetScanAuditLogsAsync(TenantContext context, int limit, CancellationToken ct)
    {
        var list = new List<dynamic>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, scanned_barcode, barcode_format, parsed_entity_type,
                   operation_context, device_id, scanned_at, status, error_details
            FROM warehouse.scan_audit_logs
            ORDER BY scanned_at DESC
            LIMIT @limit;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new
            {
                Id = reader.GetGuid(0),
                ScannedBarcode = reader.GetString(1),
                BarcodeFormat = reader.GetString(2),
                ParsedEntityType = reader.IsDBNull(3) ? null : reader.GetString(3),
                OperationContext = reader.GetString(4),
                DeviceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                ScannedAt = ReadDateTimeOffset(reader, 6),
                Status = reader.GetString(7),
                ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return list;
    }

    // ── 5. Picking Waves ──────────────────────────────────────────────────────
    public async Task<IReadOnlyList<PickingWaveRecord>> GetPickingWavesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct)
    {
        var list = new List<PickingWaveRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, wave_number, warehouse_id, wave_type,
                   status, total_lines, completed_lines, priority, created_at, completed_at
            FROM warehouse.picking_waves
            WHERE warehouse_id = @whId AND (@status IS NULL OR status = @status)
            ORDER BY priority, created_at DESC;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PickingWaveRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                ReadDateTimeOffset(reader, 10),
                ReadNullableDateTimeOffset(reader, 11)));
        }
        return list;
    }

    public async Task<PickingWaveRecord?> GetPickingWaveByIdAsync(TenantContext context, Guid waveId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, wave_number, warehouse_id, wave_type,
                   status, total_lines, completed_lines, priority, created_at, completed_at
            FROM warehouse.picking_waves
            WHERE id = @id;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", waveId);

        PickingWaveRecord? wave = null;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                wave = new PickingWaveRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    ReadDateTimeOffset(reader, 10),
                    ReadNullableDateTimeOffset(reader, 11));
            }
        }

        if (wave == null) return null;

        // Fetch items
        var items = new List<WavePickItemRecord>();
        cmd.CommandText = """
            SELECT i.id, i.wave_id, i.source_document_type, i.source_document_id, i.source_line_id,
                   i.product_variant_id, i.source_bin_id, b.code, b.aisle, b.rack, b.shelf,
                   i.target_pack_station_id, i.requested_quantity, i.picked_quantity,
                   i.status, i.picker_id, i.scanned_bin_barcode, i.scanned_product_barcode, i.picked_at
            FROM warehouse.wave_pick_items i
            JOIN erp_inventory.stock_bin b ON i.source_bin_id = b.bin_id
            WHERE i.wave_id = @waveId
            ORDER BY b.aisle, b.rack, b.shelf;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("waveId", waveId);

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(new WavePickItemRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.GetGuid(5),
                    reader.GetGuid(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetGuid(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetString(17),
                    ReadNullableDateTimeOffset(reader, 18)));
            }
        }

        return wave with { Items = items };
    }

    public async Task<Result<PickingWaveRecord>> CreatePickingWaveAsync(TenantContext context, Guid warehouseId, CreatePickingWaveRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var waveId = Guid.NewGuid();
        var waveNum = $"WAV-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        cmd.CommandText = """
            INSERT INTO warehouse.picking_waves
                (id, tenant_id, company_id, wave_number, warehouse_id, wave_type, status, total_lines, completed_lines, priority)
            VALUES
                (@id, @tenantId, @companyId, @num, @whId, @type, 'created', @total, 0, @priority)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", waveId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("num", waveNum);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("type", req.WaveType);
        cmd.Parameters.AddWithValue("total", req.Items.Count);
        cmd.Parameters.AddWithValue("priority", req.Priority);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        foreach (var item in req.Items)
        {
            cmd.CommandText = """
                INSERT INTO warehouse.wave_pick_items
                    (id, tenant_id, company_id, wave_id, source_document_type, source_document_id, source_line_id,
                     product_variant_id, source_bin_id, requested_quantity, picked_quantity, status)
                VALUES
                    (@itemId, @tenantId, @companyId, @waveId, @srcType, @srcDocId, @srcLineId,
                     @variantId, @srcBinId, @qty, 0, 'pending');
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("itemId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", context.TenantId);
            cmd.Parameters.AddWithValue("companyId", context.CompanyId);
            cmd.Parameters.AddWithValue("waveId", waveId);
            cmd.Parameters.AddWithValue("srcType", item.SourceDocumentType);
            cmd.Parameters.AddWithValue("srcDocId", item.SourceDocumentId);
            cmd.Parameters.AddWithValue("srcLineId", item.SourceLineId);
            cmd.Parameters.AddWithValue("variantId", item.ProductVariantId);
            cmd.Parameters.AddWithValue("srcBinId", item.SourceBinId);
            cmd.Parameters.AddWithValue("qty", item.Quantity);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return Result<PickingWaveRecord>.Success(new PickingWaveRecord(
            waveId, context.TenantId, context.CompanyId, waveNum, warehouseId,
            req.WaveType, "created", req.Items.Count, 0, req.Priority, createdAt, null));
    }

    public async Task<Result<WavePickItemRecord>> ConfirmPickItemAsync(TenantContext context, Guid waveId, Guid itemId, ConfirmPickItemRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        // Fetch item details and bin code
        cmd.CommandText = """
            SELECT i.source_document_type, i.source_document_id, i.source_line_id, i.product_variant_id,
                   i.source_bin_id, b.code, b.aisle, b.rack, b.shelf, i.target_pack_station_id,
                   i.requested_quantity, i.picked_quantity, i.status
            FROM warehouse.wave_pick_items i
            JOIN erp_inventory.stock_bin b ON i.source_bin_id = b.bin_id
            WHERE i.id = @itemId AND i.wave_id = @waveId;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("waveId", waveId);

        string srcType, binCode, status;
        string? aisle, rack, shelf;
        Guid srcDocId, srcLineId, variantId, srcBinId;
        Guid? targetPack;
        decimal reqQty, currentPicked;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Result<WavePickItemRecord>.Failure("ITEM_NOT_FOUND");

            srcType = reader.GetString(0);
            srcDocId = reader.GetGuid(1);
            srcLineId = reader.GetGuid(2);
            variantId = reader.GetGuid(3);
            srcBinId = reader.GetGuid(4);
            binCode = reader.GetString(5);
            aisle = reader.IsDBNull(6) ? null : reader.GetString(6);
            rack = reader.IsDBNull(7) ? null : reader.GetString(7);
            shelf = reader.IsDBNull(8) ? null : reader.GetString(8);
            targetPack = reader.IsDBNull(9) ? null : reader.GetGuid(9);
            reqQty = reader.GetDecimal(10);
            currentPicked = reader.GetDecimal(11);
            status = reader.GetString(12);
        }

        var validation = WavePickingEngine.ValidatePickConfirmation(
            new PickPathItem { RequestedQuantity = reqQty, PickedQuantity = currentPicked },
            req.PickedQuantity, req.ScannedBinBarcode, binCode, req.ScannedProductBarcode);

        if (!validation.IsValid)
            return Result<WavePickItemRecord>.Failure(validation.ErrorMessage!);

        var newPicked = currentPicked + req.PickedQuantity;
        var newStatus = newPicked >= reqQty ? "picked" : "pending";

        cmd.CommandText = """
            UPDATE warehouse.wave_pick_items
            SET picked_quantity = @picked, status = @status,
                scanned_bin_barcode = @binBar, scanned_product_barcode = @prodBar,
                picked_at = now()
            WHERE id = @itemId
            RETURNING picked_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("picked", newPicked);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("binBar", (object?)req.ScannedBinBarcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("prodBar", (object?)req.ScannedProductBarcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("itemId", itemId);

        var pickedAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        // Update picking wave status & completed line count
        cmd.CommandText = """
            UPDATE warehouse.picking_waves
            SET completed_lines = (SELECT COUNT(*) FROM warehouse.wave_pick_items WHERE wave_id = @waveId AND status = 'picked'),
                status = CASE
                    WHEN (SELECT COUNT(*) FROM warehouse.wave_pick_items WHERE wave_id = @waveId AND status != 'picked') = 0 THEN 'picked'
                    ELSE 'in_progress'
                END,
                completed_at = CASE
                    WHEN (SELECT COUNT(*) FROM warehouse.wave_pick_items WHERE wave_id = @waveId AND status != 'picked') = 0 THEN now()
                    ELSE completed_at
                END
            WHERE id = @waveId;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("waveId", waveId);
        await cmd.ExecuteNonQueryAsync(ct);

        return Result<WavePickItemRecord>.Success(new WavePickItemRecord(
            itemId, waveId, srcType, srcDocId, srcLineId, variantId, srcBinId,
            binCode, aisle, rack, shelf, targetPack, reqQty, newPicked, newStatus,
            null, req.ScannedBinBarcode, req.ScannedProductBarcode, pickedAt));
    }

    public async Task<Result<PickingWaveRecord>> ReleasePickingWaveAsync(TenantContext context, Guid waveId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            UPDATE warehouse.picking_waves
            SET status = 'released'
            WHERE id = @waveId AND status = 'created'
            RETURNING id;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("waveId", waveId);

        var updated = await cmd.ExecuteScalarAsync(ct);
        if (updated == null)
            return Result<PickingWaveRecord>.Failure("WAVE_NOT_ELIGIBLE_FOR_RELEASE");

        var wave = await GetPickingWaveByIdAsync(context, waveId, ct);
        return Result<PickingWaveRecord>.Success(wave!);
    }

    // ── 6. Shipment Packages (Cartonization & Dispatch) ──────────────────────
    public async Task<IReadOnlyList<ShipmentPackageRecord>> GetShipmentPackagesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct)
    {
        var list = new List<ShipmentPackageRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, package_number, warehouse_id,
                   sales_order_id, sales_dispatch_id, package_type, tracking_number,
                   carrier_name, gross_weight_kg, net_weight_kg, status, staged_bin_id,
                   sealed_at, shipped_at, created_at
            FROM warehouse.shipment_packages
            WHERE warehouse_id = @whId AND (@status IS NULL OR status = @status)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ShipmentPackageRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                ReadNullableDateTimeOffset(reader, 14),
                ReadNullableDateTimeOffset(reader, 15),
                ReadDateTimeOffset(reader, 16)));
        }
        return list;
    }

    public async Task<Result<ShipmentPackageRecord>> CreateShipmentPackageAsync(TenantContext context, Guid warehouseId, CreatePackageRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var pkgId = Guid.NewGuid();
        var pkgNum = $"PKG-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        cmd.CommandText = """
            INSERT INTO warehouse.shipment_packages
                (id, tenant_id, company_id, package_number, warehouse_id, sales_order_id,
                 sales_dispatch_id, package_type, carrier_name, tracking_number,
                 gross_weight_kg, net_weight_kg, status)
            VALUES
                (@id, @tenantId, @companyId, @num, @whId, @soId,
                 @dispId, @type, @carrier, @track,
                 @gross, @net, 'packing')
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", pkgId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("num", pkgNum);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("soId", (object?)req.SalesOrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dispId", (object?)req.SalesDispatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", req.PackageType);
        cmd.Parameters.AddWithValue("carrier", (object?)req.CarrierName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("track", (object?)req.TrackingNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("gross", (object?)req.GrossWeightKg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("net", (object?)req.NetWeightKg ?? DBNull.Value);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        if (req.Items != null)
        {
            foreach (var item in req.Items)
            {
                cmd.CommandText = """
                    INSERT INTO warehouse.shipment_package_items
                        (id, tenant_id, company_id, package_id, product_variant_id, quantity, lot_number, serial_number)
                    VALUES
                        (@itemId, @tenantId, @companyId, @pkgId, @variantId, @qty, @lot, @ser);
                    """;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("itemId", Guid.NewGuid());
                cmd.Parameters.AddWithValue("tenantId", context.TenantId);
                cmd.Parameters.AddWithValue("companyId", context.CompanyId);
                cmd.Parameters.AddWithValue("pkgId", pkgId);
                cmd.Parameters.AddWithValue("variantId", item.ProductVariantId);
                cmd.Parameters.AddWithValue("qty", item.Quantity);
                cmd.Parameters.AddWithValue("lot", (object?)item.LotNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("ser", (object?)item.SerialNumber ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        return Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(
            pkgId, context.TenantId, context.CompanyId, pkgNum, warehouseId,
            req.SalesOrderId, req.SalesDispatchId, req.PackageType, req.TrackingNumber,
            req.CarrierName, req.GrossWeightKg, req.NetWeightKg, "packing", null, null, null, createdAt));
    }

    public async Task<Result<ShipmentPackageRecord>> SealShipmentPackageAsync(TenantContext context, Guid packageId, SealPackageRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            UPDATE warehouse.shipment_packages
            SET gross_weight_kg = @gross, staged_bin_id = @stagedBin,
                status = 'sealed', sealed_at = now()
            WHERE id = @pkgId AND status = 'packing'
            RETURNING warehouse_id, package_number, sales_order_id, sales_dispatch_id, package_type, tracking_number, carrier_name, net_weight_kg, sealed_at, created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("gross", req.GrossWeightKg);
        cmd.Parameters.AddWithValue("stagedBin", (object?)req.StagedBinId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pkgId", packageId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var whId = reader.GetGuid(0);
            var num = reader.GetString(1);
            var soId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            var dispId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
            var pType = reader.GetString(4);
            var track = reader.IsDBNull(5) ? null : reader.GetString(5);
            var carrier = reader.IsDBNull(6) ? null : reader.GetString(6);
            var net = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7);
            var sealedAt = ReadDateTimeOffset(reader, 8);
            var createdAt = ReadDateTimeOffset(reader, 9);

            return Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(
                packageId, context.TenantId, context.CompanyId, num, whId,
                soId, dispId, pType, track, carrier, req.GrossWeightKg, net, "sealed", req.StagedBinId, sealedAt, null, createdAt));
        }
        return Result<ShipmentPackageRecord>.Failure("PACKAGE_NOT_ELIGIBLE_FOR_SEALING");
    }

    public async Task<Result<ShipmentPackageRecord>> DispatchShipmentAsync(TenantContext context, Guid packageId, DispatchShipmentRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            UPDATE warehouse.shipment_packages
            SET carrier_name = @carrier, tracking_number = @track,
                status = 'shipped', shipped_at = now()
            WHERE id = @pkgId AND status IN ('packing', 'sealed')
            RETURNING warehouse_id, package_number, sales_order_id, sales_dispatch_id, package_type, gross_weight_kg, net_weight_kg, staged_bin_id, sealed_at, shipped_at, created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("carrier", req.CarrierName);
        cmd.Parameters.AddWithValue("track", req.TrackingNumber);
        cmd.Parameters.AddWithValue("pkgId", packageId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var whId = reader.GetGuid(0);
            var num = reader.GetString(1);
            var soId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            var dispId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
            var pType = reader.GetString(4);
            var gross = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);
            var net = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6);
            var stagedBin = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7);
            var sealedAt = ReadNullableDateTimeOffset(reader, 8);
            var shippedAt = ReadDateTimeOffset(reader, 9);
            var createdAt = ReadDateTimeOffset(reader, 10);

            return Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(
                packageId, context.TenantId, context.CompanyId, num, whId,
                soId, dispId, pType, req.TrackingNumber, req.CarrierName, gross, net, "shipped", stagedBin, sealedAt, shippedAt, createdAt));
        }
        return Result<ShipmentPackageRecord>.Failure("PACKAGE_NOT_FOUND_OR_ALREADY_SHIPPED");
    }

    // ── 7. Replenishment ──────────────────────────────────────────────────────
    public async Task<IReadOnlyList<ReplenishmentRuleRecord>> GetReplenishmentRulesAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        var list = new List<ReplenishmentRuleRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, warehouse_id, product_variant_id,
                   pick_bin_id, reserve_zone_id, min_quantity, max_quantity,
                   reorder_point, replenish_increment_qty, is_active, created_at
            FROM warehouse.replenishment_rules
            WHERE warehouse_id = @whId
            ORDER BY created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ReplenishmentRuleRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetBoolean(11),
                ReadDateTimeOffset(reader, 12)));
        }
        return list;
    }

    public async Task<Result<ReplenishmentRuleRecord>> CreateReplenishmentRuleAsync(TenantContext context, Guid warehouseId, CreateReplenishmentRuleRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var ruleId = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO warehouse.replenishment_rules
                (id, tenant_id, company_id, warehouse_id, product_variant_id, pick_bin_id,
                 reserve_zone_id, min_quantity, max_quantity, reorder_point, replenish_increment_qty, is_active)
            VALUES
                (@id, @tenantId, @companyId, @whId, @variantId, @pickBin,
                 @zoneId, @min, @max, @reorder, @inc, true)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", ruleId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("variantId", req.ProductVariantId);
        cmd.Parameters.AddWithValue("pickBin", req.PickBinId);
        cmd.Parameters.AddWithValue("zoneId", (object?)req.ReserveZoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("min", req.MinQuantity);
        cmd.Parameters.AddWithValue("max", req.MaxQuantity);
        cmd.Parameters.AddWithValue("reorder", req.ReorderPoint);
        cmd.Parameters.AddWithValue("inc", req.ReplenishIncrementQty);

        try
        {
            var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
            return Result<ReplenishmentRuleRecord>.Success(new ReplenishmentRuleRecord(
                ruleId, context.TenantId, context.CompanyId, warehouseId,
                req.ProductVariantId, req.PickBinId, req.ReserveZoneId,
                req.MinQuantity, req.MaxQuantity, req.ReorderPoint, req.ReplenishIncrementQty, true, createdAt));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Result<ReplenishmentRuleRecord>.Failure("DUPLICATE_REPLENISHMENT_RULE");
        }
    }

    public async Task<IReadOnlyList<ReplenishmentRecommendation>> GetReplenishmentRecommendationsAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        var rules = await GetReplenishmentRulesAsync(context, warehouseId, ct);
        var recommendations = new List<ReplenishmentRecommendation>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        foreach (var rule in rules.Where(r => r.IsActive))
        {
            // Current stock in pick bin
            cmd.CommandText = "SELECT COALESCE(on_hand_qty, 0) FROM erp_inventory.stock_balance WHERE bin_id = @binId AND variant_id = @varId;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("binId", rule.PickBinId);
            cmd.Parameters.AddWithValue("varId", rule.ProductVariantId);
            var currentStock = (decimal)(await cmd.ExecuteScalarAsync(ct) ?? 0m);

            // Fetch reserve stock
            cmd.CommandText = """
                SELECT l.bin_id, b.code, l.layer_id, l.remaining_qty, l.unit_cost, l.received_at
                FROM erp_inventory.inventory_cost_layer l
                JOIN erp_inventory.stock_bin b ON l.bin_id = b.bin_id
                WHERE l.tenant_id = @tenantId AND l.company_id = @companyId
                  AND l.warehouse_id = @whId AND l.variant_id = @varId AND l.bin_id != @pickBin AND l.remaining_qty > 0
                  AND (@reserveZoneId IS NULL OR b.zone_id = @reserveZoneId)
                ORDER BY l.received_at ASC;
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("tenantId", context.TenantId);
            cmd.Parameters.AddWithValue("companyId", context.CompanyId);
            cmd.Parameters.AddWithValue("whId", warehouseId);
            cmd.Parameters.AddWithValue("varId", rule.ProductVariantId);
            cmd.Parameters.AddWithValue("pickBin", rule.PickBinId);
            cmd.Parameters.AddWithValue("reserveZoneId", (object?)rule.ReserveZoneId ?? DBNull.Value);

            var reserveStock = new List<ReserveBinStock>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    reserveStock.Add(new ReserveBinStock
                    {
                        BinId = reader.GetGuid(0),
                        BinCode = reader.GetString(1),
                        LayerId = reader.GetGuid(2),
                        AvailableQuantity = reader.GetDecimal(3),
                        UnitCost = reader.GetDecimal(4),
                        ReceivedAt = ReadDateTimeOffset(reader, 5)
                    });
                }
            }

            var rec = ReplenishmentEngine.EvaluateReplenishment(rule, currentStock, reserveStock);
            if (rec.TriggerNeeded)
            {
                recommendations.Add(rec);
            }
        }

        return recommendations;
    }

    public async Task<Result<ReplenishmentTaskRecord>> CreateReplenishmentTaskAsync(TenantContext context, Guid warehouseId, CreateReplenishmentTaskRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var taskId = Guid.NewGuid();
        var taskNum = $"REP-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        cmd.CommandText = """
            INSERT INTO warehouse.replenishment_tasks
                (id, tenant_id, company_id, task_number, warehouse_id, rule_id,
                 product_variant_id, from_bin_id, to_bin_id, requested_quantity, transferred_quantity, status)
            VALUES
                (@id, @tenantId, @companyId, @num, @whId, @ruleId,
                 @variantId, @fromBin, @toBin, @qty, 0, 'pending')
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", taskId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("num", taskNum);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("ruleId", (object?)req.RuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variantId", req.ProductVariantId);
        cmd.Parameters.AddWithValue("fromBin", req.FromBinId);
        cmd.Parameters.AddWithValue("toBin", req.ToBinId);
        cmd.Parameters.AddWithValue("qty", req.Quantity);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
        return Result<ReplenishmentTaskRecord>.Success(new ReplenishmentTaskRecord(
            taskId, context.TenantId, context.CompanyId, taskNum, warehouseId,
            req.RuleId, req.ProductVariantId, req.FromBinId, req.ToBinId,
            req.Quantity, 0m, "pending", createdAt, null));
    }

    public async Task<IReadOnlyList<ReplenishmentTaskRecord>> GetReplenishmentTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct)
    {
        var list = new List<ReplenishmentTaskRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, task_number, warehouse_id, rule_id,
                   product_variant_id, from_bin_id, to_bin_id, requested_quantity, transferred_quantity,
                   status, created_at, completed_at
            FROM warehouse.replenishment_tasks
            WHERE warehouse_id = @whId AND (@status IS NULL OR status = @status)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ReplenishmentTaskRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetGuid(6),
                reader.GetGuid(7),
                reader.GetGuid(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetString(11),
                ReadDateTimeOffset(reader, 12),
                ReadNullableDateTimeOffset(reader, 13)));
        }
        return list;
    }

    public async Task<Result<ReplenishmentTaskRecord>> ExecuteReplenishmentTaskAsync(TenantContext context, Guid taskId, decimal quantity, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT warehouse_id, rule_id, product_variant_id, from_bin_id, to_bin_id, requested_quantity, transferred_quantity, status, task_number, created_at
            FROM warehouse.replenishment_tasks
            WHERE id = @id;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", taskId);

        Guid whId, variantId, fromBin, toBin;
        Guid? ruleId;
        decimal reqQty, transferred;
        string status, taskNum;
        DateTimeOffset createdAt;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Result<ReplenishmentTaskRecord>.Failure("TASK_NOT_FOUND");

            whId = reader.GetGuid(0);
            ruleId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            variantId = reader.GetGuid(2);
            fromBin = reader.GetGuid(3);
            toBin = reader.GetGuid(4);
            reqQty = reader.GetDecimal(5);
            transferred = reader.GetDecimal(6);
            status = reader.GetString(7);
            taskNum = reader.GetString(8);
            createdAt = ReadDateTimeOffset(reader, 9);
        }

        if (status == "completed")
            return Result<ReplenishmentTaskRecord>.Failure("TASK_ALREADY_COMPLETED");

        var transferResult = await ExecuteBinTransferInternalAsync(conn, context, new BinStockTransferRequest(
            variantId, whId, fromBin, whId, toBin, quantity, $"Replenishment task {taskNum}"), ct);

        if (!transferResult.IsSuccess)
            return Result<ReplenishmentTaskRecord>.Failure(transferResult.Error!);

        var newTransferred = transferred + quantity;
        var newStatus = newTransferred >= reqQty ? "completed" : "in_progress";

        cmd.CommandText = """
            UPDATE warehouse.replenishment_tasks
            SET transferred_quantity = @transferred, status = @status,
                completed_at = CASE WHEN @status = 'completed' THEN now() ELSE completed_at END
            WHERE id = @id
            RETURNING completed_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("transferred", newTransferred);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("id", taskId);

        var completedAt = ToNullableDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        return Result<ReplenishmentTaskRecord>.Success(new ReplenishmentTaskRecord(
            taskId, context.TenantId, context.CompanyId, taskNum, whId,
            ruleId, variantId, fromBin, toBin, reqQty, newTransferred, newStatus, createdAt, completedAt));
    }

    // ── 8. Bin-to-Bin Transfers (Model A FIFO Preserving) ─────────────────────
    public async Task<Result<dynamic>> ExecuteBinTransferAsync(TenantContext context, BinStockTransferRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await ExecuteBinTransferInternalAsync(conn, context, req, ct);
    }

    private static async Task<Result<dynamic>> ExecuteBinTransferInternalAsync(
        NpgsqlConnection conn,
        TenantContext context,
        BinStockTransferRequest req,
        CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        await SetContextAsync(cmd, context);

        // 1. Verify available stock in source bin
        cmd.CommandText = """
            SELECT COALESCE(on_hand_qty, 0)
            FROM erp_inventory.stock_balance
            WHERE warehouse_id = @whId AND bin_id = @binId AND variant_id = @varId
            FOR UPDATE;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", req.SourceWarehouseId);
        cmd.Parameters.AddWithValue("binId", req.SourceBinId);
        cmd.Parameters.AddWithValue("varId", req.VariantId);

        var onHand = (decimal)(await cmd.ExecuteScalarAsync(ct) ?? 0m);
        if (onHand < req.Quantity)
        {
            await tx.RollbackAsync(ct);
            return Result<dynamic>.Failure($"INSUFFICIENT_STOCK: Source bin on-hand ({onHand}) is less than transfer quantity ({req.Quantity}).");
        }

        // 2. Fetch active FIFO layers in source bin in chronological order (oldest first)
        cmd.CommandText = """
            SELECT layer_id, remaining_qty, unit_cost, currency_code, source_document_type, source_document_id, source_line_number, received_at
            FROM erp_inventory.inventory_cost_layer
            WHERE warehouse_id = @whId AND bin_id = @binId AND variant_id = @varId AND remaining_qty > 0
            ORDER BY received_at ASC
            FOR UPDATE;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", req.SourceWarehouseId);
        cmd.Parameters.AddWithValue("binId", req.SourceBinId);
        cmd.Parameters.AddWithValue("varId", req.VariantId);

        var layers = new List<(Guid LayerId, decimal RemainingQty, decimal UnitCost, string Currency, string DocType, Guid? DocId, int? LineNo, DateTimeOffset ReceivedAt)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                layers.Add((
                    reader.GetGuid(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    ReadDateTimeOffset(reader, 7)));
            }
        }

        var availableValued = layers.Sum(l => l.RemainingQty);
        if (availableValued < req.Quantity)
        {
            await tx.RollbackAsync(ct);
            return Result<dynamic>.Failure($"INSUFFICIENT_VALUED_LAYERS: Available valued layers ({availableValued}) in source bin cannot cover requested quantity ({req.Quantity}).");
        }

        // 3. Record inventory movement
        var movementId = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO erp_inventory.inventory_movement
                (movement_id, tenant_id, company_id, client_transaction_id, movement_type,
                 source_document_type, source_document_id, occurred_at, created_by)
            VALUES
                (@movId, @tenantId, @companyId, @movId, 'transfer',
                 'warehouse_transfer', @movId, now(), @createdBy);
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("movId", movementId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("createdBy", context.UserId != Guid.Empty ? context.UserId : Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync(ct);

        // 4. Consume source FIFO layers and create child destination FIFO layers
        var remainingToTransfer = req.Quantity;
        var allocLineNo = 1;

        foreach (var layer in layers)
        {
            if (remainingToTransfer <= 0) break;

            var takeQty = Math.Min(remainingToTransfer, layer.RemainingQty);

            // Decrement source layer
            cmd.CommandText = """
                UPDATE erp_inventory.inventory_cost_layer
                SET remaining_qty = remaining_qty - @qty
                WHERE layer_id = @layerId;
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("qty", takeQty);
            cmd.Parameters.AddWithValue("layerId", layer.LayerId);
            await cmd.ExecuteNonQueryAsync(ct);

            // Record cost allocation
            cmd.CommandText = """
                INSERT INTO erp_inventory.inventory_cost_allocation
                    (tenant_id, company_id, allocation_id, layer_id, movement_id,
                     source_document_type, source_document_id, source_line_number,
                     variant_id, consumed_qty, unit_cost, extended_cost, currency_code)
                VALUES
                    (@tenantId, @companyId, @allocId, @layerId, @movId,
                     'warehouse_transfer', @movId, @lineNo,
                     @varId, @qty, @unitCost, @extCost, @curr);
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("tenantId", context.TenantId);
            cmd.Parameters.AddWithValue("companyId", context.CompanyId);
            cmd.Parameters.AddWithValue("allocId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("layerId", layer.LayerId);
            cmd.Parameters.AddWithValue("movId", movementId);
            cmd.Parameters.AddWithValue("lineNo", allocLineNo++);
            cmd.Parameters.AddWithValue("varId", req.VariantId);
            cmd.Parameters.AddWithValue("qty", takeQty);
            cmd.Parameters.AddWithValue("unitCost", layer.UnitCost);
            cmd.Parameters.AddWithValue("extCost", takeQty * layer.UnitCost);
            cmd.Parameters.AddWithValue("curr", layer.Currency);
            await cmd.ExecuteNonQueryAsync(ct);

            // Create destination child layer preserving receipt timestamp and original unit cost
            cmd.CommandText = """
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, parent_layer_id, variant_id,
                     warehouse_id, bin_id, status_code, source_document_type,
                     source_document_id, source_line_number, source_movement_id,
                     received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tenantId, @companyId, @childId, @parentLayerId, @varId,
                     @destWhId, @destBinId, 'available', @docType,
                     @docId, @lineNoVal, @movId,
                     @recAt, @origQty, @remQty, @unitCost, @curr);
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("tenantId", context.TenantId);
            cmd.Parameters.AddWithValue("companyId", context.CompanyId);
            cmd.Parameters.AddWithValue("childId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("parentLayerId", layer.LayerId);
            cmd.Parameters.AddWithValue("varId", req.VariantId);
            cmd.Parameters.AddWithValue("destWhId", req.DestinationWarehouseId);
            cmd.Parameters.AddWithValue("destBinId", req.DestinationBinId);
            cmd.Parameters.AddWithValue("docType", layer.DocType);
            cmd.Parameters.AddWithValue("docId", (object?)layer.DocId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("lineNoVal", (object?)layer.LineNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("movId", movementId);
            cmd.Parameters.AddWithValue("recAt", layer.ReceivedAt);
            cmd.Parameters.AddWithValue("origQty", takeQty);
            cmd.Parameters.AddWithValue("remQty", takeQty);
            cmd.Parameters.AddWithValue("unitCost", layer.UnitCost);
            cmd.Parameters.AddWithValue("curr", layer.Currency);
            await cmd.ExecuteNonQueryAsync(ct);

            remainingToTransfer -= takeQty;
        }

        // 5. Update stock balances
        var balanceId = Guid.NewGuid();
        cmd.CommandText = """
            UPDATE erp_inventory.stock_balance
            SET on_hand_qty = on_hand_qty - @qty, updated_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND warehouse_id = @srcWh AND bin_id = @srcBin AND variant_id = @varId AND status_code = 'available';

            INSERT INTO erp_inventory.stock_balance
                (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
            VALUES
                (@tenantId, @companyId, @varId, @destWh, @destBin, 'available', @qty, @balanceId)
            ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
            DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now();
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("qty", req.Quantity);
        cmd.Parameters.AddWithValue("srcWh", req.SourceWarehouseId);
        cmd.Parameters.AddWithValue("srcBin", req.SourceBinId);
        cmd.Parameters.AddWithValue("varId", req.VariantId);
        cmd.Parameters.AddWithValue("destWh", req.DestinationWarehouseId);
        cmd.Parameters.AddWithValue("destBin", req.DestinationBinId);
        cmd.Parameters.AddWithValue("balanceId", balanceId);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        await cmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);

        return Result<dynamic>.Success(new
        {
            MovementId = movementId,
            Quantity = req.Quantity,
            SourceWarehouseId = req.SourceWarehouseId,
            SourceBinId = req.SourceBinId,
            DestinationWarehouseId = req.DestinationWarehouseId,
            DestinationBinId = req.DestinationBinId
        });
    }

    // ── 9. Quarantine & Returns ───────────────────────────────────────────────
    public async Task<IReadOnlyList<QuarantineRecord>> GetQuarantineRecordsAsync(TenantContext context, Guid warehouseId, string? disposition, CancellationToken ct)
    {
        var list = new List<QuarantineRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT id, tenant_id, company_id, record_number, warehouse_id,
                   product_variant_id, source_type, source_reference_id,
                   quarantine_bin_id, quantity, reason, disposition,
                   target_disposition_bin_id, inspected_by, disposed_at,
                   disposition_notes, created_at
            FROM warehouse.quarantine_records
            WHERE warehouse_id = @whId AND (@disp IS NULL OR disposition = @disp)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("disp", (object?)disposition ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new QuarantineRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetGuid(8),
                reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                ReadNullableDateTimeOffset(reader, 14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                ReadDateTimeOffset(reader, 16)));
        }
        return list;
    }

    public async Task<Result<QuarantineRecord>> CreateQuarantineRecordAsync(TenantContext context, Guid warehouseId, CreateQuarantineRecordRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var id = Guid.NewGuid();
        var recordNum = $"QAR-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        cmd.CommandText = """
            INSERT INTO warehouse.quarantine_records
                (id, tenant_id, company_id, record_number, warehouse_id, product_variant_id,
                 source_type, source_reference_id, quarantine_bin_id, quantity, reason,
                 disposition, inspected_by)
            VALUES
                (@id, @tenantId, @companyId, @num, @whId, @variantId,
                 @srcType, @srcRef, @binId, @qty, @reason,
                 'pending', @inspected)
            RETURNING created_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("num", recordNum);
        cmd.Parameters.AddWithValue("whId", warehouseId);
        cmd.Parameters.AddWithValue("variantId", req.ProductVariantId);
        cmd.Parameters.AddWithValue("srcType", req.SourceType);
        cmd.Parameters.AddWithValue("srcRef", (object?)req.SourceReferenceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("binId", req.QuarantineBinId);
        cmd.Parameters.AddWithValue("qty", req.Quantity);
        cmd.Parameters.AddWithValue("reason", req.Reason);
        cmd.Parameters.AddWithValue("inspected", (object?)req.InspectedBy ?? DBNull.Value);

        var createdAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));
        return Result<QuarantineRecord>.Success(new QuarantineRecord(
            id, context.TenantId, context.CompanyId, recordNum, warehouseId,
            req.ProductVariantId, req.SourceType, req.SourceReferenceId,
            req.QuarantineBinId, req.Quantity, req.Reason, "pending", null, req.InspectedBy, null, null, createdAt));
    }

    public async Task<Result<QuarantineRecord>> DisposeQuarantineRecordAsync(TenantContext context, Guid recordId, DisposeQuarantineRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        cmd.CommandText = """
            SELECT warehouse_id, product_variant_id, source_type, source_reference_id,
                   quarantine_bin_id, quantity, reason, disposition, record_number, created_at
            FROM warehouse.quarantine_records
            WHERE id = @id;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("id", recordId);

        Guid whId, variantId, qBinId;
        Guid? srcRef;
        decimal qty;
        string srcType, reason, currentDisp, num;
        DateTimeOffset createdAt;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Result<QuarantineRecord>.Failure("RECORD_NOT_FOUND");

            whId = reader.GetGuid(0);
            variantId = reader.GetGuid(1);
            srcType = reader.GetString(2);
            srcRef = reader.IsDBNull(3) ? null : reader.GetGuid(3);
            qBinId = reader.GetGuid(4);
            qty = reader.GetDecimal(5);
            reason = reader.GetString(6);
            currentDisp = reader.GetString(7);
            num = reader.GetString(8);
            createdAt = ReadDateTimeOffset(reader, 9);
        }

        if (currentDisp != "pending")
            return Result<QuarantineRecord>.Failure("RECORD_ALREADY_DISPOSED");

        // If releasing to stock and target bin is specified, transfer stock out of quarantine if physical balance exists
        if (req.Disposition == "released_to_stock" && req.TargetDispositionBinId.HasValue)
        {
            cmd.CommandText = "SELECT COALESCE(SUM(on_hand_qty), 0) FROM erp_inventory.stock_balance WHERE tenant_id = @tenantId AND company_id = @companyId AND bin_id = @qBin AND variant_id = @varId;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("tenantId", context.TenantId);
            cmd.Parameters.AddWithValue("companyId", context.CompanyId);
            cmd.Parameters.AddWithValue("qBin", qBinId);
            cmd.Parameters.AddWithValue("varId", variantId);
            var qStock = (decimal)(await cmd.ExecuteScalarAsync(ct) ?? 0m);

            if (qStock >= qty)
            {
                var transferResult = await ExecuteBinTransferInternalAsync(conn, context, new BinStockTransferRequest(
                    variantId, whId, qBinId, whId, req.TargetDispositionBinId.Value, qty, $"Quarantine release: {num}"), ct);

                if (!transferResult.IsSuccess)
                    return Result<QuarantineRecord>.Failure(transferResult.Error!);
            }
        }

        cmd.CommandText = """
            UPDATE warehouse.quarantine_records
            SET disposition = @disp, target_disposition_bin_id = @targetBin,
                disposition_notes = @notes, disposed_at = now()
            WHERE id = @id
            RETURNING disposed_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("disp", req.Disposition);
        cmd.Parameters.AddWithValue("targetBin", (object?)req.TargetDispositionBinId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)req.DispositionNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", recordId);

        var disposedAt = ToDateTimeOffset(await cmd.ExecuteScalarAsync(ct));

        return Result<QuarantineRecord>.Success(new QuarantineRecord(
            recordId, context.TenantId, context.CompanyId, num, whId,
            variantId, srcType, srcRef, qBinId, qty, reason, req.Disposition,
            req.TargetDispositionBinId, null, disposedAt, req.DispositionNotes, createdAt));
    }

    // ── 10. Warehouse Metrics ────────────────────────────────────────────────
    public async Task<WarehouseMetricsReport> GetWarehouseMetricsReportAsync(TenantContext context, Guid warehouseId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        await SetContextAsync(cmd, context);

        var zones = await GetZonesAsync(context, warehouseId, ct);
        var binProfiles = await GetBinProfilesAsync(context, warehouseId, ct);

        // Counts
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM warehouse.putaway_tasks WHERE warehouse_id = @whId AND putaway_status = 'pending') as pending_putaway,
                (SELECT COUNT(*) FROM warehouse.picking_waves WHERE warehouse_id = @whId AND status IN ('created', 'released', 'in_progress')) as open_waves,
                (SELECT COUNT(*) FROM warehouse.replenishment_tasks WHERE warehouse_id = @whId AND status IN ('pending', 'in_progress')) as pending_replen,
                (SELECT COUNT(*) FROM warehouse.quarantine_records WHERE warehouse_id = @whId AND disposition = 'pending') as pending_quarantine;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("whId", warehouseId);

        int pendingPutaway = 0, openWaves = 0, pendingReplen = 0, pendingQuarantine = 0;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                pendingPutaway = Convert.ToInt32(reader.GetInt64(0));
                openWaves = Convert.ToInt32(reader.GetInt64(1));
                pendingReplen = Convert.ToInt32(reader.GetInt64(2));
                pendingQuarantine = Convert.ToInt32(reader.GetInt64(3));
            }
        }

        var totalBins = binProfiles.Count;
        var pickFaces = binProfiles.Count(b => b.IsPickFace);
        var avgUtil = totalBins > 0
            ? Math.Round(binProfiles.Average(b => b.UtilizationPercent), 1)
            : 0m;

        var zoneSummaries = zones.Select(z =>
        {
            var zBins = binProfiles.Where(b => b.ZoneId == z.Id).ToList();
            var zUtil = zBins.Count > 0 ? Math.Round(zBins.Average(b => b.UtilizationPercent), 1) : 0m;
            return new WarehouseZoneSummary(z.Id, z.ZoneCode, z.Name, z.ZoneType, zBins.Count, zUtil);
        }).ToList();

        return new WarehouseMetricsReport(
            zones.Count, totalBins, pickFaces, avgUtil,
            pendingPutaway, openWaves, pendingReplen, pendingQuarantine, zoneSummaries);
    }
}
