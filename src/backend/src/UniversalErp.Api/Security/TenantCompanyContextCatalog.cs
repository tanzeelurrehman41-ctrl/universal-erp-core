using Npgsql;

namespace UniversalErp.Api.Security;

public sealed record TenantCompanyContext(
    Guid TenantId,
    string TenantCode,
    string TenantName,
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    string BaseCurrencyCode,
    string Timezone,
    string FunctionalCurrencyCode = "PKR",
    string PresentationCurrencyCode = "PKR",
    DateOnly? CurrencyEffectiveDate = null);

public sealed class TenantCompanyContextCatalog(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<TenantCompanyContext>> ListForIdentityAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT tenant_id, tenant_code, tenant_name, company_id, company_code, company_name,
                   base_currency_code, timezone, functional_currency_code, presentation_currency_code, currency_effective_date
            FROM identity.list_user_company_contexts(@issuer, @subject)
            """,
            connection);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);

        var contexts = new List<TenantCompanyContext>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var baseCurr = reader.GetString(6).Trim();
            var funcCurr = reader.IsDBNull(8) ? baseCurr : reader.GetString(8).Trim();
            var presCurr = reader.IsDBNull(9) ? funcCurr : reader.GetString(9).Trim();
            DateOnly? effectiveDate = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10);

            contexts.Add(new TenantCompanyContext(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                baseCurr,
                reader.GetString(7),
                funcCurr,
                presCurr,
                effectiveDate));
        }

        return contexts;
    }
}
