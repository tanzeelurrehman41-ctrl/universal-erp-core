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
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Standard 15% VAT under Royal Decree No. M/113.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "GOSI Annuities (9% EE / 9% ER) and SANED (0.75% EE / 0.75% ER) for Saudi nationals (SAR 45k cap); Occupational Hazards (2% ER) for all employees; Saudi Labor Law Art. 84 EOSA accrual for expatriates.",
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
    statutoryAuthorities: ["ZATCA", "GOSI", "Ministry of Human Resources and Social Development"],
    supportedSubnationalJurisdictions: ["RIYADH", "MAKKAH", "EASTERN_PROVINCE", "MADINAH", "ASIR"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against ZATCA Phase 2 E-Invoicing Technical Specifications, GOSI Law, and Saudi Labor Law Art. 84.",
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
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Federal GST 5%, HST (ON 13%, NS/NB/NL/PE 15%), GST+PST (BC 12%, SK 11%, MB 12%), GST+QST (QC 14.975%), GST-only (AB/YT/NT/NU 5%), B2B reverse charge.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "2026 CRA payroll: Base CPP 5.95% ($74,600 YMPE, $3,500 exemption; 2025: $71,300), Enhanced CPP2 4.0% ($85,000 YAMPE; 2025: $81,200), EI 1.63% EE / 2.282% ER ($68,900 max; 2025: $65,700), federal & provincial TD1 progressive withholding.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "CAD 2-decimal ledger, bilingual English/French support.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "CRA digital reporting architecture & Peppol BIS Billing 3.0 standard.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "Interac rails & CPA 005 EFT 80-byte / 1464-byte batch payroll files.",
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
    supportedSubnationalJurisdictions: ["ON", "QC", "BC", "AB", "SK", "MB", "NS", "NB", "NL", "PE", "YT", "NT", "NU"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against CRA T4001, T4032, Schedule 8, and Excise Tax Act Part IX.",
  },

  AU: {
    roadmapPriority: 8,
    countryCode: "AU",
    countryName: "Australia",
    primaryCurrency: "AUD",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "ATO standard 10% GST, BAS reporting (G1, 1A, 1B), B2C inbound digital supplies (Subdiv 84-B), B2B reverse charge (Subdiv 84-A).",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "Superannuation Guarantee 12.0% effective 1 July 2026, Maximum Contribution Base AUD 270,830/yr (quarterly AUD 67,707.50; prior AUD 65,070), Payday Super 7-business-day fund clearance rule, STP Phase 2 disaggregated lines, state payroll tax rules (NSW, VIC, QLD, WA, SA, TAS, ACT, NT).",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "AUD 2-decimal ledger, Australian fiscal year (1 July - 30 June).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "ATO Peppol E-Invoicing standard (A-NZ Specification based on Peppol BIS 3.0).",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "NPP (New Payments Platform) / PayID & ABA (Australian Bankers Association) BECS Direct Entry file format.",
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
    supportedSubnationalJurisdictions: ["NSW", "VIC", "QLD", "WA", "SA", "TAS", "ACT", "NT"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against A New Tax System (GST) Act 1999, Superannuation Guarantee Administration Act 1992, and STP2 specifications.",
  },

  IN: {
    roadmapPriority: 9,
    countryCode: "IN",
    countryName: "India",
    primaryCurrency: "INR",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Dual GST: Intra-state 9% CGST + 9% SGST, Interstate 18% IGST, Section 5(3) reverse charge, OIDAR B2C 18%, ITC input credit offset ledger.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "EPF 12% EE / 12% ER (EPS 8.33% up to ₹25,000 ceiling effective 17 Sept 2026 = max ₹2,082.50; prior ₹15,000 cap = max ₹1,250 + EPF balance + 0.5% EDLI up to ₹25,000 = max ₹125), ESI 0.75% EE / 3.25% ER (₹21,000 wage ceiling), State PT (MH ₹200/mo, ₹300 Feb), Section 115BAC New Tax Regime (₹75k std deduction, 6 slabs).",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "INR 2-decimal ledger, South Asian numbering format (Lakh/Crore), fiscal year (1 April - 31 March).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "NIC E-Invoice System (IRP schema, IRN cryptographic hash & signed QR code).",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "UPI, NEFT, RTGS, and NACHA / NPCI payment rails.",
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
    statutoryAuthorities: ["CBIC", "EPFO", "Income Tax Department", "ESIC"],
    supportedSubnationalJurisdictions: ["MH", "KA", "DL", "TN", "GJ", "UP", "TG"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Central Goods and Services Tax Act 2017, EPF Act 1952, ESI Act 1948, and Finance Act Section 115BAC.",
  },

  NL: {
    roadmapPriority: 10,
    countryCode: "NL",
    countryName: "Netherlands",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Dutch BTW standard 21%, reduced 9%, EU OSS destination VAT, Article 196 reverse charge.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "Loonheffing Box 1 progressive withholding (36.97% / 49.50%), ZVW employer levy (6.57%), algemene heffingskorting.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, Dutch GAAP / RJ principles, calendar fiscal year.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "Peppol BIS Billing 3.0 e-invoicing mandatory for government and B2B.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA ISO 20022 XML and iDEAL instant payment integration.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Dutch (nl-NL), 4 digits + 2 letters postal code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals, standard comma grouping.",
      },
    },
    statutoryAuthorities: ["Belastingdienst", "UWV"],
    supportedSubnationalJurisdictions: ["NL_ALL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Wet op de omzetbelasting 1968 and Wet op de loonbelasting 1964.",
  },

  ES: {
    roadmapPriority: 11,
    countryCode: "ES",
    countryName: "Spain",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Spanish IVA standard 21%, reduced 10%, super-reduced 4%, EU OSS, Article 196 reverse charge.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "Seguridad Social (6.35% employee, 29.9% employer), IRPF withholding scale.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, Plan General de Contabilidad (PGC).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "FACe platform and Suministro Inmediato de Información (SII) electronic reporting.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA ISO 20022 XML, Bizum business rails.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Spanish (es-ES), 5-digit postal code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["Agencia Tributaria (AEAT)", "Seguridad Social (TGSS)"],
    supportedSubnationalJurisdictions: ["ES_ALL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Ley 37/1992 del IVA and Real Decreto Legislativo 8/2015 LGSS.",
  },

  IT: {
    roadmapPriority: 12,
    countryCode: "IT",
    countryName: "Italy",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Italian IVA standard 22%, reduced 10%/5%/4%, EU OSS, Article 196 reverse charge.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "INPS (9.19% employee, 30.0% employer), TFR statutory severance accrual (6.91%), IRPEF withholding.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, Codice Civile / OIC principles.",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "Sistema di Interscambio (SdI) mandatory FatturaPA XML transmission.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "SEPA ISO 20022 XML, RiBa (Ricevuta Bancaria).",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Italian (it-IT), 5-digit CAP postal code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Euro (€ EUR), 2 decimals.",
      },
    },
    statutoryAuthorities: ["Agenzia delle Entrate", "INPS", "INAIL"],
    supportedSubnationalJurisdictions: ["IT_ALL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against D.P.R. 633/1972 and Legge di Bilancio 2026.",
  },

  PL: {
    roadmapPriority: 13,
    countryCode: "PL",
    countryName: "Poland",
    primaryCurrency: "PLN",
    overallStatus: "Implemented and tested",
    subsystems: {
      tax: {
        status: "Implemented and tested",
        notes: "Polish VAT standard 23%, reduced 8%/5%, EU OSS, Article 196 reverse charge.",
      },
      payroll: {
        status: "Implemented and tested",
        notes: "ZUS social contributions (13.71% employee, 20.38% employer), PIT 12% progressive withholding.",
      },
      accounting: {
        status: "Implemented and tested",
        notes: "PLN 2-decimal ledger, Polish Accounting Act (Ustawa o rachunkowości).",
      },
      eInvoicing: {
        status: "Implemented and tested",
        notes: "Krajowy System e-Faktur (KSeF) mandatory national structured e-invoicing schema.",
      },
      paymentIntegration: {
        status: "Implemented and tested",
        notes: "Elixir clearing and SEPA ISO 20022 XML.",
      },
      languageAndLocale: {
        status: "Implemented and tested",
        notes: "Polish (pl-PL), 5-digit format 00-000 postal code validator.",
      },
      currencyAndFormatting: {
        status: "Implemented and tested",
        notes: "Polish Zloty (zł PLN), 2 decimals.",
      },
    },
    statutoryAuthorities: ["Krajowa Administracja Skarbowa (KAS)", "Zakład Ubezpieczeń Społecznych (ZUS)"],
    supportedSubnationalJurisdictions: ["PL_ALL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Ustawa o VAT and Ustawa o systemie ubezpieczeń społecznych.",
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
