using Npgsql;

namespace UniversalErp.Infrastructure.Finance;

internal static class CompanyCurrencyResolver
{
    public static async Task<string> ResolveFunctionalCurrencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(functional_currency_code, base_currency_code, 'PKR')
            FROM organization.companies
            WHERE tenant_id = @tenantId AND id = @companyId;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        var val = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(val) ? "PKR" : val.Trim().ToUpperInvariant();
    }

    public static async Task<(string FunctionalCurrency, string PresentationCurrency, DateOnly EffectiveDate)> ResolveSettingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(functional_currency_code, base_currency_code, 'PKR'),
                   COALESCE(presentation_currency_code, functional_currency_code, base_currency_code, 'PKR'),
                   currency_effective_date
            FROM organization.companies
            WHERE tenant_id = @tenantId AND id = @companyId;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString(0).Trim().ToUpperInvariant(),
                reader.GetString(1).Trim().ToUpperInvariant(),
                reader.GetFieldValue<DateOnly>(2));
        }

        return ("PKR", "PKR", DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
