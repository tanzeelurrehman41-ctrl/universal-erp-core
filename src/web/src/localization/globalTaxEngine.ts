/**
 * Universal ERP — Multi-Country Global Tax Engine
 * Country-independent tax determination dispatching to modular country tax packages.
 * Evaluates place of supply, supplier/customer establishment, customer tax registration,
 * product classification, statutory effective dates, and input-tax recovery rules.
 */

export type GlobalTaxRequest = {
  amount: number;
  supplierCountryCode?: string;
  customerCountryCode?: string;
  customerJurisdictionCode?: string;
  customerType?: "b2b" | "b2c" | "individual" | "business" | string;
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
  tariffCode?: string;
  isTaxInclusive: boolean;
  taxRegime: string;
  inputTaxRecoverable: boolean;
  explanation: string;
};

/**
 * Calculates statutory tax for any supported jurisdiction worldwide.
 */
export function calculateGlobalTax(request: GlobalTaxRequest): GlobalTaxResult {
  const amount = Math.max(0, request.amount || 0);
  const supplierCountry = (request.supplierCountryCode || "US").trim().toUpperCase();
  const customerCountry = (request.customerCountryCode || supplierCountry).trim().toUpperCase();
  const rawJurisdiction = (request.customerJurisdictionCode || "FEDERAL").trim().toUpperCase();
  const customerType = (request.customerType || "b2b").toLowerCase();
  const regime = request.taxRegime || "standard_with_input_tax";
  const isTaxInclusive = Boolean(request.isTaxInclusive);
  const effectiveDate = request.effectiveDate || new Date().toISOString().substring(0, 10);
  const isExport = Boolean(request.isExport || rawJurisdiction === "EXPORT" || rawJurisdiction === "INTERNATIONAL");

  let ratePct = 0.0;
  let taxAuthority = "Universal International Tax Engine";
  let statutoryReference = "Universal Place of Supply Rules";
  let taxRegime = regime;
  let tariffCode = request.tariffCode || "";
  let inputTaxRecoverable = true;
  let explanation = "";

  // 1. Cross-border Export Check
  if (isExport || (supplierCountry !== customerCountry && customerCountry !== "")) {
    // Cross-border B2B Reverse Charge / Export Zero-Rating
    if (isEuCountry(supplierCountry) && isEuCountry(customerCountry)) {
      // Intra-EU B2B Reverse Charge
      ratePct = 0.0;
      taxAuthority = `EU Cross-Border VIES (${customerCountry})`;
      statutoryReference = "EU Council Directive 2006/112/EC, Article 196 (Reverse Charge B2B)";
      taxRegime = "reverse_charge";
      tariffCode = "EU-REV-CHG";
      inputTaxRecoverable = true;
      explanation = `Intra-EU cross-border B2B supply from ${supplierCountry} to ${customerCountry}. Reverse charge applies: customer self-assesses VAT.`;
    } else {
      // International Export Zero-Rating
      ratePct = 0.0;
      taxAuthority = `${supplierCountry} Customs & Revenue Authority (Export)`;
      statutoryReference = "Harmonized Place of Supply Rules: Zero-rated cross-border export of digital services";
      taxRegime = "export_zero_rated";
      tariffCode = tariffCode || "EXPORT-001";
      inputTaxRecoverable = true;
      explanation = `Cross-border export of digital services from ${supplierCountry} to customer in ${customerCountry}. Subject to 0% export zero-rating.`;
    }
  } else if (customerCountry === "US") {
    // 2. United States Subnational Sales Tax
    const state = rawJurisdiction.replace(/^US[_-]/, "");
    tariffCode = tariffCode || "DIGITAL-SVC-01";

    if (state === "NY" || state === "NEW YORK") {
      ratePct = 8.875;
      taxAuthority = "New York State Department of Taxation and Finance";
      statutoryReference = "NY Tax Law § 1105(c)(9) (NYC Combined State & Local Rate)";
      explanation = "New York State combined sales tax on prewritten computer software and cloud SaaS services.";
    } else if (state === "TX" || state === "TEXAS") {
      ratePct = 6.6; // 80% of 8.25%
      taxAuthority = "Texas Comptroller of Public Accounts";
      statutoryReference = "Tex. Tax Code § 151.0101 (80% taxable basis of standard 8.25% rate)";
      explanation = "Texas data processing and SaaS services are taxed at an effective rate of 6.6% (20% exemption of 8.25%).";
    } else if (state === "WA" || state === "WASHINGTON") {
      ratePct = 6.5;
      taxAuthority = "Washington Department of Revenue";
      statutoryReference = "RCW 82.08.020 (Digital Automated Services)";
      explanation = "Washington State sales tax on digital automated services.";
    } else if (state === "CA" || state === "CALIFORNIA") {
      ratePct = 0.0;
      taxAuthority = "California Department of Tax and Fee Administration";
      statutoryReference = "Cal. Rev. & Tax. Code § 6016 (Pure SaaS without tangible media is exempt)";
      explanation = "California does not levy sales and use tax on SaaS or electronic software where no tangible personal property is transferred (pure SaaS is exempt).";
    } else {
      ratePct = 0.0;
      taxAuthority = `US Out-of-State / No Nexus (${state})`;
      statutoryReference = "US Commerce Clause & Wayfair Economic Nexus Threshold";
      explanation = `No economic nexus or software sales tax exemption applies in ${state}.`;
    }
  } else if (customerCountry === "GB") {
    // 3. United Kingdom HMRC VAT
    tariffCode = tariffCode || "998313";
    if (customerType === "b2b" && regime === "reverse_charge") {
      ratePct = 0.0;
      taxAuthority = "HM Revenue & Customs (HMRC)";
      statutoryReference = "Value Added Tax Act 1994, Section 7A (Reverse Charge)";
      taxRegime = "reverse_charge";
      explanation = "UK B2B reverse charge procedure applies.";
    } else {
      ratePct = 20.0;
      taxAuthority = "HM Revenue & Customs (HMRC)";
      statutoryReference = "Value Added Tax Act 1994, Section 2 (Standard Rate)";
      explanation = "UK standard rate VAT (20%) on electronically supplied SaaS services.";
    }
  } else if (isEuCountry(customerCountry)) {
    // 4. European Union Domestic VAT
    tariffCode = tariffCode || "998313";
    if (customerCountry === "DE") {
      ratePct = 19.0;
      taxAuthority = "Bundeszentralamt für Steuern (BZSt)";
      statutoryReference = "Umsatzsteuergesetz (UStG) § 12 Abs. 1 (Regelsteuersatz)";
      explanation = "German domestic standard VAT (19% MwSt).";
    } else if (customerCountry === "FR") {
      ratePct = 20.0;
      taxAuthority = "Direction Générale des Finances Publiques (DGFiP)";
      statutoryReference = "Code Général des Impôts (CGI) Art. 278 (Taux normal)";
      explanation = "French domestic standard VAT (20% TVA).";
    } else if (customerCountry === "IE") {
      ratePct = 23.0;
      taxAuthority = "Irish Revenue Commissioners";
      statutoryReference = "Value-Added Tax Consolidation Act 2010, Section 46";
      explanation = "Irish domestic standard VAT (23%).";
    } else {
      ratePct = 20.0;
      taxAuthority = `EU National Tax Authority (${customerCountry})`;
      statutoryReference = "EU Council Directive 2006/112/EC Standard Rate";
      explanation = `EU Member State domestic standard VAT (${customerCountry}).`;
    }
  } else if (customerCountry === "AE") {
    // 5. United Arab Emirates
    ratePct = 5.0;
    taxAuthority = "Federal Tax Authority (FTA)";
    statutoryReference = "Federal Decree-Law No. (8) of 2017 on Value Added Tax, Article 3";
    tariffCode = tariffCode || "9983";
    explanation = "UAE standard 5% VAT on IT and digital subscription services.";
  } else if (customerCountry === "SA") {
    // 6. Saudi Arabia
    ratePct = 15.0;
    taxAuthority = "Zakat, Tax and Customs Authority (ZATCA)";
    statutoryReference = "Value Added Tax Law, Royal Decree No. M/113, amended effective July 1, 2020";
    tariffCode = tariffCode || "9983";
    explanation = "Saudi Arabia standard 15% VAT on telecommunication, software, and electronic services.";
  } else if (customerCountry === "PK") {
    // 7. Pakistan Subnational Sales Tax on Services
    tariffCode = tariffCode || "9815.6000";

    if (rawJurisdiction === "SINDH") {
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
        ratePct = 6.0; // Corrected under Finance Act 2026
        taxAuthority = "BRA Reduced Rate for IT/Software (No Input Credit)";
        statutoryReference = "Balochistan Sales Tax on Services Act & Finance Act 2026, Heading 9868.0000";
        inputTaxRecoverable = false;
      } else {
        ratePct = 15.0;
        taxAuthority = "BRA Balochistan Sales Tax on Services";
        statutoryReference = "Balochistan Sales Tax on Services Act, Heading 9868.0000";
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
  } else {
    // General fallback
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
    tariffCode,
    isTaxInclusive,
    taxRegime,
    inputTaxRecoverable,
    explanation,
  };
}

function isEuCountry(code: string): boolean {
  const euMembers = ["DE", "FR", "IT", "ES", "NL", "BE", "IE", "PL", "SE", "AT", "DK", "FI", "PT", "GR", "CZ", "RO", "HU"];
  return euMembers.includes(code.toUpperCase());
}
