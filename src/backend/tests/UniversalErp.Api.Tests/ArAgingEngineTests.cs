using UniversalErp.Application.Sales;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class ArAgingEngineTests
{
    [Fact]
    public void CalculateCustomerAging_DistributesInvoicesAcrossCorrectBuckets()
    {
        var asOf = new DateOnly(2026, 11, 15);
        var custId = Guid.NewGuid();

        var invoices = new List<ArAgingEngine.InvoiceAgingSlice>
        {
            // Due 2026-11-05 (10 days past due -> Current 0-30 days)
            new(Guid.NewGuid(), "INV-001", new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 5), 10_000m, 0m),
            // Due 2026-10-05 (41 days past due -> 31-60 days)
            new(Guid.NewGuid(), "INV-002", new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 5), 20_000m, 5_000m), // Outstanding 15,000
            // Due 2026-08-30 (77 days past due -> 61-90 days)
            new(Guid.NewGuid(), "INV-003", new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 30), 8_000m, 0m),
            // Due 2026-07-01 (137 days past due -> Over 90 days)
            new(Guid.NewGuid(), "INV-004", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), 12_000m, 2_000m), // Outstanding 10,000
            // Fully paid invoice (should not appear in aging buckets)
            new(Guid.NewGuid(), "INV-005", new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), 5_000m, 5_000m)
        };

        var result = ArAgingEngine.CalculateCustomerAging(
            custId,
            "CUST-001",
            "Acme Logistics",
            "PKR",
            invoices,
            totalCreditNotes: 3_000m,
            totalReceipts: 12_000m,
            asOfDate: asOf);

        Assert.Equal(custId, result.CustomerId);
        Assert.Equal("CUST-001", result.CustomerCode);
        Assert.Equal("Acme Logistics", result.CustomerName);
        Assert.Equal(4, result.OpenInvoiceCount);
        Assert.Equal(10_000m, result.CurrentBalance);
        Assert.Equal(15_000m, result.Days31To60);
        Assert.Equal(8_000m, result.Days61To90);
        Assert.Equal(10_000m, result.Over90Days);
        Assert.Equal(43_000m, result.TotalReceivable);
        Assert.Equal(3_000m, result.TotalCreditNotes);
        Assert.Equal(12_000m, result.TotalReceipts);
        Assert.Equal(40_000m, result.NetOutstanding); // 43,000 - 3,000
    }

    [Fact]
    public void CalculateCustomerAging_WhenNoInvoices_ReturnsZeroBalances()
    {
        var asOf = new DateOnly(2026, 11, 15);
        var custId = Guid.NewGuid();

        var result = ArAgingEngine.CalculateCustomerAging(
            custId,
            "CUST-EMPTY",
            "Zero Balance Customer",
            "PKR",
            [],
            totalCreditNotes: 0m,
            totalReceipts: 0m,
            asOfDate: asOf);

        Assert.Equal(0, result.OpenInvoiceCount);
        Assert.Equal(0m, result.TotalReceivable);
        Assert.Equal(0m, result.NetOutstanding);
    }
}
