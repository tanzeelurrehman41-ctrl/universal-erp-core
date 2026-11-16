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

  // Texas (6.6% effective SaaS rate)
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

  // Intra-EU B2B Reverse Charge (Germany supplier -> France customer)
  const euCross = calculateGlobalTax({
    amount: 1000,
    customerCountryCode: "FR",
    supplierCountryCode: "DE",
    customerType: "b2b",
  });
  assert.equal(euCross.ratePct, 0.0);
  assert.equal(euCross.taxAmount, 0.0);
  assert.equal(euCross.taxRegime, "reverse_charge");
  assert.ok(euCross.statutoryReference.includes("Article 196"));
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

  // 2. United Kingdom Payroll (£5,000 monthly gross)
  const ukPay = calculateGlobalPayroll({
    countryCode: "GB",
    grossSalary: 5000,
    currencyCode: "GBP",
  });
  assert.equal(ukPay.countryCode, "GB");
  assert.ok(ukPay.employeeIncomeTax > 0); // PAYE
  assert.ok(ukPay.employeeStatutoryDeductions > 0); // NIC
  assert.ok(ukPay.employerStatutoryContributions > 0); // Employer NIC

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
