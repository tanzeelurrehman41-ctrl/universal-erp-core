import { test } from "node:test";
import assert from "node:assert/strict";
import {
  isRtlLocale,
  getLayoutDirection,
  validateTaxOrNationalId,
  validatePostalCode,
  formatAddress,
  getBusinessCalendar,
  LOCALIZATION_COVERAGE_MATRIX,
  getCountryCoverage,
  getCoverageByStatus,
  getAllCoverageEntries,
} from "../src/localization/index.ts";

test("isRtlLocale and getLayoutDirection correctly detect RTL scripts", () => {
  // RTL locales
  assert.equal(isRtlLocale("ar-SA"), true);
  assert.equal(isRtlLocale("ar-AE"), true);
  assert.equal(isRtlLocale("ur-PK"), true);
  assert.equal(isRtlLocale("fa-IR"), true);
  assert.equal(isRtlLocale("he-IL"), true);
  assert.equal(getLayoutDirection("ar-SA"), "rtl");
  assert.equal(getLayoutDirection("ur-PK"), "rtl");

  // LTR locales
  assert.equal(isRtlLocale("en-US"), false);
  assert.equal(isRtlLocale("en-GB"), false);
  assert.equal(isRtlLocale("de-DE"), false);
  assert.equal(isRtlLocale("fr-FR"), false);
  assert.equal(getLayoutDirection("en-US"), "ltr");
  assert.equal(getLayoutDirection("de-DE"), "ltr");
});

test("validateTaxOrNationalId validates international statutory identifiers", () => {
  // US EIN & SSN
  const usEin = validateTaxOrNationalId("US", "12-3456789");
  assert.equal(usEin.isValid, true);
  assert.equal(usEin.idType, "EIN");
  assert.equal(usEin.formattedId, "12-3456789");

  const usSsn = validateTaxOrNationalId("US", "123-45-6789", "SSN");
  assert.equal(usSsn.isValid, true);
  assert.equal(usSsn.idType, "SSN");
  assert.equal(usSsn.formattedId, "123-45-6789");

  // UK VAT & NINO
  const ukVat = validateTaxOrNationalId("GB", "987654321");
  assert.equal(ukVat.isValid, true);
  assert.equal(ukVat.idType, "VAT");
  assert.equal(ukVat.formattedId, "GB987654321");

  const ukNino = validateTaxOrNationalId("GB", "JH123456C", "NINO");
  assert.equal(ukNino.isValid, true);
  assert.equal(ukNino.idType, "NINO");

  // EU Member State VAT Numbers
  const deVat = validateTaxOrNationalId("DE", "123456789");
  assert.equal(deVat.isValid, true);
  assert.equal(deVat.formattedId, "DE123456789");

  const frVat = validateTaxOrNationalId("FR", "XX123456789");
  assert.equal(frVat.isValid, true);
  assert.equal(frVat.formattedId, "FRXX123456789");

  const nlVat = validateTaxOrNationalId("NL", "123456789B01");
  assert.equal(nlVat.isValid, true);
  assert.equal(nlVat.formattedId, "NL123456789B01");

  // UAE TRN (15 digits starting with 100)
  const uaeTrn = validateTaxOrNationalId("AE", "100123456789012");
  assert.equal(uaeTrn.isValid, true);
  assert.equal(uaeTrn.idType, "TRN");

  const uaeInvalid = validateTaxOrNationalId("AE", "200123456789012");
  assert.equal(uaeInvalid.isValid, false);

  // Saudi Arabia VAT/TIN (15 digits starting and ending with 3)
  const saVat = validateTaxOrNationalId("SA", "300123456789013");
  assert.equal(saVat.isValid, true);
  assert.equal(saVat.idType, "VAT_TIN");

  const saInvalid = validateTaxOrNationalId("SA", "100123456789012");
  assert.equal(saInvalid.isValid, false);

  // Pakistan NTN & CNIC
  const pkNtn = validateTaxOrNationalId("PK", "1234567-8");
  assert.equal(pkNtn.isValid, true);
  assert.equal(pkNtn.idType, "NTN");

  const pkCnic = validateTaxOrNationalId("PK", "4210112345671");
  assert.equal(pkCnic.isValid, true);
  assert.equal(pkCnic.idType, "CNIC");
  assert.equal(pkCnic.formattedId, "42101-1234567-1");

  // Australia ABN (11 digits)
  const auAbn = validateTaxOrNationalId("AU", "12345678901");
  assert.equal(auAbn.isValid, true);
  assert.equal(auAbn.idType, "ABN");

  // India GSTIN (15 characters)
  const inGstin = validateTaxOrNationalId("IN", "27AAAAA0000A1Z5");
  assert.equal(inGstin.isValid, true);
  assert.equal(inGstin.idType, "GSTIN");
});

test("validatePostalCode validates multi-national postal codes", () => {
  // US ZIP & ZIP+4
  assert.equal(validatePostalCode("US", "10001"), true);
  assert.equal(validatePostalCode("US", "94105-1234"), true);
  assert.equal(validatePostalCode("US", "INVALID"), false);

  // UK Postcodes
  assert.equal(validatePostalCode("GB", "SW1A 1AA"), true);
  assert.equal(validatePostalCode("GB", "EC1A 1BB"), true);

  // Canada
  assert.equal(validatePostalCode("CA", "M5V 2T6"), true);
  assert.equal(validatePostalCode("CA", "K1A0B1"), true);

  // Australia
  assert.equal(validatePostalCode("AU", "2000"), true);
  assert.equal(validatePostalCode("AU", "3000"), true);

  // Germany & France (5 digits)
  assert.equal(validatePostalCode("DE", "10115"), true);
  assert.equal(validatePostalCode("FR", "75001"), true);

  // Netherlands
  assert.equal(validatePostalCode("NL", "1012 JS"), true);

  // Pakistan
  assert.equal(validatePostalCode("PK", "75500"), true);
});

test("formatAddress formats country-specific multi-line postal addresses", () => {
  // US format
  const usAddr = formatAddress("US", {
    line1: "350 5th Ave",
    line2: "Suite 400",
    city: "New York",
    stateOrProvince: "NY",
    postalCode: "10118",
    countryName: "United States",
  });
  assert.ok(usAddr.includes("New York, NY 10118"));
  assert.ok(usAddr.includes("United States"));

  // UK format
  const ukAddr = formatAddress("GB", {
    line1: "10 Downing Street",
    city: "London",
    postalCode: "SW1A 2AA",
    countryName: "United Kingdom",
  });
  assert.ok(ukAddr.includes("London"));
  assert.ok(ukAddr.includes("SW1A 2AA"));

  // German format
  const deAddr = formatAddress("DE", {
    line1: "Unter den Linden 77",
    city: "Berlin",
    postalCode: "10117",
    countryName: "Germany",
  });
  assert.ok(deAddr.includes("10117 Berlin"));
});

test("getBusinessCalendar reflects accurate national working weeks and weekends", () => {
  // Saudi Arabia: Sun-Thu working, Fri/Sat weekend
  const saCal = getBusinessCalendar("SA");
  assert.deepEqual(saCal.workDays, [0, 1, 2, 3, 4]);
  assert.deepEqual(saCal.weekendDays, [5, 6]);

  // UAE: Mon-Fri working, Sat/Sun weekend
  const aeCal = getBusinessCalendar("AE");
  assert.deepEqual(aeCal.workDays, [1, 2, 3, 4, 5]);
  assert.deepEqual(aeCal.weekendDays, [6, 0]);

  // US: Mon-Fri working, Sat/Sun weekend
  const usCal = getBusinessCalendar("US");
  assert.deepEqual(usCal.workDays, [1, 2, 3, 4, 5]);
  assert.deepEqual(usCal.weekendDays, [6, 0]);
});

test("LOCALIZATION_COVERAGE_MATRIX maintains authoritative status integrity", () => {
  const entries = getAllCoverageEntries();
  assert.ok(entries.length >= 10);

  // Verify roadmap ordering (1. PK, 2. US, 3. GB, 4. DE/FR/IE/EU, 5. AE, 6. SA, 7. CA, 8. AU, 9. IN)
  const pk = getCountryCoverage("PK");
  assert.ok(pk);
  assert.equal(pk.roadmapPriority, 1);
  assert.equal(pk.overallStatus, "Implemented and tested");

  const us = getCountryCoverage("US");
  assert.ok(us);
  assert.equal(us.roadmapPriority, 2);
  assert.equal(us.overallStatus, "Implemented and tested");

  const gb = getCountryCoverage("GB");
  assert.ok(gb);
  assert.equal(gb.roadmapPriority, 3);
  assert.equal(gb.overallStatus, "Implemented and tested");

  const ca = getCountryCoverage("CA");
  assert.ok(ca);
  assert.equal(ca.overallStatus, "Partially implemented");

  const inEntry = getCountryCoverage("IN");
  assert.ok(inEntry);
  assert.equal(inEntry.overallStatus, "Architecture configured only");

  // Query by status
  const implemented = getCoverageByStatus("Implemented and tested");
  assert.ok(implemented.length >= 5);
  const codes = implemented.map(e => e.countryCode);
  assert.ok(codes.includes("PK"));
  assert.ok(codes.includes("US"));
  assert.ok(codes.includes("GB"));
  assert.ok(codes.includes("AE"));
  assert.ok(codes.includes("SA"));
});
