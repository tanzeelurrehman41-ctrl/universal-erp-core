/**
 * Universal ERP — Authoritative Country Localization Coverage Matrix
 * System-wide regulatory and statutory capability matrix categorizing countries across 7 distinct subsystems:
 * 1. Tax
 * 2. Payroll
 * 3. Accounting
 * 4. E-Invoicing
 * 5. Payment Integration
 * 6. Language and Locale
 * 7. Currency and Formatting
 *
 * Statuses:
 * - "Implemented and tested"
 * - "Partially implemented"
 * - "Architecture configured only"
 * - "Not yet supported"
 * - "Awaiting regulatory verification"
 */

export type CoverageStatus =
  | "Implemented and tested"
  | "Partially implemented"
  | "Architecture configured only"
  | "Not yet supported"
  | "Awaiting regulatory verification";

export type DomainCapability = {
  status: CoverageStatus;
  notes: string;
};

export type SubsystemsCoverage = {
  tax: DomainCapability;
  payroll: DomainCapability;
  accounting: DomainCapability;
  eInvoicing: DomainCapability;
  paymentIntegration: DomainCapability;
  languageAndLocale: DomainCapability;
  currencyAndFormatting: DomainCapability;
};

export type CountryCoverageEntry = {
  roadmapPriority: number;
  countryCode: string;
  countryName: string;
  primaryCurrency: string;
  overallStatus: CoverageStatus;
  subsystems: SubsystemsCoverage;
  statutoryAuthorities: string[];
  supportedSubnationalJurisdictions?: string[];
  activeVersion: string;
  verificationEvidence: string;
};

export const LOCALIZATION_COVERAGE_MATRIX: Record<string, CountryCoverageEntry> = {
  PK: {
    roadmapPriority: 1,
    countryCode: "PK",
    countryName: "Pakistan",
    primaryCurrency: "PKR",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "SRB 15%/3%, PRA 16%/5%, KPRA 15%/2%, BRA 15%/6% (Finance Act 2026, input tax credit barred), ICT 15%/5%, export zero-rating.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "FBR Tax Year 2027 8-tier salaried slabs (Finance Act 2026), statutory EOBI (PKR 37,000 base: PKR 370 EE, PKR 1,850 ER), SESSI/PESSI 5% on PKR 40k ceiling.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "Double-entry general ledger in PKR, custom fiscal year (July 1 - June 30), multi-branch trial balance.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "FBR Digital Invoicing integration under Sales Tax General Order 01/2024.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "Raast instant payments, 1Link clearing rails, PRAL banking clearing support.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Urdu (ur-PK) with Right-to-Left (dir=\"rtl\") UI layout and English (en-PK).",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Pakistani Rupee (PKR), South Asian numbering system (lakh/crore grouping: 1,50,000.00).",
      },
    },
    statutoryAuthorities: ["FBR", "SRB", "PRA", "KPRA", "BRA", "EOBI"],
    supportedSubnationalJurisdictions: ["SINDH", "PUNJAB", "KP", "BALOCHISTAN", "FEDERAL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Finance Act 2026, provincial sales tax acts, and EOBI Act 1976.",
  },

  US: {
    roadmapPriority: 2,
    countryCode: "US",
    countryName: "United States",
    primaryCurrency: "USD",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "State & local sales tax resolution: NY (8.875%), TX (state 6.25% + local up to 2.0% with 80% taxable base = 5.0% state-only or 6.6% combined), WA (6.5%), CA (pure SaaS exempt), resale certs.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "2026 OASDI cap ($184,500), Medicare 1.45% + Additional 0.9% over $200k/$250k, 2026 standard deductions ($16,100 Single, $32,200 MFJ, $24,150 HOH), Form W-4 settings (Steps 2-4), FUTA 0.6%, SUTA.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "USD 2-decimal ledger, US GAAP double-entry, calendar fiscal year (Jan 1 - Dec 31).",
      },
      eInvoicing: {
        status: "Architecture configured only",
        notes: "Business Payments Coalition (BPC) / Peppol US pilot framework architecture configured.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "ACH (NACHA) file generation, Fedwire, Stripe sandbox integration.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-US), US date format (MM/DD/YYYY), imperial units.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "US Dollar ($ USD), standard 2-decimal Western comma grouping ($ 1,250.50).",
      },
    },
    statutoryAuthorities: ["IRS", "SSA", "NYS DTF", "TX Comptroller", "CDTFA", "WA DOR"],
    supportedSubnationalJurisdictions: ["NY", "TX", "WA", "CA", "IL"],
    activeVersion: "2026.1.0",
    verificationEvidence: "Verified against IRS Publication 15-T (2026), Form W-4, and SSA statutory wage base card.",
  },

  GB: {
    roadmapPriority: 3,
    countryCode: "GB",
    countryName: "United Kingdom",
    primaryCurrency: "GBP",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "HMRC standard 20% VAT, cross-border B2B reverse charge (VATA 1994 s7A), overseas vendor digital rules.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "2026-27 Employer Class 1 NIC (15.0%, £417/mo secondary threshold), categories (A/B/C/H/M/J), Scottish 6-band PAYE, Student Loans (Plan 1 £2,241.66, Plan 2 £2,448.75, Plan 4 £2,816.25, Plan 5 £2,083.33, PG £1,750.00), Pension Auto-Enrolment.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "UK GAAP / FRS 102 ledger, UK tax year (6 April - 5 April).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "HMRC Making Tax Digital (MTD) for VAT compliance.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "BACS direct entry, Faster Payments Service (FPS), CHAPS clearing.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-GB), UK date format (DD/MM/YYYY), UK postcode validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "British Pound (£ GBP), 2 decimals, standard comma grouping (£ 2,450.75).",
      },
    },
    statutoryAuthorities: ["HMRC", "TPR", "SLC"],
    supportedSubnationalJurisdictions: ["ENGLAND", "SCOTLAND", "WALES", "NI"],
    activeVersion: "2026.2.0",
    verificationEvidence: "Verified against Autumn Budget 2024 NIC reform, HMRC PAYE, and Scottish Income Tax 2026/27.",
  },

  AE: {
    roadmapPriority: 4,
    countryCode: "AE",
    countryName: "United Arab Emirates",
    primaryCurrency: "AED",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Federal Decree-Law No. (8) of 2017 standard 5% VAT, FTA tax invoice generation.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "0% income tax; separate cohort rules for Federal Law 7 of 1999 (5% EE, 12.5% ER, cap AED 50k), Federal Decree-Law 57 of 2023 (11% EE, 15% ER, cap AED 70k/100k), ADPF (11% EE, 15% ER), SSSF (5% EE, 15% ER), expat EOSG under Decree-Law 33 of 2021.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "AED double-entry ledger, Gregorian and Hijri calendar support.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "UAE Ministry of Finance E-Invoicing standard based on Peppol PINT.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "Wages Protection System (WPS) SIF file generation, UAE Central Bank IPP.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Arabic (ar-AE) with RTL (dir=\"rtl\") layout and English (en-AE).",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "UAE Dirham (AED), 2 decimals.",
      },
    },
    statutoryAuthorities: ["FTA", "MOHRE", "GPSSA", "ADPF", "SSSF"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Federal Decree-Law 57 of 2023, Law 7 of 1999, and MOHRE Labour Law 33 of 2021.",
  },

  SA: {
    roadmapPriority: 5,
    countryCode: "SA",
    countryName: "Saudi Arabia",
    primaryCurrency: "SAR",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Standard 15% VAT under Royal Decree No. M/113.",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "General Organization for Social Insurance (GOSI) statutory architecture configured in roadmap; awaiting regulatory release.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "SAR double-entry ledger, Hijri/Umm al-Qura calendar support.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "ZATCA FATOORA Phase 2 (Cryptographic Stamp, ECDSA secp256k1, QR Code, UBL 2.1 XML).",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SARIE instant payments, Mada, Saudi National WPS.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Arabic (ar-SA) with RTL (dir=\"rtl\") layout.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Saudi Riyal (SAR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["ZATCA", "GOSI"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against ZATCA Phase 2 E-Invoicing Technical Specifications.",
  },

  DE: {
    roadmapPriority: 6,
    countryCode: "DE",
    countryName: "Germany (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "German MwSt 19%, EU OSS Destination VAT, Intra-EU B2B Reverse Charge (Directive 2006/112/EC Art 196).",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "Lohnsteuer & Sozialversicherung architecture defined in roadmap; not yet activated.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, GoBD principles, metric system.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "ZUGFeRD 2.2 / XRechnung (EN 16931) mandatory e-invoicing architecture.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA Credit Transfer (SCT) and Direct Debit (SDD) ISO 20022 XML.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "German (de-DE), DIN 5008 address format, 5-digit postal code.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["BZSt", "Finanzamt"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against German UStG § 12 and B2B E-Invoicing Mandate 2025/2026.",
  },

  FR: {
    roadmapPriority: 6,
    countryCode: "FR",
    countryName: "France (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "French TVA 20%, EU OSS Destination VAT, Intra-EU Art 196 Reverse Charge.",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "Bulletin de paie & URSSAF architecture configured in roadmap; not yet activated.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, Plan Comptable Général (PCG).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "Chorus Pro / Portail Public de Facturation (PPF) integration.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA ISO 20022 XML formats.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "French (fr-FR), French address and postal code formats.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["DGFiP", "URSSAF"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against French CGI Art 278 and PPF mandate.",
  },

  IE: {
    roadmapPriority: 6,
    countryCode: "IE",
    countryName: "Ireland (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Irish standard VAT 23%, EU OSS, Intra-EU Art 196 Reverse Charge.",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "PAYE Modernisation architecture configured in roadmap; not yet activated.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, calendar fiscal year.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "Peppol BIS Billing 3.0 e-invoicing.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA SCT/SDD clearing.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-IE), Eircode postal validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["Irish Revenue Commissioners"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Irish VAT Consolidation Act 2010.",
  },

  EU_OTHER: {
    roadmapPriority: 6,
    countryCode: "EU",
    countryName: "Other EU Member States (24 Countries)",
    primaryCurrency: "EUR",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Standard rates for all 27 EU member states, EU One Stop Shop (OSS) destination VAT, and Article 196 Reverse Charge.",
      },
      payroll: {
        status: "Not yet supported",
        notes: "Member-state specific payroll modules planned for future localizations.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "Multi-currency support for EUR and non-EUR members (SEK, PLN, DKK, CZK, HUF, RON, BGN).",
      },
      eInvoicing: {
        status: "Partially implemented",
        notes: "Italy SdI, Poland KSeF, Spain FACe, and Peppol BIS 3.0 architecture mapped.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA Single Euro Payments Area ISO 20022 XML.",
      },
      languageAndLocale: {
        status: "Partially implemented",
        notes: "European date and address format standards.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "ISO 4217 compliant formatting.",
      },
    },
    statutoryAuthorities: ["European Commission / Member State Tax Authorities"],
    activeVersion: "2026.1",
    verificationEvidence: "Complies with EU Council Directive 2006/112/EC and OSS Regulation 2019/1995.",
  },

  CA: {
    roadmapPriority: 7,
    countryCode: "CA",
    countryName: "Canada",
    primaryCurrency: "CAD",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Non-resident digital services regime: ON HST 13%, BC 12%, QC QST 14.975%, Atlantic 15%, Federal 5%, B2B reverse charge.",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "CPP/EI & TD1 federal tax credit architecture seeded under Migration 041.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "CAD 2-decimal ledger, bilingual English/French support.",
      },
      eInvoicing: {
        status: "Architecture configured only",
        notes: "CRA digital reporting architecture configured.",
      },
      paymentIntegration: {
        status: "Partially implemented",
        notes: "Interac & EFT 80-byte / 1464-byte batch files mapped.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-CA) and French (fr-CA), postal code A1A 1A1.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Canadian Dollar (CA$ CAD), 2 decimals.",
      },
    },
    statutoryAuthorities: ["CRA", "Revenu Québec"],
    supportedSubnationalJurisdictions: ["ON", "BC", "QC", "AB", "NS", "NB", "NL", "PE"],
    activeVersion: "0.1.0-draft",
    verificationEvidence: "Verified against CRA Excise Tax Act Part IX Non-Resident Digital Framework.",
  },

  AU: {
    roadmapPriority: 8,
    countryCode: "AU",
    countryName: "Australia",
    primaryCurrency: "AUD",
    overallStatus: "Partially implemented",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "ATO standard 10% GST, B2C inbound digital supplies (Subdiv 84-B), B2B reverse charge (Subdiv 84-A).",
      },
      payroll: {
        status: "Partially implemented",
        notes: "Superannuation Guarantee 12.0% effective 1 July 2026, OTE qualification, Payday Super 7-business-day fund clearance rule, PAYG withholding.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "AUD 2-decimal ledger, Australian fiscal year (1 July - 30 June).",
      },
      eInvoicing: {
        status: "Partially implemented",
        notes: "ATO Peppol E-Invoicing framework.",
      },
      paymentIntegration: {
        status: "Partially implemented",
        notes: "NPP (New Payments Platform) / PayID & BECS Direct Entry file format.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-AU), 4-digit postal code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Australian Dollar (A$ AUD), 2 decimals.",
      },
    },
    statutoryAuthorities: ["ATO"],
    supportedSubnationalJurisdictions: ["NSW", "VIC", "QLD", "WA", "SA"],
    activeVersion: "0.1.0-draft",
    verificationEvidence: "Verified against A New Tax System (GST) Act 1999 and Superannuation Guarantee Administration Act 1992.",
  },

  IN: {
    roadmapPriority: 9,
    countryCode: "IN",
    countryName: "India",
    primaryCurrency: "INR",
    overallStatus: "Architecture configured only",
    subsystems: {
      tax: {
        status: "Architecture configured only",
        notes: "Dual GST (CGST + SGST / IGST) and OIDAR non-resident digital services tax rules configured.",
      },
      payroll: {
        status: "Architecture configured only",
        notes: "EPF (12%), ESI, Professional Tax, and Section 192 TDS architecture seeded.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "INR 2-decimal ledger, South Asian numbering format, fiscal year (1 April - 31 March).",
      },
      eInvoicing: {
        status: "Architecture configured only",
        notes: "NIC E-Invoice Portal / Invoice Registration Portal (IRP) architecture mapped.",
      },
      paymentIntegration: {
        status: "Partially implemented",
        notes: "UPI, NEFT, RTGS rails mapped.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "English (en-IN), Hindi (hi-IN), Indian pin code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Indian Rupee (₹ INR), South Asian lakh/crore grouping.",
      },
    },
    statutoryAuthorities: ["CBIC", "EPFO", "Income Tax Department"],
    supportedSubnationalJurisdictions: ["MH", "KA", "DL", "TN", "UP"],
    activeVersion: "0.1.0-draft",
    verificationEvidence: "Verified against GST Acts 2017 and OIDAR statutory guidelines.",
  },
};

/**
 * Gets localization coverage entry for a given country code.
 */
export function getCountryCoverage(countryCode: string): CountryCoverageEntry | undefined {
  const code = (countryCode || "").trim().toUpperCase();
  return LOCALIZATION_COVERAGE_MATRIX[code];
}

/**
 * Filters matrix entries by overall coverage status.
 */
export function getCoverageByStatus(status: CoverageStatus): CountryCoverageEntry[] {
  return Object.values(LOCALIZATION_COVERAGE_MATRIX).filter(entry => entry.overallStatus === status);
}

/**
 * Returns all coverage matrix entries sorted by roadmap priority.
 */
export function getAllCoverageEntries(): CountryCoverageEntry[] {
  return Object.values(LOCALIZATION_COVERAGE_MATRIX).sort((a, b) => a.roadmapPriority - b.roadmapPriority);
}
