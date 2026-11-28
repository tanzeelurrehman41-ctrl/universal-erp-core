using System;
using UniversalErp.Application.Warehouse;
using Xunit;

namespace UniversalErp.Api.Tests;

public class BarcodeEngineTests
{
    [Fact]
    public void ParseGs1Barcode_WithParentheses_ExtractsAllApplicationIdentifiers()
    {
        var raw = "(01)00843210987654(17)261231(10)LOT-2026-A(21)SN-9988(37)50(3102)001250";
        var parsed = BarcodeEngine.ParseGs1Barcode(raw);

        Assert.Equal("00843210987654", parsed.Gtin);
        Assert.Equal("261231", parsed.ExpiryDate);
        Assert.Equal("LOT-2026-A", parsed.LotNumber);
        Assert.Equal("SN-9988", parsed.SerialNumber);
        Assert.Equal(50m, parsed.Quantity);
        Assert.Equal(12.50m, parsed.NetWeightKg); // 3102 -> 1250 / 10^2 = 12.50 kg
    }

    [Fact]
    public void GenerateGs1Barcode_BuildsCorrectStandardString()
    {
        var generated = BarcodeEngine.GenerateGs1Barcode(
            gtin: "843210987654",
            lotNumber: "BATCH-42",
            expiryDateYymmdd: "270630",
            serialNumber: "SER-001",
            count: 25);

        Assert.Contains("(01)00843210987654", generated);
        Assert.Contains("(17)270630", generated);
        Assert.Contains("(10)BATCH-42", generated);
        Assert.Contains("(21)SER-001", generated);
        Assert.Contains("(37)25", generated);
    }

    [Fact]
    public void ParseStandardBarcode_RecognizesStandardPrefixes()
    {
        var (binType, binId) = BarcodeEngine.ParseStandardBarcode("BIN:MAIN-W1:PICK-01");
        Assert.Equal("stock_bin", binType);
        Assert.Equal("MAIN-W1:PICK-01", binId);

        var (prodType, prodSku) = BarcodeEngine.ParseStandardBarcode("PROD:SKU-WIDGET-01");
        Assert.Equal("product_variant", prodType);
        Assert.Equal("SKU-WIDGET-01", prodSku);

        var (waveType, waveNum) = BarcodeEngine.ParseStandardBarcode("WAVE:WAV-2026-001");
        Assert.Equal("picking_wave", waveType);
        Assert.Equal("WAV-2026-001", waveNum);

        var (pkgType, pkgNum) = BarcodeEngine.ParseStandardBarcode("PKG:PKG-2026-099");
        Assert.Equal("shipping_carton", pkgType);
        Assert.Equal("PKG-2026-099", pkgNum);
    }

    [Fact]
    public void IsDuplicateScan_DetectsDebounceWithinWindow()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var barcode = "PROD:ITEM-123";

        // Immediate subsequent scan (500ms later) -> duplicate
        var isDup = BarcodeEngine.IsDuplicateScan(barcode, baseTime.AddMilliseconds(500), barcode, baseTime, 1500);
        Assert.True(isDup);

        // Later scan (3000ms later) -> not duplicate
        var isNotDup = BarcodeEngine.IsDuplicateScan(barcode, baseTime.AddMilliseconds(3000), barcode, baseTime, 1500);
        Assert.False(isNotDup);

        // Different barcode -> not duplicate
        var diffBarcode = BarcodeEngine.IsDuplicateScan("PROD:ITEM-456", baseTime.AddMilliseconds(500), barcode, baseTime, 1500);
        Assert.False(diffBarcode);
    }

    [Fact]
    public void CalculateGtinCheckDigit_ComputesMod10Correctly()
    {
        // For GTIN-13: 400638133393 without check digit -> check digit is 1
        var check = BarcodeEngine.CalculateGtinCheckDigit("400638133393");
        Assert.Equal('1', check);
    }
}
