/**
 * Universal ERP — Authoritative Country Localization Coverage Matrix
 * System-wide regulatory and statutory capability matrix categorizing countries into:
 * 1. "Implemented and tested"
 * 2. "Partially implemented"
 * 3. "Architecture configured only"
 * 4. "Not yet supported"
 * 5. "Awaiting regulatory verification"
 *
 * Roadmap order:
 * 1. Pakistan, 2. United States, 3. United Kingdom, 4. European Union (EU-27),
 * 5. UAE, 6. Saudi Arabia, 7. Canada, 8. Australia, 9. India, 10. Future roadmap countries.
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

export type CountryCoverageEntry = {
  roadmapPriority: number;
  countryCode: string;
  countryName: string;
  primaryCurrency: string;
  overallStatus: CoverageStatus;
  domains: {
    generalLedgerAndCurrencies: DomainCapability;
    salesTaxAndVat: DomainCapability;
    payrollAndWithholding: DomainCapability;
    eInvoicingAndReporting: DomainCapability;
    bankingAndPayments: DomainCapability;
  };
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
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "Full PKR ledger, South Asian lakh/crore number formatting, custom fiscal years (July-June).",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "SRB 15%/3%, PRA 16%/5%, KPRA 15%/2%, BRA 15%/6% (Finance Act 2026, input credit barred), ICT 15%/5%, export zero-rating.",
      },
      payrollAndWithholding: {
        status: "Implemented and tested",
        notes: "FBR Tax Year 2027 8-tier salaried slabs, EOBI (PKR 37,000 base), SESSI/PESSI 5% on PKR 40k ceiling.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "FBR Digital Invoicing integration under Sales Tax General Order 01/2024.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "Raast instant payments, 1Link, PRAL banking clearing support.",
      },
    },
    statutoryAuthorities: ["FBR", "SRB", "PRA", "KPRA", "BRA", "EOBI"],
    supportedSubnationalJurisdictions: ["SINDH", "PUNJAB", "KP", "BALOCHISTAN", "FEDERAL"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Finance Act 2026 and official provincial statutory notifications.",
  },

  US: {
    roadmapPriority: 2,
    countryCode: "US",
    countryName: "United States",
    primaryCurrency: "USD",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "USD 2-decimal ledger, calendar fiscal year (Jan-Dec), imperial units.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Destination-based sales tax: NY (8.875%), TX (6.6% via 80% taxable base), WA (6.5%), CA (pure SaaS exempt), resale cert support.",
      },
      payrollAndWithholding: {
        status: "Implemented and tested",
        notes: "2026 OASDI cap ($184,500), Medicare 1.45% + Additional 0.9% over $200k/$250k, 2026 FIT schedules (Single/MFJ/HOH), FUTA 0.6%, SUTA.",
      },
      eInvoicingAndReporting: {
        status: "Architecture configured only",
        notes: "BPC Digital Invoicing / Peppol US pilot architecture configured.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "ACH (NACHA) file generation, Fedwire, Stripe sandbox integration.",
      },
    },
    statutoryAuthorities: ["IRS", "SSA", "NYS_TAX", "TX_COMPTROLLER", "CDTFA", "WA_DOR"],
    supportedSubnationalJurisdictions: ["NY", "TX", "WA", "CA", "IL"],
    activeVersion: "2026.1.0",
    verificationEvidence: "Verified against IRS Publication 15-T (2026) and SSA 2026 Social Security changes.",
  },

  GB: {
    roadmapPriority: 3,
    countryCode: "GB",
    countryName: "United Kingdom",
    primaryCurrency: "GBP",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "GBP 2-decimal ledger, UK tax year (6 April - 5 April).",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Standard 20% VAT, cross-border B2B reverse charge (VATA 1994 s7A), overseas vendor digital rules.",
      },
      payrollAndWithholding: {
        status: "Implemented and tested",
        notes: "2026-27 Employer Class 1 NIC (15.0%, £417/mo secondary threshold), categories (A/B/C/H/M/J), Scottish 6-band PAYE, Student Loans (1/2/4/5/PG), Pension Auto-Enrolment.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "HMRC Making Tax Digital (MTD) for VAT compliance.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "BACS, Faster Payments Service (FPS), CHAPS clearing.",
      },
    },
    statutoryAuthorities: ["HMRC", "TPR", "SLC"],
    supportedSubnationalJurisdictions: ["ENGLAND", "SCOTLAND", "WALES", "NI"],
    activeVersion: "2026.2.0",
    verificationEvidence: "Verified against Autumn Budget 2024 National Insurance reforms and Scottish Income Tax 2026/27.",
  },

  DE: {
    roadmapPriority: 4,
    countryCode: "DE",
    countryName: "Germany (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, GoBD principles, metric system.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "German MwSt 19%, EU OSS Destination VAT, Intra-EU B2B Reverse Charge (Directive 2006/112/EC Art 196).",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "Lohnsteuer & Sozialversicherung architecture defined in roadmap.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "ZUGFeRD 2.2 / XRechnung (EN 16931) mandatory e-invoicing architecture.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "SEPA Credit Transfer (SCT) and SEPA Direct Debit (SDD) XML ISO 20022.",
      },
    },
    statutoryAuthorities: ["Bundeszentralamt für Steuern (BZSt)", "Finanzamt"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against German UStG § 12 and B2B E-Invoicing Mandate 2025/2026.",
  },

  FR: {
    roadmapPriority: 4,
    countryCode: "FR",
    countryName: "France (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, Plan Comptable Général (PCG).",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "French TVA 20%, EU OSS Destination VAT, Intra-EU Art 196 Reverse Charge.",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "Bulletin de paie & URSSAF architecture configured.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "Chorus Pro / Portail Public de Facturation (PPF) integration.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "SEPA ISO 20022 XML formats.",
      },
    },
    statutoryAuthorities: ["Direction Générale des Finances Publiques (DGFiP)", "URSSAF"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against French CGI Art 278 and PPF mandate.",
  },

  IE: {
    roadmapPriority: 4,
    countryCode: "IE",
    countryName: "Ireland (European Union)",
    primaryCurrency: "EUR",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "EUR 2-decimal ledger, calendar fiscal year.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Irish standard VAT 23%, EU OSS, Intra-EU Art 196 Reverse Charge.",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "PAYE Modernisation architecture configured.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "Peppol BIS Billing 3.0 e-invoicing.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "SEPA SCT/SDD clearing.",
      },
    },
    statutoryAuthorities: ["Irish Revenue Commissioners"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against Irish VAT Consolidation Act 2010.",
  },

  EU_OTHER: {
    roadmapPriority: 4,
    countryCode: "EU",
    countryName: "Other EU Member States (24 Countries)",
    primaryCurrency: "EUR",
    overallStatus: "Partially implemented",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "Multi-currency support for EUR and non-EUR members (SEK, PLN, DKK, CZK, HUF, RON, BGN).",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Standard rates for all 27 EU member states, EU One Stop Shop (OSS) destination VAT, and Article 196 Reverse Charge.",
      },
      payrollAndWithholding: {
        status: "Not yet supported",
        notes: "Member-state specific payroll modules planned for future phases.",
      },
      eInvoicingAndReporting: {
        status: "Partially implemented",
        notes: "Italy SdI, Poland KSeF, Spain FACe, and Peppol BIS 3.0 architecture mapped.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "SEPA Single Euro Payments Area ISO 20022 XML.",
      },
    },
    statutoryAuthorities: ["European Commission / Member State Tax Authorities"],
    activeVersion: "2026.1",
    verificationEvidence: "Complies with EU Council Directive 2006/112/EC and OSS Regulation 2019/1995.",
  },

  AE: {
    roadmapPriority: 5,
    countryCode: "AE",
    countryName: "United Arab Emirates",
    primaryCurrency: "AED",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "AED 2-decimal ledger, bilingual Arabic/English, Islamic & Gregorian calendars.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Standard 5% VAT under Federal Decree-Law No. (8) of 2017, FTA tax invoice generation.",
      },
      payrollAndWithholding: {
        status: "Implemented and tested",
        notes: "0% personal income tax, GPSSA pension (5% EE, 12.5% ER) for UAE nationals, MOHRE End of Service Gratuity (EOSG) monthly accrual.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "UAE MoF E-Invoicing system using Peppol PINT standard.",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "WPS (Wages Protection System) SIF file generation, UAE Central Bank IPP.",
      },
    },
    statutoryAuthorities: ["Federal Tax Authority (FTA)", "MOHRE", "GPSSA"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against FTA VAT Law and MOHRE Labour Law Decree No. 33 of 2021.",
  },

  SA: {
    roadmapPriority: 6,
    countryCode: "SA",
    countryName: "Saudi Arabia",
    primaryCurrency: "SAR",
    overallStatus: "Implemented and tested",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "SAR 2-decimal ledger, Hijri/Umm al-Qura calendar support, Arabic RTL typography.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Standard 15% VAT under Royal Decree No. M/113.",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "GOSI (General Organization for Social Insurance) rules configured in roadmap.",
      },
      eInvoicingAndReporting: {
        status: "Implemented and tested",
        notes: "ZATCA FATOORA Phase 2 (Cryptographic Stamp, ECDSA secp256k1, QR Code, UBL 2.1 XML).",
      },
      bankingAndPayments: {
        status: "Implemented and tested",
        notes: "SARIE instant payments, Mada, Saudi National WPS.",
      },
    },
    statutoryAuthorities: ["Zakat, Tax and Customs Authority (ZATCA)", "GOSI"],
    activeVersion: "2026.1",
    verificationEvidence: "Verified against ZATCA Phase 2 E-Invoicing Technical Specifications.",
  },

  CA: {
    roadmapPriority: 7,
    countryCode: "CA",
    countryName: "Canada",
    primaryCurrency: "CAD",
    overallStatus: "Partially implemented",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "CAD 2-decimal ledger, bilingual English/French support.",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "Non-resident digital services regime: ON HST 13%, BC 12%, QC QST 14.975%, Atlantic 15%, Federal 5%, B2B reverse charge.",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "CPP/EI & TD1 federal tax credit architecture seeded under Migration 041.",
      },
      eInvoicingAndReporting: {
        status: "Architecture configured only",
        notes: "CRA digital reporting architecture configured.",
      },
      bankingAndPayments: {
        status: "Partially implemented",
        notes: "Interac & EFT 80-byte / 1464-byte batch files mapped.",
      },
    },
    statutoryAuthorities: ["Canada Revenue Agency (CRA)", "Revenu Québec"],
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
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "AUD 2-decimal ledger, Australian fiscal year (1 July - 30 June).",
      },
      salesTaxAndVat: {
        status: "Implemented and tested",
        notes: "ATO standard 10% GST, B2C inbound digital supplies (Subdiv 84-B), B2B reverse charge (Subdiv 84-A).",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "Superannuation Guarantee (12%) & PAYG withholding architecture seeded under Migration 041.",
      },
      eInvoicingAndReporting: {
        status: "Partially implemented",
        notes: "ATO Peppol E-Invoicing framework.",
      },
      bankingAndPayments: {
        status: "Partially implemented",
        notes: "NPP (New Payments Platform) / PayID & BECS Direct Entry file format.",
      },
    },
    statutoryAuthorities: ["Australian Taxation Office (ATO)"],
    supportedSubnationalJurisdictions: ["NSW", "VIC", "QLD", "WA", "SA"],
    activeVersion: "0.1.0-draft",
    verificationEvidence: "Verified against A New Tax System (Goods and Services Tax) Act 1999.",
  },

  IN: {
    roadmapPriority: 9,
    countryCode: "IN",
    countryName: "India",
    primaryCurrency: "INR",
    overallStatus: "Architecture configured only",
    domains: {
      generalLedgerAndCurrencies: {
        status: "Implemented and tested",
        notes: "INR 2-decimal ledger, South Asian numbering format, fiscal year (1 April - 31 March).",
      },
      salesTaxAndVat: {
        status: "Architecture configured only",
        notes: "Dual GST (CGST + SGST / IGST) and OIDAR non-resident digital services tax rules configured.",
      },
      payrollAndWithholding: {
        status: "Architecture configured only",
        notes: "EPF (12%), ESI, Professional Tax, and Section 192 TDS architecture seeded.",
      },
      eInvoicingAndReporting: {
        status: "Architecture configured only",
        notes: "NIC E-Invoice Portal / Invoice Registration Portal (IRP) architecture mapped.",
      },
      bankingAndPayments: {
        status: "Partially implemented",
        notes: "UPI, NEFT, RTGS rails mapped.",
      },
    },
    statutoryAuthorities: ["Central Board of Indirect Taxes and Customs (CBIC)", "EPFO", "Income Tax Department"],
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
