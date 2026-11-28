using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Manufacturing;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Finance;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Manufacturing;

public sealed class PostgresManufacturingRepository(
    NpgsqlDataSource dataSource,
    ITenantContextAccessor tenantContext) : IManufacturingRepository
{
    private TenantContext Ctx => tenantContext.Current;

    private static async Task SetScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        command.Parameters.AddWithValue("companyId", companyId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> GetOrCreateOpenPeriodAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        const string checkSql = """
            SELECT id FROM finance.fiscal_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND status = 'open' AND starts_on <= @date AND ends_on >= @date
            LIMIT 1;
            """;
        await using (var cmd = new NpgsqlCommand(checkSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("date", date);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result is Guid g) return g;
        }

        // Fallback: any open period
        const string fallbackSql = """
            SELECT id FROM finance.fiscal_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open'
            ORDER BY starts_on DESC LIMIT 1;
            """;
        await using (var cmd = new NpgsqlCommand(fallbackSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result is Guid g) return g;
        }

        // Create one if none exists (e.g. in test fixture)
        var yearId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var startOfYear = new DateOnly(date.Year, 1, 1);
        var endOfYear = new DateOnly(date.Year, 12, 31);

        const string createYearSql = """
            INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
            VALUES (@tenantId, @companyId, @yearId, @code, @start, @end, 'open')
            ON CONFLICT (tenant_id, company_id, code) DO UPDATE SET status = 'open'
            RETURNING id;
            """;
        await using (var cmd = new NpgsqlCommand(createYearSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("yearId", yearId);
            cmd.Parameters.AddWithValue("code", $"FY{date.Year}");
            cmd.Parameters.AddWithValue("start", startOfYear);
            cmd.Parameters.AddWithValue("end", endOfYear);
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (res is Guid actualYearId) yearId = actualYearId;
        }

        const string createPeriodSql = """
            INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
            VALUES (@tenantId, @companyId, @periodId, @yearId, @num, @name, @start, @end, 'open')
            ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO UPDATE SET status = 'open'
            RETURNING id;
            """;
        await using (var cmd = new NpgsqlCommand(createPeriodSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("periodId", periodId);
            cmd.Parameters.AddWithValue("yearId", yearId);
            cmd.Parameters.AddWithValue("num", (short)date.Month);
            cmd.Parameters.AddWithValue("name", $"Period {date.Month}");
            cmd.Parameters.AddWithValue("start", new DateOnly(date.Year, date.Month, 1));
            cmd.Parameters.AddWithValue("end", new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (res is Guid actualPeriodId) return actualPeriodId;
        }

        return periodId;
    }

    private static async Task<Guid> GetAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        string accountCode,
        string fallbackType,
        string fallbackNormal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id FROM finance.accounts
            WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = @code
            LIMIT 1;
            """;
        await using (var cmd = new NpgsqlCommand(sql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("code", accountCode);
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (res != null && res is Guid g) return g;
        }

        // If not found, create it on demand
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            connection, transaction, tenantId, companyId, cancellationToken);
        var newId = Guid.NewGuid();
        const string insertSql = """
            INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
            VALUES (@tenantId, @companyId, @newId, @code, @name, @type, @normal, @currency, true, false, true)
            ON CONFLICT (tenant_id, company_id, account_code) DO UPDATE SET is_active = true
            RETURNING id;
            """;
        await using (var cmd = new NpgsqlCommand(insertSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("newId", newId);
            cmd.Parameters.AddWithValue("code", accountCode);
            cmd.Parameters.AddWithValue("name", $"Manufacturing Account {accountCode}");
            cmd.Parameters.AddWithValue("type", fallbackType);
            cmd.Parameters.AddWithValue("normal", fallbackNormal);
            cmd.Parameters.AddWithValue("currency", functionalCurrency);
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (res is Guid createdId) return createdId;
        }

        return newId;
    }

    // ── Work Centers ──────────────────────────────────────────────────────────

    public async Task<WorkCenterRecord> CreateWorkCenterAsync(WorkCenterRecord record, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            INSERT INTO manufacturing.work_centers
                (tenant_id, company_id, id, code, name, hourly_labor_rate, hourly_overhead_rate, daily_capacity_hours, efficiency_percent, is_active, created_at, updated_at)
            VALUES
                (@tid, @cid, @id, @code, @name, @laborRate, @overheadRate, @dailyCap, @efficiency, @isActive, @createdAt, @updatedAt)
            RETURNING id, code, name, hourly_labor_rate, hourly_overhead_rate, daily_capacity_hours, efficiency_percent, is_active, created_at, updated_at;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("code", record.Code);
        cmd.Parameters.AddWithValue("name", record.Name);
        cmd.Parameters.AddWithValue("laborRate", record.HourlyLaborRate);
        cmd.Parameters.AddWithValue("overheadRate", record.HourlyOverheadRate);
        cmd.Parameters.AddWithValue("dailyCap", record.DailyCapacityHours);
        cmd.Parameters.AddWithValue("efficiency", record.EfficiencyPercent);
        cmd.Parameters.AddWithValue("isActive", record.IsActive);
        cmd.Parameters.AddWithValue("createdAt", record.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", record.UpdatedAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var res = new WorkCenterRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9));

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return res;
    }

    public async Task<IReadOnlyList<WorkCenterRecord>> ListWorkCentersAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, code, name, hourly_labor_rate, hourly_overhead_rate, daily_capacity_hours, efficiency_percent, is_active, created_at, updated_at
            FROM manufacturing.work_centers
            WHERE tenant_id = @tid AND company_id = @cid
            ORDER BY code;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<WorkCenterRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new WorkCenterRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetBoolean(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<WorkCenterRecord?> GetWorkCenterByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, code, name, hourly_labor_rate, hourly_overhead_rate, daily_capacity_hours, efficiency_percent, is_active, created_at, updated_at
            FROM manufacturing.work_centers
            WHERE tenant_id = @tid AND company_id = @cid AND id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var res = new WorkCenterRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9));

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return res;
    }

    // ── Routings & Operations ──────────────────────────────────────────────────

    public async Task<RoutingRecord> CreateRoutingAsync(RoutingRecord record, IReadOnlyList<RoutingOperationRecord> operations, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            INSERT INTO manufacturing.routings
                (tenant_id, company_id, id, routing_number, name, product_variant_id, is_active, description, created_at, updated_at)
            VALUES
                (@tid, @cid, @id, @num, @name, @variantId, @isActive, @desc, @createdAt, @updatedAt)
            RETURNING id, routing_number, name, product_variant_id, is_active, description, created_at;
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", record.Id);
            cmd.Parameters.AddWithValue("num", record.RoutingNumber);
            cmd.Parameters.AddWithValue("name", record.Name);
            cmd.Parameters.AddWithValue("variantId", record.ProductVariantId);
            cmd.Parameters.AddWithValue("isActive", record.IsActive);
            cmd.Parameters.AddWithValue("desc", (object?)record.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", record.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", record.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var savedOps = new List<RoutingOperationRecord>();
        foreach (var op in operations)
        {
            const string opSql = """
                INSERT INTO manufacturing.routing_operations
                    (tenant_id, company_id, id, routing_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, description, created_at)
                VALUES
                    (@tid, @cid, @id, @rid, @seq, @opName, @wcid, @setup, @run, @desc, @createdAt)
                RETURNING id, routing_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, description, created_at;
                """;
            await using var cmd = new NpgsqlCommand(opSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", op.Id);
            cmd.Parameters.AddWithValue("rid", record.Id);
            cmd.Parameters.AddWithValue("seq", op.OperationSequence);
            cmd.Parameters.AddWithValue("opName", op.OperationName);
            cmd.Parameters.AddWithValue("wcid", op.WorkCenterId);
            cmd.Parameters.AddWithValue("setup", op.SetupTimeMinutes);
            cmd.Parameters.AddWithValue("run", op.RunTimeMinutesPerUnit);
            cmd.Parameters.AddWithValue("desc", (object?)op.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", op.CreatedAt);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            savedOps.Add(new RoutingOperationRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return record with { Operations = savedOps };
    }

    public async Task<IReadOnlyList<RoutingRecord>> ListRoutingsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, routing_number, name, product_variant_id, is_active, description, created_at
            FROM manufacturing.routings
            WHERE tenant_id = @tid AND company_id = @cid
            ORDER BY routing_number;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<RoutingRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RoutingRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<RoutingRecord?> GetRoutingByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, routing_number, name, product_variant_id, is_active, description, created_at
            FROM manufacturing.routings
            WHERE tenant_id = @tid AND company_id = @cid AND id = @id;
            """;

        RoutingRecord? routing = null;
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                routing = new RoutingRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6));
            }
            await reader.CloseAsync();
        }

        if (routing == null) return null;

        const string opsSql = """
            SELECT id, routing_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, description, created_at
            FROM manufacturing.routing_operations
            WHERE tenant_id = @tid AND company_id = @cid AND routing_id = @id
            ORDER BY operation_sequence;
            """;

        var ops = new List<RoutingOperationRecord>();
        await using (var cmd = new NpgsqlCommand(opsSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ops.Add(new RoutingOperationRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetFieldValue<DateTimeOffset>(8)));
            }
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return routing with { Operations = ops };
    }

    // ── BOMs & Revisions ───────────────────────────────────────────────────────

    public async Task<BomRecord> CreateBomAsync(BomRecord record, BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string bomSql = """
            INSERT INTO manufacturing.boms
                (tenant_id, company_id, id, bom_number, product_variant_id, name, base_quantity, uom_id, yield_percent, current_revision, status, created_at, updated_at)
            VALUES
                (@tid, @cid, @id, @num, @vid, @name, @baseQty, @uomId, @yield, @rev, @status, @createdAt, @updatedAt)
            RETURNING id, bom_number, product_variant_id, name, base_quantity, uom_id, yield_percent, current_revision, status, created_at, updated_at;
            """;

        await using (var cmd = new NpgsqlCommand(bomSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", record.Id);
            cmd.Parameters.AddWithValue("num", record.BomNumber);
            cmd.Parameters.AddWithValue("vid", record.ProductVariantId);
            cmd.Parameters.AddWithValue("name", record.Name);
            cmd.Parameters.AddWithValue("baseQty", record.BaseQuantity);
            cmd.Parameters.AddWithValue("uomId", (object?)record.UomId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("yield", record.YieldPercent);
            cmd.Parameters.AddWithValue("rev", record.CurrentRevision);
            cmd.Parameters.AddWithValue("status", record.Status);
            cmd.Parameters.AddWithValue("createdAt", record.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", record.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string revSql = """
            INSERT INTO manufacturing.bom_revisions
                (tenant_id, company_id, id, bom_id, revision_code, effective_from, effective_to, status, change_reason, approved_by, approved_at, created_at)
            VALUES
                (@tid, @cid, @id, @bomId, @code, @from, @to, @status, @reason, @by, @at, @createdAt)
            RETURNING id, bom_id, revision_code, effective_from, effective_to, status, change_reason, approved_by, approved_at, created_at;
            """;

        await using (var cmd = new NpgsqlCommand(revSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", revision.Id);
            cmd.Parameters.AddWithValue("bomId", record.Id);
            cmd.Parameters.AddWithValue("code", revision.RevisionCode);
            cmd.Parameters.AddWithValue("from", revision.EffectiveFrom);
            cmd.Parameters.AddWithValue("to", (object?)revision.EffectiveTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status", revision.Status);
            cmd.Parameters.AddWithValue("reason", (object?)revision.ChangeReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("by", (object?)revision.ApprovedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("at", (object?)revision.ApprovedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", revision.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var savedItems = new List<BomItemRecord>();
        foreach (var itm in items)
        {
            const string itmSql = """
                INSERT INTO manufacturing.bom_items
                    (tenant_id, company_id, id, bom_revision_id, line_number, component_variant_id, component_type, quantity_per, scrap_percentage, notes, created_at)
                VALUES
                    (@tid, @cid, @id, @revid, @line, @cvid, @ctype, @qty, @scrap, @notes, @createdAt)
                RETURNING id, bom_revision_id, line_number, component_variant_id, component_type, quantity_per, scrap_percentage, notes, created_at;
                """;

            await using var cmd = new NpgsqlCommand(itmSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", itm.Id);
            cmd.Parameters.AddWithValue("revid", revision.Id);
            cmd.Parameters.AddWithValue("line", itm.LineNumber);
            cmd.Parameters.AddWithValue("cvid", itm.ComponentVariantId);
            cmd.Parameters.AddWithValue("ctype", itm.ComponentType);
            cmd.Parameters.AddWithValue("qty", itm.QuantityPer);
            cmd.Parameters.AddWithValue("scrap", itm.ScrapPercentage);
            cmd.Parameters.AddWithValue("notes", (object?)itm.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", itm.CreatedAt);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            savedItems.Add(new BomItemRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        var revRecord = revision with { Items = savedItems };
        return record with { Revisions = new[] { revRecord } };
    }

    public async Task<BomRevisionRecord> CreateBomRevisionAsync(BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string revSql = """
            INSERT INTO manufacturing.bom_revisions
                (tenant_id, company_id, id, bom_id, revision_code, effective_from, effective_to, status, change_reason, approved_by, approved_at, created_at)
            VALUES
                (@tid, @cid, @id, @bomId, @code, @from, @to, @status, @reason, @by, @at, @createdAt);
            """;

        await using (var cmd = new NpgsqlCommand(revSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", revision.Id);
            cmd.Parameters.AddWithValue("bomId", revision.BomId);
            cmd.Parameters.AddWithValue("code", revision.RevisionCode);
            cmd.Parameters.AddWithValue("from", revision.EffectiveFrom);
            cmd.Parameters.AddWithValue("to", (object?)revision.EffectiveTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status", revision.Status);
            cmd.Parameters.AddWithValue("reason", (object?)revision.ChangeReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("by", (object?)revision.ApprovedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("at", (object?)revision.ApprovedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", revision.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var savedItems = new List<BomItemRecord>();
        foreach (var itm in items)
        {
            const string itmSql = """
                INSERT INTO manufacturing.bom_items
                    (tenant_id, company_id, id, bom_revision_id, line_number, component_variant_id, component_type, quantity_per, scrap_percentage, notes, created_at)
                VALUES
                    (@tid, @cid, @id, @revid, @line, @cvid, @ctype, @qty, @scrap, @notes, @createdAt)
                RETURNING id, bom_revision_id, line_number, component_variant_id, component_type, quantity_per, scrap_percentage, notes, created_at;
                """;

            await using var cmd = new NpgsqlCommand(itmSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", itm.Id);
            cmd.Parameters.AddWithValue("revid", revision.Id);
            cmd.Parameters.AddWithValue("line", itm.LineNumber);
            cmd.Parameters.AddWithValue("cvid", itm.ComponentVariantId);
            cmd.Parameters.AddWithValue("ctype", itm.ComponentType);
            cmd.Parameters.AddWithValue("qty", itm.QuantityPer);
            cmd.Parameters.AddWithValue("scrap", itm.ScrapPercentage);
            cmd.Parameters.AddWithValue("notes", (object?)itm.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", itm.CreatedAt);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            savedItems.Add(new BomItemRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
            await reader.CloseAsync();
        }

        // Update current_revision in BOM
        const string updateBomSql = """
            UPDATE manufacturing.boms
            SET current_revision = @rev, updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @bomId;
            """;
        await using (var cmd = new NpgsqlCommand(updateBomSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("rev", revision.RevisionCode);
            cmd.Parameters.AddWithValue("bomId", revision.BomId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return revision with { Items = savedItems };
    }

    public async Task<IReadOnlyList<BomRecord>> ListBomsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, bom_number, product_variant_id, name, base_quantity, uom_id, yield_percent, current_revision, status, created_at, updated_at
            FROM manufacturing.boms
            WHERE tenant_id = @tid AND company_id = @cid
            ORDER BY bom_number;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<BomRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BomRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetDecimal(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<BomRecord?> GetBomByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, bom_number, product_variant_id, name, base_quantity, uom_id, yield_percent, current_revision, status, created_at, updated_at
            FROM manufacturing.boms
            WHERE tenant_id = @tid AND company_id = @cid AND id = @id;
            """;

        BomRecord? bom = null;
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                bom = new BomRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetDecimal(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetFieldValue<DateTimeOffset>(10));
            }
            await reader.CloseAsync();
        }

        if (bom == null) return null;

        const string revSql = """
            SELECT id, bom_id, revision_code, effective_from, effective_to, status, change_reason, approved_by, approved_at, created_at
            FROM manufacturing.bom_revisions
            WHERE tenant_id = @tid AND company_id = @cid AND bom_id = @id
            ORDER BY revision_code;
            """;

        var revs = new List<BomRevisionRecord>();
        await using (var cmd = new NpgsqlCommand(revSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                revs.Add(new BomRevisionRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateOnly>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetGuid(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.GetFieldValue<DateTimeOffset>(9)));
            }
            await reader.CloseAsync();
        }

        var fullRevs = new List<BomRevisionRecord>();
        foreach (var rev in revs)
        {
            const string itemSql = """
                SELECT id, bom_revision_id, line_number, component_variant_id, component_type, quantity_per, scrap_percentage, notes, created_at
                FROM manufacturing.bom_items
                WHERE tenant_id = @tid AND company_id = @cid AND bom_revision_id = @revid
                ORDER BY line_number;
                """;

            var items = new List<BomItemRecord>();
            await using (var cmd = new NpgsqlCommand(itemSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("revid", rev.Id);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    items.Add(new BomItemRecord(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetInt32(2),
                        reader.GetGuid(3),
                        reader.GetString(4),
                        reader.GetDecimal(5),
                        reader.GetDecimal(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.GetFieldValue<DateTimeOffset>(8)));
                }
                await reader.CloseAsync();
            }
            fullRevs.Add(rev with { Items = items });
        }

        await tx.CommitAsync(ct);
        return bom with { Revisions = fullRevs };
    }

    public async Task<BomRecord?> GetActiveBomForVariantAsync(Guid variantId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id FROM manufacturing.boms
            WHERE tenant_id = @tid AND company_id = @cid AND product_variant_id = @vid AND status = 'active'
            ORDER BY created_at DESC LIMIT 1;
            """;

        Guid? bomId = null;
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("vid", variantId);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res is Guid g) bomId = g;
        }

        await tx.CommitAsync(ct);
        return bomId.HasValue ? await GetBomByIdAsync(bomId.Value, ct) : null;
    }

    // ── Material Substitutions ─────────────────────────────────────────────────

    public async Task<MaterialSubstitutionRecord> CreateSubstitutionAsync(MaterialSubstitutionRecord record, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            INSERT INTO manufacturing.material_substitutions
                (tenant_id, company_id, id, original_variant_id, substitute_variant_id, conversion_factor, priority, is_active, notes, created_at)
            VALUES
                (@tid, @cid, @id, @orig, @sub, @factor, @priority, @active, @notes, @createdAt)
            RETURNING id, original_variant_id, substitute_variant_id, conversion_factor, priority, is_active, notes, created_at;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("orig", record.OriginalVariantId);
        cmd.Parameters.AddWithValue("sub", record.SubstituteVariantId);
        cmd.Parameters.AddWithValue("factor", record.ConversionFactor);
        cmd.Parameters.AddWithValue("priority", record.Priority);
        cmd.Parameters.AddWithValue("active", record.IsActive);
        cmd.Parameters.AddWithValue("notes", (object?)record.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", record.CreatedAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var res = new MaterialSubstitutionRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetDecimal(3),
            reader.GetInt32(4),
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return res;
    }

    public async Task<IReadOnlyList<MaterialSubstitutionRecord>> ListSubstitutionsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, original_variant_id, substitute_variant_id, conversion_factor, priority, is_active, notes, created_at
            FROM manufacturing.material_substitutions
            WHERE tenant_id = @tid AND company_id = @cid
            ORDER BY priority;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<MaterialSubstitutionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new MaterialSubstitutionRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetDecimal(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    // ── Work Orders ────────────────────────────────────────────────────────────

    public async Task<WorkOrderRecord> CreateWorkOrderAsync(
        WorkOrderRecord wo,
        IReadOnlyList<WorkOrderMaterialRecord> materials,
        IReadOnlyList<WorkOrderOperationRecord> operations,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        // Resolve WIP Account ID
        var wipAccountId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1250", "asset", "debit", ct);

        const string woSql = """
            INSERT INTO manufacturing.work_orders
                (tenant_id, company_id, id, work_order_number, product_variant_id, bom_id, bom_revision_id, routing_id,
                 target_warehouse_id, target_bin_id, planned_quantity, completed_quantity, scrapped_quantity, rejected_quantity,
                 start_date, due_date, status, wip_gl_account_id, total_material_cost, total_labor_cost, total_overhead_cost,
                 total_production_cost, unit_cost_final, origin_demand_type, origin_demand_id, notes, created_at, updated_at)
            VALUES
                (@tid, @cid, @id, @num, @vid, @bomId, @bomRevId, @routingId,
                 @targetWh, @targetBin, @plannedQty, 0, 0, 0,
                 @start, @due, @status, @wipGl, 0, 0, 0,
                 0, 0, @demandType, @demandId, @notes, @createdAt, @updatedAt);
            """;

        await using (var cmd = new NpgsqlCommand(woSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", wo.Id);
            cmd.Parameters.AddWithValue("num", wo.WorkOrderNumber);
            cmd.Parameters.AddWithValue("vid", wo.ProductVariantId);
            cmd.Parameters.AddWithValue("bomId", (object?)wo.BomId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bomRevId", (object?)wo.BomRevisionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("routingId", (object?)wo.RoutingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("targetWh", wo.TargetWarehouseId);
            cmd.Parameters.AddWithValue("targetBin", wo.TargetBinId);
            cmd.Parameters.AddWithValue("plannedQty", wo.PlannedQuantity);
            cmd.Parameters.AddWithValue("start", wo.StartDate);
            cmd.Parameters.AddWithValue("due", wo.DueDate);
            cmd.Parameters.AddWithValue("status", wo.Status);
            cmd.Parameters.AddWithValue("wipGl", wipAccountId);
            cmd.Parameters.AddWithValue("demandType", (object?)wo.OriginDemandType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("demandId", (object?)wo.OriginDemandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("notes", (object?)wo.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", wo.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", wo.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var savedMaterials = new List<WorkOrderMaterialRecord>();
        foreach (var m in materials)
        {
            const string matSql = """
                INSERT INTO manufacturing.work_order_materials
                    (tenant_id, company_id, id, work_order_id, line_number, component_variant_id, required_quantity, reserved_quantity, issued_quantity, returned_quantity, reservation_id, consumed_cost, status, created_at)
                VALUES
                    (@tid, @cid, @id, @woid, @line, @cvid, @reqQty, @resQty, @issQty, @retQty, @resid, @cost, @status, @createdAt)
                RETURNING id, work_order_id, line_number, component_variant_id, required_quantity, reserved_quantity, issued_quantity, returned_quantity, reservation_id, consumed_cost, status, created_at;
                """;

            await using var cmd = new NpgsqlCommand(matSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", m.Id);
            cmd.Parameters.AddWithValue("woid", wo.Id);
            cmd.Parameters.AddWithValue("line", m.LineNumber);
            cmd.Parameters.AddWithValue("cvid", m.ComponentVariantId);
            cmd.Parameters.AddWithValue("reqQty", m.RequiredQuantity);
            cmd.Parameters.AddWithValue("resQty", m.ReservedQuantity);
            cmd.Parameters.AddWithValue("issQty", m.IssuedQuantity);
            cmd.Parameters.AddWithValue("retQty", m.ReturnedQuantity);
            cmd.Parameters.AddWithValue("resid", (object?)m.ReservationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cost", m.ConsumedCost);
            cmd.Parameters.AddWithValue("status", m.Status);
            cmd.Parameters.AddWithValue("createdAt", m.CreatedAt);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            savedMaterials.Add(new WorkOrderMaterialRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetGuid(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
            await reader.CloseAsync();
        }

        var savedOps = new List<WorkOrderOperationRecord>();
        foreach (var op in operations)
        {
            const string opSql = """
                INSERT INTO manufacturing.work_order_operations
                    (tenant_id, company_id, id, work_order_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, actual_setup_minutes, actual_run_minutes, completed_quantity, scrapped_quantity, allocated_labor_cost, allocated_overhead_cost, status, completed_at, notes)
                VALUES
                    (@tid, @cid, @id, @woid, @seq, @opName, @wcid, @setup, @run, 0, 0, 0, 0, 0, 0, 'pending', null, @notes)
                RETURNING id, work_order_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, actual_setup_minutes, actual_run_minutes, completed_quantity, scrapped_quantity, allocated_labor_cost, allocated_overhead_cost, status, completed_at, notes;
                """;

            await using var cmd = new NpgsqlCommand(opSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", op.Id);
            cmd.Parameters.AddWithValue("woid", wo.Id);
            cmd.Parameters.AddWithValue("seq", op.OperationSequence);
            cmd.Parameters.AddWithValue("opName", op.OperationName);
            cmd.Parameters.AddWithValue("wcid", op.WorkCenterId);
            cmd.Parameters.AddWithValue("setup", op.SetupTimeMinutes);
            cmd.Parameters.AddWithValue("run", op.RunTimeMinutesPerUnit);
            cmd.Parameters.AddWithValue("notes", (object?)op.Notes ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            savedOps.Add(new WorkOrderOperationRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return wo with { WipGlAccountId = wipAccountId, Materials = savedMaterials, Operations = savedOps };
    }

    public async Task<IReadOnlyList<WorkOrderRecord>> ListWorkOrdersAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, work_order_number, product_variant_id, bom_id, bom_revision_id, routing_id,
                   target_warehouse_id, target_bin_id, planned_quantity, completed_quantity, scrapped_quantity, rejected_quantity,
                   start_date, due_date, status, wip_gl_account_id, total_material_cost, total_labor_cost, total_overhead_cost,
                   total_production_cost, unit_cost_final, origin_demand_type, origin_demand_id, notes, created_at, updated_at,
                   released_at, completed_at, cancelled_at
            FROM manufacturing.work_orders
            WHERE tenant_id = @tid AND company_id = @cid
            ORDER BY created_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

        var list = new List<WorkOrderRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new WorkOrderRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetGuid(6),
                reader.GetGuid(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetFieldValue<DateOnly>(12),
                reader.GetFieldValue<DateOnly>(13),
                reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.GetDecimal(18),
                reader.GetDecimal(19),
                reader.GetDecimal(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetGuid(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.GetFieldValue<DateTimeOffset>(24),
                reader.GetFieldValue<DateTimeOffset>(25),
                reader.IsDBNull(26) ? null : reader.GetFieldValue<DateTimeOffset>(26),
                reader.IsDBNull(27) ? null : reader.GetFieldValue<DateTimeOffset>(27),
                reader.IsDBNull(28) ? null : reader.GetFieldValue<DateTimeOffset>(28)));
        }

        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<WorkOrderRecord?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, work_order_number, product_variant_id, bom_id, bom_revision_id, routing_id,
                   target_warehouse_id, target_bin_id, planned_quantity, completed_quantity, scrapped_quantity, rejected_quantity,
                   start_date, due_date, status, wip_gl_account_id, total_material_cost, total_labor_cost, total_overhead_cost,
                   total_production_cost, unit_cost_final, origin_demand_type, origin_demand_id, notes, created_at, updated_at,
                   released_at, completed_at, cancelled_at
            FROM manufacturing.work_orders
            WHERE tenant_id = @tid AND company_id = @cid AND id = @id;
            """;

        WorkOrderRecord? wo = null;
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                wo = new WorkOrderRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetGuid(6),
                    reader.GetGuid(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetFieldValue<DateOnly>(12),
                    reader.GetFieldValue<DateOnly>(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetGuid(15),
                    reader.GetDecimal(16),
                    reader.GetDecimal(17),
                    reader.GetDecimal(18),
                    reader.GetDecimal(19),
                    reader.GetDecimal(20),
                    reader.IsDBNull(21) ? null : reader.GetString(21),
                    reader.IsDBNull(22) ? null : reader.GetGuid(22),
                    reader.IsDBNull(23) ? null : reader.GetString(23),
                    reader.GetFieldValue<DateTimeOffset>(24),
                    reader.GetFieldValue<DateTimeOffset>(25),
                    reader.IsDBNull(26) ? null : reader.GetFieldValue<DateTimeOffset>(26),
                    reader.IsDBNull(27) ? null : reader.GetFieldValue<DateTimeOffset>(27),
                    reader.IsDBNull(28) ? null : reader.GetFieldValue<DateTimeOffset>(28));
            }
            await reader.CloseAsync();
        }

        if (wo == null) return null;

        const string matSql = """
            SELECT id, work_order_id, line_number, component_variant_id, required_quantity, reserved_quantity, issued_quantity, returned_quantity, reservation_id, consumed_cost, status, created_at
            FROM manufacturing.work_order_materials
            WHERE tenant_id = @tid AND company_id = @cid AND work_order_id = @id
            ORDER BY line_number;
            """;

        var materials = new List<WorkOrderMaterialRecord>();
        await using (var cmd = new NpgsqlCommand(matSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                materials.Add(new WorkOrderMaterialRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetGuid(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.GetDecimal(9),
                    reader.GetString(10),
                    reader.GetFieldValue<DateTimeOffset>(11)));
            }
            await reader.CloseAsync();
        }

        const string opSql = """
            SELECT id, work_order_id, operation_sequence, operation_name, work_center_id, setup_time_minutes, run_time_minutes_per_unit, actual_setup_minutes, actual_run_minutes, completed_quantity, scrapped_quantity, allocated_labor_cost, allocated_overhead_cost, status, completed_at, notes
            FROM manufacturing.work_order_operations
            WHERE tenant_id = @tid AND company_id = @cid AND work_order_id = @id
            ORDER BY operation_sequence;
            """;

        var ops = new List<WorkOrderOperationRecord>();
        await using (var cmd = new NpgsqlCommand(opSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ops.Add(new WorkOrderOperationRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15)));
            }
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);
        return wo with { Materials = materials, Operations = ops };
    }

    public async Task<WorkOrderRecord> ReleaseWorkOrderAsync(Guid workOrderId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        // Fetch Work Order
        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) throw new InvalidOperationException("WORK_ORDER_NOT_FOUND");

        // Reserve stock for each material requirement
        if (wo.Materials != null)
        {
            foreach (var mat in wo.Materials)
            {
                var reservationId = Guid.NewGuid();
                var clientTxId = Guid.NewGuid();

                const string resSql = """
                    INSERT INTO erp_inventory.stock_reservation
                        (tenant_id, company_id, reservation_id, client_transaction_id, variant_id, warehouse_id, bin_id, status_code, quantity, fulfilled_qty, reservation_status, source_document_type, source_document_id, created_at)
                    VALUES
                        (@tid, @cid, @resid, @txid, @vid, @whid, @binid, 'available', @qty, 0, 'active', 'work_order', @woid, now());
                    """;

                await using (var cmd = new NpgsqlCommand(resSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                    cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    cmd.Parameters.AddWithValue("resid", reservationId);
                    cmd.Parameters.AddWithValue("txid", clientTxId);
                    cmd.Parameters.AddWithValue("vid", mat.ComponentVariantId);
                    cmd.Parameters.AddWithValue("whid", wo.TargetWarehouseId);
                    cmd.Parameters.AddWithValue("binid", wo.TargetBinId);
                    cmd.Parameters.AddWithValue("qty", mat.RequiredQuantity);
                    cmd.Parameters.AddWithValue("woid", workOrderId);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                const string updateMatSql = """
                    UPDATE manufacturing.work_order_materials
                    SET reservation_id = @resid, reserved_quantity = @reqQty, status = 'reserved'
                    WHERE tenant_id = @tid AND company_id = @cid AND id = @matId;
                    """;

                await using (var cmd = new NpgsqlCommand(updateMatSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                    cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                    cmd.Parameters.AddWithValue("resid", reservationId);
                    cmd.Parameters.AddWithValue("reqQty", mat.RequiredQuantity);
                    cmd.Parameters.AddWithValue("matId", mat.Id);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        const string updateWoSql = """
            UPDATE manufacturing.work_orders
            SET status = 'released', released_at = now(), updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;

        await using (var cmd = new NpgsqlCommand(updateWoSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return (await GetWorkOrderByIdAsync(workOrderId, ct))!;
    }

    public async Task<MaterialIssueResult> IssueMaterialAsync(Guid workOrderId, IssueMaterialRequest request, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        // Fetch work order and material requirement
        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) throw new InvalidOperationException("WORK_ORDER_NOT_FOUND");

        // 1. Fetch open FIFO layers strictly for this variant, warehouse, and bin
        const string layersSql = """
            SELECT layer_id, received_at, remaining_qty, unit_cost, warehouse_id, bin_id
            FROM erp_inventory.inventory_cost_layer
            WHERE tenant_id = @tid AND company_id = @cid
              AND variant_id = @vid AND warehouse_id = @whid AND bin_id = @binid
              AND remaining_qty > 0 AND status_code = 'available'
            ORDER BY received_at, created_at, layer_id
            FOR UPDATE;
            """;

        var openLayers = new List<FifoCostLayerSlice>();
        await using (var cmd = new NpgsqlCommand(layersSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("vid", request.ComponentVariantId);
            cmd.Parameters.AddWithValue("whid", request.WarehouseId);
            cmd.Parameters.AddWithValue("binid", request.BinId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                openLayers.Add(new FifoCostLayerSlice(
                    reader.GetGuid(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetGuid(4),
                    reader.GetGuid(5)));
            }
            await reader.CloseAsync();
        }

        // 2. Allocate FIFO layers
        var fifoResult = FifoCostAllocator.Allocate(
            availableLayers: openLayers,
            requestedQty: request.Quantity,
            locationDescription: $"Warehouse {request.WarehouseId}/Bin {request.BinId}",
            expectedWarehouseId: request.WarehouseId,
            expectedBinId: request.BinId);

        // 3. Create Inventory Movement (production_out)
        var movementId = Guid.NewGuid();
        var clientTxId = Guid.NewGuid();
        const string movSql = """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, status, occurred_at, posted_at, created_by, note)
            VALUES
                (@tid, @cid, @movid, @txid, 'production_out', 'work_order', @woid, 'posted', now(), now(), @userId, @note);
            """;

        await using (var cmd = new NpgsqlCommand(movSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("txid", clientTxId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            cmd.Parameters.AddWithValue("note", (object?)request.Notes ?? $"Material issue for WO {wo.WorkOrderNumber} Line {request.LineNumber}");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Get UOM ID for variant
        var uomId = Guid.Empty;
        const string uomSql = """
            SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @vid;
            """;
        await using (var cmd = new NpgsqlCommand(uomSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("vid", request.ComponentVariantId);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res is Guid u) uomId = u;
        }

        // Create Movement Leg
        const string legSql = """
            INSERT INTO erp_inventory.inventory_movement_leg
                (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
            VALUES
                (@tid, @cid, @movid, 1, @vid, @whid, @binid, 'available', @qtyDelta, @uomid);
            """;

        await using (var cmd = new NpgsqlCommand(legSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("vid", request.ComponentVariantId);
            cmd.Parameters.AddWithValue("whid", request.WarehouseId);
            cmd.Parameters.AddWithValue("binid", request.BinId);
            cmd.Parameters.AddWithValue("qtyDelta", -request.Quantity);
            cmd.Parameters.AddWithValue("uomid", uomId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var issueFeedPayload = JsonSerializer.Serialize(new
        {
            movementId,
            movementType = "production_out",
            legs = new[]
            {
                new
                {
                    variantId = request.ComponentVariantId,
                    warehouseId = request.WarehouseId,
                    binId = request.BinId,
                    stockStatus = "available",
                    quantityDelta = -request.Quantity,
                    unitOfMeasureId = uomId,
                },
            },
            occurredAt = DateTimeOffset.UtcNow,
            postedAt = DateTimeOffset.UtcNow,
        });
        await CompanyChangeLogWriter.AppendAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId,
            "inventory_movement", movementId, 1, "upsert", issueFeedPayload, ct);

        // 4. Update layers and insert cost allocations
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        foreach (var alloc in fifoResult.Allocations)
        {
            const string updateLayerSql = """
                UPDATE erp_inventory.inventory_cost_layer
                SET remaining_qty = remaining_qty - @qty, updated_at = now()
                WHERE tenant_id = @tid AND company_id = @cid AND layer_id = @lid;
                """;
            await using (var cmd = new NpgsqlCommand(updateLayerSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                cmd.Parameters.AddWithValue("lid", alloc.LayerId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            const string insertAllocSql = """
                INSERT INTO erp_inventory.inventory_cost_allocation
                    (tenant_id, company_id, allocation_id, layer_id, movement_id, source_document_type, source_document_id, source_line_number, variant_id, consumed_qty, unit_cost, extended_cost, currency_code, allocated_at)
                VALUES
                    (@tid, @cid, gen_random_uuid(), @lid, @movid, 'work_order', @woid, @line, @vid, @qty, @unitCost, @extCost, @cur, now());
                """;
            await using (var cmd = new NpgsqlCommand(insertAllocSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("lid", alloc.LayerId);
                cmd.Parameters.AddWithValue("movid", movementId);
                cmd.Parameters.AddWithValue("woid", workOrderId);
                cmd.Parameters.AddWithValue("line", request.LineNumber);
                cmd.Parameters.AddWithValue("vid", request.ComponentVariantId);
                cmd.Parameters.AddWithValue("qty", alloc.ConsumedQty);
                cmd.Parameters.AddWithValue("unitCost", alloc.UnitCost);
                cmd.Parameters.AddWithValue("extCost", alloc.ExtendedCost);
                cmd.Parameters.AddWithValue("cur", functionalCurrency);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // 5. Update work order material line and reservation
        const string updateWoMatSql = """
            UPDATE manufacturing.work_order_materials
            SET issued_quantity = issued_quantity + @qty,
                consumed_cost = consumed_cost + @cost,
                status = CASE WHEN (issued_quantity + @qty) >= required_quantity THEN 'issued' ELSE 'partially_issued' END
            WHERE tenant_id = @tid AND company_id = @cid AND work_order_id = @woid AND line_number = @line
            RETURNING reservation_id;
            """;

        Guid? resId = null;
        await using (var cmd = new NpgsqlCommand(updateWoMatSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("qty", request.Quantity);
            cmd.Parameters.AddWithValue("cost", fifoResult.TotalCost);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("line", request.LineNumber);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res is Guid g) resId = g;
        }

        if (resId.HasValue)
        {
            const string updateResSql = """
                UPDATE erp_inventory.stock_reservation
                SET fulfilled_qty = fulfilled_qty + @qty,
                    reservation_status = CASE WHEN (fulfilled_qty + @qty) >= quantity THEN 'fulfilled' ELSE 'partially_fulfilled' END
                WHERE tenant_id = @tid AND company_id = @cid AND reservation_id = @resid;
                """;
            await using var cmd = new NpgsqlCommand(updateResSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("qty", request.Quantity);
            cmd.Parameters.AddWithValue("resid", resId.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Update work order totals
        const string updateWoTotalsSql = """
            UPDATE manufacturing.work_orders
            SET total_material_cost = total_material_cost + @cost,
                total_production_cost = total_production_cost + @cost,
                status = 'in_progress',
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;

        await using (var cmd = new NpgsqlCommand(updateWoTotalsSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("cost", fifoResult.TotalCost);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 6. Post balanced double-entry journal: Dr 1250 (WIP), Cr 1200 (Raw Material Inventory)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetOrCreateOpenPeriodAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, today, ct);
        var wipAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1250", "asset", "debit", ct);
        var rawAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1200", "asset", "debit", ct);

        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-MFG-ISS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var idemKey = $"MFG-ISS-{workOrderId}-{request.LineNumber}-{movementId}";

        const string jourSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description, functional_currency, source_module, source_type, source_id, idempotency_key, status, created_by, posted_by, posted_at)
            VALUES
                (@id, @tid, @cid, @no, @date, @per, @desc, @cur, 'manufacturing', 'material_issue', @srcId, @key, 'posted', @userId, @userId, now());
            """;

        await using (var cmd = new NpgsqlCommand(jourSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", journalId);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("no", journalNo);
            cmd.Parameters.AddWithValue("date", today);
            cmd.Parameters.AddWithValue("per", periodId);
            cmd.Parameters.AddWithValue("desc", $"Material issue to WIP for WO {wo.WorkOrderNumber} Line {request.LineNumber}");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("srcId", workOrderId.ToString());
            cmd.Parameters.AddWithValue("key", idemKey);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string lineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tid, @cid, @jid, @line, @acc, @desc, @cur, @amt, 1.0, @dr, @cr);
            """;

        // Dr WIP 1250
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", 1);
            cmd.Parameters.AddWithValue("acc", wipAccId);
            cmd.Parameters.AddWithValue("desc", $"Debit WIP Inventory for consumed materials (Line {request.LineNumber})");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", fifoResult.TotalCost);
            cmd.Parameters.AddWithValue("dr", fifoResult.TotalCost);
            cmd.Parameters.AddWithValue("cr", 0m);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Cr Raw Materials 1200
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", 2);
            cmd.Parameters.AddWithValue("acc", rawAccId);
            cmd.Parameters.AddWithValue("desc", $"Credit Raw Materials Inventory (Line {request.LineNumber})");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", fifoResult.TotalCost);
            cmd.Parameters.AddWithValue("dr", 0m);
            cmd.Parameters.AddWithValue("cr", fifoResult.TotalCost);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new MaterialIssueResult(
            WorkOrderId: workOrderId,
            LineNumber: request.LineNumber,
            IssuedQuantity: request.Quantity,
            ConsumedCost: fifoResult.TotalCost,
            MovementId: movementId,
            JournalId: journalId);
    }

    public async Task<MaterialReturnResult> ReturnMaterialAsync(Guid workOrderId, ReturnMaterialRequest request, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) throw new InvalidOperationException("WORK_ORDER_NOT_FOUND");

        // Find material requirement
        var mat = wo.Materials?.FirstOrDefault(m => m.LineNumber == request.LineNumber);
        if (mat == null) throw new InvalidOperationException("MATERIAL_LINE_NOT_FOUND");

        if (mat.IssuedQuantity <= 0m || mat.ConsumedCost <= 0m)
            throw new InvalidOperationException("NO_ISSUED_MATERIAL_TO_RETURN");

        var unitCost = mat.ConsumedCost / mat.IssuedQuantity;
        var returnCost = Math.Round(request.Quantity * unitCost, 4, MidpointRounding.AwayFromZero);

        // Create Movement (return_in)
        var movementId = Guid.NewGuid();
        var clientTxId = Guid.NewGuid();

        const string movSql = """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, status, occurred_at, posted_at, created_by, note)
            VALUES
                (@tid, @cid, @movid, @txid, 'return_in', 'work_order', @woid, 'posted', now(), now(), @userId, @note);
            """;

        await using (var cmd = new NpgsqlCommand(movSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("txid", clientTxId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            cmd.Parameters.AddWithValue("note", (object?)request.Reason ?? $"Material return from WO {wo.WorkOrderNumber}");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Get UOM ID
        var uomId = Guid.Empty;
        const string uomSql = """
            SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @vid;
            """;
        await using (var cmd = new NpgsqlCommand(uomSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("vid", mat.ComponentVariantId);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res is Guid u) uomId = u;
        }

        const string legSql = """
            INSERT INTO erp_inventory.inventory_movement_leg
                (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
            VALUES
                (@tid, @cid, @movid, 1, @vid, @whid, @binid, 'available', @qtyDelta, @uomid);
            """;
        await using (var cmd = new NpgsqlCommand(legSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("vid", mat.ComponentVariantId);
            cmd.Parameters.AddWithValue("whid", request.TargetWarehouseId);
            cmd.Parameters.AddWithValue("binid", request.TargetBinId);
            cmd.Parameters.AddWithValue("qtyDelta", request.Quantity);
            cmd.Parameters.AddWithValue("uomid", uomId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var returnFeedPayload = JsonSerializer.Serialize(new
        {
            movementId,
            movementType = "return_in",
            legs = new[]
            {
                new
                {
                    variantId = mat.ComponentVariantId,
                    warehouseId = request.TargetWarehouseId,
                    binId = request.TargetBinId,
                    stockStatus = "available",
                    quantityDelta = request.Quantity,
                    unitOfMeasureId = uomId,
                },
            },
            occurredAt = DateTimeOffset.UtcNow,
            postedAt = DateTimeOffset.UtcNow,
        });
        await CompanyChangeLogWriter.AppendAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId,
            "inventory_movement", movementId, 1, "upsert", returnFeedPayload, ct);

        // Create new FIFO layer for returned stock
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var newLayerId = Guid.NewGuid();
        const string layerSql = """
            INSERT INTO erp_inventory.inventory_cost_layer
                (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code, source_document_type, source_document_id, source_movement_id, received_at, original_qty, remaining_qty, unit_cost, currency_code)
            VALUES
                (@tid, @cid, @lid, @vid, @whid, @binid, 'available', 'work_order', @woid, @movid, now(), @qty, @qty, @unitCost, @cur);
            """;

        await using (var cmd = new NpgsqlCommand(layerSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("lid", newLayerId);
            cmd.Parameters.AddWithValue("vid", mat.ComponentVariantId);
            cmd.Parameters.AddWithValue("whid", request.TargetWarehouseId);
            cmd.Parameters.AddWithValue("binid", request.TargetBinId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("qty", request.Quantity);
            cmd.Parameters.AddWithValue("unitCost", unitCost);
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Update material requirement line
        const string updateMatSql = """
            UPDATE manufacturing.work_order_materials
            SET returned_quantity = returned_quantity + @qty,
                consumed_cost = GREATEST(0, consumed_cost - @cost)
            WHERE tenant_id = @tid AND company_id = @cid AND id = @matId;
            """;

        await using (var cmd = new NpgsqlCommand(updateMatSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("qty", request.Quantity);
            cmd.Parameters.AddWithValue("cost", returnCost);
            cmd.Parameters.AddWithValue("matId", mat.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Update WO totals
        const string updateWoTotals = """
            UPDATE manufacturing.work_orders
            SET total_material_cost = GREATEST(0, total_material_cost - @cost),
                total_production_cost = GREATEST(0, total_production_cost - @cost),
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;

        await using (var cmd = new NpgsqlCommand(updateWoTotals, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("cost", returnCost);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Post balanced journal: Dr 1200 (Inventory) / Cr 1250 (WIP)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetOrCreateOpenPeriodAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, today, ct);
        var rawAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1200", "asset", "debit", ct);
        var wipAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1250", "asset", "debit", ct);

        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-MFG-RET-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var idemKey = $"MFG-RET-{workOrderId}-{request.LineNumber}-{movementId}";

        const string jourSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description, functional_currency, source_module, source_type, source_id, idempotency_key, status, created_by, posted_by, posted_at)
            VALUES
                (@id, @tid, @cid, @no, @date, @per, @desc, @cur, 'manufacturing', 'material_return', @srcId, @key, 'posted', @userId, @userId, now());
            """;

        await using (var cmd = new NpgsqlCommand(jourSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", journalId);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("no", journalNo);
            cmd.Parameters.AddWithValue("date", today);
            cmd.Parameters.AddWithValue("per", periodId);
            cmd.Parameters.AddWithValue("desc", $"Material return from WIP for WO {wo.WorkOrderNumber} Line {request.LineNumber}");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("srcId", workOrderId.ToString());
            cmd.Parameters.AddWithValue("key", idemKey);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string lineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tid, @cid, @jid, @line, @acc, @desc, @cur, @amt, 1.0, @dr, @cr);
            """;

        // Dr Raw Material 1200
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", 1);
            cmd.Parameters.AddWithValue("acc", rawAccId);
            cmd.Parameters.AddWithValue("desc", "Credit to Raw Material Inventory from WIP return");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", returnCost);
            cmd.Parameters.AddWithValue("dr", returnCost);
            cmd.Parameters.AddWithValue("cr", 0m);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Cr WIP 1250
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", 2);
            cmd.Parameters.AddWithValue("acc", wipAccId);
            cmd.Parameters.AddWithValue("desc", "Debit reduction from WIP Inventory");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", returnCost);
            cmd.Parameters.AddWithValue("dr", 0m);
            cmd.Parameters.AddWithValue("cr", returnCost);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new MaterialReturnResult(
            WorkOrderId: workOrderId,
            LineNumber: request.LineNumber,
            ReturnedQuantity: request.Quantity,
            CreditedCost: returnCost,
            MovementId: movementId,
            JournalId: journalId);
    }

    public async Task<OperationProgressResult> ReportOperationProgressAsync(Guid workOrderId, ReportOperationProgressRequest request, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) throw new InvalidOperationException("WORK_ORDER_NOT_FOUND");

        var op = wo.Operations?.FirstOrDefault(o => o.OperationSequence == request.OperationSequence);
        if (op == null) throw new InvalidOperationException("OPERATION_NOT_FOUND");

        // Fetch Work Center rates
        var wc = await GetWorkCenterByIdAsync(op.WorkCenterId, ct);
        if (wc == null) throw new InvalidOperationException("WORK_CENTER_NOT_FOUND");

        var costCalc = WorkOrderCostingEngine.CalculateOperationCost(
            request.ActualSetupMinutes,
            request.ActualRunMinutes,
            wc.HourlyLaborRate,
            wc.HourlyOverheadRate);

        // Update operation
        const string opSql = """
            UPDATE manufacturing.work_order_operations
            SET actual_setup_minutes = actual_setup_minutes + @setup,
                actual_run_minutes = actual_run_minutes + @run,
                completed_quantity = completed_quantity + @completed,
                scrapped_quantity = scrapped_quantity + @scrapped,
                allocated_labor_cost = allocated_labor_cost + @labor,
                allocated_overhead_cost = allocated_overhead_cost + @overhead,
                status = 'completed',
                completed_at = now(),
                notes = COALESCE(@notes, notes)
            WHERE tenant_id = @tid AND company_id = @cid AND work_order_id = @woid AND operation_sequence = @seq;
            """;

        await using (var cmd = new NpgsqlCommand(opSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("setup", request.ActualSetupMinutes);
            cmd.Parameters.AddWithValue("run", request.ActualRunMinutes);
            cmd.Parameters.AddWithValue("completed", request.CompletedQuantity);
            cmd.Parameters.AddWithValue("scrapped", request.ScrappedQuantity);
            cmd.Parameters.AddWithValue("labor", costCalc.LaborCost);
            cmd.Parameters.AddWithValue("overhead", costCalc.OverheadCost);
            cmd.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("seq", request.OperationSequence);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Update work order totals
        const string woTotalsSql = """
            UPDATE manufacturing.work_orders
            SET total_labor_cost = total_labor_cost + @labor,
                total_overhead_cost = total_overhead_cost + @overhead,
                total_production_cost = total_production_cost + @totalCost,
                status = 'in_progress',
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;

        await using (var cmd = new NpgsqlCommand(woTotalsSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("labor", costCalc.LaborCost);
            cmd.Parameters.AddWithValue("overhead", costCalc.OverheadCost);
            cmd.Parameters.AddWithValue("totalCost", costCalc.TotalOperationCost);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        Guid? journalId = null;
        if (costCalc.TotalOperationCost > 0m)
        {
            var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
                conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var periodId = await GetOrCreateOpenPeriodAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, today, ct);
            var wipAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1250", "asset", "debit", ct);
            var laborAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "2050", "liability", "credit", ct);
            var overheadAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "6060", "expense", "credit", ct);

            journalId = Guid.NewGuid();
            var journalNo = $"JRN-MFG-LAB-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            var idemKey = $"MFG-LAB-{workOrderId}-{request.OperationSequence}-{Guid.NewGuid()}";

            const string jourSql = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description, functional_currency, source_module, source_type, source_id, idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@id, @tid, @cid, @no, @date, @per, @desc, @cur, 'manufacturing', 'operation_labor_overhead', @srcId, @key, 'posted', @userId, @userId, now());
                """;

            await using (var cmd = new NpgsqlCommand(jourSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", journalId.Value);
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("no", journalNo);
                cmd.Parameters.AddWithValue("date", today);
                cmd.Parameters.AddWithValue("per", periodId);
                cmd.Parameters.AddWithValue("desc", $"Direct labor and applied overhead for WO {wo.WorkOrderNumber} Op {request.OperationSequence}");
                cmd.Parameters.AddWithValue("cur", functionalCurrency);
                cmd.Parameters.AddWithValue("srcId", workOrderId.ToString());
                cmd.Parameters.AddWithValue("key", idemKey);
                cmd.Parameters.AddWithValue("userId", Ctx.UserId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            const string lineSql = """
                INSERT INTO finance.journal_lines
                    (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
                VALUES
                    (@tid, @cid, @jid, @line, @acc, @desc, @cur, @amt, 1.0, @dr, @cr);
                """;

            var lineNo = 1;
            // Dr WIP 1250
            await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("jid", journalId.Value);
                cmd.Parameters.AddWithValue("line", lineNo++);
                cmd.Parameters.AddWithValue("acc", wipAccId);
                cmd.Parameters.AddWithValue("desc", $"Debit WIP for Labor and Overhead (Op {request.OperationSequence})");
                cmd.Parameters.AddWithValue("cur", functionalCurrency);
                cmd.Parameters.AddWithValue("amt", costCalc.TotalOperationCost);
                cmd.Parameters.AddWithValue("dr", costCalc.TotalOperationCost);
                cmd.Parameters.AddWithValue("cr", 0m);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (costCalc.LaborCost > 0m)
            {
                await using var cmd = new NpgsqlCommand(lineSql, conn, tx);
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("jid", journalId.Value);
                cmd.Parameters.AddWithValue("line", lineNo++);
                cmd.Parameters.AddWithValue("acc", laborAccId);
                cmd.Parameters.AddWithValue("desc", $"Credit Direct Labor Clearing (Op {request.OperationSequence})");
                cmd.Parameters.AddWithValue("cur", functionalCurrency);
                cmd.Parameters.AddWithValue("amt", costCalc.LaborCost);
                cmd.Parameters.AddWithValue("dr", 0m);
                cmd.Parameters.AddWithValue("cr", costCalc.LaborCost);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (costCalc.OverheadCost > 0m)
            {
                await using var cmd = new NpgsqlCommand(lineSql, conn, tx);
                cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
                cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
                cmd.Parameters.AddWithValue("jid", journalId.Value);
                cmd.Parameters.AddWithValue("line", lineNo++);
                cmd.Parameters.AddWithValue("acc", overheadAccId);
                cmd.Parameters.AddWithValue("desc", $"Credit Applied Overhead (Op {request.OperationSequence})");
                cmd.Parameters.AddWithValue("cur", functionalCurrency);
                cmd.Parameters.AddWithValue("amt", costCalc.OverheadCost);
                cmd.Parameters.AddWithValue("dr", 0m);
                cmd.Parameters.AddWithValue("cr", costCalc.OverheadCost);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);

        return new OperationProgressResult(
            WorkOrderId: workOrderId,
            OperationSequence: request.OperationSequence,
            AllocatedLaborCost: costCalc.LaborCost,
            AllocatedOverheadCost: costCalc.OverheadCost,
            JournalId: journalId);
    }

    public async Task<ProductionInspectionRecord> RecordProductionInspectionAsync(ProductionInspectionRecord inspection, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            INSERT INTO manufacturing.production_inspections
                (tenant_id, company_id, id, work_order_id, inspection_number, inspection_type, inspected_quantity, accepted_quantity, rejected_quantity, quarantine_bin_id, disposition, inspector_user_id, inspection_date, notes, created_at)
            VALUES
                (@tid, @cid, @id, @woid, @num, @type, @inspected, @accepted, @rejected, @qbin, @disp, @userId, @date, @notes, @createdAt)
            RETURNING id, work_order_id, inspection_number, inspection_type, inspected_quantity, accepted_quantity, rejected_quantity, quarantine_bin_id, disposition, inspector_user_id, inspection_date, notes, created_at;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
        cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
        cmd.Parameters.AddWithValue("id", inspection.Id);
        cmd.Parameters.AddWithValue("woid", inspection.WorkOrderId);
        cmd.Parameters.AddWithValue("num", inspection.InspectionNumber);
        cmd.Parameters.AddWithValue("type", inspection.InspectionType);
        cmd.Parameters.AddWithValue("inspected", inspection.InspectedQuantity);
        cmd.Parameters.AddWithValue("accepted", inspection.AcceptedQuantity);
        cmd.Parameters.AddWithValue("rejected", inspection.RejectedQuantity);
        cmd.Parameters.AddWithValue("qbin", (object?)inspection.QuarantineBinId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("disp", inspection.Disposition);
        cmd.Parameters.AddWithValue("userId", inspection.InspectorUserId);
        cmd.Parameters.AddWithValue("date", inspection.InspectionDate);
        cmd.Parameters.AddWithValue("notes", (object?)inspection.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", inspection.CreatedAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var res = new ProductionInspectionRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.GetString(8),
            reader.GetGuid(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12));

        await reader.CloseAsync();

        // Update rejected quantity on work order
        const string updateWoSql = """
            UPDATE manufacturing.work_orders
            SET rejected_quantity = rejected_quantity + @rejected,
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;
        await using (var updateCmd = new NpgsqlCommand(updateWoSql, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            updateCmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            updateCmd.Parameters.AddWithValue("rejected", inspection.RejectedQuantity);
            updateCmd.Parameters.AddWithValue("woid", inspection.WorkOrderId);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return res;
    }

    public async Task<WorkOrderCompletionResult> CompleteWorkOrderAsync(Guid workOrderId, decimal acceptedQty, decimal scrappedQty, string? notes, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) throw new InvalidOperationException("WORK_ORDER_NOT_FOUND");

        // 1. Calculate final costing summary
        var costSummary = WorkOrderCostingEngine.SummarizeWorkOrderCosts(
            wo.TotalMaterialCost,
            wo.TotalLaborCost,
            wo.TotalOverheadCost,
            acceptedQty,
            scrappedQty);

        // 2. Create Finished Goods Inventory Movement (production_in)
        var movementId = Guid.NewGuid();
        var clientTxId = Guid.NewGuid();

        const string movSql = """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, status, occurred_at, posted_at, created_by, note)
            VALUES
                (@tid, @cid, @movid, @txid, 'production_in', 'work_order', @woid, 'posted', now(), now(), @userId, @note);
            """;

        await using (var cmd = new NpgsqlCommand(movSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("txid", clientTxId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            cmd.Parameters.AddWithValue("note", (object?)notes ?? $"Finished goods completion from WO {wo.WorkOrderNumber}");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Get UOM ID for product variant
        var uomId = Guid.Empty;
        const string uomSql = """
            SELECT base_uom_id FROM erp_inventory.product_variant WHERE tenant_id = @tid AND variant_id = @vid;
            """;
        await using (var cmd = new NpgsqlCommand(uomSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("vid", wo.ProductVariantId);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res is Guid u) uomId = u;
        }

        // Movement Leg for Accepted FG
        const string legSql = """
            INSERT INTO erp_inventory.inventory_movement_leg
                (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
            VALUES
                (@tid, @cid, @movid, 1, @vid, @whid, @binid, 'available', @qtyDelta, @uomid);
            """;

        await using (var cmd = new NpgsqlCommand(legSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("vid", wo.ProductVariantId);
            cmd.Parameters.AddWithValue("whid", wo.TargetWarehouseId);
            cmd.Parameters.AddWithValue("binid", wo.TargetBinId);
            cmd.Parameters.AddWithValue("qtyDelta", acceptedQty);
            cmd.Parameters.AddWithValue("uomid", uomId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var completionFeedPayload = JsonSerializer.Serialize(new
        {
            movementId,
            movementType = "production_in",
            legs = new[]
            {
                new
                {
                    variantId = wo.ProductVariantId,
                    warehouseId = wo.TargetWarehouseId,
                    binId = wo.TargetBinId,
                    stockStatus = "available",
                    quantityDelta = acceptedQty,
                    unitOfMeasureId = uomId,
                },
            },
            occurredAt = DateTimeOffset.UtcNow,
            postedAt = DateTimeOffset.UtcNow,
        });
        await CompanyChangeLogWriter.AppendAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId,
            "inventory_movement", movementId, 1, "upsert", completionFeedPayload, ct);

        // 3. Create Finished Goods FIFO layer
        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);
        var fgLayerId = Guid.NewGuid();
        const string fgLayerSql = """
            INSERT INTO erp_inventory.inventory_cost_layer
                (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code, source_document_type, source_document_id, source_movement_id, received_at, original_qty, remaining_qty, unit_cost, currency_code)
            VALUES
                (@tid, @cid, @lid, @vid, @whid, @binid, 'available', 'work_order', @woid, @movid, now(), @qty, @qty, @unitCost, @cur);
            """;

        await using (var cmd = new NpgsqlCommand(fgLayerSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("lid", fgLayerId);
            cmd.Parameters.AddWithValue("vid", wo.ProductVariantId);
            cmd.Parameters.AddWithValue("whid", wo.TargetWarehouseId);
            cmd.Parameters.AddWithValue("binid", wo.TargetBinId);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            cmd.Parameters.AddWithValue("movid", movementId);
            cmd.Parameters.AddWithValue("qty", acceptedQty);
            cmd.Parameters.AddWithValue("unitCost", costSummary.UnitCostFinal > 0m ? costSummary.UnitCostFinal : 1.0m);
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 4. Post balanced GL Journal:
        // Dr 1210 (Finished Goods) = fgValuation
        // Dr 5020 (Scrap Expense) = scrapExpense (if any)
        // Cr 1250 (WIP Inventory) = totalProductionCost
        // Balance variance (if any) to 5030 (Manufacturing Cost Variance)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetOrCreateOpenPeriodAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, today, ct);
        var fgAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1210", "asset", "debit", ct);
        var wipAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "1250", "asset", "debit", ct);
        var scrapAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "5020", "expense", "debit", ct);
        var varianceAccId = await GetAccountIdAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, "5030", "expense", "debit", ct);

        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-MFG-CMP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var idemKey = $"MFG-CMP-{workOrderId}-{movementId}";

        const string jourSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description, functional_currency, source_module, source_type, source_id, idempotency_key, status, created_by, posted_by, posted_at)
            VALUES
                (@id, @tid, @cid, @no, @date, @per, @desc, @cur, 'manufacturing', 'work_order_completion', @srcId, @key, 'posted', @userId, @userId, now());
            """;

        await using (var cmd = new NpgsqlCommand(jourSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", journalId);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("no", journalNo);
            cmd.Parameters.AddWithValue("date", today);
            cmd.Parameters.AddWithValue("per", periodId);
            cmd.Parameters.AddWithValue("desc", $"Finished goods capitalization for WO {wo.WorkOrderNumber}");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("srcId", workOrderId.ToString());
            cmd.Parameters.AddWithValue("key", idemKey);
            cmd.Parameters.AddWithValue("userId", Ctx.UserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string lineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tid, @cid, @jid, @line, @acc, @desc, @cur, @amt, 1.0, @dr, @cr);
            """;

        var lineNo = 1;

        // If total cost is zero (e.g. no costs logged), keep nominal 1 unit in functional currency or balanced lines
        var creditWipAmount = wo.TotalProductionCost > 0m ? wo.TotalProductionCost : 1.00m;
        var debitFgAmount = costSummary.FinishedGoodsValuation > 0m ? costSummary.FinishedGoodsValuation : creditWipAmount;

        // Dr FG 1210
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", lineNo++);
            cmd.Parameters.AddWithValue("acc", fgAccId);
            cmd.Parameters.AddWithValue("desc", $"Capitalize Finished Goods Inventory ({acceptedQty} units)");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", debitFgAmount);
            cmd.Parameters.AddWithValue("dr", debitFgAmount);
            cmd.Parameters.AddWithValue("cr", 0m);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Dr Scrap Expense 5020 (if scrapped units exist)
        if (costSummary.ScrapExpense > 0m)
        {
            await using var cmd = new NpgsqlCommand(lineSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", lineNo++);
            cmd.Parameters.AddWithValue("acc", scrapAccId);
            cmd.Parameters.AddWithValue("desc", $"Production scrap loss ({scrappedQty} units)");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", costSummary.ScrapExpense);
            cmd.Parameters.AddWithValue("dr", costSummary.ScrapExpense);
            cmd.Parameters.AddWithValue("cr", 0m);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Handle rounding variance between Debits and Credits
        var totalDebits = debitFgAmount + costSummary.ScrapExpense;
        var variance = creditWipAmount - totalDebits;

        if (variance > 0m)
        {
            // Dr 5030 Variance
            await using var cmd = new NpgsqlCommand(lineSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", lineNo++);
            cmd.Parameters.AddWithValue("acc", varianceAccId);
            cmd.Parameters.AddWithValue("desc", "Manufacturing rounding/cost variance debit");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", variance);
            cmd.Parameters.AddWithValue("dr", variance);
            cmd.Parameters.AddWithValue("cr", 0m);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        else if (variance < 0m)
        {
            // Cr 5030 Variance
            var absVar = Math.Abs(variance);
            await using var cmd = new NpgsqlCommand(lineSql, conn, tx);
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", lineNo++);
            cmd.Parameters.AddWithValue("acc", varianceAccId);
            cmd.Parameters.AddWithValue("desc", "Manufacturing rounding/cost variance credit");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", absVar);
            cmd.Parameters.AddWithValue("dr", 0m);
            cmd.Parameters.AddWithValue("cr", absVar);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Cr WIP 1250
        await using (var cmd = new NpgsqlCommand(lineSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("jid", journalId);
            cmd.Parameters.AddWithValue("line", lineNo++);
            cmd.Parameters.AddWithValue("acc", wipAccId);
            cmd.Parameters.AddWithValue("desc", $"Credit WIP Inventory completion for WO {wo.WorkOrderNumber}");
            cmd.Parameters.AddWithValue("cur", functionalCurrency);
            cmd.Parameters.AddWithValue("amt", creditWipAmount);
            cmd.Parameters.AddWithValue("dr", 0m);
            cmd.Parameters.AddWithValue("cr", creditWipAmount);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 5. Update Work Order status to completed
        const string updateWoCompleteSql = """
            UPDATE manufacturing.work_orders
            SET status = 'completed',
                completed_quantity = completed_quantity + @accepted,
                scrapped_quantity = scrapped_quantity + @scrapped,
                unit_cost_final = @unitCost,
                completed_at = now(),
                updated_at = now()
            WHERE tenant_id = @tid AND company_id = @cid AND id = @woid;
            """;

        await using (var cmd = new NpgsqlCommand(updateWoCompleteSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("accepted", acceptedQty);
            cmd.Parameters.AddWithValue("scrapped", scrappedQty);
            cmd.Parameters.AddWithValue("unitCost", costSummary.UnitCostFinal > 0m ? costSummary.UnitCostFinal : 1.0m);
            cmd.Parameters.AddWithValue("woid", workOrderId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new WorkOrderCompletionResult(
            WorkOrderId: workOrderId,
            AcceptedQuantity: acceptedQty,
            ScrappedQuantity: scrappedQty,
            UnitCostFinal: costSummary.UnitCostFinal > 0m ? costSummary.UnitCostFinal : 1.0m,
            TotalProductionCost: wo.TotalProductionCost,
            FinishedGoodsLayerId: fgLayerId,
            MovementId: movementId,
            JournalId: journalId);
    }

    // ── Genealogy Traceability & Reporting ─────────────────────────────────────

    public async Task<GenealogyRecord?> GetGenealogyAsync(Guid workOrderId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        var wo = await GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null) return null;

        const string sql = """
            SELECT a.variant_id, a.consumed_qty, a.unit_cost, a.extended_cost,
                   a.layer_id, a.movement_id, l.received_at, l.source_document_type, l.source_document_id
            FROM erp_inventory.inventory_cost_allocation a
            JOIN erp_inventory.inventory_cost_layer l
              ON l.tenant_id = a.tenant_id AND l.company_id = a.company_id AND l.layer_id = a.layer_id
            WHERE a.tenant_id = @tid AND a.company_id = @cid
              AND a.source_document_type = 'work_order' AND a.source_document_id = @woid
            ORDER BY a.variant_id, l.received_at;
            """;

        var compDict = new Dictionary<Guid, List<GenealogyLayerSource>>();
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);
            cmd.Parameters.AddWithValue("woid", workOrderId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var vid = reader.GetGuid(0);
                var qty = reader.GetDecimal(1);
                var ucost = reader.GetDecimal(2);
                var extCost = reader.GetDecimal(3);
                var lid = reader.GetGuid(4);
                var movId = reader.GetGuid(5);
                var recAt = reader.GetFieldValue<DateTimeOffset>(6);
                var srcType = reader.GetString(7);
                var srcId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);

                if (!compDict.TryGetValue(vid, out var list))
                {
                    list = new List<GenealogyLayerSource>();
                    compDict[vid] = list;
                }

                list.Add(new GenealogyLayerSource(
                    LayerId: lid,
                    MovementId: movId,
                    ConsumedQty: qty,
                    UnitCost: ucost,
                    ExtendedCost: extCost,
                    ReceivedAt: recAt,
                    SourceDocumentType: srcType,
                    SourceDocumentId: srcId));
            }
            await reader.CloseAsync();
        }

        var componentNodes = compDict.Select(kvp => new GenealogyComponentNode(
            ComponentVariantId: kvp.Key,
            ConsumedQuantity: kvp.Value.Sum(l => l.ConsumedQty),
            ConsumedCost: kvp.Value.Sum(l => l.ExtendedCost),
            LayerSources: kvp.Value)).ToList();

        await tx.CommitAsync(ct);

        return new GenealogyRecord(
            WorkOrderId: wo.Id,
            WorkOrderNumber: wo.WorkOrderNumber,
            ProductVariantId: wo.ProductVariantId,
            CompletedQuantity: wo.CompletedQuantity,
            CompletedAt: wo.CompletedAt,
            ConsumedComponents: componentNodes);
    }

    public async Task<WipVarianceSummaryReport> GetWipVarianceReportAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetScopeAsync(conn, tx, Ctx.TenantId, Ctx.CompanyId, ct);

        const string sql = """
            SELECT id, work_order_number, status, planned_quantity, completed_quantity,
                   total_material_cost, total_labor_cost, total_overhead_cost, total_production_cost
            FROM manufacturing.work_orders
            WHERE tenant_id = @tid AND company_id = @cid
              AND status IN ('released', 'in_progress', 'partially_completed', 'planned')
            ORDER BY created_at DESC;
            """;

        var list = new List<WorkOrderWipSummary>();
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("tid", Ctx.TenantId);
            cmd.Parameters.AddWithValue("cid", Ctx.CompanyId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new WorkOrderWipSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8)));
            }
            await reader.CloseAsync();
        }

        await tx.CommitAsync(ct);

        return new WipVarianceSummaryReport(
            TotalActiveWorkOrders: list.Count,
            TotalAccumulatedWip: list.Sum(w => w.TotalWip),
            TotalMaterialCost: list.Sum(w => w.MaterialCost),
            TotalLaborCost: list.Sum(w => w.LaborCost),
            TotalOverheadCost: list.Sum(w => w.OverheadCost),
            WorkOrders: list);
    }
}
