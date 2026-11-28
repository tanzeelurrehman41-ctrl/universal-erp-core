using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UniversalErp.Application.Warehouse;

public class ParsedGs1Barcode
{
    public string? Gtin { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public decimal? NetWeightKg { get; set; }
    public decimal? Quantity { get; set; }
    public Dictionary<string, string> AdditionalAis { get; set; } = new();
}

public static class BarcodeEngine
{
    private static readonly Regex Gs1ParenthesesRegex = new(
        @"\((?<ai>\d{2,4})\)(?<val>[^()]+)",
        RegexOptions.Compiled);

    public static ParsedGs1Barcode ParseGs1Barcode(string rawBarcode)
    {
        if (string.IsNullOrWhiteSpace(rawBarcode))
            throw new ArgumentException("Barcode cannot be null or empty.", nameof(rawBarcode));

        var result = new ParsedGs1Barcode();
        var matches = Gs1ParenthesesRegex.Matches(rawBarcode);

        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                var ai = match.Groups["ai"].Value;
                var val = match.Groups["val"].Value.Trim();
                ApplyAi(result, ai, val);
            }
            return result;
        }

        // FNC1 separator or raw AI format
        var trimmed = rawBarcode.Trim();
        var index = 0;
        while (index < trimmed.Length)
        {
            if (trimmed.Length - index >= 16 && trimmed.Substring(index, 2) == "01")
            {
                // GTIN (14 chars)
                result.Gtin = trimmed.Substring(index + 2, 14);
                index += 16;
            }
            else if (trimmed.Length - index >= 8 && trimmed.Substring(index, 2) == "17")
            {
                // Expiry date (6 chars: YYMMDD)
                result.ExpiryDate = trimmed.Substring(index + 2, 6);
                index += 8;
            }
            else if (trimmed.Length - index >= 3 && trimmed.Substring(index, 2) == "10")
            {
                // Lot (variable, up to FNC1 / GS char or end)
                index += 2;
                var lotEnd = trimmed.IndexOfAny(new[] { '\u001d', '\u001e', ' ' }, index);
                if (lotEnd == -1)
                {
                    result.LotNumber = trimmed[index..];
                    break;
                }
                result.LotNumber = trimmed[index..lotEnd];
                index = lotEnd + 1;
            }
            else if (trimmed.Length - index >= 3 && trimmed.Substring(index, 2) == "21")
            {
                // Serial (variable, up to FNC1 / GS char or end)
                index += 2;
                var serEnd = trimmed.IndexOfAny(new[] { '\u001d', '\u001e', ' ' }, index);
                if (serEnd == -1)
                {
                    result.SerialNumber = trimmed[index..];
                    break;
                }
                result.SerialNumber = trimmed[index..serEnd];
                index = serEnd + 1;
            }
            else
            {
                // Stop if unrecognized format
                break;
            }
        }

        return result;
    }

    private static void ApplyAi(ParsedGs1Barcode result, string ai, string val)
    {
        switch (ai)
        {
            case "01":
                result.Gtin = val;
                break;
            case "10":
                result.LotNumber = val;
                break;
            case "17":
                result.ExpiryDate = val;
                break;
            case "21":
                result.SerialNumber = val;
                break;
            case "37":
                if (decimal.TryParse(val, out var count))
                    result.Quantity = count;
                break;
            default:
                if (ai.StartsWith("310") && ai.Length == 4)
                {
                    // Net weight in kg with decimal point position indicated by 4th digit
                    var decimals = int.Parse(ai.Substring(3, 1));
                    if (decimal.TryParse(val, out var weightRaw))
                    {
                        result.NetWeightKg = weightRaw / (decimal)Math.Pow(10, decimals);
                    }
                }
                else
                {
                    result.AdditionalAis[ai] = val;
                }
                break;
        }
    }

    public static string GenerateGs1Barcode(
        string gtin,
        string? lotNumber = null,
        string? expiryDateYymmdd = null,
        string? serialNumber = null,
        decimal? count = null)
    {
        if (string.IsNullOrWhiteSpace(gtin))
            throw new ArgumentException("GTIN is required for GS1 barcode.", nameof(gtin));

        var cleanGtin = gtin.PadLeft(14, '0');
        var builder = new System.Text.StringBuilder();
        builder.Append($"(01){cleanGtin}");

        if (!string.IsNullOrWhiteSpace(expiryDateYymmdd))
            builder.Append($"(17){expiryDateYymmdd.Trim()}");

        if (!string.IsNullOrWhiteSpace(lotNumber))
            builder.Append($"(10){lotNumber.Trim()}");

        if (!string.IsNullOrWhiteSpace(serialNumber))
            builder.Append($"(21){serialNumber.Trim()}");

        if (count.HasValue)
            builder.Append($"(37){(long)count.Value}");

        return builder.ToString();
    }

    public static (string EntityType, string RawIdentifier) ParseStandardBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return ("unknown", string.Empty);

        var trimmed = barcode.Trim();

        if (trimmed.StartsWith("BIN:", StringComparison.OrdinalIgnoreCase))
            return ("stock_bin", trimmed[4..]);

        if (trimmed.StartsWith("PROD:", StringComparison.OrdinalIgnoreCase))
            return ("product_variant", trimmed[5..]);

        if (trimmed.StartsWith("WAVE:", StringComparison.OrdinalIgnoreCase))
            return ("picking_wave", trimmed[5..]);

        if (trimmed.StartsWith("PKG:", StringComparison.OrdinalIgnoreCase))
            return ("shipping_carton", trimmed[4..]);

        if (trimmed.StartsWith("WO:", StringComparison.OrdinalIgnoreCase))
            return ("work_order", trimmed[3..]);

        if (trimmed.StartsWith("LOT:", StringComparison.OrdinalIgnoreCase))
            return ("lot", trimmed[4..]);

        if (trimmed.StartsWith("SN:", StringComparison.OrdinalIgnoreCase))
            return ("serial", trimmed[3..]);

        // If it starts with (01), it's GS1
        if (trimmed.StartsWith("(01)"))
            return ("product_variant", trimmed);

        // Fallback: raw barcode could be variant SKU or bin code
        return ("unspecified", trimmed);
    }

    public static bool IsDuplicateScan(
        string currentBarcode,
        DateTimeOffset currentTimestamp,
        string? previousBarcode,
        DateTimeOffset? previousTimestamp,
        int debounceWindowMs = 1500)
    {
        if (string.IsNullOrWhiteSpace(previousBarcode) || !previousTimestamp.HasValue)
            return false;

        if (!string.Equals(currentBarcode.Trim(), previousBarcode.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var elapsedMs = (currentTimestamp - previousTimestamp.Value).TotalMilliseconds;
        return elapsedMs >= 0 && elapsedMs <= debounceWindowMs;
    }

    public static char CalculateGtinCheckDigit(string digitsWithoutCheck)
    {
        if (digitsWithoutCheck.Length < 7 || digitsWithoutCheck.Length > 17)
            throw new ArgumentException("Invalid digit count for check digit calculation.", nameof(digitsWithoutCheck));

        var sum = 0;
        var multiplyByThree = true;

        for (var i = digitsWithoutCheck.Length - 1; i >= 0; i--)
        {
            var digit = digitsWithoutCheck[i] - '0';
            if (digit < 0 || digit > 9)
                throw new ArgumentException("Barcode must contain only digits.", nameof(digitsWithoutCheck));

            sum += digit * (multiplyByThree ? 3 : 1);
            multiplyByThree = !multiplyByThree;
        }

        var mod = sum % 10;
        return mod == 0 ? '0' : (char)('0' + (10 - mod));
    }
}
