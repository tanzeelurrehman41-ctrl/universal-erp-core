using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Localization;

public sealed record CountryDescriptor(
    string CountryCode,
    string Alpha3Code,
    int NumericCode,
    string CountryName,
    string DefaultCurrencyCode,
    string DefaultTimezone,
    string DefaultLocale,
    int FiscalYearStartMonth,
    int FiscalYearStartDay,
    string AddressFormatType,
    string TaxSystemType,
    string EInvoicingMandateType,
    bool IsSupported);

public sealed record CurrencyDescriptor(
    string CurrencyCode,
    string Name,
    string Symbol,
    int MinorUnits,
    string DisplayFormat);

public sealed record JurisdictionDescriptor(
    string CountryCode,
    string JurisdictionCode,
    string Name,
    string JurisdictionType,
    decimal StandardTaxRate,
    decimal? ReducedTaxRate,
    string StatutoryAuthority);

public sealed record TaxDeterminationRequest(
    string SupplierCountryCode,
    string? SupplierTaxId,
    string CustomerCountryCode,
    string? CustomerProvinceState,
    string? CustomerPostalCode,
    string CustomerType = "all", // "all", "b2b", "b2c", "b2b_registered", "b2c_unregistered"
    string? CustomerTaxId = null,
    string? ProductServiceClassification = null,
    bool IsSaaSOrDigitalService = true,
    bool IsExport = false,
    bool HasValidForeignRemittanceProof = false,
    string? ExemptionCertificateNumber = null,
    bool ProviderElectedReducedRateWithoutInputTax = false,
    DateTimeOffset? AsOfDate = null);

public sealed record TaxDeterminationResult(
    string Jurisdiction,
    string CountryCode,
    string? ProvinceState,
    string Authority,
    string TaxSystem, // "sales_tax", "vat", "gst", "export_zero_rated", "reverse_charge", "exempt"
    string TaxName,
    string TariffOrClassificationCode,
    decimal RatePercentage,
    bool InputTaxAllowable,
    bool IsReverseCharge,
    bool IsExempt,
    string StatutoryReference,
    string CalculationExplanation,
    string AuditEvidenceSummary);

public sealed record GlobalInvoiceCalculationResult(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal CreditsApplied,
    decimal TaxableBase,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal TotalAmount,
    string CurrencyCode,
    bool IsTaxInclusive,
    string CalculationExplanation);

public sealed record GlobalCreditNoteResult(
    decimal RefundSubtotal,
    decimal TaxReversed,
    decimal TotalCreditNoteAmount,
    decimal TaxRatePercentage,
    string CurrencyCode,
    bool IsTaxInclusive,
    string ReversalExplanation);
