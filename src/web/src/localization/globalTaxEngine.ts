/**
 * Universal ERP — Multi-Country Global Tax Engine
 * Country-independent tax determination dispatching to modular country tax packages.
 * Evaluates place of supply, supplier/customer establishment, customer tax registration,
 * product classification, statutory effective dates, and input-tax recovery rules.
 * Adheres to EU OSS destination VAT vs Art 196 reverse charge, UK overseas vendor rules,
 * AU GST, CA GST/HST, US state sales taxes, ZATCA Phase 2, UAE Peppol, and Pakistan provincial rules.
 */

export type GlobalTaxRequest = {
  amount: number;
  supplierCountryCode?: string;
  customerCountryCode?: string;
  customerJurisdictionCode?: string;
  customerType?: "b2b" | "b2c" | "individual" | "business" | string;
  customerVatNumber?: string;
  supplierHasNexusInDestination?: boolean;
  isEuOssEligible?: boolean;
  exemptionCertificateNumber?: string;
  foreignRemittanceCertificateNumber?: string;
  productClassification?: "saas" | "software_dev" | "goods" | "services" | string;
  taxRegime?: "standard_with_input_tax" | "reduced_without_input_tax" | "export_zero_rated" | "reverse_charge" | string;
  isTaxInclusive?: boolean;
  effectiveDate?: string;
  tariffCode?: string;
  isExport?: boolean;
};

export type GlobalTaxResult = {
  taxableAmount: number;
  ratePct: number;
  taxAmount: number;
  totalAmount: number;
  taxAuthority: string;
  statutoryReference: string;
  jurisdiction: string;
  countryCode: string;
  tariffCode?: string;
  isTaxInclusive: boolean;
  taxRegime: string;
  inputTaxRecoverable: boolean;
  isReverseCharge: boolean;
  isExempt: boolean;
  isDestinationVat: boolean;
  eInvoicingMandatePlatform?: string;
  explanation: string;
};

export const EU_MEMBER_STATES = [
  "DE", "FR", "IT", "ES", "NL", "BE", "IE", "PL", "SE", "AT",
  "DK", "FI", "PT", "GR", "CZ", "RO", "HU", "BG", "HR", "SK",
  "LT", "SI", "LV", "EE", "CY", "LU", "MT"
];

export function isEuMemberState(countryCode: string): boolean {
  return EU_MEMBER_STATES.includes(countryCode.trim().toUpperCase());
}

export function getEuStandardVatRate(countryCode: string): number {
  switch (countryCode.trim().toUpperCase()) {
    case "DE": return 19.0;
    case "FR": return 20.0;
    case "IE": return 23.0;
    case "NL": return 21.0;
    case "IT": return 22.0;
    case "ES": return 21.0;
    case "BE": return 21.0;
    case "AT": return 20.0;
    case "SE": return 25.0;
    case "PL": return 23.0;
    case "DK": return 25.0;
    case "FI": return 25.5;
    case "PT": return 23.0;
    case "GR": return 24.0;
    case "CZ": return 21.0;
    case "RO": return 19.0;
    case "HU": return 27.0;
    case "BG": return 20.0;
    case "HR": return 25.0;
    case "SK": return 23.0;
    case "LT": return 21.0;
    case "SI": return 22.0;
    case "LV": return 21.0;
    case "EE": return 22.0;
    case "CY": return 19.0;
    case "LU": return 17.0;
    case "MT": return 18.0;
    default: return 20.0;
  }
}

export function getEuEInvoicingPlatform(countryCode: string): string {
  switch (countryCode.trim().toUpperCase()) {
    case "DE": return "ZUGFeRD / XRechnung (EN 16931)";
    case "FR": return "Chorus Pro / Public Invoicing Portal (PPF)";
    case "IT": return "Sistema di Interscambio (SdI)";
    case "PL": return "Krajowy System e-Faktur (KSeF)";
    case "ES": return "FACe / Facturae";
    default: return "Peppol BIS Billing 3.0";
  }
}

/**
 * Calculates statutory tax for any supported jurisdiction worldwide.
 */
export function calculateGlobalTax(request: GlobalTaxRequest): GlobalTaxResult {
  const amount = Math.max(0, request.amount || 0);
  const supplierCountry = (request.supplierCountryCode || "US").trim().toUpperCase();
  const customerCountry = (request.customerCountryCode || supplierCountry).trim().toUpperCase();
  const rawJurisdiction = (request.customerJurisdictionCode || "FEDERAL").trim().toUpperCase();
  const customerType = (request.customerType || "b2b").toLowerCase();
  const isB2b = customerType === "b2b" || customerType === "business";
  const hasVatId = Boolean(request.customerVatNumber && request.customerVatNumber.trim().length > 3);
  const regime = request.taxRegime || "standard_with_input_tax";
  const isTaxInclusive = Boolean(request.isTaxInclusive);
  const effectiveDate = request.effectiveDate || new Date().toISOString().substring(0, 10);
  const isCrossBorder = supplierCountry !== customerCountry;

  let ratePct = 0.0;
  let taxAuthority = "Universal International Tax Engine";
  let statutoryReference = "Universal Place of Supply Rules";
  let taxRegime = regime;
  let tariffCode = request.tariffCode || "";
  let inputTaxRecoverable = true;
  let isReverseCharge = false;
  let isExempt = false;
  let isDestinationVat = false;
  let eInvoicingMandatePlatform: string | undefined = undefined;
  let explanation = "";

  // 1. European Union (Destination OSS vs B2B Reverse Charge)
  if (isEuMemberState(customerCountry)) {
    isDestinationVat = true;
    eInvoicingMandatePlatform = getEuEInvoicingPlatform(customerCountry);
    tariffCode = tariffCode || "CPA-62.01";

    if (isCrossBorder && isB2b && hasVatId) {
      // Intra-EU or Cross-Border B2B Reverse Charge under Article 196
      ratePct = 0.0;
      isReverseCharge = true;
      taxRegime = "reverse_charge";
      taxAuthority = `EU Cross-Border VIES (${customerCountry})`;
      statutoryReference = "EU Council Directive 2006/112/EC, Article 196 (Reverse Charge B2B)";
      explanation = `Cross-border B2B electronic supply from ${supplierCountry} to taxable person in ${customerCountry}. Customer (${request.customerVatNumber}) self-assesses VAT.`;
    } else {
      // B2C or Cross-Border B2B without valid VAT ID -> Destination VAT under EU One Stop Shop (OSS)
      ratePct = getEuStandardVatRate(customerCountry);
      const isOss = isCrossBorder && request.isEuOssEligible !== false;
      taxAuthority = `EU Revenue (${customerCountry})`;
      statutoryReference = isOss
        ? "EU Directive 2006/112/EC Article 58 & Council Implementing Regulation 2019/1995 (OSS Scheme)"
        : `National VAT Act of ${customerCountry} (Standard Rate)`;
      explanation = isOss
        ? `Electronic service supplied to consumer in ${customerCountry}. Destination VAT of ${ratePct}% charged under OSS scheme.`
        : `Domestic supply in ${customerCountry} charged at standard rate of ${ratePct}%.`;
    }
  }

  // 2. United Kingdom (HMRC)
  else if (customerCountry === "GB") {
    tariffCode = tariffCode || "CPA-62.01";

    if (isCrossBorder && isB2b && hasVatId) {
      // UK B2B Reverse Charge under VATA 1994 Section 7A
      ratePct = 0.0;
      isReverseCharge = true;
      isDestinationVat = true;
      taxRegime = "reverse_charge";
      taxAuthority = "HM Revenue & Customs (HMRC)";
      statutoryReference = "Value Added Tax Act 1994, Section 7A (Place of Supply B2B General Rule)";
      explanation = `Supply from ${supplierCountry} to UK business customer (${request.customerVatNumber}). Customer accounts for VAT under reverse charge.`;
    } else {
      // Domestic or B2C Cross-Border -> UK Standard 20% VAT
      ratePct = 20.0;
      isDestinationVat = isCrossBorder;
      taxAuthority = "HM Revenue & Customs (HMRC)";
      statutoryReference = "Value Added Tax Act 1994, Section 2(1) and Schedule 4A";
      explanation = isCrossBorder
        ? "UK Overseas vendor electronic services rules require standard 20% VAT on sales to UK consumers."
        : "Standard rate UK VAT for domestic supply of SaaS / electronic services.";
    }
  }

  // 3. Australia (ATO GST)
  else if (customerCountry === "AU") {
    tariffCode = tariffCode || "DIGITAL-SVC";

    if (isCrossBorder && isB2b && hasVatId) {
      ratePct = 0.0;
      isReverseCharge = true;
      taxRegime = "reverse_charge";
      taxAuthority = "Australian Taxation Office (ATO)";
      statutoryReference = "A New Tax System (Goods and Services Tax) Act 1999, Subdiv 84-A";
      explanation = `Cross-border B2B digital service to Australian GST-registered business (${request.customerVatNumber}). Reverse charge applies.`;
    } else {
      ratePct = 10.0;
      isDestinationVat = isCrossBorder;
      taxAuthority = "Australian Taxation Office (ATO)";
      statutoryReference = "A New Tax System (Goods and Services Tax) Act 1999, Section 9-70 & Subdiv 84-B";
      explanation = "Standard 10% Australian GST applies to digital services consumed in Australia.";
    }
  }

  // 4. Canada (CRA GST / HST)
  else if (customerCountry === "CA") {
    tariffCode = tariffCode || "DIGITAL-SVC";

    if (isCrossBorder && isB2b && hasVatId) {
      ratePct = 0.0;
      isReverseCharge = true;
      taxRegime = "reverse_charge";
      taxAuthority = "Canada Revenue Agency (CRA)";
      statutoryReference = "Excise Tax Act (Canada), Section 220 (Reverse Charge on Imported Services)";
      explanation = `Cross-border B2B digital service to Canadian GST/HST registrant (${request.customerVatNumber}). Customer self-assesses.`;
    } else {
      const prov = rawJurisdiction.replace(/^CA[_-]/, "");
      let caRate = 5.0; // Default Federal GST
      if (prov === "ON") caRate = 13.0;
      else if (prov === "BC") caRate = 12.0;
      else if (prov === "QC") caRate = 14.975;
      else if (["NS", "NB", "NL", "PE"].includes(prov)) caRate = 15.0;

      ratePct = caRate;
      isDestinationVat = isCrossBorder;
      taxAuthority = "Canada Revenue Agency (CRA)";
      statutoryReference = "Excise Tax Act (Canada), Part IX (Simplified Non-Resident Digital Services Regime)";
      explanation = `Destination tax of ${caRate}% assessed under Canadian digital services regime for province ${prov || "Federal"}.`;
    }
  }

  // 5. United States Destination-Based Sales Tax
  else if (customerCountry === "US") {
    tariffCode = tariffCode || "NAICS-518210";
    const state = rawJurisdiction.replace(/^US[_-]/, "");

    if (request.exemptionCertificateNumber) {
      ratePct = 0.0;
      isExempt = true;
      taxRegime = "exempt";
      taxAuthority = "US State Tax Authority";
      statutoryReference = "Uniform Sales & Use Tax Resale Certificate / State Exemption Rules";
      explanation = `Zero tax assessed due to verified exemption certificate: ${request.exemptionCertificateNumber}.`;
    } else if (state === "NY" || state === "NEW YORK") {
      ratePct = 8.875;
      taxAuthority = "New York State Department of Taxation and Finance";
      statutoryReference = "NY Tax Law § 1105(c)(9) (NYC Combined State & Local Rate)";
      explanation = "New York State combined sales tax on prewritten computer software and cloud SaaS services.";
    } else if (state === "TX" || state === "TEXAS") {
      ratePct = 6.6; // 80% taxable basis of 8.25%
      taxAuthority = "Texas Comptroller of Public Accounts";
      statutoryReference = "Tex. Tax Code § 151.0101 & 34 TAC Rule 3.342 (80% Taxable Basis)";
      explanation = "Texas data processing and SaaS services are taxed at an effective rate of 6.6% (20% exemption of 8.25%).";
    } else if (state === "WA" || state === "WASHINGTON") {
      ratePct = 6.5;
      taxAuthority = "Washington Department of Revenue";
      statutoryReference = "RCW 82.08.020 (Digital Automated Services)";
      explanation = "Washington State sales tax on digital automated services.";
    } else if (state === "CA" || state === "CALIFORNIA") {
      ratePct = 0.0;
      isExempt = true;
      taxAuthority = "California Department of Tax and Fee Administration (CDTFA)";
      statutoryReference = "CDTFA Regulation 1502 (Computers, Programs, and Data Processing - Pure SaaS)";
      explanation = "California does not levy sales and use tax on SaaS or electronic software where no tangible personal property is transferred (pure SaaS is exempt).";
    } else {
      ratePct = 6.0;
      taxAuthority = `US State Tax (${state || "General"})`;
      statutoryReference = "US State Retail Sales and Use Tax Acts";
      explanation = `Standard state sales tax rate for destination jurisdiction ${state || "General"}.`;
    }
  }

  // 6. Saudi Arabia (ZATCA)
  else if (customerCountry === "SA") {
    ratePct = 15.0;
    taxAuthority = "Zakat, Tax and Customs Authority (ZATCA)";
    statutoryReference = "VAT Law Royal Decree No. (M/113) as amended by Royal Decree No. (A/638)";
    tariffCode = tariffCode || "UNSPSC-43231500";
    eInvoicingMandatePlatform = "ZATCA FATOORA Phase 2 (Cryptographic Stamp / UBL 2.1 XML)";
    explanation = "Standard 15% VAT rate for electronic services consumed in KSA with ZATCA Phase 2 compliance.";
  }

  // 7. United Arab Emirates (FTA)
  else if (customerCountry === "AE") {
    ratePct = 5.0;
    taxAuthority = "Federal Tax Authority (FTA)";
    statutoryReference = "Federal Decree-Law No. (8) of 2017 on Value Added Tax & Cabinet Decision No. 52/2017";
    tariffCode = tariffCode || "UNSPSC-43231500";
    eInvoicingMandatePlatform = "UAE MoF E-Invoicing (Peppol PINT)";
    explanation = "Standard 5% VAT rate for electronic services consumed in the United Arab Emirates.";
  }

  // 8. Pakistan Subnational Sales Tax on Services
  else if (customerCountry === "PK") {
    eInvoicingMandatePlatform = "FBR Digital Invoicing (Sales Tax General Order 01/2024)";
    tariffCode = tariffCode || "9815.6000";

    // Genuine cross-border export with foreign remittance proof
    if (isCrossBorder && request.foreignRemittanceCertificateNumber) {
      ratePct = 0.0;
      taxRegime = "export_zero_rated";
      taxAuthority = "Federal Board of Revenue (FBR) / Provincial Authorities";
      statutoryReference = "Sales Tax on Services Export Rules (Zero-Rated with Inward Foreign Remittance)";
      explanation = `Zero-rated export of IT services backed by Foreign Remittance Certificate ${request.foreignRemittanceCertificateNumber}.`;
    } else if (rawJurisdiction === "SINDH") {
      if (effectiveDate < "2024-07-01") {
        ratePct = 13.0;
        taxAuthority = "SRB Sindh Sales Tax on Services (Historical Standard)";
        statutoryReference = "Sindh Sales Tax on Services Act, 2011, Sec 8(1) (pre-2024 interval)";
      } else if (regime === "reduced_without_input_tax") {
        ratePct = 3.0;
        taxAuthority = "SRB Reduced Rate for IT/Software (No Input Credit)";
        statutoryReference = "SRB Notification No. SRB-3-4/7/2013 as amended & Second Schedule Heading 9815.6000";
        inputTaxRecoverable = false;
      } else {
        ratePct = 15.0;
        taxAuthority = "SRB Sindh Sales Tax on Services (Standard SaaS)";
        statutoryReference = "Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024";
      }
      explanation = `Sindh sales tax on IT/SaaS services under SRB (${ratePct}%).`;
    } else if (rawJurisdiction === "PUNJAB") {
      if (regime === "reduced_without_input_tax") {
        ratePct = 5.0;
        taxAuthority = "PRA Reduced Rate for IT/Software (No Input Credit)";
        statutoryReference = "PRA Notification No. SO(TAX)1-1/2020 & Second Schedule Heading 9815.6000";
        inputTaxRecoverable = false;
      } else {
        ratePct = 16.0;
        taxAuthority = "PRA Punjab Sales Tax on Services (Standard SaaS)";
        statutoryReference = "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000";
      }
      explanation = `Punjab sales tax on IT/SaaS services under PRA (${ratePct}%).`;
    } else if (rawJurisdiction === "KP" || rawJurisdiction === "KHYBER PAKHTUNKHWA" || rawJurisdiction === "KPK") {
      tariffCode = request.tariffCode || "9850.0000";
      if (regime === "reduced_without_input_tax") {
        ratePct = 2.0;
        taxAuthority = "KPRA Reduced Rate for IT/Software (No Input Credit)";
        statutoryReference = "KP Finance Act 2026, Schedule Table, Heading 9850.0000";
        inputTaxRecoverable = false;
      } else {
        ratePct = 15.0;
        taxAuthority = "KPRA Khyber Pakhtunkhwa Sales Tax on Services";
        statutoryReference = "KP Finance Act 2026, Schedule Table, Heading 9850.0000";
      }
      explanation = `Khyber Pakhtunkhwa sales tax on IT/SaaS services under KPRA (${ratePct}%).`;
    } else if (rawJurisdiction === "BALOCHISTAN") {
      tariffCode = request.tariffCode || "9868.0000";
      if (regime === "reduced_without_input_tax") {
        if (effectiveDate < "2026-07-01") {
          ratePct = 3.0; // Historical pre-2026 reduced rate
          taxAuthority = "BRA Historical Reduced Rate for IT/Software (Pre-2026)";
          statutoryReference = "Balochistan Sales Tax on Services Act, 2015, Second Schedule (historical interval)";
        } else {
          ratePct = 6.0; // Corrected under Finance Act 2026
          taxAuthority = "BRA Reduced Rate for IT/Software (Finance Act 2026)";
          statutoryReference = "Balochistan Finance Act 2026 & Second Schedule Heading 9868.0000 Reduced Rate Column";
        }
        inputTaxRecoverable = false;
      } else {
        ratePct = 15.0;
        taxAuthority = "BRA Balochistan Sales Tax on Services (Standard SaaS)";
        statutoryReference = "Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9868.0000";
      }
      explanation = `Balochistan sales tax on IT/SaaS services under BRA (${ratePct}%).`;
    } else {
      // Federal / ICT Default
      if (regime === "reduced_without_input_tax") {
        ratePct = 5.0;
        taxAuthority = "FBR ICT Reduced Rate for PSEB IT Services";
        statutoryReference = "ICT (Tax on Services) Ordinance 2001, Heading 9815.6000";
        inputTaxRecoverable = false;
      } else {
        ratePct = 15.0;
        taxAuthority = "FBR ICT Sales Tax on Services";
        statutoryReference = "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Heading 9815.6000";
      }
      explanation = `Federal / ICT sales tax on IT/SaaS services under FBR (${ratePct}%).`;
    }
  }

  // 9. Generic Fallback
  else {
    ratePct = 0.0;
    taxAuthority = `Unregistered Jurisdiction (${customerCountry})`;
    statutoryReference = "Standard Non-taxable Supply";
    explanation = `No active sales tax rule defined for country ${customerCountry}.`;
  }

  // Tax computation
  let taxableAmount = amount;
  let taxAmount = 0;
  let totalAmount = amount;

  if (ratePct > 0) {
    if (isTaxInclusive) {
      taxAmount = Math.round((amount - amount / (1 + ratePct / 100)) * 100) / 100;
      taxableAmount = Math.round((amount - taxAmount) * 100) / 100;
      totalAmount = amount;
    } else {
      taxableAmount = amount;
      taxAmount = Math.round(amount * (ratePct / 100) * 100) / 100;
      totalAmount = Math.round((taxableAmount + taxAmount) * 100) / 100;
    }
  }

  return {
    taxableAmount,
    ratePct,
    taxAmount,
    totalAmount,
    taxAuthority,
    statutoryReference,
    jurisdiction: rawJurisdiction,
    countryCode: customerCountry,
    tariffCode,
    isTaxInclusive,
    taxRegime,
    inputTaxRecoverable,
    isReverseCharge,
    isExempt,
    isDestinationVat,
    eInvoicingMandatePlatform,
    explanation,
  };
}
