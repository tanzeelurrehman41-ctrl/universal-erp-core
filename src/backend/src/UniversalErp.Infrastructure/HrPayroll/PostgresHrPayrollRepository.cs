using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.HrPayroll;
using UniversalErp.Infrastructure.Finance;

namespace UniversalErp.Infrastructure.HrPayroll;

public class PostgresHrPayrollRepository : IHrPayrollRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly string? _connectionString;

    public PostgresHrPayrollRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public PostgresHrPayrollRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(Guid tenantId, Guid companyId, CancellationToken ct)
    {
        var conn = _dataSource != null
            ? await _dataSource.OpenConnectionAsync(ct)
            : new NpgsqlConnection(_connectionString);

        if (_dataSource == null)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true);";
        cmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", companyId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
        return conn;
    }

    private static DateTimeOffset ToDateTimeOffset(object? val)
    {
        if (val == null || val is DBNull) return DateTimeOffset.MinValue;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val is DateTimeOffset dto) return dto;
        return DateTimeOffset.MinValue;
    }

    private static DateTimeOffset? ToNullableDateTimeOffset(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val is DateTimeOffset dto) return dto;
        return null;
    }

    private static DateOnly ToDateOnly(object? val)
    {
        if (val == null || val is DBNull) return DateOnly.MinValue;
        if (val is DateOnly d) return d;
        if (val is DateTime dt) return DateOnly.FromDateTime(dt);
        return DateOnly.MinValue;
    }

    private static DateOnly? ToNullableDateOnly(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (val is DateOnly d) return d;
        if (val is DateTime dt) return DateOnly.FromDateTime(dt);
        return null;
    }

    // ── Departments & Designations ───────────────────────────────────────────

    public async Task<DepartmentDto> CreateDepartmentAsync(Guid tenantId, Guid companyId, CreateDepartmentRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.departments (tenant_id, company_id, department_code, name, parent_department_id, manager_id)
            VALUES (@tenantId, @companyId, @code, @name, @parentId, @managerId)
            RETURNING id, department_code, name, parent_department_id, manager_id, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.DepartmentCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", request.Name.Trim());
        cmd.Parameters.AddWithValue("parentId", (object?)request.ParentDepartmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("managerId", (object?)request.ManagerId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new DepartmentDto(
            reader.GetGuid(0),
            tenantId,
            companyId,
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            null,
            reader.GetBoolean(5),
            ToDateTimeOffset(reader.GetValue(6))
        );
    }

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT d.id, d.department_code, d.name, d.parent_department_id, d.manager_id,
                   e.first_name || ' ' || e.last_name as manager_name, d.is_active, d.created_at
            FROM hr.departments d
            LEFT JOIN hr.employees e ON e.id = d.manager_id
            WHERE d.tenant_id = @tenantId AND d.company_id = @companyId
            ORDER BY d.department_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<DepartmentDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DepartmentDto(
                reader.GetGuid(0),
                tenantId,
                companyId,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                ToDateTimeOffset(reader.GetValue(7))
            ));
        }
        return list;
    }

    public async Task<DesignationDto> CreateDesignationAsync(Guid tenantId, Guid companyId, CreateDesignationRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.designations (tenant_id, company_id, designation_code, title, grade_level)
            VALUES (@tenantId, @companyId, @code, @title, @grade)
            RETURNING id, designation_code, title, grade_level, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.DesignationCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("title", request.Title.Trim());
        cmd.Parameters.AddWithValue("grade", string.IsNullOrWhiteSpace(request.GradeLevel) ? "G1" : request.GradeLevel.Trim().ToUpperInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new DesignationDto(
            reader.GetGuid(0),
            tenantId,
            companyId,
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            ToDateTimeOffset(reader.GetValue(5))
        );
    }

    public async Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, designation_code, title, grade_level, is_active, created_at
            FROM hr.designations
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY designation_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<DesignationDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DesignationDto(
                reader.GetGuid(0),
                tenantId,
                companyId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                ToDateTimeOffset(reader.GetValue(5))
            ));
        }
        return list;
    }

    // ── Employees ────────────────────────────────────────────────────────────

    public async Task<EmployeeDto> CreateEmployeeAsync(Guid tenantId, Guid companyId, CreateEmployeeRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string empSql = @"
            INSERT INTO hr.employees (
                tenant_id, company_id, employee_code, first_name, last_name, email, phone,
                cnic_or_national_id, date_of_birth, gender, hire_date, status, employment_type,
                department_id, designation_id, manager_id, user_id, bank_name, bank_account_title,
                bank_account_number, bank_iban, tax_identification_number, is_eobi_applicable, is_pf_applicable,
                work_jurisdiction, skill_category
            )
            VALUES (
                @tenantId, @companyId, @code, @fn, @ln, @email, @phone,
                @cnic, @dob, @gender, @hire, @status, @empType,
                @deptId, @desigId, @mgrId, @userId, @bankName, @bankTitle,
                @bankAcc, @iban, @ntn, @eobi, @pf,
                @workJur, @skillCat
            )
            RETURNING id, created_at;";

        await using var cmd = new NpgsqlCommand(empSql, conn, tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.EmployeeCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("fn", request.FirstName.Trim());
        cmd.Parameters.AddWithValue("ln", request.LastName.Trim());
        cmd.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("phone", (object?)request.Phone?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cnic", request.CnicOrNationalId.Trim());
        cmd.Parameters.AddWithValue("dob", (object?)request.DateOfBirth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("gender", (object?)request.Gender?.ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hire", request.HireDate);
        cmd.Parameters.AddWithValue("status", request.Status.ToLowerInvariant());
        cmd.Parameters.AddWithValue("empType", request.EmploymentType.ToLowerInvariant());
        cmd.Parameters.AddWithValue("deptId", (object?)request.DepartmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("desigId", (object?)request.DesignationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("mgrId", (object?)request.ManagerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userId", (object?)request.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bankName", (object?)request.BankName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bankTitle", (object?)request.BankAccountTitle?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bankAcc", (object?)request.BankAccountNumber?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("iban", (object?)request.BankIban?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ntn", (object?)request.TaxIdentificationNumber?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eobi", request.IsEobiApplicable);
        cmd.Parameters.AddWithValue("pf", request.IsPfApplicable);
        cmd.Parameters.AddWithValue("workJur", string.IsNullOrWhiteSpace(request.WorkJurisdiction) ? "FEDERAL" : request.WorkJurisdiction.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("skillCat", string.IsNullOrWhiteSpace(request.SkillCategory) ? "unskilled" : request.SkillCategory.Trim().ToLowerInvariant());

        Guid employeeId;
        DateTimeOffset createdAt;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            employeeId = reader.GetGuid(0);
            createdAt = ToDateTimeOffset(reader.GetValue(1));
        }

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, tenantId, companyId, ct);

        // Set initial compensation structure if base salary > 0
        CompensationStructureDto? comp = null;
        if (request.BaseSalaryPkr > 0)
        {
            const string compSql = @"
                INSERT INTO hr.compensation_structures (
                    tenant_id, company_id, employee_id, effective_date, base_salary_pkr,
                    house_rent_allowance_pkr, utility_allowance_pkr, conveyance_allowance_pkr,
                    medical_allowance_pkr, special_allowance_pkr, gross_salary_pkr, is_active
                )
                VALUES (
                    @tenantId, @companyId, @empId, @effDate, @base,
                    @hra, @util, @conv, @med, @spec, @gross, true
                )
                RETURNING id, created_at;";

            decimal gross = request.BaseSalaryPkr + request.HouseRentAllowancePkr + request.UtilityAllowancePkr +
                            request.ConveyanceAllowancePkr + request.MedicalAllowancePkr + request.SpecialAllowancePkr;

            await using var compCmd = new NpgsqlCommand(compSql, conn, tx);
            compCmd.Parameters.AddWithValue("tenantId", tenantId);
            compCmd.Parameters.AddWithValue("companyId", companyId);
            compCmd.Parameters.AddWithValue("empId", employeeId);
            compCmd.Parameters.AddWithValue("effDate", request.HireDate);
            compCmd.Parameters.AddWithValue("base", request.BaseSalaryPkr);
            compCmd.Parameters.AddWithValue("hra", request.HouseRentAllowancePkr);
            compCmd.Parameters.AddWithValue("util", request.UtilityAllowancePkr);
            compCmd.Parameters.AddWithValue("conv", request.ConveyanceAllowancePkr);
            compCmd.Parameters.AddWithValue("med", request.MedicalAllowancePkr);
            compCmd.Parameters.AddWithValue("spec", request.SpecialAllowancePkr);
            compCmd.Parameters.AddWithValue("gross", gross);

            await using var cReader = await compCmd.ExecuteReaderAsync(ct);
            await cReader.ReadAsync(ct);
            comp = new CompensationStructureDto(
                cReader.GetGuid(0),
                employeeId,
                request.HireDate,
                request.BaseSalaryPkr,
                request.HouseRentAllowancePkr,
                request.UtilityAllowancePkr,
                request.ConveyanceAllowancePkr,
                request.MedicalAllowancePkr,
                request.SpecialAllowancePkr,
                gross,
                "monthly",
                functionalCurrency,
                true,
                ToDateTimeOffset(cReader.GetValue(1))
            );
        }

        await tx.CommitAsync(ct);

        return new EmployeeDto(
            employeeId,
            tenantId,
            companyId,
            request.EmployeeCode.Trim().ToUpperInvariant(),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            request.Email.Trim().ToLowerInvariant(),
            request.Phone?.Trim(),
            request.CnicOrNationalId.Trim(),
            request.DateOfBirth,
            request.Gender,
            request.HireDate,
            null,
            request.Status.ToLowerInvariant(),
            request.EmploymentType.ToLowerInvariant(),
            request.DepartmentId,
            null,
            request.DesignationId,
            null,
            request.ManagerId,
            null,
            request.UserId,
            request.BankName?.Trim(),
            request.BankAccountTitle?.Trim(),
            request.BankAccountNumber?.Trim(),
            request.BankIban?.Trim(),
            request.TaxIdentificationNumber?.Trim(),
            request.IsEobiApplicable,
            request.IsPfApplicable,
            comp,
            createdAt,
            string.IsNullOrWhiteSpace(request.WorkJurisdiction) ? "FEDERAL" : request.WorkJurisdiction.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.SkillCategory) ? "unskilled" : request.SkillCategory.Trim().ToLowerInvariant()
        );
    }

    public async Task<EmployeeDto?> GetEmployeeByIdAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default)
    {
        var list = await GetEmployeesAsync(tenantId, companyId, null, null, ct);
        return list.FirstOrDefault(e => e.Id == employeeId);
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid tenantId, Guid companyId, string? status = null, Guid? departmentId = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT e.id, e.employee_code, e.first_name, e.last_name, e.email, e.phone,
                   e.cnic_or_national_id, e.date_of_birth, e.gender, e.hire_date, e.termination_date,
                   e.status, e.employment_type, e.department_id, dept.name as dept_name,
                   e.designation_id, desig.title as desig_title, e.manager_id,
                   mgr.first_name || ' ' || mgr.last_name as mgr_name, e.user_id,
                   e.bank_name, e.bank_account_title, e.bank_account_number, e.bank_iban,
                   e.tax_identification_number, e.is_eobi_applicable, e.is_pf_applicable,
                   c.id as comp_id, c.effective_date, c.base_salary_pkr, c.house_rent_allowance_pkr,
                   c.utility_allowance_pkr, c.conveyance_allowance_pkr, c.medical_allowance_pkr,
                   c.special_allowance_pkr, c.gross_salary_pkr, c.payment_frequency, c.currency_code,
                   c.created_at as comp_created_at, e.created_at,
                   COALESCE(e.work_jurisdiction, 'FEDERAL'), COALESCE(e.skill_category, 'unskilled')
            FROM hr.employees e
            LEFT JOIN hr.departments dept ON dept.id = e.department_id
            LEFT JOIN hr.designations desig ON desig.id = e.designation_id
            LEFT JOIN hr.employees mgr ON mgr.id = e.manager_id
            LEFT JOIN LATERAL (
                SELECT * FROM hr.compensation_structures
                WHERE employee_id = e.id AND is_active = true
                ORDER BY effective_date DESC LIMIT 1
            ) c ON true
            WHERE e.tenant_id = @tenantId AND e.company_id = @companyId
              AND (@status::text IS NULL OR e.status = @status)
              AND (@deptId::uuid IS NULL OR e.department_id = @deptId)
            ORDER BY e.employee_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("status", (object?)status?.ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deptId", (object?)departmentId ?? DBNull.Value);

        var list = new List<EmployeeDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var empId = reader.GetGuid(0);
            CompensationStructureDto? comp = null;
            if (!reader.IsDBNull(27))
            {
                comp = new CompensationStructureDto(
                    reader.GetGuid(27),
                    empId,
                    ToDateOnly(reader.GetValue(28)),
                    reader.GetDecimal(29),
                    reader.GetDecimal(30),
                    reader.GetDecimal(31),
                    reader.GetDecimal(32),
                    reader.GetDecimal(33),
                    reader.GetDecimal(34),
                    reader.GetDecimal(35),
                    reader.GetString(36),
                    reader.GetString(37),
                    true,
                    ToDateTimeOffset(reader.GetValue(38))
                );
            }

            list.Add(new EmployeeDto(
                empId,
                tenantId,
                companyId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                $"{reader.GetString(2)} {reader.GetString(3)}",
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                ToNullableDateOnly(reader.GetValue(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ToDateOnly(reader.GetValue(9)),
                ToNullableDateOnly(reader.GetValue(10)),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetGuid(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetGuid(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetString(24),
                reader.GetBoolean(25),
                reader.GetBoolean(26),
                comp,
                ToDateTimeOffset(reader.GetValue(39)),
                reader.IsDBNull(40) ? "FEDERAL" : reader.GetString(40),
                reader.IsDBNull(41) ? "unskilled" : reader.GetString(41)
            ));
        }
        return list;
    }

    public async Task<EmployeeDto> UpdateEmployeeAsync(Guid tenantId, Guid companyId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            UPDATE hr.employees SET
                first_name = COALESCE(@fn, first_name),
                last_name = COALESCE(@ln, last_name),
                email = COALESCE(@email, email),
                phone = COALESCE(@phone, phone),
                status = COALESCE(@status, status),
                department_id = COALESCE(@deptId, department_id),
                designation_id = COALESCE(@desigId, designation_id),
                manager_id = COALESCE(@mgrId, manager_id),
                bank_name = COALESCE(@bName, bank_name),
                bank_account_title = COALESCE(@bTitle, bank_account_title),
                bank_account_number = COALESCE(@bNum, bank_account_number),
                bank_iban = COALESCE(@iban, bank_iban),
                tax_identification_number = COALESCE(@ntn, tax_identification_number),
                is_eobi_applicable = COALESCE(@eobi, is_eobi_applicable),
                is_pf_applicable = COALESCE(@pf, is_pf_applicable),
                work_jurisdiction = COALESCE(@workJur, work_jurisdiction),
                skill_category = COALESCE(@skillCat, skill_category)
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @empId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", employeeId);
        cmd.Parameters.AddWithValue("fn", (object?)request.FirstName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ln", (object?)request.LastName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)request.Email?.Trim().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)request.Phone?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)request.Status?.ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deptId", (object?)request.DepartmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("desigId", (object?)request.DesignationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("mgrId", (object?)request.ManagerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bName", (object?)request.BankName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bTitle", (object?)request.BankAccountTitle?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bNum", (object?)request.BankAccountNumber?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("iban", (object?)request.BankIban?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ntn", (object?)request.TaxIdentificationNumber?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eobi", (object?)request.IsEobiApplicable ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pf", (object?)request.IsPfApplicable ?? DBNull.Value);
        cmd.Parameters.AddWithValue("workJur", (object?)request.WorkJurisdiction?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("skillCat", (object?)request.SkillCategory?.Trim().ToLowerInvariant() ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        var updated = await GetEmployeeByIdAsync(tenantId, companyId, employeeId, ct);
        return updated ?? throw new InvalidOperationException("Employee not found after update.");
    }

    public async Task<CompensationStructureDto> SetCompensationAsync(Guid tenantId, Guid companyId, SetCompensationRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Lookup previous active gross salary for audit logging
        const string prevGrossSql = @"
            SELECT gross_salary_pkr FROM hr.compensation_structures
            WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId AND is_active = true
            ORDER BY effective_date DESC LIMIT 1;";
        await using var pgCmd = new NpgsqlCommand(prevGrossSql, conn, tx);
        pgCmd.Parameters.AddWithValue("tenantId", tenantId);
        pgCmd.Parameters.AddWithValue("companyId", companyId);
        pgCmd.Parameters.AddWithValue("empId", request.EmployeeId);
        var prevGrossObj = await pgCmd.ExecuteScalarAsync(ct);
        decimal? prevGross = prevGrossObj is decimal d ? d : null;

        // Deactivate previous active compensations
        const string deactSql = @"
            UPDATE hr.compensation_structures SET is_active = false
            WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId AND is_active = true;";
        await using var deactCmd = new NpgsqlCommand(deactSql, conn, tx);
        deactCmd.Parameters.AddWithValue("tenantId", tenantId);
        deactCmd.Parameters.AddWithValue("companyId", companyId);
        deactCmd.Parameters.AddWithValue("empId", request.EmployeeId);
        await deactCmd.ExecuteNonQueryAsync(ct);

        decimal gross = request.BaseSalaryPkr + request.HouseRentAllowancePkr + request.UtilityAllowancePkr +
                        request.ConveyanceAllowancePkr + request.MedicalAllowancePkr + request.SpecialAllowancePkr;

        const string insSql = @"
            INSERT INTO hr.compensation_structures (
                tenant_id, company_id, employee_id, effective_date, base_salary_pkr,
                house_rent_allowance_pkr, utility_allowance_pkr, conveyance_allowance_pkr,
                medical_allowance_pkr, special_allowance_pkr, gross_salary_pkr, payment_frequency, is_active
            )
            VALUES (
                @tenantId, @companyId, @empId, @effDate, @base,
                @hra, @util, @conv, @med, @spec, @gross, @freq, true
            )
            RETURNING id, created_at;";

        await using var insCmd = new NpgsqlCommand(insSql, conn, tx);
        insCmd.Parameters.AddWithValue("tenantId", tenantId);
        insCmd.Parameters.AddWithValue("companyId", companyId);
        insCmd.Parameters.AddWithValue("empId", request.EmployeeId);
        insCmd.Parameters.AddWithValue("effDate", request.EffectiveDate);
        insCmd.Parameters.AddWithValue("base", request.BaseSalaryPkr);
        insCmd.Parameters.AddWithValue("hra", request.HouseRentAllowancePkr);
        insCmd.Parameters.AddWithValue("util", request.UtilityAllowancePkr);
        insCmd.Parameters.AddWithValue("conv", request.ConveyanceAllowancePkr);
        insCmd.Parameters.AddWithValue("med", request.MedicalAllowancePkr);
        insCmd.Parameters.AddWithValue("spec", request.SpecialAllowancePkr);
        insCmd.Parameters.AddWithValue("gross", gross);
        insCmd.Parameters.AddWithValue("freq", request.PaymentFrequency.ToLowerInvariant());

        await using var reader = await insCmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var comp = new CompensationStructureDto(
            reader.GetGuid(0),
            request.EmployeeId,
            request.EffectiveDate,
            request.BaseSalaryPkr,
            request.HouseRentAllowancePkr,
            request.UtilityAllowancePkr,
            request.ConveyanceAllowancePkr,
            request.MedicalAllowancePkr,
            request.SpecialAllowancePkr,
            gross,
            request.PaymentFrequency.ToLowerInvariant(),
            await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(conn, tx, tenantId, companyId, ct),
            true,
            ToDateTimeOffset(reader.GetValue(1))
        );
        await reader.CloseAsync();

        // Record entry in hr.salary_audit_logs
        const string auditSql = @"
            INSERT INTO hr.salary_audit_logs (
                tenant_id, company_id, employee_id, action_type,
                previous_gross_salary_pkr, new_gross_salary_pkr,
                changed_by, change_reason, changed_at
            )
            VALUES (
                @tenantId, @companyId, @empId, 'compensation_update',
                @prevGross, @newGross, @changedBy, @reason, now()
            );";
        await using var aCmd = new NpgsqlCommand(auditSql, conn, tx);
        aCmd.Parameters.AddWithValue("tenantId", tenantId);
        aCmd.Parameters.AddWithValue("companyId", companyId);
        aCmd.Parameters.AddWithValue("empId", request.EmployeeId);
        aCmd.Parameters.AddWithValue("prevGross", (object?)prevGross ?? DBNull.Value);
        aCmd.Parameters.AddWithValue("newGross", gross);
        aCmd.Parameters.AddWithValue("changedBy", (object?)request.ChangedBy ?? Guid.Empty);
        aCmd.Parameters.AddWithValue("reason", (object?)request.ChangeReason ?? "Compensation update");
        await aCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return comp;
    }

    public async Task<CompensationStructureDto?> GetActiveCompensationAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, employee_id, effective_date, base_salary_pkr, house_rent_allowance_pkr,
                   utility_allowance_pkr, conveyance_allowance_pkr, medical_allowance_pkr,
                   special_allowance_pkr, gross_salary_pkr, payment_frequency, currency_code, is_active, created_at
            FROM hr.compensation_structures
            WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId AND is_active = true
            ORDER BY effective_date DESC LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", employeeId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new CompensationStructureDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ToDateOnly(reader.GetValue(2)),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetBoolean(12),
            ToDateTimeOffset(reader.GetValue(13))
        );
    }

    // ── Shifts & Attendance ──────────────────────────────────────────────────

    public async Task<ShiftDto> CreateShiftAsync(Guid tenantId, Guid companyId, CreateShiftRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.shifts (
                tenant_id, company_id, shift_code, name, start_time, end_time,
                break_duration_minutes, grace_period_minutes, half_day_threshold_hours, full_day_hours
            )
            VALUES (
                @tenantId, @companyId, @code, @name, @start, @end,
                @brk, @grace, @half, @full
            )
            RETURNING id, shift_code, name, start_time, end_time, break_duration_minutes,
                      grace_period_minutes, half_day_threshold_hours, full_day_hours, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.ShiftCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", request.Name.Trim());
        cmd.Parameters.AddWithValue("start", request.StartTime);
        cmd.Parameters.AddWithValue("end", request.EndTime);
        cmd.Parameters.AddWithValue("brk", request.BreakDurationMinutes);
        cmd.Parameters.AddWithValue("grace", request.GracePeriodMinutes);
        cmd.Parameters.AddWithValue("half", request.HalfDayThresholdHours);
        cmd.Parameters.AddWithValue("full", request.FullDayHours);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ShiftDto(
            reader.GetGuid(0),
            tenantId,
            companyId,
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<TimeOnly>(3),
            reader.GetFieldValue<TimeOnly>(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetBoolean(9),
            ToDateTimeOffset(reader.GetValue(10))
        );
    }

    public async Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, shift_code, name, start_time, end_time, break_duration_minutes,
                   grace_period_minutes, half_day_threshold_hours, full_day_hours, is_active, created_at
            FROM hr.shifts
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY shift_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<ShiftDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ShiftDto(
                reader.GetGuid(0),
                tenantId,
                companyId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetFieldValue<TimeOnly>(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                ToDateTimeOffset(reader.GetValue(10))
            ));
        }
        return list;
    }

    public async Task AssignShiftAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid shiftId, DateOnly effectiveFrom, DateOnly? effectiveTo = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.employee_shift_assignments (
                tenant_id, company_id, employee_id, shift_id, effective_from, effective_to, is_active
            )
            VALUES (@tenantId, @companyId, @empId, @shiftId, @effFrom, @effTo, true);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", employeeId);
        cmd.Parameters.AddWithValue("shiftId", shiftId);
        cmd.Parameters.AddWithValue("effFrom", effectiveFrom);
        cmd.Parameters.AddWithValue("effTo", (object?)effectiveTo ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<AttendanceRecordDto> RecordAttendanceAsync(Guid tenantId, Guid companyId, Guid employeeId, DateTimeOffset timestamp, bool isCheckIn, DateOnly? attendanceDate = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        var date = attendanceDate ?? DateOnly.FromDateTime(timestamp.Date);

        // Get assigned shift or default shift
        const string shiftSql = @"
            SELECT s.id, s.shift_code, s.name, s.start_time, s.end_time, s.break_duration_minutes,
                   s.grace_period_minutes, s.half_day_threshold_hours, s.full_day_hours, s.is_active, s.created_at
            FROM hr.shifts s
            JOIN hr.employee_shift_assignments a ON a.shift_id = s.id
            WHERE a.tenant_id = @tenantId AND a.company_id = @companyId AND a.employee_id = @empId
              AND a.effective_from <= @date AND (a.effective_to IS NULL OR a.effective_to >= @date)
            ORDER BY a.effective_from DESC LIMIT 1;";

        ShiftDto? shift = null;
        await using (var shiftCmd = new NpgsqlCommand(shiftSql, conn))
        {
            shiftCmd.Parameters.AddWithValue("tenantId", tenantId);
            shiftCmd.Parameters.AddWithValue("companyId", companyId);
            shiftCmd.Parameters.AddWithValue("empId", employeeId);
            shiftCmd.Parameters.AddWithValue("date", date);
            await using var sReader = await shiftCmd.ExecuteReaderAsync(ct);
            if (await sReader.ReadAsync(ct))
            {
                shift = new ShiftDto(
                    sReader.GetGuid(0), tenantId, companyId, sReader.GetString(1), sReader.GetString(2),
                    sReader.GetFieldValue<TimeOnly>(3), sReader.GetFieldValue<TimeOnly>(4), sReader.GetInt32(5),
                    sReader.GetInt32(6), sReader.GetDecimal(7), sReader.GetDecimal(8), sReader.GetBoolean(9),
                    ToDateTimeOffset(sReader.GetValue(10))
                );
            }
        }

        // Fallback standard shift 09:00 - 18:00 if none assigned
        shift ??= new ShiftDto(
            Guid.Empty, tenantId, companyId, "STD", "Standard Shift",
            new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 15, 4.0m, 8.0m, true, DateTimeOffset.UtcNow
        );

        // Check if record already exists for date
        const string findSql = @"
            SELECT id, check_in_time, check_out_time FROM hr.attendance_records
            WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId AND attendance_date = @date;";

        Guid? recordId = null;
        DateTimeOffset? existingCheckIn = null;
        DateTimeOffset? existingCheckOut = null;

        await using (var findCmd = new NpgsqlCommand(findSql, conn))
        {
            findCmd.Parameters.AddWithValue("tenantId", tenantId);
            findCmd.Parameters.AddWithValue("companyId", companyId);
            findCmd.Parameters.AddWithValue("empId", employeeId);
            findCmd.Parameters.AddWithValue("date", date);
            await using var fReader = await findCmd.ExecuteReaderAsync(ct);
            if (await fReader.ReadAsync(ct))
            {
                recordId = fReader.GetGuid(0);
                existingCheckIn = ToNullableDateTimeOffset(fReader.GetValue(1));
                existingCheckOut = ToNullableDateTimeOffset(fReader.GetValue(2));
            }
        }

        var newCheckIn = isCheckIn ? timestamp : existingCheckIn;
        var newCheckOut = !isCheckIn ? timestamp : existingCheckOut;

        var evaluated = AttendanceEvaluationEngine.EvaluateAttendance(shift, newCheckIn, newCheckOut);

        if (recordId == null)
        {
            const string insSql = @"
                INSERT INTO hr.attendance_records (
                    tenant_id, company_id, employee_id, attendance_date, check_in_time, check_out_time,
                    total_work_minutes, overtime_minutes, status, late_minutes
                )
                VALUES (
                    @tenantId, @companyId, @empId, @date, @cin, @cout,
                    @workMin, @otMin, @status, @lateMin
                )
                RETURNING id, created_at;";

            await using var insCmd = new NpgsqlCommand(insSql, conn);
            insCmd.Parameters.AddWithValue("tenantId", tenantId);
            insCmd.Parameters.AddWithValue("companyId", companyId);
            insCmd.Parameters.AddWithValue("empId", employeeId);
            insCmd.Parameters.AddWithValue("date", date);
            insCmd.Parameters.AddWithValue("cin", (object?)newCheckIn?.ToUniversalTime() ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("cout", (object?)newCheckOut?.ToUniversalTime() ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("workMin", evaluated.TotalWorkMinutes);
            insCmd.Parameters.AddWithValue("otMin", evaluated.OvertimeMinutes);
            insCmd.Parameters.AddWithValue("status", evaluated.Status);
            insCmd.Parameters.AddWithValue("lateMin", evaluated.LateMinutes);

            await using var reader = await insCmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new AttendanceRecordDto(
                reader.GetGuid(0), tenantId, companyId, employeeId, null, null, date,
                newCheckIn, newCheckOut, evaluated.TotalWorkMinutes, evaluated.OvertimeMinutes,
                evaluated.Status, evaluated.LateMinutes, false, null, null, ToDateTimeOffset(reader.GetValue(1))
            );
        }
        else
        {
            const string updSql = @"
                UPDATE hr.attendance_records SET
                    check_in_time = @cin,
                    check_out_time = @cout,
                    total_work_minutes = @workMin,
                    overtime_minutes = @otMin,
                    status = @status,
                    late_minutes = @lateMin
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id
                RETURNING created_at;";

            await using var updCmd = new NpgsqlCommand(updSql, conn);
            updCmd.Parameters.AddWithValue("tenantId", tenantId);
            updCmd.Parameters.AddWithValue("companyId", companyId);
            updCmd.Parameters.AddWithValue("id", recordId.Value);
            updCmd.Parameters.AddWithValue("cin", (object?)newCheckIn?.ToUniversalTime() ?? DBNull.Value);
            updCmd.Parameters.AddWithValue("cout", (object?)newCheckOut?.ToUniversalTime() ?? DBNull.Value);
            updCmd.Parameters.AddWithValue("workMin", evaluated.TotalWorkMinutes);
            updCmd.Parameters.AddWithValue("otMin", evaluated.OvertimeMinutes);
            updCmd.Parameters.AddWithValue("status", evaluated.Status);
            updCmd.Parameters.AddWithValue("lateMin", evaluated.LateMinutes);

            var createdAt = ToDateTimeOffset(await updCmd.ExecuteScalarAsync(ct));
            return new AttendanceRecordDto(
                recordId.Value, tenantId, companyId, employeeId, null, null, date,
                newCheckIn, newCheckOut, evaluated.TotalWorkMinutes, evaluated.OvertimeMinutes,
                evaluated.Status, evaluated.LateMinutes, false, null, null, createdAt
            );
        }
    }

    public async Task<AttendanceRecordDto> RegularizeAttendanceAsync(Guid tenantId, Guid companyId, RegularizeAttendanceRequest request, Guid userId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        int totalMinutes = (int)(request.CheckOutTime - request.CheckInTime).TotalMinutes;

        const string sql = @"
            UPDATE hr.attendance_records SET
                check_in_time = @cin,
                check_out_time = @cout,
                total_work_minutes = @workMin,
                status = @status,
                is_regularized = true,
                regularization_reason = @reason,
                regularized_by = @userId
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id
            RETURNING employee_id, attendance_date, overtime_minutes, late_minutes, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", request.AttendanceRecordId);
        cmd.Parameters.AddWithValue("cin", request.CheckInTime.ToUniversalTime());
        cmd.Parameters.AddWithValue("cout", request.CheckOutTime.ToUniversalTime());
        cmd.Parameters.AddWithValue("workMin", Math.Max(0, totalMinutes));
        cmd.Parameters.AddWithValue("status", request.Status.ToLowerInvariant());
        cmd.Parameters.AddWithValue("reason", request.Reason.Trim());
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new AttendanceRecordDto(
            request.AttendanceRecordId, tenantId, companyId, reader.GetGuid(0), null, null,
            ToDateOnly(reader.GetValue(1)), request.CheckInTime, request.CheckOutTime,
            Math.Max(0, totalMinutes), reader.GetInt32(2), request.Status.ToLowerInvariant(),
            reader.GetInt32(3), true, request.Reason.Trim(), userId, ToDateTimeOffset(reader.GetValue(4))
        );
    }

    public async Task<IReadOnlyList<AttendanceRecordDto>> GetAttendanceRecordsAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, Guid? employeeId = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT a.id, a.employee_id, e.employee_code, e.first_name || ' ' || e.last_name as emp_name,
                   a.attendance_date, a.check_in_time, a.check_out_time, a.total_work_minutes,
                   a.overtime_minutes, a.status, a.late_minutes, a.is_regularized,
                   a.regularization_reason, a.regularized_by, a.created_at
            FROM hr.attendance_records a
            JOIN hr.employees e ON e.id = a.employee_id
            WHERE a.tenant_id = @tenantId AND a.company_id = @companyId
              AND a.attendance_date >= @from AND a.attendance_date <= @to
              AND (@empId::uuid IS NULL OR a.employee_id = @empId)
            ORDER BY a.attendance_date DESC, e.employee_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("from", fromDate);
        cmd.Parameters.AddWithValue("to", toDate);
        cmd.Parameters.AddWithValue("empId", (object?)employeeId ?? DBNull.Value);

        var list = new List<AttendanceRecordDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AttendanceRecordDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                ToDateOnly(reader.GetValue(4)), ToNullableDateTimeOffset(reader.GetValue(5)), ToNullableDateTimeOffset(reader.GetValue(6)),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9), reader.GetInt32(10),
                reader.GetBoolean(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13), ToDateTimeOffset(reader.GetValue(14))
            ));
        }
        return list;
    }

    // ── Leaves & Holidays ────────────────────────────────────────────────────

    public async Task<LeaveTypeDto> CreateLeaveTypeAsync(Guid tenantId, Guid companyId, CreateLeaveTypeRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.leave_types (
                tenant_id, company_id, code, name, default_annual_quota, is_paid,
                is_carry_forward_allowed, max_carry_forward_days, is_encashable
            )
            VALUES (@tenantId, @companyId, @code, @name, @quota, @paid, @cf, @maxCf, @encash)
            RETURNING id, code, name, default_annual_quota, is_paid, is_carry_forward_allowed,
                      max_carry_forward_days, is_encashable, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.Code.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", request.Name.Trim());
        cmd.Parameters.AddWithValue("quota", request.DefaultAnnualQuota);
        cmd.Parameters.AddWithValue("paid", request.IsPaid);
        cmd.Parameters.AddWithValue("cf", request.IsCarryForwardAllowed);
        cmd.Parameters.AddWithValue("maxCf", request.MaxCarryForwardDays);
        cmd.Parameters.AddWithValue("encash", request.IsEncashable);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new LeaveTypeDto(
            reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetString(2),
            reader.GetDecimal(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetDecimal(6),
            reader.GetBoolean(7), reader.GetBoolean(8), ToDateTimeOffset(reader.GetValue(9))
        );
    }

    public async Task<IReadOnlyList<LeaveTypeDto>> GetLeaveTypesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, code, name, default_annual_quota, is_paid, is_carry_forward_allowed,
                   max_carry_forward_days, is_encashable, is_active, created_at
            FROM hr.leave_types
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<LeaveTypeDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new LeaveTypeDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetDecimal(6),
                reader.GetBoolean(7), reader.GetBoolean(8), ToDateTimeOffset(reader.GetValue(9))
            ));
        }
        return list;
    }

    public async Task<LeaveAllocationDto> AllocateLeavesAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid leaveTypeId, int year, decimal days, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.leave_allocations (
                tenant_id, company_id, employee_id, leave_type_id, year,
                allocated_days, used_days, pending_days, carried_forward_days, remaining_days
            )
            VALUES (
                @tenantId, @companyId, @empId, @ltId, @yr,
                @days, 0, 0, 0, @days
            )
            ON CONFLICT (tenant_id, company_id, employee_id, leave_type_id, year)
            DO UPDATE SET allocated_days = EXCLUDED.allocated_days,
                          remaining_days = (EXCLUDED.allocated_days + hr.leave_allocations.carried_forward_days) - hr.leave_allocations.used_days
            RETURNING id, allocated_days, used_days, pending_days, carried_forward_days, remaining_days, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", employeeId);
        cmd.Parameters.AddWithValue("ltId", leaveTypeId);
        cmd.Parameters.AddWithValue("yr", year);
        cmd.Parameters.AddWithValue("days", days);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new LeaveAllocationDto(
            reader.GetGuid(0), tenantId, companyId, employeeId, leaveTypeId, "", "", year,
            reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
            reader.GetDecimal(5), ToDateTimeOffset(reader.GetValue(6))
        );
    }

    public async Task<IReadOnlyList<LeaveAllocationDto>> GetLeaveAllocationsAsync(Guid tenantId, Guid companyId, Guid employeeId, int year, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT a.id, a.employee_id, a.leave_type_id, lt.code, lt.name, a.year,
                   a.allocated_days, a.used_days, a.pending_days, a.carried_forward_days,
                   a.remaining_days, a.created_at
            FROM hr.leave_allocations a
            JOIN hr.leave_types lt ON lt.id = a.leave_type_id
            WHERE a.tenant_id = @tenantId AND a.company_id = @companyId AND a.employee_id = @empId AND a.year = @yr
            ORDER BY lt.code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", employeeId);
        cmd.Parameters.AddWithValue("yr", year);

        var list = new List<LeaveAllocationDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new LeaveAllocationDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
                ToDateTimeOffset(reader.GetValue(11))
            ));
        }
        return list;
    }

    public async Task<LeaveRequestDto> SubmitLeaveRequestAsync(Guid tenantId, Guid companyId, SubmitLeaveRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.leave_requests (
                tenant_id, company_id, employee_id, leave_type_id, start_date, end_date, total_days, reason, status
            )
            VALUES (
                @tenantId, @companyId, @empId, @ltId, @start, @end, @days, @reason, 'pending'
            )
            RETURNING id, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", request.EmployeeId);
        cmd.Parameters.AddWithValue("ltId", request.LeaveTypeId);
        cmd.Parameters.AddWithValue("start", request.StartDate);
        cmd.Parameters.AddWithValue("end", request.EndDate);
        cmd.Parameters.AddWithValue("days", request.TotalDays);
        cmd.Parameters.AddWithValue("reason", request.Reason.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new LeaveRequestDto(
            reader.GetGuid(0), tenantId, companyId, request.EmployeeId, null, null,
            request.LeaveTypeId, null, request.StartDate, request.EndDate, request.TotalDays,
            request.Reason.Trim(), "pending", null, null, null, ToDateTimeOffset(reader.GetValue(1))
        );
    }

    public async Task<LeaveRequestDto> ProcessLeaveApprovalAsync(Guid tenantId, Guid companyId, ApproveLeaveRequest request, Guid approvedBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string findReqSql = @"
            SELECT employee_id, leave_type_id, start_date, end_date, total_days, reason, status
            FROM hr.leave_requests
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id FOR UPDATE;";

        Guid empId, ltId;
        DateOnly start, end;
        decimal days;
        string reason, currentStatus;

        await using (var fCmd = new NpgsqlCommand(findReqSql, conn, tx))
        {
            fCmd.Parameters.AddWithValue("tenantId", tenantId);
            fCmd.Parameters.AddWithValue("companyId", companyId);
            fCmd.Parameters.AddWithValue("id", request.LeaveRequestId);
            await using var reader = await fCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Leave request not found.");

            empId = reader.GetGuid(0);
            ltId = reader.GetGuid(1);
            start = ToDateOnly(reader.GetValue(2));
            end = ToDateOnly(reader.GetValue(3));
            days = reader.GetDecimal(4);
            reason = reader.GetString(5);
            currentStatus = reader.GetString(6);
        }

        if (currentStatus != "pending")
            throw new InvalidOperationException($"Cannot process leave request with status '{currentStatus}'.");

        string newStatus = request.Approved ? "approved" : "rejected";

        if (request.Approved)
        {
            // Update leave allocation used & remaining balance
            int year = start.Year;
            const string updAllocSql = @"
                UPDATE hr.leave_allocations SET
                    used_days = used_days + @days,
                    remaining_days = remaining_days - @days
                WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId
                  AND leave_type_id = @ltId AND year = @yr;";

            await using var aCmd = new NpgsqlCommand(updAllocSql, conn, tx);
            aCmd.Parameters.AddWithValue("tenantId", tenantId);
            aCmd.Parameters.AddWithValue("companyId", companyId);
            aCmd.Parameters.AddWithValue("empId", empId);
            aCmd.Parameters.AddWithValue("ltId", ltId);
            aCmd.Parameters.AddWithValue("yr", year);
            aCmd.Parameters.AddWithValue("days", days);
            await aCmd.ExecuteNonQueryAsync(ct);
        }

        const string updReqSql = @"
            UPDATE hr.leave_requests SET
                status = @status,
                approved_by = @appBy,
                approved_at = now(),
                rejection_reason = @notes
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id
            RETURNING created_at;";

        await using var updCmd = new NpgsqlCommand(updReqSql, conn, tx);
        updCmd.Parameters.AddWithValue("tenantId", tenantId);
        updCmd.Parameters.AddWithValue("companyId", companyId);
        updCmd.Parameters.AddWithValue("id", request.LeaveRequestId);
        updCmd.Parameters.AddWithValue("status", newStatus);
        updCmd.Parameters.AddWithValue("appBy", approvedBy);
        updCmd.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);

        var createdAt = ToDateTimeOffset(await updCmd.ExecuteScalarAsync(ct));
        await tx.CommitAsync(ct);

        return new LeaveRequestDto(
            request.LeaveRequestId, tenantId, companyId, empId, null, null, ltId, null,
            start, end, days, reason, newStatus, approvedBy, DateTimeOffset.UtcNow,
            request.Approved ? null : request.Notes, createdAt
        );
    }

    public async Task<IReadOnlyList<LeaveRequestDto>> GetLeaveRequestsAsync(Guid tenantId, Guid companyId, string? status = null, Guid? employeeId = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT r.id, r.employee_id, e.employee_code, e.first_name || ' ' || e.last_name as emp_name,
                   r.leave_type_id, lt.name as leave_name, r.start_date, r.end_date, r.total_days,
                   r.reason, r.status, r.approved_by, r.approved_at, r.rejection_reason, r.created_at
            FROM hr.leave_requests r
            JOIN hr.employees e ON e.id = r.employee_id
            JOIN hr.leave_types lt ON lt.id = r.leave_type_id
            WHERE r.tenant_id = @tenantId AND r.company_id = @companyId
              AND (@status::text IS NULL OR r.status = @status)
              AND (@empId::uuid IS NULL OR r.employee_id = @empId)
            ORDER BY r.created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("status", (object?)status?.ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("empId", (object?)employeeId ?? DBNull.Value);

        var list = new List<LeaveRequestDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new LeaveRequestDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetGuid(4), reader.GetString(5), ToDateOnly(reader.GetValue(6)), ToDateOnly(reader.GetValue(7)),
                reader.GetDecimal(8), reader.GetString(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11), ToNullableDateTimeOffset(reader.GetValue(12)),
                reader.IsDBNull(13) ? null : reader.GetString(13), ToDateTimeOffset(reader.GetValue(14))
            ));
        }
        return list;
    }

    public async Task<HolidayCalendarDto> CreateHolidayAsync(Guid tenantId, Guid companyId, CreateHolidayRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.holiday_calendars (tenant_id, company_id, holiday_date, name, description, is_recurring)
            VALUES (@tenantId, @companyId, @date, @name, @desc, @rec)
            RETURNING id, holiday_date, name, description, is_recurring, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("date", request.HolidayDate);
        cmd.Parameters.AddWithValue("name", request.Name.Trim());
        cmd.Parameters.AddWithValue("desc", (object?)request.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rec", request.IsRecurring);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new HolidayCalendarDto(
            reader.GetGuid(0), tenantId, companyId, ToDateOnly(reader.GetValue(1)), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), ToDateTimeOffset(reader.GetValue(5))
        );
    }

    public async Task<IReadOnlyList<HolidayCalendarDto>> GetHolidaysAsync(Guid tenantId, Guid companyId, int? year = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, holiday_date, name, description, is_recurring, created_at
            FROM hr.holiday_calendars
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND (@yr::int IS NULL OR EXTRACT(YEAR FROM holiday_date) = @yr)
            ORDER BY holiday_date ASC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("yr", (object?)year ?? DBNull.Value);

        var list = new List<HolidayCalendarDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new HolidayCalendarDto(
                reader.GetGuid(0), tenantId, companyId, ToDateOnly(reader.GetValue(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), ToDateTimeOffset(reader.GetValue(5))
            ));
        }
        return list;
    }

    // ── Statutory & Tax Slabs ────────────────────────────────────────────────

    public async Task<StatutoryConfigDto> SetStatutoryConfigAsync(Guid tenantId, Guid companyId, SetStatutoryConfigRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.statutory_configurations (
                tenant_id, company_id, config_key, config_name, effective_from, effective_to,
                rate_percentage, threshold_amount_pkr, employer_rate_percentage,
                employer_fixed_amount_pkr, employee_fixed_amount_pkr, is_active,
                jurisdiction, authoritative_source, approval_reference
            )
            VALUES (
                @tenantId, @companyId, @key, @name, @effFrom, @effTo,
                @rate, @thresh, @empRate, @empFix, @eeFix, true,
                @jurisdiction, @authSource, @apprRef
            )
            ON CONFLICT (tenant_id, company_id, config_key, effective_from)
            DO UPDATE SET
                config_name = EXCLUDED.config_name,
                effective_to = EXCLUDED.effective_to,
                rate_percentage = EXCLUDED.rate_percentage,
                threshold_amount_pkr = EXCLUDED.threshold_amount_pkr,
                employer_rate_percentage = EXCLUDED.employer_rate_percentage,
                employer_fixed_amount_pkr = EXCLUDED.employer_fixed_amount_pkr,
                employee_fixed_amount_pkr = EXCLUDED.employee_fixed_amount_pkr,
                jurisdiction = EXCLUDED.jurisdiction,
                authoritative_source = EXCLUDED.authoritative_source,
                approval_reference = EXCLUDED.approval_reference,
                is_active = true
            RETURNING id, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("key", request.ConfigKey.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("name", request.ConfigName.Trim());
        cmd.Parameters.AddWithValue("effFrom", request.EffectiveFrom);
        cmd.Parameters.AddWithValue("effTo", (object?)request.EffectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rate", (object?)request.RatePercentage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("thresh", (object?)request.ThresholdAmountPkr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("empRate", (object?)request.EmployerRatePercentage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("empFix", (object?)request.EmployerFixedAmountPkr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eeFix", (object?)request.EmployeeFixedAmountPkr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("jurisdiction", request.Jurisdiction);
        cmd.Parameters.AddWithValue("authSource", request.AuthoritativeSource);
        cmd.Parameters.AddWithValue("apprRef", request.ApprovalReference);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new StatutoryConfigDto(
            reader.GetGuid(0), tenantId, companyId, request.ConfigKey.Trim().ToLowerInvariant(),
            request.ConfigName.Trim(), request.EffectiveFrom, request.EffectiveTo, request.RatePercentage,
            request.ThresholdAmountPkr, request.EmployerRatePercentage, request.EmployerFixedAmountPkr,
            request.EmployeeFixedAmountPkr, true, ToDateTimeOffset(reader.GetValue(1)),
            request.Jurisdiction, request.AuthoritativeSource, request.ApprovalReference
        );
    }

    public async Task<IReadOnlyList<StatutoryConfigDto>> GetStatutoryConfigsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, config_key, config_name, effective_from, effective_to, rate_percentage,
                   threshold_amount_pkr, employer_rate_percentage, employer_fixed_amount_pkr,
                   employee_fixed_amount_pkr, is_active, created_at,
                   COALESCE(jurisdiction, 'FEDERAL'),
                   COALESCE(authoritative_source, 'EOBI Act 1976 / Provincial Labour Gazette'),
                   COALESCE(approval_reference, 'STAT-REG-2026-01')
            FROM hr.statutory_configurations
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY config_key, effective_from DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<StatutoryConfigDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new StatutoryConfigDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetString(2),
                ToDateOnly(reader.GetValue(3)), ToNullableDateOnly(reader.GetValue(4)),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.GetBoolean(10), ToDateTimeOffset(reader.GetValue(11)),
                reader.GetString(12), reader.GetString(13), reader.GetString(14)
            ));
        }
        return list;
    }

    public async Task<TaxSlabDto> CreateTaxSlabAsync(Guid tenantId, Guid companyId, CreateTaxSlabRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.tax_slabs (
                tenant_id, company_id, tax_year, effective_from, effective_to, slab_order,
                min_annual_income_pkr, max_annual_income_pkr, fixed_tax_amount_pkr,
                marginal_tax_rate_percentage, description,
                jurisdiction, authoritative_source, approval_reference,
                surcharge_rate_percentage, surcharge_threshold_pkr
            )
            VALUES (
                @tenantId, @companyId, @yr, @effFrom, @effTo, @order,
                @min, @max, @fixed, @rate, @desc,
                @jurisdiction, @authSource, @apprRef, @surchargeRate, @surchargeThresh
            )
            ON CONFLICT (tenant_id, company_id, tax_year, slab_order)
            DO UPDATE SET
                min_annual_income_pkr = EXCLUDED.min_annual_income_pkr,
                max_annual_income_pkr = EXCLUDED.max_annual_income_pkr,
                fixed_tax_amount_pkr = EXCLUDED.fixed_tax_amount_pkr,
                marginal_tax_rate_percentage = EXCLUDED.marginal_tax_rate_percentage,
                description = EXCLUDED.description,
                jurisdiction = EXCLUDED.jurisdiction,
                authoritative_source = EXCLUDED.authoritative_source,
                approval_reference = EXCLUDED.approval_reference,
                surcharge_rate_percentage = EXCLUDED.surcharge_rate_percentage,
                surcharge_threshold_pkr = EXCLUDED.surcharge_threshold_pkr
            RETURNING id, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("yr", request.TaxYear);
        cmd.Parameters.AddWithValue("effFrom", request.EffectiveFrom);
        cmd.Parameters.AddWithValue("effTo", (object?)request.EffectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("order", request.SlabOrder);
        cmd.Parameters.AddWithValue("min", request.MinAnnualIncomePkr);
        cmd.Parameters.AddWithValue("max", (object?)request.MaxAnnualIncomePkr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fixed", request.FixedTaxAmountPkr);
        cmd.Parameters.AddWithValue("rate", request.MarginalTaxRatePercentage);
        cmd.Parameters.AddWithValue("desc", (object?)request.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("jurisdiction", request.Jurisdiction);
        cmd.Parameters.AddWithValue("authSource", request.AuthoritativeSource);
        cmd.Parameters.AddWithValue("apprRef", request.ApprovalReference);
        cmd.Parameters.AddWithValue("surchargeRate", request.SurchargeRatePercentage);
        cmd.Parameters.AddWithValue("surchargeThresh", request.SurchargeThresholdPkr);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new TaxSlabDto(
            reader.GetGuid(0), tenantId, companyId, request.TaxYear, request.EffectiveFrom, request.EffectiveTo,
            request.SlabOrder, request.MinAnnualIncomePkr, request.MaxAnnualIncomePkr, request.FixedTaxAmountPkr,
            request.MarginalTaxRatePercentage, request.Description, ToDateTimeOffset(reader.GetValue(1)),
            request.Jurisdiction, request.AuthoritativeSource, request.ApprovalReference,
            request.SurchargeRatePercentage, request.SurchargeThresholdPkr
        );
    }

    public async Task<IReadOnlyList<TaxSlabDto>> GetTaxSlabsAsync(Guid tenantId, Guid companyId, int? taxYear = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT id, tax_year, effective_from, effective_to, slab_order, min_annual_income_pkr,
                   max_annual_income_pkr, fixed_tax_amount_pkr, marginal_tax_rate_percentage,
                   description, created_at,
                   COALESCE(jurisdiction, 'FEDERAL'),
                   COALESCE(authoritative_source, 'Income Tax Ordinance, 2001 / Finance Act 2026'),
                   COALESCE(approval_reference, 'FBR-CIRCULAR-2026-SALARIED'),
                   COALESCE(surcharge_rate_percentage, 0),
                   COALESCE(surcharge_threshold_pkr, 10000000)
            FROM hr.tax_slabs
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND (@yr::int IS NULL OR tax_year = @yr)
            ORDER BY slab_order ASC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("yr", (object?)taxYear ?? DBNull.Value);

        var list = new List<TaxSlabDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TaxSlabDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetInt32(1), ToDateOnly(reader.GetValue(2)),
                ToNullableDateOnly(reader.GetValue(3)), reader.GetInt32(4), reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), ToDateTimeOffset(reader.GetValue(10)),
                reader.GetString(11), reader.GetString(12), reader.GetString(13),
                reader.GetDecimal(14), reader.GetDecimal(15)
            ));
        }
        return list;
    }

    // ── Salary Loans ─────────────────────────────────────────────────────────

    public async Task<SalaryLoanDto> CreateSalaryLoanAsync(Guid tenantId, Guid companyId, CreateSalaryLoanRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        string loanNumber = request.LoanNumber ?? $"LN-{DateTime.UtcNow.Year}-{Math.Abs(Guid.NewGuid().GetHashCode()) % 90000 + 10000}";

        const string sql = @"
            INSERT INTO hr.salary_loans (
                tenant_id, company_id, loan_number, employee_id, principal_amount_pkr,
                monthly_installment_pkr, total_installments, paid_installments,
                remaining_balance_pkr, status
            )
            VALUES (
                @tenantId, @companyId, @loanNo, @empId, @principal,
                @installment, @total, 0, @principal, 'active'
            )
            RETURNING id, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("loanNo", loanNumber);
        cmd.Parameters.AddWithValue("empId", request.EmployeeId);
        cmd.Parameters.AddWithValue("principal", request.PrincipalAmountPkr);
        cmd.Parameters.AddWithValue("installment", request.MonthlyInstallmentPkr);
        cmd.Parameters.AddWithValue("total", request.TotalInstallments);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new SalaryLoanDto(
            reader.GetGuid(0), tenantId, companyId, loanNumber, request.EmployeeId, null, null,
            request.PrincipalAmountPkr, request.MonthlyInstallmentPkr, request.TotalInstallments, 0,
            request.PrincipalAmountPkr, "active", null, null, ToDateTimeOffset(reader.GetValue(1))
        );
    }

    public async Task<IReadOnlyList<SalaryLoanDto>> GetSalaryLoansAsync(Guid tenantId, Guid companyId, Guid? employeeId = null, string? status = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT l.id, l.loan_number, l.employee_id, e.employee_code, e.first_name || ' ' || e.last_name as emp_name,
                   l.principal_amount_pkr, l.monthly_installment_pkr, l.total_installments,
                   l.paid_installments, l.remaining_balance_pkr, l.status, l.approved_by,
                   l.disbursed_at, l.created_at
            FROM hr.salary_loans l
            JOIN hr.employees e ON e.id = l.employee_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId
              AND (@empId::uuid IS NULL OR l.employee_id = @empId)
              AND (@status::text IS NULL OR l.status = @status)
            ORDER BY l.created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("empId", (object?)employeeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status?.ToLowerInvariant() ?? DBNull.Value);

        var list = new List<SalaryLoanDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SalaryLoanDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetDecimal(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11), ToNullableDateTimeOffset(reader.GetValue(12)),
                ToDateTimeOffset(reader.GetValue(13))
            ));
        }
        return list;
    }

    // ── Payroll Periods & Runs ───────────────────────────────────────────────

    public async Task<PayrollPeriodDto> CreatePayrollPeriodAsync(Guid tenantId, Guid companyId, CreatePayrollPeriodRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            INSERT INTO hr.payroll_periods (tenant_id, company_id, period_name, period_code, start_date, end_date, status)
            VALUES (@tenantId, @companyId, @name, @code, @start, @end, 'draft')
            RETURNING id, period_name, period_code, start_date, end_date, status, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("name", request.PeriodName.Trim());
        cmd.Parameters.AddWithValue("code", request.PeriodCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("start", request.StartDate);
        cmd.Parameters.AddWithValue("end", request.EndDate);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new PayrollPeriodDto(
            reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetString(2),
            ToDateOnly(reader.GetValue(3)), ToDateOnly(reader.GetValue(4)), reader.GetString(5),
            0, 0, 0, 0, null, null, null, null, null, null, null, 0, ToDateTimeOffset(reader.GetValue(6))
        );
    }

    public async Task<IReadOnlyList<PayrollPeriodDto>> GetPayrollPeriodsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT p.id, p.period_name, p.period_code, p.start_date, p.end_date, p.status,
                   p.total_gross_pkr, p.total_deductions_pkr, p.total_net_pkr,
                   p.total_employer_contributions_pkr, p.gl_journal_id, p.processed_by,
                   p.processed_at, p.approved_by, p.approved_at, p.posted_by, p.posted_at,
                   COUNT(ps.id) as total_payslips, p.created_at
            FROM hr.payroll_periods p
            LEFT JOIN hr.payslips ps ON ps.payroll_period_id = p.id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId
            GROUP BY p.id, p.period_name, p.period_code, p.start_date, p.end_date, p.status,
                     p.total_gross_pkr, p.total_deductions_pkr, p.total_net_pkr,
                     p.total_employer_contributions_pkr, p.gl_journal_id, p.processed_by,
                     p.processed_at, p.approved_by, p.approved_at, p.posted_by, p.posted_at, p.created_at
            ORDER BY p.start_date DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<PayrollPeriodDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PayrollPeriodDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetString(1), reader.GetString(2),
                ToDateOnly(reader.GetValue(3)), ToDateOnly(reader.GetValue(4)), reader.GetString(5),
                reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.IsDBNull(11) ? null : reader.GetGuid(11),
                ToNullableDateTimeOffset(reader.GetValue(12)), reader.IsDBNull(13) ? null : reader.GetGuid(13),
                ToNullableDateTimeOffset(reader.GetValue(14)), reader.IsDBNull(15) ? null : reader.GetGuid(15),
                ToNullableDateTimeOffset(reader.GetValue(16)), Convert.ToInt32(reader.GetInt64(17)),
                ToDateTimeOffset(reader.GetValue(18))
            ));
        }
        return list;
    }

    public async Task<PayrollPeriodDto?> GetPayrollPeriodByIdAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default)
    {
        var periods = await GetPayrollPeriodsAsync(tenantId, companyId, ct);
        return periods.FirstOrDefault(p => p.Id == periodId);
    }

    public async Task<PayrollPeriodDto> ProcessPayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid processedBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Fetch period details
        const string getPeriodSql = @"
            SELECT period_name, period_code, start_date, end_date, status
            FROM hr.payroll_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @periodId FOR UPDATE;";

        DateOnly startDate, endDate;
        string periodName, periodCode, status;
        await using (var pCmd = new NpgsqlCommand(getPeriodSql, conn, tx))
        {
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            pCmd.Parameters.AddWithValue("periodId", periodId);
            await using var reader = await pCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Payroll period not found.");

            periodName = reader.GetString(0);
            periodCode = reader.GetString(1);
            startDate = ToDateOnly(reader.GetValue(2));
            endDate = ToDateOnly(reader.GetValue(3));
            status = reader.GetString(4);
        }

        if (status is "posted" or "reversed")
            throw new InvalidOperationException($"Cannot recalculate payroll period with status '{status}'.");

        // Clear existing payslips for recalculation
        const string delSql = @"
            DELETE FROM hr.payslips
            WHERE tenant_id = @tenantId AND company_id = @companyId AND payroll_period_id = @periodId;";
        await using var delCmd = new NpgsqlCommand(delSql, conn, tx);
        delCmd.Parameters.AddWithValue("tenantId", tenantId);
        delCmd.Parameters.AddWithValue("companyId", companyId);
        delCmd.Parameters.AddWithValue("periodId", periodId);
        await delCmd.ExecuteNonQueryAsync(ct);

        // Fetch active employees
        var employees = await GetEmployeesAsync(tenantId, companyId, "active", null, ct);

        // Resolve effective-dated statutory rules for the pay period (Tax Year runs July 1 - June 30)
        int taxYear = startDate.Month >= 7 ? startDate.Year + 1 : startDate.Year;
        var allSlabs = await GetTaxSlabsAsync(tenantId, companyId, taxYear, ct);
        var effectiveTaxSlabs = allSlabs
            .Where(s => s.EffectiveFrom <= endDate && (s.EffectiveTo == null || s.EffectiveTo >= startDate))
            .ToList();

        // Resolve effective statutory configurations for the pay period
        var statConfigs = await GetStatutoryConfigsAsync(tenantId, companyId, ct);
        var activeStatConfigs = statConfigs
            .Where(c => c.EffectiveFrom <= endDate && (c.EffectiveTo == null || c.EffectiveTo >= startDate))
            .ToList();

        // Decoupled EOBI statutory contribution base
        var eobiConfig = activeStatConfigs.FirstOrDefault(c => c.ConfigKey == "eobi_contribution_base_2027")
            ?? activeStatConfigs.FirstOrDefault(c => c.ConfigKey.StartsWith("eobi_contribution_base"))
            ?? activeStatConfigs.FirstOrDefault(c => c.ConfigKey.StartsWith("eobi_min_wage"));
        decimal defaultEobiBase = eobiConfig?.ThresholdAmountPkr ?? 37_000m;

        var pfConfig = activeStatConfigs.FirstOrDefault(c => c.ConfigKey == "provident_fund_policy");
        decimal companyPfRate = pfConfig?.RatePercentage ?? 8.33m;

        var activeLoans = await GetSalaryLoansAsync(tenantId, companyId, null, "active", ct);

        decimal totalPeriodGross = 0;
        decimal totalPeriodDeductions = 0;
        decimal totalPeriodNet = 0;
        decimal totalPeriodEmployer = 0;
        int workingDaysInPeriod = (endDate.DayNumber - startDate.DayNumber) + 1;

        int slipIndex = 1;
        foreach (var emp in employees)
        {
            // Determine active loan deduction for this employee
            var empLoan = activeLoans.FirstOrDefault(l => l.EmployeeId == emp.Id);
            decimal loanDeduction = 0;
            if (empLoan != null && empLoan.RemainingBalancePkr > 0)
            {
                loanDeduction = Math.Min(empLoan.MonthlyInstallmentPkr, empLoan.RemainingBalancePkr);
            }

            // Resolve jurisdiction and skill-specific statutory minimum wage
            string jur = (emp.WorkJurisdiction ?? "FEDERAL").Trim().ToLowerInvariant();
            string skill = (emp.SkillCategory ?? "unskilled").Trim().ToLowerInvariant();
            string jurSkillKey = $"min_wage_{jur}_{skill}";

            var wageConfig = activeStatConfigs.FirstOrDefault(c => c.ConfigKey == jurSkillKey)
                ?? activeStatConfigs.FirstOrDefault(c => c.ConfigKey == $"min_wage_{jur}")
                ?? activeStatConfigs.FirstOrDefault(c => c.ConfigKey == "min_wage_federal");

            decimal empMinWage = wageConfig?.ThresholdAmountPkr
                ?? PayrollCalculationEngine.GetJurisdictionalMinimumWage(emp.WorkJurisdiction, emp.SkillCategory, startDate);

            var computed = PayrollCalculationEngine.CalculateEmployeePayroll(
                emp,
                workingDays: workingDaysInPeriod,
                attendedDays: workingDaysInPeriod,
                leaveDays: 0,
                absentDays: 0,
                overtimeHours: 0,
                bonusAmount: 0,
                loanInstallment: loanDeduction,
                otherDeductions: 0,
                taxSlabs: effectiveTaxSlabs,
                statutoryMinWage: empMinWage,
                pfRatePercentage: companyPfRate,
                eobiContributionBase: defaultEobiBase
            );

            totalPeriodGross += computed.GrossPay;
            totalPeriodDeductions += computed.TotalDeductions;
            totalPeriodNet += computed.NetPay;
            totalPeriodEmployer += (computed.EobiEmployer + computed.PfEmployer);

            string slipNumber = $"PAY-{periodCode}-{slipIndex:D3}";
            slipIndex++;

            const string insSlipSql = @"
                INSERT INTO hr.payslips (
                    tenant_id, company_id, payroll_period_id, employee_id, payslip_number,
                    working_days, attended_days, leave_days, absent_days, overtime_hours,
                    base_salary_pkr, allowances_pkr, overtime_pay_pkr, bonus_pkr, gross_pay_pkr,
                    tax_deduction_pkr, eobi_employee_pkr, pf_employee_pkr, loan_deduction_pkr,
                    other_deductions_pkr, total_deductions_pkr, net_pay_pkr, eobi_employer_pkr,
                    pf_employer_pkr, payment_status, payment_method
                )
                VALUES (
                    @tenantId, @companyId, @periodId, @empId, @slipNo,
                    @workDays, @attDays, 0, 0, @otHrs,
                    @base, @allow, @otPay, @bonus, @gross,
                    @tax, @eobiEe, @pfEe, @loanDed,
                    @otherDed, @totDed, @net, @eobiEr,
                    @pfEr, 'pending', 'bank_transfer'
                )
                RETURNING id;";

            Guid payslipId;
            await using (var sCmd = new NpgsqlCommand(insSlipSql, conn, tx))
            {
                sCmd.Parameters.AddWithValue("tenantId", tenantId);
                sCmd.Parameters.AddWithValue("companyId", companyId);
                sCmd.Parameters.AddWithValue("periodId", periodId);
                sCmd.Parameters.AddWithValue("empId", emp.Id);
                sCmd.Parameters.AddWithValue("slipNo", slipNumber);
                sCmd.Parameters.AddWithValue("workDays", (decimal)workingDaysInPeriod);
                sCmd.Parameters.AddWithValue("attDays", (decimal)workingDaysInPeriod);
                sCmd.Parameters.AddWithValue("otHrs", computed.OvertimeHours);
                sCmd.Parameters.AddWithValue("base", computed.BaseSalary);
                sCmd.Parameters.AddWithValue("allow", computed.Allowances);
                sCmd.Parameters.AddWithValue("otPay", computed.OvertimePay);
                sCmd.Parameters.AddWithValue("bonus", computed.Bonus);
                sCmd.Parameters.AddWithValue("gross", computed.GrossPay);
                sCmd.Parameters.AddWithValue("tax", computed.IncomeTax);
                sCmd.Parameters.AddWithValue("eobiEe", computed.EobiEmployee);
                sCmd.Parameters.AddWithValue("pfEe", computed.PfEmployee);
                sCmd.Parameters.AddWithValue("loanDed", computed.LoanDeduction);
                sCmd.Parameters.AddWithValue("otherDed", computed.OtherDeductions);
                sCmd.Parameters.AddWithValue("totDed", computed.TotalDeductions);
                sCmd.Parameters.AddWithValue("net", computed.NetPay);
                sCmd.Parameters.AddWithValue("eobiEr", computed.EobiEmployer);
                sCmd.Parameters.AddWithValue("pfEr", computed.PfEmployer);

                payslipId = (Guid)(await sCmd.ExecuteScalarAsync(ct))!;
            }

            // Insert itemized lines
            foreach (var line in computed.Lines)
            {
                const string insLineSql = @"
                    INSERT INTO hr.payslip_lines (
                        tenant_id, company_id, payslip_id, line_type, category, description, amount_pkr
                    )
                    VALUES (@tenantId, @companyId, @payslipId, @lType, @cat, @desc, @amt);";

                await using var lCmd = new NpgsqlCommand(insLineSql, conn, tx);
                lCmd.Parameters.AddWithValue("tenantId", tenantId);
                lCmd.Parameters.AddWithValue("companyId", companyId);
                lCmd.Parameters.AddWithValue("payslipId", payslipId);
                lCmd.Parameters.AddWithValue("lType", line.LineType);
                lCmd.Parameters.AddWithValue("cat", line.Category);
                lCmd.Parameters.AddWithValue("desc", line.Description);
                lCmd.Parameters.AddWithValue("amt", line.Amount);
                await lCmd.ExecuteNonQueryAsync(ct);
            }
        }

        // Update period totals & status to calculated
        const string updPeriodSql = @"
            UPDATE hr.payroll_periods SET
                status = 'calculated',
                total_gross_pkr = @gross,
                total_deductions_pkr = @ded,
                total_net_pkr = @net,
                total_employer_contributions_pkr = @er,
                processed_by = @procBy,
                processed_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @periodId
            RETURNING created_at;";

        await using var updCmd = new NpgsqlCommand(updPeriodSql, conn, tx);
        updCmd.Parameters.AddWithValue("tenantId", tenantId);
        updCmd.Parameters.AddWithValue("companyId", companyId);
        updCmd.Parameters.AddWithValue("periodId", periodId);
        updCmd.Parameters.AddWithValue("gross", totalPeriodGross);
        updCmd.Parameters.AddWithValue("ded", totalPeriodDeductions);
        updCmd.Parameters.AddWithValue("net", totalPeriodNet);
        updCmd.Parameters.AddWithValue("er", totalPeriodEmployer);
        updCmd.Parameters.AddWithValue("procBy", processedBy);

        var createdAt = ToDateTimeOffset(await updCmd.ExecuteScalarAsync(ct));
        await tx.CommitAsync(ct);

        return new PayrollPeriodDto(
            periodId, tenantId, companyId, periodName, periodCode, startDate, endDate, "calculated",
            totalPeriodGross, totalPeriodDeductions, totalPeriodNet, totalPeriodEmployer, null,
            processedBy, DateTimeOffset.UtcNow, null, null, null, null, employees.Count, createdAt
        );
    }

    public async Task<PayrollPeriodDto> ApprovePayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid approvedBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            UPDATE hr.payroll_periods SET
                status = 'approved',
                approved_by = @appBy,
                approved_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @periodId AND status = 'calculated';";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("appBy", approvedBy);

        int rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
            throw new InvalidOperationException("Payroll period must be in 'calculated' status to approve.");

        var period = await GetPayrollPeriodByIdAsync(tenantId, companyId, periodId, ct);
        return period!;
    }

    public async Task<PayrollPeriodDto> PostPayrollToGlAsync(Guid tenantId, Guid companyId, PostPayrollToGlRequest request, Guid postedBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Fetch period and payslips
        var period = await GetPayrollPeriodByIdAsync(tenantId, companyId, request.PayrollPeriodId, ct);
        if (period == null) throw new InvalidOperationException("Payroll period not found.");
        if (period.Status != "approved")
            throw new InvalidOperationException($"Cannot post payroll to GL with status '{period.Status}'. Period must be approved.");
        if (period.GlJournalId != null)
            throw new InvalidOperationException("Payroll has already been posted to GL.");

        var payslips = await GetPayslipsForPeriodAsync(tenantId, companyId, request.PayrollPeriodId, ct);

        decimal totalGross = payslips.Sum(p => p.GrossPayPkr);
        decimal totalTax = payslips.Sum(p => p.TaxDeductionPkr);
        decimal totalEobiEe = payslips.Sum(p => p.EobiEmployeePkr);
        decimal totalPfEe = payslips.Sum(p => p.PfEmployeePkr);
        decimal totalLoan = payslips.Sum(p => p.LoanDeductionPkr);
        decimal totalNet = payslips.Sum(p => p.NetPayPkr);
        decimal totalEobiEr = payslips.Sum(p => p.EobiEmployerPkr);
        decimal totalPfEr = payslips.Sum(p => p.PfEmployerPkr);
        decimal totalEmployerStatutory = totalEobiEr + totalPfEr;

        var functionalCurrency = await CompanyCurrencyResolver.ResolveFunctionalCurrencyAsync(
            conn, tx, tenantId, companyId, ct);

        // Ensure accounts exist in finance.accounts
        async Task<Guid> EnsureAccountAsync(string code, string name, string type, string normalBalance)
        {
            const string fSql = "SELECT id FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = @code;";
            await using var fCmd = new NpgsqlCommand(fSql, conn, tx);
            fCmd.Parameters.AddWithValue("tenantId", tenantId);
            fCmd.Parameters.AddWithValue("companyId", companyId);
            fCmd.Parameters.AddWithValue("code", code);
            var existingId = await fCmd.ExecuteScalarAsync(ct);
            if (existingId != null && existingId != DBNull.Value) return (Guid)existingId;

            const string iSql = @"
                INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code)
                VALUES (@tenantId, @companyId, @code, @name, @type, @nb, @cur)
                RETURNING id;";
            await using var iCmd = new NpgsqlCommand(iSql, conn, tx);
            iCmd.Parameters.AddWithValue("tenantId", tenantId);
            iCmd.Parameters.AddWithValue("companyId", companyId);
            iCmd.Parameters.AddWithValue("code", code);
            iCmd.Parameters.AddWithValue("name", name);
            iCmd.Parameters.AddWithValue("type", type);
            iCmd.Parameters.AddWithValue("nb", normalBalance);
            iCmd.Parameters.AddWithValue("cur", functionalCurrency);
            return (Guid)(await iCmd.ExecuteScalarAsync(ct))!;
        }

        Guid accSalExpense = await EnsureAccountAsync("6020", "Salaries & Staff Welfare Expense", "expense", "debit");
        Guid accStatExpense = await EnsureAccountAsync("6025", "Employer Statutory Benefits Expense", "expense", "debit");
        Guid accSalPayable = await EnsureAccountAsync("2050", "Salaries & Wages Payable", "liability", "credit");
        Guid accTaxPayable = await EnsureAccountAsync("2120", "Income Tax Withholding Payable", "liability", "credit");
        Guid accEobiPayable = await EnsureAccountAsync("2125", "EOBI Payable", "liability", "credit");
        Guid accPfPayable = await EnsureAccountAsync("2130", "Provident Fund Payable", "liability", "credit");
        Guid accLoansReceivable = await EnsureAccountAsync("1150", "Employee Loans & Advances Receivable", "asset", "debit");

        // Lookup or create fiscal period
        const string fpSql = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
        await using var fpCmd = new NpgsqlCommand(fpSql, conn, tx);
        fpCmd.Parameters.AddWithValue("tenantId", tenantId);
        fpCmd.Parameters.AddWithValue("companyId", companyId);
        var fpIdObj = await fpCmd.ExecuteScalarAsync(ct);
        Guid fiscalPeriodId;
        if (fpIdObj != null && fpIdObj != DBNull.Value)
        {
            fiscalPeriodId = (Guid)fpIdObj;
        }
        else
        {
            // Seed a fiscal year and period
            var fyId = Guid.NewGuid();
            var nowYear = DateTime.UtcNow.Year;
            const string fySql = @"
                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tenantId, @companyId, @yearId, @code, @start, @end, 'open')
                ON CONFLICT (tenant_id, company_id, code) DO UPDATE SET status = 'open'
                RETURNING id;";
            await using var fyCmd = new NpgsqlCommand(fySql, conn, tx);
            fyCmd.Parameters.AddWithValue("tenantId", tenantId);
            fyCmd.Parameters.AddWithValue("companyId", companyId);
            fyCmd.Parameters.AddWithValue("yearId", fyId);
            fyCmd.Parameters.AddWithValue("code", $"FY{nowYear}");
            fyCmd.Parameters.AddWithValue("start", new DateOnly(nowYear, 1, 1));
            fyCmd.Parameters.AddWithValue("end", new DateOnly(nowYear, 12, 31));
            var resYear = await fyCmd.ExecuteScalarAsync(ct);
            if (resYear is Guid actualYearId) fyId = actualYearId;

            var pId = Guid.NewGuid();
            const string nfpSql = @"
                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tenantId, @companyId, @periodId, @fyId, @num, @name, @start, @end, 'open')
                ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO UPDATE SET status = 'open'
                RETURNING id;";
            await using var nfpCmd = new NpgsqlCommand(nfpSql, conn, tx);
            nfpCmd.Parameters.AddWithValue("tenantId", tenantId);
            nfpCmd.Parameters.AddWithValue("companyId", companyId);
            nfpCmd.Parameters.AddWithValue("periodId", pId);
            nfpCmd.Parameters.AddWithValue("fyId", fyId);
            nfpCmd.Parameters.AddWithValue("num", (short)DateTime.UtcNow.Month);
            nfpCmd.Parameters.AddWithValue("name", $"Period {DateTime.UtcNow.Month}");
            nfpCmd.Parameters.AddWithValue("start", new DateOnly(nowYear, DateTime.UtcNow.Month, 1));
            var daysInMonth = DateTime.DaysInMonth(nowYear, DateTime.UtcNow.Month);
            nfpCmd.Parameters.AddWithValue("end", new DateOnly(nowYear, DateTime.UtcNow.Month, daysInMonth));
            var resPeriod = await nfpCmd.ExecuteScalarAsync(ct);
            fiscalPeriodId = resPeriod is Guid actualPeriodId ? actualPeriodId : pId;
        }

        // Insert GL journal with status 'draft' first, then insert lines, then update to 'posted'
        string journalNo = $"JRN-PAY-{period.PeriodCode}-{DateTime.UtcNow.Ticks % 10000}";
        string idempotencyKey = $"PAYROLL-GL-{period.Id}";

        const string insJrnSql = @"
            INSERT INTO finance.journals (
                tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                functional_currency, source_module, source_type, source_id, idempotency_key,
                status, created_by
            )
            VALUES (
                @tenantId, @companyId, @jrnNo, @jrnDate, @fpId, @desc,
                @funcCurr, 'hr_payroll', 'payroll_period', @srcId, @idemp,
                'draft', @userId
            )
            RETURNING id;";

        Guid journalId;
        await using (var jCmd = new NpgsqlCommand(insJrnSql, conn, tx))
        {
            jCmd.Parameters.AddWithValue("tenantId", tenantId);
            jCmd.Parameters.AddWithValue("companyId", companyId);
            jCmd.Parameters.AddWithValue("jrnNo", journalNo);
            jCmd.Parameters.AddWithValue("jrnDate", request.JournalDate);
            jCmd.Parameters.AddWithValue("fpId", fiscalPeriodId);
            jCmd.Parameters.AddWithValue("desc", $"{request.Description} - {period.PeriodName}");
            jCmd.Parameters.AddWithValue("funcCurr", functionalCurrency);
            jCmd.Parameters.AddWithValue("srcId", period.Id.ToString());
            jCmd.Parameters.AddWithValue("idemp", idempotencyKey);
            jCmd.Parameters.AddWithValue("userId", postedBy);
            journalId = (Guid)(await jCmd.ExecuteScalarAsync(ct))!;
        }

        // Add Journal Lines:
        int lineNum = 1;
        async Task AddJournalLineAsync(Guid accountId, decimal debit, decimal credit, string desc)
        {
            if (debit <= 0 && credit <= 0) return;
            decimal txAmount = debit > 0 ? debit : credit;
            const string lSql = @"
                INSERT INTO finance.journal_lines (
                    tenant_id, company_id, journal_id, line_no, account_id,
                    description, transaction_currency, transaction_amount, exchange_rate,
                    debit_functional, credit_functional
                )
                VALUES (
                    @tenantId, @companyId, @jrnId, @lNo, @accId,
                    @desc, @txCurr, @txAmt, 1.0,
                    @debit, @credit
                );";
            await using var lCmd = new NpgsqlCommand(lSql, conn, tx);
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("jrnId", journalId);
            lCmd.Parameters.AddWithValue("lNo", lineNum++);
            lCmd.Parameters.AddWithValue("accId", accountId);
            lCmd.Parameters.AddWithValue("desc", desc);
            lCmd.Parameters.AddWithValue("txCurr", functionalCurrency);
            lCmd.Parameters.AddWithValue("txAmt", txAmount);
            lCmd.Parameters.AddWithValue("debit", debit);
            lCmd.Parameters.AddWithValue("credit", credit);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        // 1. Debit Gross Salaries Expense (6020)
        await AddJournalLineAsync(accSalExpense, totalGross, 0, $"Gross Salaries Expense - {period.PeriodName}");
        // 2. Debit Employer Statutory Benefits Expense (6025)
        if (totalEmployerStatutory > 0)
            await AddJournalLineAsync(accStatExpense, totalEmployerStatutory, 0, $"Employer Statutory Benefits (EOBI/PF) - {period.PeriodName}");
        // 3. Credit Salaries Payable (2050) -> Net Pay
        await AddJournalLineAsync(accSalPayable, 0, totalNet, $"Net Salaries Payable - {period.PeriodName}");
        // 4. Credit Income Tax Withholding Payable (2120)
        if (totalTax > 0)
            await AddJournalLineAsync(accTaxPayable, 0, totalTax, $"Income Tax Withheld - {period.PeriodName}");
        // 5. Credit EOBI Payable (2125) -> Employee + Employer
        decimal totalEobi = totalEobiEe + totalEobiEr;
        if (totalEobi > 0)
            await AddJournalLineAsync(accEobiPayable, 0, totalEobi, $"EOBI Contribution Payable (EE+ER) - {period.PeriodName}");
        // 6. Credit Provident Fund Payable (2130) -> Employee + Employer
        decimal totalPf = totalPfEe + totalPfEr;
        if (totalPf > 0)
            await AddJournalLineAsync(accPfPayable, 0, totalPf, $"Provident Fund Payable (EE+ER) - {period.PeriodName}");
        // 7. Credit Staff Loans & Advances (1150) -> Loan installment deduction
        if (totalLoan > 0)
            await AddJournalLineAsync(accLoansReceivable, 0, totalLoan, $"Staff Loan Installments Recovered - {period.PeriodName}");

        // Now post the journal (trigger checks debit == credit)
        const string postJrnSql = @"
            UPDATE finance.journals SET
                status = 'posted',
                posted_by = @userId,
                posted_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @jrnId;";
        await using (var pJCmd = new NpgsqlCommand(postJrnSql, conn, tx))
        {
            pJCmd.Parameters.AddWithValue("tenantId", tenantId);
            pJCmd.Parameters.AddWithValue("companyId", companyId);
            pJCmd.Parameters.AddWithValue("userId", postedBy);
            pJCmd.Parameters.AddWithValue("jrnId", journalId);
            await pJCmd.ExecuteNonQueryAsync(ct);
        }

        // Deduct loan installments from employee loans
        foreach (var slip in payslips.Where(p => p.LoanDeductionPkr > 0))
        {
            const string updLoanSql = @"
                UPDATE hr.salary_loans SET
                    remaining_balance_pkr = GREATEST(0, remaining_balance_pkr - @amt),
                    paid_installments = paid_installments + 1,
                    status = CASE WHEN remaining_balance_pkr - @amt <= 0 THEN 'repaid' ELSE status END
                WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId AND status = 'active';";

            await using var lCmd = new NpgsqlCommand(updLoanSql, conn, tx);
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("amt", slip.LoanDeductionPkr);
            lCmd.Parameters.AddWithValue("empId", slip.EmployeeId);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        // Update period status to posted
        const string updPrdSql = @"
            UPDATE hr.payroll_periods SET
                status = 'posted',
                gl_journal_id = @jrnId,
                posted_by = @userId,
                posted_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @periodId;";
        await using (var pPrdCmd = new NpgsqlCommand(updPrdSql, conn, tx))
        {
            pPrdCmd.Parameters.AddWithValue("tenantId", tenantId);
            pPrdCmd.Parameters.AddWithValue("companyId", companyId);
            pPrdCmd.Parameters.AddWithValue("jrnId", journalId);
            pPrdCmd.Parameters.AddWithValue("userId", postedBy);
            pPrdCmd.Parameters.AddWithValue("periodId", period.Id);
            await pPrdCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var updatedPeriod = await GetPayrollPeriodByIdAsync(tenantId, companyId, period.Id, ct);
        return updatedPeriod!;
    }

    public async Task<PayrollReversalDto> ReversePayrollRunAsync(Guid tenantId, Guid companyId, ReversePayrollRunRequest request, Guid reversedBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var period = await GetPayrollPeriodByIdAsync(tenantId, companyId, request.PayrollPeriodId, ct);
        if (period == null) throw new InvalidOperationException("Payroll period not found.");
        if (period.Status != "posted" && period.Status != "approved")
            throw new InvalidOperationException($"Cannot reverse payroll period with status '{period.Status}'.");

        Guid? reversalJournalId = null;

        // If posted to GL, create reversing journal
        if (period.GlJournalId != null)
        {
            // Fetch original journal lines (preserving historical transaction currency)
            const string fLinesSql = @"
                SELECT line_no, account_id, description, debit_functional, credit_functional, transaction_currency
                FROM finance.journal_lines
                WHERE tenant_id = @tenantId AND company_id = @companyId AND journal_id = @jrnId
                ORDER BY line_no;";

            var lines = new List<(int lineNo, Guid accId, string desc, decimal debit, decimal credit, string txCurr)>();
            await using (var lCmd = new NpgsqlCommand(fLinesSql, conn, tx))
            {
                lCmd.Parameters.AddWithValue("tenantId", tenantId);
                lCmd.Parameters.AddWithValue("companyId", companyId);
                lCmd.Parameters.AddWithValue("jrnId", period.GlJournalId.Value);
                await using var reader = await lCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lines.Add((reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5)));
                }
            }

            string revJrnNo = $"REV-{period.PeriodCode}-{DateTime.UtcNow.Ticks % 10000}";
            string revIdemp = $"REV-PAYROLL-GL-{period.Id}";

            const string insRevJrnSql = @"
                INSERT INTO finance.journals (
                    tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                    functional_currency, source_module, source_type, source_id, idempotency_key,
                    status, created_by, reverses_journal_id
                )
                SELECT tenant_id, company_id, @revJrnNo, CURRENT_DATE, fiscal_period_id,
                       @desc, functional_currency, source_module, source_type, source_id, @revIdemp,
                       'draft', @userId, id
                FROM finance.journals
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @origJrnId
                RETURNING id;";

            await using (var rCmd = new NpgsqlCommand(insRevJrnSql, conn, tx))
            {
                rCmd.Parameters.AddWithValue("tenantId", tenantId);
                rCmd.Parameters.AddWithValue("companyId", companyId);
                rCmd.Parameters.AddWithValue("revJrnNo", revJrnNo);
                rCmd.Parameters.AddWithValue("desc", $"Reversal of {period.PeriodName} - {request.Reason}");
                rCmd.Parameters.AddWithValue("revIdemp", revIdemp);
                rCmd.Parameters.AddWithValue("userId", reversedBy);
                rCmd.Parameters.AddWithValue("origJrnId", period.GlJournalId.Value);
                reversalJournalId = (Guid)(await rCmd.ExecuteScalarAsync(ct))!;
            }

            // Swap debits and credits
            foreach (var l in lines)
            {
                decimal txAmount = l.credit > 0 ? l.credit : l.debit;
                const string insRevLineSql = @"
                    INSERT INTO finance.journal_lines (
                        tenant_id, company_id, journal_id, line_no, account_id,
                        description, transaction_currency, transaction_amount, exchange_rate,
                        debit_functional, credit_functional
                    )
                    VALUES (
                        @tenantId, @companyId, @jrnId, @lNo, @accId,
                        @desc, @txCurr, @txAmt, 1.0,
                        @credit, @debit
                    );";
                await using var rlCmd = new NpgsqlCommand(insRevLineSql, conn, tx);
                rlCmd.Parameters.AddWithValue("tenantId", tenantId);
                rlCmd.Parameters.AddWithValue("companyId", companyId);
                rlCmd.Parameters.AddWithValue("jrnId", reversalJournalId.Value);
                rlCmd.Parameters.AddWithValue("lNo", l.lineNo);
                rlCmd.Parameters.AddWithValue("accId", l.accId);
                rlCmd.Parameters.AddWithValue("desc", $"[Reversal] {l.desc}");
                rlCmd.Parameters.AddWithValue("txCurr", l.txCurr);
                rlCmd.Parameters.AddWithValue("txAmt", txAmount);
                rlCmd.Parameters.AddWithValue("credit", l.debit);
                rlCmd.Parameters.AddWithValue("debit", l.credit);
                await rlCmd.ExecuteNonQueryAsync(ct);
            }

            // Mark reversing journal posted and original journal reversed
            const string markPostedSql = @"
                UPDATE finance.journals SET status = 'posted', posted_by = @userId, posted_at = now()
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @jrnId;";
            await using (var mpCmd = new NpgsqlCommand(markPostedSql, conn, tx))
            {
                mpCmd.Parameters.AddWithValue("tenantId", tenantId);
                mpCmd.Parameters.AddWithValue("companyId", companyId);
                mpCmd.Parameters.AddWithValue("userId", reversedBy);
                mpCmd.Parameters.AddWithValue("jrnId", reversalJournalId.Value);
                await mpCmd.ExecuteNonQueryAsync(ct);
            }

            const string markOrigRevSql = @"
                UPDATE finance.journals SET status = 'reversed'
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @jrnId;";
            await using (var moCmd = new NpgsqlCommand(markOrigRevSql, conn, tx))
            {
                moCmd.Parameters.AddWithValue("tenantId", tenantId);
                moCmd.Parameters.AddWithValue("companyId", companyId);
                moCmd.Parameters.AddWithValue("jrnId", period.GlJournalId.Value);
                await moCmd.ExecuteNonQueryAsync(ct);
            }
        }

        // Revert loan balances
        var payslips = await GetPayslipsForPeriodAsync(tenantId, companyId, period.Id, ct);
        foreach (var slip in payslips.Where(p => p.LoanDeductionPkr > 0))
        {
            const string revLoanSql = @"
                UPDATE hr.salary_loans SET
                    remaining_balance_pkr = remaining_balance_pkr + @amt,
                    paid_installments = GREATEST(0, paid_installments - 1),
                    status = 'active'
                WHERE tenant_id = @tenantId AND company_id = @companyId AND employee_id = @empId;";
            await using var lCmd = new NpgsqlCommand(revLoanSql, conn, tx);
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("amt", slip.LoanDeductionPkr);
            lCmd.Parameters.AddWithValue("empId", slip.EmployeeId);
            await lCmd.ExecuteNonQueryAsync(ct);
        }

        // Record reversal
        const string insRevSql = @"
            INSERT INTO hr.payroll_reversals (
                tenant_id, company_id, payroll_period_id, reversal_reason, reversal_journal_id, reversed_by
            )
            VALUES (@tenantId, @companyId, @periodId, @reason, @jrnId, @userId)
            RETURNING id, created_at;";

        Guid reversalId;
        DateTimeOffset reversedAt;
        await using (var rCmd = new NpgsqlCommand(insRevSql, conn, tx))
        {
            rCmd.Parameters.AddWithValue("tenantId", tenantId);
            rCmd.Parameters.AddWithValue("companyId", companyId);
            rCmd.Parameters.AddWithValue("periodId", period.Id);
            rCmd.Parameters.AddWithValue("reason", request.Reason.Trim());
            rCmd.Parameters.AddWithValue("jrnId", (object?)reversalJournalId ?? DBNull.Value);
            rCmd.Parameters.AddWithValue("userId", reversedBy);

            await using var rdr = await rCmd.ExecuteReaderAsync(ct);
            await rdr.ReadAsync(ct);
            reversalId = rdr.GetGuid(0);
            reversedAt = ToDateTimeOffset(rdr.GetValue(1));
        }

        // Update period status to reversed
        const string updPeriodSql = @"
            UPDATE hr.payroll_periods SET status = 'reversed'
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @periodId;";
        await using (var uCmd = new NpgsqlCommand(updPeriodSql, conn, tx))
        {
            uCmd.Parameters.AddWithValue("tenantId", tenantId);
            uCmd.Parameters.AddWithValue("companyId", companyId);
            uCmd.Parameters.AddWithValue("periodId", period.Id);
            await uCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new PayrollReversalDto(
            reversalId, period.Id, request.Reason.Trim(), reversalJournalId, reversedBy, reversedAt
        );
    }

    public async Task<IReadOnlyList<PayslipDto>> GetPayslipsForPeriodAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT p.id, p.payroll_period_id, p.employee_id, e.employee_code,
                   e.first_name || ' ' || e.last_name as emp_name,
                   desig.title as desig_title, dept.name as dept_name,
                   p.payslip_number, p.currency_code, p.working_days, p.attended_days,
                   p.leave_days, p.absent_days, p.overtime_hours, p.base_salary_pkr,
                   p.allowances_pkr, p.overtime_pay_pkr, p.bonus_pkr, p.gross_pay_pkr,
                   p.tax_deduction_pkr, p.eobi_employee_pkr, p.pf_employee_pkr,
                   p.loan_deduction_pkr, p.other_deductions_pkr, p.total_deductions_pkr,
                   p.net_pay_pkr, p.eobi_employer_pkr, p.pf_employer_pkr, p.payment_status,
                   p.payment_method, p.payment_reference, p.paid_at, p.created_at
            FROM hr.payslips p
            JOIN hr.employees e ON e.id = p.employee_id
            LEFT JOIN hr.designations desig ON desig.id = e.designation_id
            LEFT JOIN hr.departments dept ON dept.id = e.department_id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId AND p.payroll_period_id = @periodId
            ORDER BY e.employee_code;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("periodId", periodId);

        var list = new List<PayslipDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PayslipDto(
                reader.GetGuid(0), tenantId, companyId, reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12),
                reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16),
                reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20),
                reader.GetDecimal(21), reader.GetDecimal(22), reader.GetDecimal(23), reader.GetDecimal(24),
                reader.GetDecimal(25), reader.GetDecimal(26), reader.GetDecimal(27), reader.GetString(28),
                reader.GetString(29), reader.IsDBNull(30) ? null : reader.GetString(30),
                ToNullableDateTimeOffset(reader.GetValue(31)), null, ToDateTimeOffset(reader.GetValue(32))
            ));
        }
        return list;
    }

    public async Task<PayslipDto?> GetPayslipByIdAsync(Guid tenantId, Guid companyId, Guid payslipId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);
        const string sql = @"
            SELECT p.id, p.payroll_period_id, p.employee_id, e.employee_code,
                   e.first_name || ' ' || e.last_name as emp_name,
                   desig.title as desig_title, dept.name as dept_name,
                   p.payslip_number, p.currency_code, p.working_days, p.attended_days,
                   p.leave_days, p.absent_days, p.overtime_hours, p.base_salary_pkr,
                   p.allowances_pkr, p.overtime_pay_pkr, p.bonus_pkr, p.gross_pay_pkr,
                   p.tax_deduction_pkr, p.eobi_employee_pkr, p.pf_employee_pkr,
                   p.loan_deduction_pkr, p.other_deductions_pkr, p.total_deductions_pkr,
                   p.net_pay_pkr, p.eobi_employer_pkr, p.pf_employer_pkr, p.payment_status,
                   p.payment_method, p.payment_reference, p.paid_at, p.created_at
            FROM hr.payslips p
            JOIN hr.employees e ON e.id = p.employee_id
            LEFT JOIN hr.designations desig ON desig.id = e.designation_id
            LEFT JOIN hr.departments dept ON dept.id = e.department_id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId AND p.id = @payslipId;";

        PayslipDto? slip = null;
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("payslipId", payslipId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                slip = new PayslipDto(
                    reader.GetGuid(0), tenantId, companyId, reader.GetGuid(1), reader.GetGuid(2),
                    reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
                    reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12),
                    reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16),
                    reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20),
                    reader.GetDecimal(21), reader.GetDecimal(22), reader.GetDecimal(23), reader.GetDecimal(24),
                    reader.GetDecimal(25), reader.GetDecimal(26), reader.GetDecimal(27), reader.GetString(28),
                    reader.GetString(29), reader.IsDBNull(30) ? null : reader.GetString(30),
                    ToNullableDateTimeOffset(reader.GetValue(31)), new List<PayslipLineDto>(), ToDateTimeOffset(reader.GetValue(32))
                );
            }
        }

        if (slip == null) return null;

        // Fetch lines
        const string linesSql = @"
            SELECT id, line_type, category, description, amount_pkr, created_at
            FROM hr.payslip_lines
            WHERE tenant_id = @tenantId AND company_id = @companyId AND payslip_id = @payslipId
            ORDER BY line_type, amount_pkr DESC;";

        await using (var lCmd = new NpgsqlCommand(linesSql, conn))
        {
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("payslipId", payslipId);
            await using var lReader = await lCmd.ExecuteReaderAsync(ct);
            while (await lReader.ReadAsync(ct))
            {
                slip.Lines!.Add(new PayslipLineDto(
                    lReader.GetGuid(0), payslipId, lReader.GetString(1), lReader.GetString(2),
                    lReader.GetString(3), lReader.GetDecimal(4), ToDateTimeOffset(lReader.GetValue(5))
                ));
            }
        }

        return slip;
    }
}
