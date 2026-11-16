using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalErp.Application.Localization;

public sealed class CurrencyFormattingService
{
    private static readonly Dictionary<string, (string Symbol, int MinorUnits, string Name)> Currencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = ("$", 2, "US Dollar"),
            ["EUR"] = ("€", 2, "Euro"),
            ["GBP"] = ("£", 2, "Pound Sterling"),
            ["AED"] = ("AED ", 2, "UAE Dirham"),
            ["SAR"] = ("SAR ", 2, "Saudi Riyal"),
            ["PKR"] = ("PKR ", 2, "Pakistani Rupee"),
            ["CAD"] = ("CA$", 2, "Canadian Dollar"),
            ["AUD"] = ("A$", 2, "Australian Dollar"),
            ["INR"] = ("₹", 2, "Indian Rupee"),
            ["JPY"] = ("¥", 0, "Japanese Yen"),
            ["BHD"] = ("BHD ", 3, "Bahraini Dinar"),
            ["KWD"] = ("KWD ", 3, "Kuwaiti Dinar"),
            ["CHF"] = ("CHF ", 2, "Swiss Franc"),
            ["SGD"] = ("S$", 2, "Singapore Dollar")
        };

    public string Format(decimal amount, string currencyCode = "USD", string? locale = null)
    {
        var code = (currencyCode ?? "USD").Trim().ToUpperInvariant();
        if (!Currencies.TryGetValue(code, out var meta))
        {
            meta = (code + " ", 2, code);
        }

        var culture = !string.IsNullOrWhiteSpace(locale)
            ? CultureInfo.GetCultureInfo(locale)
            : CultureInfo.InvariantCulture;

        var rounded = Math.Round(amount, meta.MinorUnits, MidpointRounding.AwayFromZero);
        var formatSpec = "N" + meta.MinorUnits;

        return $"{meta.Symbol}{rounded.ToString(formatSpec, culture)}";
    }

    public int GetMinorUnits(string currencyCode)
    {
        var code = (currencyCode ?? "USD").Trim().ToUpperInvariant();
        return Currencies.TryGetValue(code, out var meta) ? meta.MinorUnits : 2;
    }
}
