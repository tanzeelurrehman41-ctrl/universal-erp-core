namespace UniversalErp.Api.Analytics;

public static class AnalyticsExportPermissions
{
    public static IReadOnlyList<string> GetRequiredPermissions(string? reportType)
    {
        return reportType?.Trim().ToLowerInvariant() switch
        {
            "sales" or "purchasing" or "inventory_ageing" =>
                ["analytics.supply_chain.view"],
            "workforce" =>
                ["analytics.workforce.view"],
            "reconciliation" =>
                ["analytics.financial.view", "analytics.supply_chain.view"],
            _ => Array.Empty<string>(),
        };
    }
}
