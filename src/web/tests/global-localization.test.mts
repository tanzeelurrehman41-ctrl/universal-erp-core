import { test } from "node:test";
import assert from "node:assert/strict";
import {
  getSupportedCountries,
  getCountryByCode,
  getJurisdictionsForCountry,
  formatCurrency,
  getCurrencyDecimals,
  getCurrencySymbol,
  calculateGlobalTax,
  calculateGlobalPayroll,
  calculateJurisdictionalTax,
} from "../src/saasHelpers.ts";

test("getSupportedCountries returns all registered international jurisdictions", () => {
  const countries = getSupportedCountries();
  assert.ok(countries.length >= 8);

  const codes = countries.map((c) => c.countryCode);
  assert.ok(codes.includes("US"));
  assert.ok(codes.includes("GB"));
  assert.ok(codes.includes("DE"));
  assert.ok(codes.includes("FR"));
  assert.ok(codes.includes("AE"));
  assert.ok(codes.includes("SA"));
  assert.ok(codes.includes("PK"));

  const us = getCountryByCode("US");
  assert.ok(us);
  assert.equal(us.defaultCurrencyCode, "USD");
  assert.equal(us.taxSystemName, "Sales Tax");
  assert.equal(us.measurementSystem, "imperial");

  const pk = getCountryByCode("PK");
  assert.ok(pk);
  assert.equal(pk.defaultCurrencyCode, "PKR");
  assert.equal(pk.fiscalYearStartMonth, 7);

  const gb = getCountryByCode("GB");
  assert.ok(gb);
  assert.equal(gb.defaultCurrencyCode, "GBP");
  assert.equal(gb.taxSystemName, "VAT");
});

test("formatCurrency handles international decimal precisions and currency symbols", () => {
  // USD (2 decimals, Western formatting)
  assert.equal(formatCurrency(1250.5, "USD"), "$ 1,250.50");

  // EUR (2 decimals)
  assert.equal(formatCurrency(50000, "EUR"), "€ 50,000.00");

  // GBP (2 decimals)
  assert.equal(formatCurrency(2450.75, "GBP"), "£ 2,450.75");

  // JPY (0 decimals)
  assert.equal(getCurrencyDecimals("JPY"), 0);
  assert.equal(formatCurrency(15000, "JPY"), "¥ 15,000");

  // BHD (3 decimals)
  assert.equal(getCurrencyDecimals("BHD"), 3);
  assert.equal(formatCurrency(12.345, "BHD"), "BD 12.345");

  // PKR (South Asian numbering system)
  const formattedPkr = formatCurrency(150000, "PKR", "ur-PK");
  assert.equal(formattedPkr, "Rs. 1,50,000.00");
});

test("calculateGlobalTax resolves United States state-level sales taxes", () => {
  // New York (8.875% NYC combined)
  const ny = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "US",
    customerJurisdictionCode: "NY",
    supplierCountryCode: "US",
  });
  assert.equal(ny.ratePct, 8.875);
  assert.equal(ny.taxAmount, 88.75);
  assert.equal(ny.totalAmount, 1088.75);
  assert.ok(ny.taxAuthority.includes("New York"));

  // Texas Combined State + Local (6.6% effective SaaS rate on 80% taxable base)
  const tx = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "US",
    customerJurisdictionCode: "TX",
    supplierCountryCode: "US",
  });
  assert.equal(tx.ratePct, 6.6);
  assert.equal(tx.taxAmount, 66.0);
  assert.equal(tx.totalAmount, 1066.0);
  assert.ok(tx.statutoryReference.includes("151.0101"));

  // Texas State-Only (5.0% effective SaaS rate on 80% taxable base of 6.25%)
  const txState = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "US",
    customerJurisdictionCode: "TX_STATE",
    supplierCountryCode: "US",
  });
  assert.equal(txState.ratePct, 5.0);
  assert.equal(txState.taxAmount, 50.0);
  assert.equal(txState.totalAmount, 1050.0);

  // Washington (6.5%)
  const wa = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "US",
    customerJurisdictionCode: "WA",
    supplierCountryCode: "US",
  });
  assert.equal(wa.ratePct, 6.5);
  assert.equal(wa.taxAmount, 65.0);
  assert.equal(wa.totalAmount, 1065.0);

  // California (0% exempt on pure SaaS)
  const ca = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "US",
    customerJurisdictionCode: "CA",
    supplierCountryCode: "US",
  });
  assert.equal(ca.ratePct, 0.0);
  assert.equal(ca.taxAmount, 0.0);
  assert.equal(ca.totalAmount, 1000.0);
  assert.ok(ca.explanation.includes("exempt"));
});

test("calculateGlobalTax resolves UK and European Union VAT rules", () => {
  // UK Domestic (20% standard)
  const uk = calculateGlobalTax({
    amount: 500,
    customerCountryCode: "GB",
    supplierCountryCode: "GB",
  });
  assert.equal(uk.ratePct, 20.0);
  assert.equal(uk.taxAmount, 100.0);
  assert.equal(uk.totalAmount, 600.0);
  assert.ok(uk.taxAuthority.includes("HMRC"));

  // Germany Domestic (19% standard MwSt)
  const de = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "DE",
    supplierCountryCode: "DE",
  });
  assert.equal(de.ratePct, 19.0);
  assert.equal(de.taxAmount, 190.0);
  assert.equal(de.totalAmount, 1190.0);

  // France Domestic (20% standard TVA)
  const fr = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "FR",
    supplierCountryCode: "FR",
  });
  assert.equal(fr.ratePct, 20.0);
  assert.equal(fr.taxAmount, 200.0);
  assert.equal(fr.totalAmount, 1200.0);

  // Intra-EU B2B Reverse Charge (Germany supplier -> France customer with verified VAT ID)
  const euCross = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "FR",
    supplierCountryCode: "DE",
    customerType: "b2b",
    customerVatNumber: "FR123456789",
  });
  assert.equal(euCross.ratePct, 0.0);
  assert.equal(euCross.taxAmount, 0.0);
  assert.equal(euCross.taxRegime, "reverse_charge");
  assert.ok(euCross.statutoryReference.includes("Article 196"));

  // Intra-EU B2B without valid VAT ID falls back to French destination VAT (20%)
  const euCrossNoVat = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "FR",
    supplierCountryCode: "DE",
    customerType: "b2b",
  });
  assert.equal(euCrossNoVat.ratePct, 20.0);
  assert.equal(euCrossNoVat.taxAmount, 200.0);
  assert.equal(euCrossNoVat.isDestinationVat, true);
});

test("calculateGlobalTax resolves Middle East GCC VAT (UAE & Saudi Arabia)", () => {
  // UAE (5% standard FTA)
  const uae = calculateGlobalTax({
    amount: 2000,
    customerCountryCode: "AE",
    supplierCountryCode: "AE",
  });
  assert.equal(uae.ratePct, 5.0);
  assert.equal(uae.taxAmount, 100.0);
  assert.equal(uae.totalAmount, 2100.0);
  assert.ok(uae.taxAuthority.includes("Federal Tax Authority"));

  // Saudi Arabia (15% standard ZATCA)
  const ksa = calculateGlobalTax({
    amount: 2000,
    customerCountryCode: "SA",
    supplierCountryCode: "SA",
  });
  assert.equal(ksa.ratePct, 15.0);
  assert.equal(ksa.taxAmount, 300.0);
  assert.equal(ksa.totalAmount, 2300.0);
  assert.ok(ksa.taxAuthority.includes("ZATCA"));
});

test("calculateJurisdictionalTax cross-country isolation test", () => {
  // Invoking calculateJurisdictionalTax with a US State never triggers Pakistani FBR/SRB logic
  const usTax = calculateJurisdictionalTax(1000, "NY");
  assert.equal(usTax.ratePct, 8.875);
  assert.equal(usTax.taxAmount, 88.75);
  assert.equal(usTax.countryCode, "US");
  assert.ok(!usTax.taxAuthority.includes("SRB"));
  assert.ok(!usTax.taxAuthority.includes("FBR"));

  // Invoking calculateJurisdictionalTax with UAE
  const uaeTax = calculateJurisdictionalTax(1000, "AE");
  assert.equal(uaeTax.ratePct, 5.0);
  assert.equal(uaeTax.countryCode, "AE");
  assert.ok(uaeTax.taxAuthority.includes("Federal Tax Authority"));

  // Invoking calculateJurisdictionalTax with Pakistan SINDH keeps standard 15% under Sindh Finance Act 2024
  const pkTax = calculateJurisdictionalTax(100000, "SINDH");
  assert.equal(pkTax.ratePct, 15.0);
  assert.equal(pkTax.taxAmount, 15000.0);
  assert.equal(pkTax.countryCode, "PK");
  assert.ok(pkTax.taxAuthority.includes("SRB"));
});

test("calculateGlobalPayroll accurately calculates US, UK, UAE, and Pakistan payrolls", () => {
  // 1. United States Payroll ($10,000 monthly gross)
  const usPay = calculateGlobalPayroll({
    countryCode: "US",
    grossSalary: 10000,
    currencyCode: "USD",
  });
  assert.equal(usPay.countryCode, "US");
  assert.equal(usPay.currencyCode, "USD");
  assert.ok(usPay.employeeStatutoryDeductions > 0); // FICA SS + Medicare
  assert.ok(usPay.employeeIncomeTax > 0); // FIT
  assert.ok(usPay.netSalary < usPay.grossSalary);
  const ssLine = usPay.statutoryLines.find((l) => l.code === "US_FICA_SS");
  assert.ok(ssLine);
  assert.equal(ssLine.employeeAmount, 620.0); // 6.2% of 10,000

  // 2. United Kingdom Payroll (£5,000 monthly gross under 2026-27 reform)
  const ukPay = calculateGlobalPayroll({
    countryCode: "GB",
    grossSalary: 5000,
    currencyCode: "GBP",
    effectiveDate: "2026-05-01",
  });
  assert.equal(ukPay.countryCode, "GB");
  assert.ok(ukPay.employeeIncomeTax > 0); // PAYE
  assert.ok(ukPay.employeeStatutoryDeductions > 0); // NIC
  assert.ok(ukPay.employerStatutoryContributions > 0); // Employer NIC
  // Under 2026-27 reform: Employer NIC is 15.0% above £417.00/month -> (5000 - 417) * 0.15 = 687.45
  const erNic = ukPay.statutoryLines.find((l) => l.code === "UK_NIC");
  assert.ok(erNic);
  assert.equal(erNic.employerAmount, 687.45);

  // 3. United Arab Emirates Expatriate Payroll (AED 20,000 gross)
  const uaePay = calculateGlobalPayroll({
    countryCode: "AE",
    grossSalary: 20000,
    basicSalary: 12000,
    currencyCode: "AED",
    isExpatriate: true,
  });
  assert.equal(uaePay.countryCode, "AE");
  assert.equal(uaePay.employeeIncomeTax, 0); // 0% income tax
  assert.equal(uaePay.employeeStatutoryDeductions, 0); // 0% employee deduction
  assert.equal(uaePay.netSalary, 20000); // 100% net take-home
  assert.ok(uaePay.employerStatutoryContributions > 0); // End of Service Gratuity accrual

  // 4. Pakistan Payroll (PKR 150,000 monthly gross -> PKR 1.8M annualized)
  const pkPay = calculateGlobalPayroll({
    countryCode: "PK",
    grossSalary: 150000,
    currencyCode: "PKR",
  });
  assert.equal(pkPay.countryCode, "PK");
  assert.ok(pkPay.employeeIncomeTax > 0); // FBR TY2027 tax
  const eobiLine = pkPay.statutoryLines.find((l) => l.code === "PK_EOBI");
  assert.ok(eobiLine);
  assert.equal(eobiLine.employeeAmount, 370.0); // 1% of 37,000
  assert.equal(eobiLine.employerAmount, 1850.0); // 5% of 37,000
});

test("calculateUsPayroll verifies 2026 OASDI cap ($184,500), Additional Medicare (0.9%), MFJ, FUTA & SUTA", () => {
  // Test high-earner $25,000/mo ($300k annualized) Single filer
  const res = calculateGlobalPayroll({
    countryCode: "US",
    grossSalary: 25000,
    filingStatus: "Single",
    effectiveDate: "2026-06-01",
  });

  // OASDI: monthly cap is $184,500 / 12 = $15,375.00 -> 6.2% = $953.25
  const ss = res.statutoryLines.find((l) => l.code === "US_FICA_SS");
  assert.ok(ss);
  assert.equal(ss.employeeAmount, 953.25);
  assert.equal(ss.employerAmount, 953.25);

  // Medicare: 1.45% base on $25,000 = $362.50 + 0.9% on ($25,000 - $16,666.67 = $8,333.33) = $75.00 -> Total EE = $437.50
  const med = res.statutoryLines.find((l) => l.code === "US_FICA_MED");
  assert.ok(med);
  assert.equal(med.employeeAmount, 437.50);
  assert.equal(med.employerAmount, 362.50);

  // FUTA: 0.6% on $583.33 = $3.50
  const futa = res.statutoryLines.find((l) => l.code === "US_FUTA");
  assert.ok(futa);
  assert.equal(futa.employerAmount, 3.50);

  // FIT 2026 Single (IRS Pub 15-T, StdDed $16,100): Taxable = $283,900 -> FIT = $5,678.08
  assert.equal(res.employeeIncomeTax, 5678.08);

  // Net Take-Home Pay = $25,000.00 - ($953.25 + $437.50 + $5,678.08 = $7,068.83) = $17,931.17
  assert.equal(res.netSalary, 17931.17);

  // Married Filing Jointly should have higher standard deduction ($32,200) resulting in lower FIT
  const resMfj = calculateGlobalPayroll({
    countryCode: "US",
    grossSalary: 25000,
    filingStatus: "MarriedFilingJointly",
    effectiveDate: "2026-06-01",
  });
  assert.ok(resMfj.employeeIncomeTax < res.employeeIncomeTax);
});

test("calculateUaePayroll verifies pension cohorts: Law 7 of 1999, Decree-Law 57 of 2023, ADPF, SSSF, Expat EOSG", () => {
  // 1. Federal Law 7 of 1999 (Legacy: 5% EE, 12.5% ER, 2.5% Gov subsidy, cap AED 50k)
  const law7 = calculateGlobalPayroll({
    countryCode: "AE",
    grossSalary: 25000,
    isExpatriate: false,
    uaePensionScheme: "Law7_1999",
  });
  assert.equal(law7.employeeIncomeTax, 0);
  assert.equal(law7.employeeStatutoryDeductions, 1250.0); // 5% of 25,000
  const pensionLaw7 = law7.statutoryLines.find((l) => l.code === "AE_PENSION_EE");
  assert.ok(pensionLaw7);
  assert.equal(pensionLaw7.employeeAmount, 1250.0);
  assert.equal(pensionLaw7.employerAmount, 3125.0); // 12.5% of 25,000
  const subLaw7 = law7.statutoryLines.find((l) => l.code === "AE_PENSION_GOVT_SUBSIDY");
  assert.ok(subLaw7);
  assert.equal(subLaw7.employerAmount, 625.0); // 2.5% of 25,000

  // 2. Federal Decree-Law 57 of 2023 (New GPSSA: 11% EE, 15% ER, cap AED 70k)
  const dl57 = calculateGlobalPayroll({
    countryCode: "AE",
    grossSalary: 25000,
    isExpatriate: false,
    uaePensionScheme: "DecreeLaw57_2023",
  });
  assert.equal(dl57.employeeStatutoryDeductions, 2750.0); // 11% of 25,000
  const pensionDl57 = dl57.statutoryLines.find((l) => l.code === "AE_PENSION_EE");
  assert.ok(pensionDl57);
  assert.equal(pensionDl57.employeeAmount, 2750.0);
  assert.equal(pensionDl57.employerAmount, 3750.0); // 15% of 25,000

  // 3. Abu Dhabi Pension Fund (ADPF: 11% EE, 15% ER, cap AED 100k)
  const adpf = calculateGlobalPayroll({
    countryCode: "AE",
    grossSalary: 40000,
    isExpatriate: false,
    uaePensionScheme: "ADPF",
  });
  assert.equal(adpf.employeeStatutoryDeductions, 4400.0); // 11% of 40,000
  const pensionAdpf = adpf.statutoryLines.find((l) => l.code === "AE_PENSION_EE");
  assert.ok(pensionAdpf);
  assert.equal(pensionAdpf.employeeAmount, 4400.0);
  assert.equal(pensionAdpf.employerAmount, 6000.0); // 15% of 40,000

  // 4. Sharjah Social Security Fund (SSSF: 5% EE, 15% ER, cap AED 50k)
  const sssf = calculateGlobalPayroll({
    countryCode: "AE",
    grossSalary: 40000,
    isExpatriate: false,
    uaePensionScheme: "SSSF",
  });
  assert.equal(sssf.employeeStatutoryDeductions, 2000.0); // 5% of 40,000
  const pensionSssf = sssf.statutoryLines.find((l) => l.code === "AE_PENSION_EE");
  assert.ok(pensionSssf);
  assert.equal(pensionSssf.employeeAmount, 2000.0);
  assert.equal(pensionSssf.employerAmount, 6000.0); // 15% of 40,000
});

test("calculateAustraliaPayroll verifies 12% Super Guarantee (effective 1 July 2026), OTE, and Payday Super", () => {
  const auPay = calculateGlobalPayroll({
    countryCode: "AU",
    grossSalary: 9250,
    basicSalary: 8000,
    ordinaryTimeEarnings: 8000,
    effectiveDate: "2026-07-01",
  });
  assert.equal(auPay.countryCode, "AU");
  assert.ok(auPay.employeeIncomeTax > 0); // PAYG
  // Super Guarantee: 12.0% on OTE ($8,000) = $960.00
  const superLine = auPay.statutoryLines.find((l) => l.code === "AU_SUPER");
  assert.ok(superLine);
  assert.equal(superLine.employerAmount, 960.0);
  assert.ok(superLine.authority.includes("Australian Taxation Office"));
  assert.ok(superLine.description.includes("Payday Super 7-day rule"));

  // High Earner capping at 2026-27 MCB ($270,830/yr = $22,569.17/mo): 12% on $22,569.17 = $2,708.30
  const auHigh = calculateGlobalPayroll({
    countryCode: "AU",
    grossSalary: 25000,
    ordinaryTimeEarnings: 25000,
    effectiveDate: "2026-08-01",
  });
  const highSuper = auHigh.statutoryLines.find((l) => l.code === "AU_SUPER");
  assert.ok(highSuper);
  assert.equal(highSuper.employerAmount, 2708.30);
});

test("calculateUkPayroll verifies 2026-27 Employer NIC (15%, £417 threshold), Scottish PAYE, Student Loans, Categories", () => {
  // Category C: State pensioner (0% employee NIC, 15% employer NIC above £417)
  const catC = calculateGlobalPayroll({
    countryCode: "GB",
    grossSalary: 3000,
    ukCategoryLetter: "C",
    effectiveDate: "2026-06-01",
  });
  const nicC = catC.statutoryLines.find((l) => l.code === "UK_NIC");
  assert.ok(nicC);
  assert.equal(nicC.employeeAmount, 0.0);
  assert.equal(nicC.employerAmount, 387.45); // (3000 - 417) * 0.15 = 387.45

  // Scottish Taxpayer with Student Loan Plan 4 & Pension Auto-Enrolment
  const scot = calculateGlobalPayroll({
    countryCode: "GB",
    grossSalary: 4000,
    ukCategoryLetter: "A",
    isScottishTaxpayer: true,
    studentLoanPlan: "Plan4",
    pensionAutoEnrolment: true,
    effectiveDate: "2026-06-01",
  });
  const payeLine = scot.statutoryLines.find((l) => l.code === "UK_PAYE");
  assert.ok(payeLine);
  assert.ok(payeLine.description.includes("Scottish"));

  const slLine = scot.statutoryLines.find((l) => l.code === "UK_SL_PLAN4");
  assert.ok(slLine);
  // Plan 4 2026-27 threshold £2,816.25 -> (4000 - 2816.25) * 0.09 = 106.54
  assert.equal(slLine.employeeAmount, 106.54);

  const pensionLine = scot.statutoryLines.find((l) => l.code === "UK_PENSION_AE");
  assert.ok(pensionLine);
  // Qualifying earnings: 4000 - 520 = 3480 -> EE 5% = 174, ER 3% = 104.40
  assert.equal(pensionLine.employeeAmount, 174.0);
  assert.equal(pensionLine.employerAmount, 104.40);
});

test("calculateGlobalTax enforces EU OSS destination VAT for B2C and Art 196 reverse charge for B2B", () => {
  // Cross-Border EU B2C: US supplier -> German consumer => 19% German VAT under OSS
  const b2cDe = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "DE",
    customerType: "b2c",
    isEuOssEligible: true,
  });
  assert.equal(b2cDe.ratePct, 19.0);
  assert.equal(b2cDe.taxAmount, 190.0);
  assert.equal(b2cDe.isDestinationVat, true);
  assert.ok(b2cDe.eInvoicingMandatePlatform?.includes("ZUGFeRD"));

  // Cross-Border EU B2C: US supplier -> Irish consumer => 23% Irish VAT under OSS
  const b2cIe = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "IE",
    customerType: "b2c",
  });
  assert.equal(b2cIe.ratePct, 23.0);
  assert.equal(b2cIe.taxAmount, 230.0);

  // Cross-Border EU B2B with valid VAT ID => 0% Reverse Charge under Article 196
  const b2bDe = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "DE",
    customerType: "b2b",
    customerVatNumber: "DE123456789",
  });
  assert.equal(b2bDe.ratePct, 0.0);
  assert.equal(b2bDe.taxAmount, 0.0);
  assert.equal(b2bDe.isReverseCharge, true);
  assert.ok(b2bDe.statutoryReference.includes("Article 196"));

  // UK Overseas Vendor B2C digital services: 20% standard VAT
  const ukB2c = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "GB",
    customerType: "b2c",
  });
  assert.equal(ukB2c.ratePct, 20.0);
  assert.equal(ukB2c.taxAmount, 200.0);
  assert.equal(ukB2c.isDestinationVat, true);

  // UK Overseas Vendor B2B with UK VAT: 0% Reverse Charge
  const ukB2b = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "GB",
    customerType: "b2b",
    customerVatNumber: "GB123456789",
  });
  assert.equal(ukB2b.ratePct, 0.0);
  assert.equal(ukB2b.isReverseCharge, true);

  // Australia Inbound B2C: 10% GST
  const auB2c = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "AU",
    customerType: "b2c",
  });
  assert.equal(auB2c.ratePct, 10.0);
  assert.equal(auB2c.taxAmount, 100.0);

  // Canada Ontario B2C: 13% HST
  const caB2c = calculateGlobalTax({
    amount: 1000,
    supplierCountryCode: "US",
    customerCountryCode: "CA",
    customerJurisdictionCode: "ON",
    customerType: "b2c",
  });
  assert.equal(caB2c.ratePct, 13.0);
  assert.equal(caB2c.taxAmount, 130.0);
});

test("calculateSaudiArabiaPayroll verifies GOSI contributions, caps, and expatriate EOSA accrual", () => {
  // 1. Saudi National within cap (SAR 20,000 gross)
  const nationalRes = calculateGlobalPayroll({
    countryCode: "SA",
    grossSalary: 20000,
    isExpatriate: false,
  });
  assert.equal(nationalRes.currencyCode, "SAR");
  // EE: 9% annuities (1800) + 0.75% SANED (150) = 1950
  assert.equal(nationalRes.employeeStatutoryDeductions, 1950.0);
  // ER: 9% annuities (1800) + 0.75% SANED (150) + 2% Occ Hazards (400) = 2350
  assert.equal(nationalRes.employerStatutoryContributions, 2350.0);
  assert.equal(nationalRes.netSalary, 18050.0);

  const annuitiesLine = nationalRes.statutoryLines.find(l => l.code === "SA_GOSI_ANNUITIES");
  assert.ok(annuitiesLine);
  assert.equal(annuitiesLine.employeeAmount, 1800.0);
  assert.equal(annuitiesLine.employerAmount, 1800.0);

  // 2. Saudi National exceeding SAR 45,000 statutory cap (SAR 60,000 gross)
  const cappedRes = calculateGlobalPayroll({
    countryCode: "SA",
    grossSalary: 60000,
    isExpatriate: false,
  });
  // Cap at 45,000: EE = 9.75% of 45,000 = 4,387.50
  assert.equal(cappedRes.employeeStatutoryDeductions, 4387.5);
  // ER = 11.75% of 45,000 = 5,287.50
  assert.equal(cappedRes.employerStatutoryContributions, 5287.5);
  assert.equal(cappedRes.netSalary, 55612.5);

  // 3. Expatriate employee (SAR 15,000 gross/basic, 3 years service -> 4.167% EOSA accrual)
  const expatRes = calculateGlobalPayroll({
    countryCode: "SA",
    grossSalary: 15000,
    basicSalary: 15000,
    isExpatriate: true,
    yearsOfService: 3,
  });
  // 0% EE deduction for expats
  assert.equal(expatRes.employeeStatutoryDeductions, 0.0);
  // ER: 2% Occ Hazards (300) + EOSA (625) = 925.0
  assert.equal(expatRes.employerStatutoryContributions, 925.0);
  assert.equal(expatRes.netSalary, 15000.0);
  const eosaLine = expatRes.statutoryLines.find(l => l.code === "SA_EOSA");
  assert.ok(eosaLine);
  assert.equal(eosaLine.employerAmount, 625.0);

  // 4. Expatriate employee senior (> 5 years service -> 8.333% EOSA accrual)
  const expatSenior = calculateGlobalPayroll({
    countryCode: "SA",
    grossSalary: 15000,
    basicSalary: 15000,
    isExpatriate: true,
    yearsOfService: 7,
  });
  const seniorEosaLine = expatSenior.statutoryLines.find(l => l.code === "SA_EOSA");
  assert.ok(seniorEosaLine);
  assert.equal(seniorEosaLine.employerAmount, 1250.0);
});

test("calculateCanadaPayroll verifies CPP base, CPP2, EI, and provincial withholding", () => {
  // 1. 2026 CRA Statutory Limits: CAD 6,500/mo (YMPE $74,600, YAMPE $85,000, EI $68,900)
  const ca2026 = calculateGlobalPayroll({
    countryCode: "CA",
    grossSalary: 6500,
    jurisdiction: "ON",
    effectiveDate: "2026-01-01",
  });
  assert.equal(ca2026.currencyCode, "CAD");
  const cppBase2026 = ca2026.statutoryLines.find(l => l.code === "CA_CPP_BASE");
  assert.ok(cppBase2026);
  assert.equal(cppBase2026.employeeAmount, 352.54);
  assert.equal(cppBase2026.employerAmount, 352.54);

  const cpp2Line2026 = ca2026.statutoryLines.find(l => l.code === "CA_CPP2_ENHANCED");
  assert.ok(cpp2Line2026);
  assert.equal(cpp2Line2026.employeeAmount, 11.33);

  const eiLine2026 = ca2026.statutoryLines.find(l => l.code === "CA_EI");
  assert.ok(eiLine2026);
  assert.equal(eiLine2026.employeeAmount, 93.59);
  assert.equal(eiLine2026.employerAmount, 131.02);

  // 2. Historical 2025 CRA Limits: CAD 6,000/mo (YMPE $71,300, YAMPE $81,200, EI $65,700)
  const ca2025 = calculateGlobalPayroll({
    countryCode: "CA",
    grossSalary: 6000,
    jurisdiction: "ON",
    effectiveDate: "2025-10-01",
  });
  const cppLine2025 = ca2025.statutoryLines.find(l => l.code === "CA_CPP_BASE");
  assert.ok(cppLine2025);
  assert.equal(cppLine2025.employeeAmount, 336.18);
  assert.equal(cppLine2025.employerAmount, 336.18);

  const cpp2Line2025 = ca2025.statutoryLines.find(l => l.code === "CA_CPP2_ENHANCED");
  assert.ok(cpp2Line2025);
  assert.equal(cpp2Line2025.employeeAmount, 2.33);

  const eiLine2025 = ca2025.statutoryLines.find(l => l.code === "CA_EI");
  assert.ok(eiLine2025);
  assert.equal(eiLine2025.employeeAmount, 89.24);
  assert.equal(eiLine2025.employerAmount, 124.94);
});

test("calculateIndiaPayroll verifies EPF statutory split, ESI wage ceiling, PT and Section 115BAC", () => {
  // 1. Post-17 September 2026 Gazette Amendment: Ceiling ₹25,000 (Basic 40,000, Gross 60,000)
  const inPostSept = calculateGlobalPayroll({
    countryCode: "IN",
    grossSalary: 60000,
    basicSalary: 40000,
    jurisdiction: "MH",
    effectiveDate: "2026-10-01",
  });
  assert.equal(inPostSept.currencyCode, "INR");
  // EPF EE: 12% of 40,000 = 4,800
  const epfEeLine = inPostSept.statutoryLines.find(l => l.code === "IN_EPF_EE");
  assert.ok(epfEeLine);
  assert.equal(epfEeLine.employeeAmount, 4800.0);

  // EPF ER: 12% total = 4800 (split: EPS 8.33% of ₹25,000 = ₹2,082.50 + EPF ₹2,717.50) + EDLI 0.5% of ₹25,000 = ₹125 => Total ER = 4925.0
  const epfErLine = inPostSept.statutoryLines.find(l => l.code === "IN_EPF_ER");
  assert.ok(epfErLine);
  assert.equal(epfErLine.employerAmount, 4925.0);

  // 2. Pre-17 September 2026 Historical Period: Ceiling ₹15,000 (Basic 40,000, Gross 60,000)
  const inPreSept = calculateGlobalPayroll({
    countryCode: "IN",
    grossSalary: 60000,
    basicSalary: 40000,
    jurisdiction: "MH",
    effectiveDate: "2026-04-01",
  });
  const epfErPre = inPreSept.statutoryLines.find(l => l.code === "IN_EPF_ER");
  assert.ok(epfErPre);
  assert.equal(epfErPre.employerAmount, 4875.0); // EPS 1250 + EPF 3550 + EDLI 75 = 4875.0

  // ESI: not present because gross 60,000 > 21,000 ceiling
  const esiLine = inPostSept.statutoryLines.find(l => l.code === "IN_ESI");
  assert.equal(esiLine, undefined);

  // Professional Tax: MH standard 200
  const ptLine = inPostSept.statutoryLines.find(l => l.code === "IN_PT");
  assert.ok(ptLine);
  assert.equal(ptLine.employeeAmount, 200.0);
  assert.ok(inPostSept.netSalary > 0);

  // 2. Low-earner: Basic 15,000, Gross 18,000 (eligible for ESI <= 21,000)
  const inLow = calculateGlobalPayroll({
    countryCode: "IN",
    grossSalary: 18000,
    basicSalary: 15000,
    jurisdiction: "MH",
  });
  const lowEsiLine = inLow.statutoryLines.find(l => l.code === "IN_ESI");
  assert.ok(lowEsiLine);
  assert.equal(lowEsiLine.employeeAmount, 135.0); // 0.75% of 18,000
  assert.equal(lowEsiLine.employerAmount, 585.0); // 3.25% of 18,000
});

test("calculateEuropeanPayroll verifies Netherlands, Spain, Italy and Poland adapters", () => {
  // Netherlands: Loonheffing Box 1 and Werkgeverspremies
  const nlRes = calculateGlobalPayroll({
    countryCode: "NL",
    grossSalary: 4000,
  });
  assert.equal(nlRes.currencyCode, "EUR");
  assert.ok(nlRes.employeeIncomeTax > 0);
  assert.equal(nlRes.employerStatutoryContributions, 740.0); // 18.5%
  assert.ok(nlRes.netSalary > 0);

  // Spain: Seguridad Social 6.35% EE / 29.9% ER
  const esRes = calculateGlobalPayroll({
    countryCode: "ES",
    grossSalary: 3000,
  });
  assert.equal(esRes.currencyCode, "EUR");
  const esSsLine = esRes.statutoryLines.find(l => l.code === "ES_SEG_SOCIAL_EE");
  assert.ok(esSsLine);
  assert.equal(esSsLine.employeeAmount, 190.5); // 6.35% of 3000
  assert.ok(esRes.netSalary > 0);

  // Italy: INPS 9.19% EE / 30.0% ER + TFR 6.91%
  const itRes = calculateGlobalPayroll({
    countryCode: "IT",
    grossSalary: 3000,
  });
  assert.equal(itRes.currencyCode, "EUR");
  const inpsLine = itRes.statutoryLines.find(l => l.code === "IT_INPS_EE");
  assert.ok(inpsLine);
  assert.equal(inpsLine.employeeAmount, 275.7); // 9.19% of 3000
  const tfrLine = itRes.statutoryLines.find(l => l.code === "IT_TFR");
  assert.ok(tfrLine);
  assert.ok(tfrLine.employerAmount > 0);
  assert.ok(itRes.netSalary > 0);

  // Poland: ZUS 13.71% EE / 20.38% ER
  const plRes = calculateGlobalPayroll({
    countryCode: "PL",
    grossSalary: 8000,
  });
  assert.equal(plRes.currencyCode, "PLN");
  const zusLine = plRes.statutoryLines.find(l => l.code === "PL_ZUS_EE");
  assert.ok(zusLine);
  assert.equal(zusLine.employeeAmount, 1096.8); // 13.71% of 8000
  assert.ok(plRes.netSalary > 0);
});

test("calculateGlobalTax resolves multi-country statutory taxes across Canada, India and extensible nations", () => {
  // 1. Canada Quebec: 14.975% (5% GST + 9.975% QST)
  const caQc = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "CA",
    customerJurisdictionCode: "QC",
    supplierCountryCode: "CA",
  });
  assert.equal(caQc.ratePct, 14.975);
  assert.equal(caQc.taxAmount, 149.75);

  // 2. Canada Saskatchewan: 11% (5% GST + 6% PST)
  const caSk = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "CA",
    customerJurisdictionCode: "SK",
    supplierCountryCode: "CA",
  });
  assert.equal(caSk.ratePct, 11.0);
  assert.equal(caSk.taxAmount, 110.0);

  // 3. Canada Manitoba: 12% (5% GST + 7% RST)
  const caMb = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "CA",
    customerJurisdictionCode: "MB",
    supplierCountryCode: "CA",
  });
  assert.equal(caMb.ratePct, 12.0);
  assert.equal(caMb.taxAmount, 120.0);

  // 4. Canada Alberta: 5% (GST only)
  const caAb = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "CA",
    customerJurisdictionCode: "AB",
    supplierCountryCode: "CA",
  });
  assert.equal(caAb.ratePct, 5.0);
  assert.equal(caAb.taxAmount, 50.0);

  // 5. India Intra-State: 18% (9% CGST + 9% SGST)
  const inIntra = calculateGlobalTax({
    amount: 10000,
    supplierCountryCode: "IN",
    supplierJurisdictionCode: "MH",
    customerCountryCode: "IN",
    customerJurisdictionCode: "MH",
  });
  assert.equal(inIntra.ratePct, 18.0);
  assert.equal(inIntra.taxAmount, 1800.0);
  assert.ok(inIntra.explanation.includes("9% CGST and 9% SGST"));

  // 6. India Inter-State: 18% IGST
  const inInter = calculateGlobalTax({
    amount: 10000,
    supplierCountryCode: "IN",
    supplierJurisdictionCode: "MH",
    customerCountryCode: "IN",
    customerJurisdictionCode: "KA",
  });
  assert.equal(inInter.ratePct, 18.0);
  assert.equal(inInter.taxAmount, 1800.0);
  assert.ok(inInter.explanation.includes("18% IGST"));

  // 7. India B2B Reverse Charge: 0% tax, isReverseCharge = true
  const inRcm = calculateGlobalTax({
    amount: 10000,
    supplierCountryCode: "US",
    customerCountryCode: "IN",
    customerType: "b2b",
    customerVatNumber: "27AAAAA0000A1Z5",
  });
  assert.equal(inRcm.ratePct, 0.0);
  assert.equal(inRcm.taxAmount, 0.0);
  assert.equal(inRcm.isReverseCharge, true);
  assert.ok(inRcm.statutoryReference.includes("Section 5(3)"));

  // 8. India OIDAR Inbound B2C: 18% IGST
  const inOidar = calculateGlobalTax({
    amount: 10000,
    supplierCountryCode: "US",
    customerCountryCode: "IN",
    customerType: "b2c",
  });
  assert.equal(inOidar.ratePct, 18.0);
  assert.equal(inOidar.taxAmount, 1800.0);

  // 9. Singapore: 9% GST
  const sg = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "SG",
  });
  assert.equal(sg.ratePct, 9.0);
  assert.equal(sg.taxAmount, 90.0);

  // 10. Switzerland: 8.1% MWST
  const ch = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "CH",
  });
  assert.equal(ch.ratePct, 8.1);
  assert.equal(ch.taxAmount, 81.0);

  // 11. Japan: 10% JCT
  const jp = calculateGlobalTax({
    amount: 10000,
    customerCountryCode: "JP",
  });
  assert.equal(jp.ratePct, 10.0);
  assert.equal(jp.taxAmount, 1000.0);

  // 12. Brazil: 26.5% Dual IBS/CBS Tax Reform
  const br = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "BR",
  });
  assert.equal(br.ratePct, 26.5);
  assert.equal(br.taxAmount, 265.0);

  // 13. South Africa: 15% VAT
  const za = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "ZA",
  });
  assert.equal(za.ratePct, 15.0);
  assert.equal(za.taxAmount, 150.0);
});
