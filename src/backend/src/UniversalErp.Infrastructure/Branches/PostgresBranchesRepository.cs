using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Branches;

namespace UniversalErp.Infrastructure.Branches;

public class PostgresBranchesRepository : IBranchesRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly string? _connectionString;

    public PostgresBranchesRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public PostgresBranchesRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(Guid tenantId, Guid? companyId, CancellationToken ct)
    {
        var conn = _dataSource != null
            ? await _dataSource.OpenConnectionAsync(ct)
            : new NpgsqlConnection(_connectionString);

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string setScopeSql = "SELECT set_config('app.tenant_id', @tenantId, false), set_config('app.company_id', @companyId, false);";
        await using var cmd = new NpgsqlCommand(setScopeSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", companyId.HasValue && companyId.Value != Guid.Empty ? companyId.Value.ToString() : "");
        await cmd.ExecuteNonQueryAsync(ct);

        return conn;
    }

    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            SELECT id, tenant_id, company_id, branch_code, branch_name, branch_type,
                   parent_branch_id, tax_registration_number, address_line, city,
                   province_or_state, country_code, manager_id, is_active, created_at
            FROM organization.branches
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY branch_code ASC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<BranchDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapBranch(reader));
        }

        return list;
    }

    public async Task<BranchDto?> GetBranchByIdAsync(Guid tenantId, Guid companyId, Guid branchId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            SELECT id, tenant_id, company_id, branch_code, branch_name, branch_type,
                   parent_branch_id, tax_registration_number, address_line, city,
                   province_or_state, country_code, manager_id, is_active, created_at
            FROM organization.branches
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @branchId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("branchId", branchId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapBranch(reader);
        }

        return null;
    }

    public async Task<BranchDto> CreateBranchAsync(Guid tenantId, Guid companyId, CreateBranchRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            INSERT INTO organization.branches (
                tenant_id, company_id, branch_code, branch_name, branch_type,
                parent_branch_id, tax_registration_number, address_line, city,
                province_or_state, country_code, manager_id, is_active
            )
            VALUES (
                @tenantId, @companyId, @branchCode, @branchName, @branchType,
                @parentBranchId, @taxReg, @addressLine, @city,
                @province, @countryCode, @managerId, true
            )
            RETURNING id, tenant_id, company_id, branch_code, branch_name, branch_type,
                      parent_branch_id, tax_registration_number, address_line, city,
                      province_or_state, country_code, manager_id, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("branchCode", request.BranchCode);
        cmd.Parameters.AddWithValue("branchName", request.BranchName);
        cmd.Parameters.AddWithValue("branchType", request.BranchType);
        cmd.Parameters.AddWithValue("parentBranchId", (object?)request.ParentBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("taxReg", (object?)request.TaxRegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("addressLine", (object?)request.AddressLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("city", request.City);
        cmd.Parameters.AddWithValue("province", request.ProvinceOrState);
        cmd.Parameters.AddWithValue("countryCode", request.CountryCode);
        cmd.Parameters.AddWithValue("managerId", (object?)request.ManagerId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapBranch(reader);
        }

        throw new InvalidOperationException("Failed to insert branch record.");
    }

    public async Task<BranchDto?> UpdateBranchAsync(Guid tenantId, Guid companyId, Guid branchId, UpdateBranchRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            UPDATE organization.branches
            SET branch_name = @branchName,
                branch_type = @branchType,
                parent_branch_id = @parentBranchId,
                tax_registration_number = @taxReg,
                address_line = @addressLine,
                city = @city,
                province_or_state = @province,
                country_code = @countryCode,
                manager_id = @managerId,
                is_active = @isActive,
                updated_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @branchId
            RETURNING id, tenant_id, company_id, branch_code, branch_name, branch_type,
                      parent_branch_id, tax_registration_number, address_line, city,
                      province_or_state, country_code, manager_id, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("branchName", request.BranchName);
        cmd.Parameters.AddWithValue("branchType", request.BranchType);
        cmd.Parameters.AddWithValue("parentBranchId", (object?)request.ParentBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("taxReg", (object?)request.TaxRegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("addressLine", (object?)request.AddressLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("city", request.City);
        cmd.Parameters.AddWithValue("province", request.ProvinceOrState);
        cmd.Parameters.AddWithValue("countryCode", request.CountryCode);
        cmd.Parameters.AddWithValue("managerId", (object?)request.ManagerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isActive", request.IsActive);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapBranch(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<BranchHierarchyDto>> GetHierarchyAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        var branches = await GetBranchesAsync(tenantId, companyId, ct);
        var lookup = branches.ToLookup(b => b.ParentBranchId);

        IReadOnlyList<BranchHierarchyDto> BuildTree(Guid? parentId)
        {
            var children = lookup[parentId];
            return children.Select(b => new BranchHierarchyDto(b, BuildTree(b.Id))).ToList();
        }

        return BuildTree(null);
    }

    private static BranchDto MapBranch(NpgsqlDataReader r)
    {
        return new BranchDto(
            r.GetGuid(0),
            r.GetGuid(1),
            r.GetGuid(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetGuid(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.GetString(9),
            r.GetString(10),
            r.GetString(11),
            r.IsDBNull(12) ? null : r.GetGuid(12),
            r.GetBoolean(13),
            r.GetDateTime(14)
        );
    }
}
