using System.Linq;
using UniversalErp.Api.Analytics;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class AnalyticsExportPermissionsTests
{
    [Theory]
    [InlineData("sales")]
    [InlineData("SALES")]
    [InlineData(" purchasing ")]
    [InlineData("inventory_ageing")]
    public void SupplyChainExports_RequireSupplyChainPermission(string reportType)
    {
        Assert.Equal(
            new[] { "analytics.supply_chain.view" },
            AnalyticsExportPermissions.GetRequiredPermissions(reportType));
    }

    [Fact]
    public void WorkforceExport_RequiresWorkforcePermission()
    {
        Assert.Equal(
            new[] { "analytics.workforce.view" },
            AnalyticsExportPermissions.GetRequiredPermissions("workforce"));
    }

    [Fact]
    public void ReconciliationExport_RequiresBothFinancialAndSupplyChainPermissions()
    {
        var permissions = AnalyticsExportPermissions.GetRequiredPermissions("reconciliation");

        Assert.Equal(2, permissions.Count);
        Assert.Contains("analytics.financial.view", permissions);
        Assert.Contains("analytics.supply_chain.view", permissions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("executive")]
    [InlineData("payroll_dump")]
    [InlineData("unknown")]
    public void UnsupportedExport_ReturnsNoPermissions(string? reportType)
    {
        Assert.Empty(AnalyticsExportPermissions.GetRequiredPermissions(reportType));
    }
}
