/**
 * Universal ERP — Global Localization Country Registry
 * ISO 3166-1 country definitions, regional tax authorities, currencies, and locales.
 */

export type CountryJurisdiction = {
  code: string;
  name: string;
  taxAuthority: string;
  standardTaxRatePct: number;
  reducedTaxRatePct?: number;
  statutoryReference?: string;
  isSubnational: boolean;
};

export type CountryPackage = {
  countryCode: string;
  countryName: string;
  isoNumeric: string;
  defaultCurrencyCode: string;
  currencySymbol: string;
  currencyDecimals: number;
  defaultTimezone: string;
  defaultLocale: string;
  fiscalYearStartMonth: number; // 1 = January, 7 = July
  taxSystemName: "Sales Tax" | "VAT" | "GST" | "Harmonized Tax";
  measurementSystem: "metric" | "imperial";
  addressFormat: "us_style" | "uk_style" | "eu_style" | "south_asia";
  requiresZipCode: boolean;
  jurisdictions: CountryJurisdiction[];
  isEInvoicingMandatory: boolean;
  eInvoicingPlatform?: string;
};

export const SUPPORTED_COUNTRIES: Record<string, CountryPackage> = {
  US: {
    countryCode: "US",
    countryName: "United States",
    isoNumeric: "840",
    defaultCurrencyCode: "USD",
    currencySymbol: "$",
    currencyDecimals: 2,
    defaultTimezone: "America/New_York",
    defaultLocale: "en-US",
    fiscalYearStartMonth: 1,
    taxSystemName: "Sales Tax",
    measurementSystem: "imperial",
    addressFormat: "us_style",
    requiresZipCode: true,
    isEInvoicingMandatory: false,
    jurisdictions: [
      {
        code: "NY",
        name: "New York",
        taxAuthority: "New York State Department of Taxation and Finance",
        standardTaxRatePct: 8.875,
        statutoryReference: "NY Tax Law § 1105(c)(9) (NYC Combined State & Local Rate)",
        isSubnational: true,
      },
      {
        code: "TX",
        name: "Texas",
        taxAuthority: "Texas Comptroller of Public Accounts",
        standardTaxRatePct: 6.6,
        statutoryReference: "Tex. Tax Code § 151.0101 (80% taxable basis of 8.25% combined = 6.6% effective)",
        isSubnational: true,
      },
      {
        code: "WA",
        name: "Washington",
        taxAuthority: "Washington Department of Revenue",
        standardTaxRatePct: 6.5,
        statutoryReference: "RCW 82.08.020 (Digital Automated Services)",
        isSubnational: true,
      },
      {
        code: "CA",
        name: "California",
        taxAuthority: "California Department of Tax and Fee Administration",
        standardTaxRatePct: 0.0,
        statutoryReference: "Cal. Rev. & Tax. Code § 6016 (Pure SaaS non-taxable)",
        isSubnational: true,
      },
      {
        code: "IL",
        name: "Illinois",
        taxAuthority: "Illinois Department of Revenue",
        standardTaxRatePct: 6.25,
        statutoryReference: "35 ILCS 105 (Chicago Personal Property Lease Transaction Tax may apply)",
        isSubnational: true,
      },
    ],
  },
  GB: {
    countryCode: "GB",
    countryName: "United Kingdom",
    isoNumeric: "826",
    defaultCurrencyCode: "GBP",
    currencySymbol: "£",
    currencyDecimals: 2,
    defaultTimezone: "Europe/London",
    defaultLocale: "en-GB",
    fiscalYearStartMonth: 4,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "uk_style",
    requiresZipCode: true,
    isEInvoicingMandatory: false,
    jurisdictions: [
      {
        code: "UK_STANDARD",
        name: "United Kingdom (HMRC Standard)",
        taxAuthority: "HM Revenue & Customs (HMRC)",
        standardTaxRatePct: 20.0,
        reducedTaxRatePct: 5.0,
        statutoryReference: "Value Added Tax Act 1994, Section 2",
        isSubnational: false,
      },
    ],
  },
  DE: {
    countryCode: "DE",
    countryName: "Germany",
    isoNumeric: "276",
    defaultCurrencyCode: "EUR",
    currencySymbol: "€",
    currencyDecimals: 2,
    defaultTimezone: "Europe/Berlin",
    defaultLocale: "de-DE",
    fiscalYearStartMonth: 1,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "eu_style",
    requiresZipCode: true,
    isEInvoicingMandatory: true,
    eInvoicingPlatform: "ZUGFeRD / XRechnung",
    jurisdictions: [
      {
        code: "DE_STANDARD",
        name: "Germany (Standard MwSt)",
        taxAuthority: "Bundeszentralamt für Steuern (BZSt)",
        standardTaxRatePct: 19.0,
        reducedTaxRatePct: 7.0,
        statutoryReference: "Umsatzsteuergesetz (UStG) § 12 Abs. 1",
        isSubnational: false,
      },
    ],
  },
  FR: {
    countryCode: "FR",
    countryName: "France",
    isoNumeric: "250",
    defaultCurrencyCode: "EUR",
    currencySymbol: "€",
    currencyDecimals: 2,
    defaultTimezone: "Europe/Paris",
    defaultLocale: "fr-FR",
    fiscalYearStartMonth: 1,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "eu_style",
    requiresZipCode: true,
    isEInvoicingMandatory: true,
    eInvoicingPlatform: "Chorus Pro / PPF",
    jurisdictions: [
      {
        code: "FR_STANDARD",
        name: "France (Standard TVA)",
        taxAuthority: "Direction Générale des Finances Publiques (DGFiP)",
        standardTaxRatePct: 20.0,
        reducedTaxRatePct: 10.0,
        statutoryReference: "Code Général des Impôts (CGI) Art. 278",
        isSubnational: false,
      },
    ],
  },
  IE: {
    countryCode: "IE",
    countryName: "Ireland",
    isoNumeric: "372",
    defaultCurrencyCode: "EUR",
    currencySymbol: "€",
    currencyDecimals: 2,
    defaultTimezone: "Europe/Dublin",
    defaultLocale: "en-IE",
    fiscalYearStartMonth: 1,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "eu_style",
    requiresZipCode: true,
    isEInvoicingMandatory: false,
    jurisdictions: [
      {
        code: "IE_STANDARD",
        name: "Ireland (Standard VAT)",
        taxAuthority: "Irish Revenue Commissioners",
        standardTaxRatePct: 23.0,
        reducedTaxRatePct: 13.5,
        statutoryReference: "Value-Added Tax Consolidation Act 2010, Section 46",
        isSubnational: false,
      },
    ],
  },
  AE: {
    countryCode: "AE",
    countryName: "United Arab Emirates",
    isoNumeric: "784",
    defaultCurrencyCode: "AED",
    currencySymbol: "AED",
    currencyDecimals: 2,
    defaultTimezone: "Asia/Dubai",
    defaultLocale: "en-AE",
    fiscalYearStartMonth: 1,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "uk_style",
    requiresZipCode: false,
    isEInvoicingMandatory: true,
    eInvoicingPlatform: "UAE MoF E-Invoicing",
    jurisdictions: [
      {
        code: "AE_STANDARD",
        name: "United Arab Emirates (FTA Standard)",
        taxAuthority: "Federal Tax Authority (FTA)",
        standardTaxRatePct: 5.0,
        statutoryReference: "Federal Decree-Law No. (8) of 2017 on Value Added Tax, Article 3",
        isSubnational: false,
      },
    ],
  },
  SA: {
    countryCode: "SA",
    countryName: "Saudi Arabia",
    isoNumeric: "682",
    defaultCurrencyCode: "SAR",
    currencySymbol: "SAR",
    currencyDecimals: 2,
    defaultTimezone: "Asia/Riyadh",
    defaultLocale: "ar-SA",
    fiscalYearStartMonth: 1,
    taxSystemName: "VAT",
    measurementSystem: "metric",
    addressFormat: "south_asia",
    requiresZipCode: true,
    isEInvoicingMandatory: true,
    eInvoicingPlatform: "ZATCA FATOORA Phase 2",
    jurisdictions: [
      {
        code: "SA_STANDARD",
        name: "Saudi Arabia (ZATCA Standard)",
        taxAuthority: "Zakat, Tax and Customs Authority (ZATCA)",
        standardTaxRatePct: 15.0,
        statutoryReference: "Value Added Tax Law, Royal Decree No. M/113, amended effective July 1, 2020",
        isSubnational: false,
      },
    ],
  },
  PK: {
    countryCode: "PK",
    countryName: "Pakistan",
    isoNumeric: "586",
    defaultCurrencyCode: "PKR",
    currencySymbol: "Rs.",
    currencyDecimals: 2,
    defaultTimezone: "Asia/Karachi",
    defaultLocale: "ur-PK",
    fiscalYearStartMonth: 7,
    taxSystemName: "Sales Tax",
    measurementSystem: "metric",
    addressFormat: "south_asia",
    requiresZipCode: true,
    isEInvoicingMandatory: true,
    eInvoicingPlatform: "FBR / Provincial Digital Invoicing",
    jurisdictions: [
      {
        code: "FEDERAL",
        name: "Federal / Islamabad Capital Territory (ICT)",
        taxAuthority: "FBR ICT Sales Tax on Services",
        standardTaxRatePct: 15.0,
        reducedTaxRatePct: 5.0,
        statutoryReference: "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Heading 9815.6000",
        isSubnational: true,
      },
      {
        code: "SINDH",
        name: "Sindh",
        taxAuthority: "Sindh Revenue Board (SRB)",
        standardTaxRatePct: 15.0,
        reducedTaxRatePct: 3.0,
        statutoryReference: "Sindh Sales Tax on Services Act, 2011, Sec 8(1) & Finance Act 2024, Heading 9815.6000",
        isSubnational: true,
      },
      {
        code: "PUNJAB",
        name: "Punjab",
        taxAuthority: "Punjab Revenue Authority (PRA)",
        standardTaxRatePct: 16.0,
        reducedTaxRatePct: 5.0,
        statutoryReference: "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000",
        isSubnational: true,
      },
      {
        code: "KP",
        name: "Khyber Pakhtunkhwa (KP)",
        taxAuthority: "Khyber Pakhtunkhwa Revenue Authority (KPRA)",
        standardTaxRatePct: 15.0,
        reducedTaxRatePct: 2.0,
        statutoryReference: "KP Finance Act 2026, Schedule Table, Heading 9850.0000",
        isSubnational: true,
      },
      {
        code: "BALOCHISTAN",
        name: "Balochistan",
        taxAuthority: "Balochistan Revenue Authority (BRA)",
        standardTaxRatePct: 15.0,
        reducedTaxRatePct: 6.0,
        statutoryReference: "Balochistan Sales Tax on Services Act & Finance Act 2026, Heading 9868.0000",
        isSubnational: true,
      },
    ],
  },
  CA: {
    countryCode: "CA",
    countryName: "Canada",
    isoNumeric: "124",
    defaultCurrencyCode: "CAD",
    currencySymbol: "CA$",
    currencyDecimals: 2,
    defaultTimezone: "America/Toronto",
    defaultLocale: "en-CA",
    fiscalYearStartMonth: 1,
    taxSystemName: "Harmonized Tax",
    measurementSystem: "metric",
    addressFormat: "us_style",
    requiresZipCode: true,
    isEInvoicingMandatory: false,
    jurisdictions: [
      {
        code: "ON",
        name: "Ontario (HST)",
        taxAuthority: "Canada Revenue Agency (CRA)",
        standardTaxRatePct: 13.0,
        statutoryReference: "Excise Tax Act (Canada), Part IX (5% Federal + 8% Provincial)",
        isSubnational: true,
      },
      {
        code: "BC",
        name: "British Columbia (GST + PST)",
        taxAuthority: "Canada Revenue Agency / BC Ministry of Finance",
        standardTaxRatePct: 12.0,
        statutoryReference: "5% GST + 7% BC PST on software/SaaS",
        isSubnational: true,
      },
      {
        code: "AB",
        name: "Alberta (GST only)",
        taxAuthority: "Canada Revenue Agency (CRA)",
        standardTaxRatePct: 5.0,
        statutoryReference: "Excise Tax Act (Canada), Part IX (5% Federal GST)",
        isSubnational: true,
      },
    ],
  },
  AU: {
    countryCode: "AU",
    countryName: "Australia",
    isoNumeric: "036",
    defaultCurrencyCode: "AUD",
    currencySymbol: "A$",
    currencyDecimals: 2,
    defaultTimezone: "Australia/Sydney",
    defaultLocale: "en-AU",
    fiscalYearStartMonth: 7,
    taxSystemName: "GST",
    measurementSystem: "metric",
    addressFormat: "uk_style",
    requiresZipCode: true,
    isEInvoicingMandatory: false,
    jurisdictions: [
      {
        code: "AU_STANDARD",
        name: "Australia (ATO Standard GST)",
        taxAuthority: "Australian Taxation Office (ATO)",
        standardTaxRatePct: 10.0,
        statutoryReference: "A New Tax System (Goods and Services Tax) Act 1999",
        isSubnational: false,
      },
    ],
  },
};

export function getSupportedCountries(): CountryPackage[] {
  return Object.values(SUPPORTED_COUNTRIES);
}

export function getCountryByCode(code: string): CountryPackage | undefined {
  if (!code) return undefined;
  return SUPPORTED_COUNTRIES[code.trim().toUpperCase()];
}

export function getJurisdictionsForCountry(countryCode: string): CountryJurisdiction[] {
  const pkg = getCountryByCode(countryCode);
  return pkg ? pkg.jurisdictions : [];
}
