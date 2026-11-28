using UniversalErp.Application.Purchasing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class ThreeWayMatchingEngineTests
{
    private readonly Guid _variantId = Guid.NewGuid();
    private readonly Guid _poId = Guid.NewGuid();
    private readonly Guid _grnId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _warehouseId = Guid.NewGuid();
    private readonly Guid _uomId = Guid.NewGuid();

    [Fact]
    public void Evaluate_ExactMatch_ReturnsMatchedWithNoDiscrepancies()
    {
        var po = new PurchaseOrderSummary(
            _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "confirmed", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null,
            [
                new PurchaseOrderLineSummary(1, _variantId, "SKU-01", 10m, 10m, _uomId, "EA", 100m, "received")
            ]);

        var grn = new GrnSummary(
            _grnId, "GRN-001", _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "posted", DateOnly.FromDateTime(DateTime.UtcNow), null, null, DateTimeOffset.UtcNow,
            [
                new GrnLineSummary(1, 1, _variantId, "SKU-01", Guid.NewGuid(), "BIN-01", 10m, "EA", 100m)
            ]);

        var invoiceLines = new List<CreateSupplierInvoiceLineRequest>
        {
            new(1, 1, 1, _variantId, 10m, 100m)
        };

        var result = ThreeWayMatchingEngine.Evaluate(po, grn, invoiceLines, priceTolerancePercent: 5.0m, quantityTolerancePercent: 5.0m);

        Assert.True(result.IsMatched);
        Assert.False(result.RequiresManualApproval);
        Assert.Equal("matched", result.OverallStatus);
        Assert.Equal(1000m, result.TotalInvoicedAmount);
        Assert.Equal(1000m, result.TotalExpectedAmount);
        Assert.Equal(0m, result.TotalNetVariance);
        Assert.Empty(result.Discrepancies);

        var line = Assert.Single(result.LineResults);
        Assert.Equal(ThreeWayMatchLineStatus.ExactMatch, line.Status);
        Assert.Equal(0m, line.PriceVariance);
        Assert.Equal(0m, line.QuantityVariance);
    }

    [Fact]
    public void Evaluate_PriceWithinTolerance_ReturnsMatchedWithVariance()
    {
        var po = new PurchaseOrderSummary(
            _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "confirmed", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null,
            [
                new PurchaseOrderLineSummary(1, _variantId, "SKU-01", 10m, 10m, _uomId, "EA", 100m, "received")
            ]);

        var grn = new GrnSummary(
            _grnId, "GRN-001", _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "posted", DateOnly.FromDateTime(DateTime.UtcNow), null, null, DateTimeOffset.UtcNow,
            [
                new GrnLineSummary(1, 1, _variantId, "SKU-01", Guid.NewGuid(), "BIN-01", 10m, "EA", 100m)
            ]);

        // Invoice is 103/unit (+3% variance, within 5% tolerance)
        var invoiceLines = new List<CreateSupplierInvoiceLineRequest>
        {
            new(1, 1, 1, _variantId, 10m, 103m)
        };

        var result = ThreeWayMatchingEngine.Evaluate(po, grn, invoiceLines, priceTolerancePercent: 5.0m, quantityTolerancePercent: 5.0m, autoApproveWithinTolerance: true);

        Assert.True(result.IsMatched);
        Assert.False(result.RequiresManualApproval);
        Assert.Equal("matched_with_variance", result.OverallStatus);
        Assert.Equal(1030m, result.TotalInvoicedAmount);
        Assert.Equal(1000m, result.TotalExpectedAmount);
        Assert.Equal(30m, result.TotalNetVariance);

        var line = Assert.Single(result.LineResults);
        Assert.Equal(ThreeWayMatchLineStatus.WithinTolerance, line.Status);
        Assert.Equal(3m, line.PriceVariance);
        Assert.Equal(30m, line.ExtendedPriceVariance);
    }

    [Fact]
    public void Evaluate_PriceVarianceExceeded_BlocksAutomaticApproval()
    {
        var po = new PurchaseOrderSummary(
            _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "confirmed", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null,
            [
                new PurchaseOrderLineSummary(1, _variantId, "SKU-01", 10m, 10m, _uomId, "EA", 100m, "received")
            ]);

        var grn = new GrnSummary(
            _grnId, "GRN-001", _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "posted", DateOnly.FromDateTime(DateTime.UtcNow), null, null, DateTimeOffset.UtcNow,
            [
                new GrnLineSummary(1, 1, _variantId, "SKU-01", Guid.NewGuid(), "BIN-01", 10m, "EA", 100m)
            ]);

        // Invoice is 110/unit (+10% variance, exceeds 5% tolerance)
        var invoiceLines = new List<CreateSupplierInvoiceLineRequest>
        {
            new(1, 1, 1, _variantId, 10m, 110m)
        };

        var result = ThreeWayMatchingEngine.Evaluate(po, grn, invoiceLines, priceTolerancePercent: 5.0m, quantityTolerancePercent: 5.0m);

        Assert.False(result.IsMatched);
        Assert.True(result.RequiresManualApproval);
        Assert.Equal("variance_exceeded", result.OverallStatus);
        Assert.NotEmpty(result.Discrepancies);

        var line = Assert.Single(result.LineResults);
        Assert.Equal(ThreeWayMatchLineStatus.PriceVarianceExceeded, line.Status);
        Assert.Equal(10m, line.PriceVariance);
    }

    [Fact]
    public void Evaluate_QuantityVarianceExceeded_BlocksAutomaticApproval()
    {
        var po = new PurchaseOrderSummary(
            _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "confirmed", "PKR", DateOnly.FromDateTime(DateTime.UtcNow), null, null,
            [
                new PurchaseOrderLineSummary(1, _variantId, "SKU-01", 10m, 10m, _uomId, "EA", 100m, "received")
            ]);

        var grn = new GrnSummary(
            _grnId, "GRN-001", _poId, "PO-001", _supplierId, "Test Supplier", _warehouseId, "Main Warehouse",
            "posted", DateOnly.FromDateTime(DateTime.UtcNow), null, null, DateTimeOffset.UtcNow,
            [
                new GrnLineSummary(1, 1, _variantId, "SKU-01", Guid.NewGuid(), "BIN-01", 10m, "EA", 100m)
            ]);

        // Invoice is for 12 EA (20% above received quantity 10 EA)
        var invoiceLines = new List<CreateSupplierInvoiceLineRequest>
        {
            new(1, 1, 1, _variantId, 12m, 100m)
        };

        var result = ThreeWayMatchingEngine.Evaluate(po, grn, invoiceLines, priceTolerancePercent: 5.0m, quantityTolerancePercent: 5.0m);

        Assert.False(result.IsMatched);
        Assert.True(result.RequiresManualApproval);
        Assert.Equal("variance_exceeded", result.OverallStatus);

        var line = Assert.Single(result.LineResults);
        Assert.Equal(ThreeWayMatchLineStatus.QuantityVarianceExceeded, line.Status);
        Assert.Equal(2m, line.QuantityVariance);
    }

    [Fact]
    public void Evaluate_NoGrnOrPo_ReturnsUnmatched()
    {
        var invoiceLines = new List<CreateSupplierInvoiceLineRequest>
        {
            new(1, null, null, _variantId, 5m, 100m)
        };

        var result = ThreeWayMatchingEngine.Evaluate(null, null, invoiceLines);

        Assert.False(result.IsMatched);
        Assert.True(result.RequiresManualApproval);
        Assert.Equal("unmatched", result.OverallStatus);
    }
}
