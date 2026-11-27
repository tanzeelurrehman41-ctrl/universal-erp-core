import test from "node:test";
import assert from "node:assert/strict";
import {
  describeRateType,
  formatExchangeRateDisplay,
  resolveWorkspaceCurrencyDisplay,
  validateCurrencySettingsDraft,
  validateExchangeRateDraft,
  type CompanyCurrencySettingsDto,
  type ExchangeRateDto,
} from "../src/adminCurrencyHelpers.ts";

const sampleSettings: CompanyCurrencySettingsDto = {
  companyId: "c1",
  tenantId: "t1",
  companyCode: "CLOUD-LOGISTICS",
  companyName: "Cloud Logistics",
  functionalCurrencyCode: "PKR",
  presentationCurrencyCode: "USD",
  currencyEffectiveDate: "2026-11-26",
  hasPostedTransactions: true,
  updatedAt: null,
  functionalCurrencyGuidance: "Functional guidance",
  presentationCurrencyGuidance: "Presentation guidance",
};

const existingClosingRate: ExchangeRateDto = {
  exchangeRateId: "fx-1",
  companyId: "c1",
  fromCurrency: "USD",
  toCurrency: "PKR",
  rate: 278.5,
  effectiveDate: "2026-11-26",
  effectiveAt: "2026-11-26T00:00:00Z",
  rateType: "closing",
  source: "SBP",
  reference: "SBP-01",
  createdByUserId: "u1",
  createdByEmail: "admin@example.com",
  createdByName: "Admin",
  createdAt: "2026-11-26T00:00:00Z",
};

test("resolveWorkspaceCurrencyDisplay distinguishes functional PKR and presentation USD", () => {
  const result = resolveWorkspaceCurrencyDisplay({
    baseCurrencyCode: "PKR",
    functionalCurrencyCode: "PKR",
    presentationCurrencyCode: "USD",
  });

  assert.equal(result.functionalCurrency, "PKR");
  assert.equal(result.presentationCurrency, "USD");
  assert.equal(result.badgeText, "Functional: PKR · Reporting: USD");
});

test("validateCurrencySettingsDraft requires confirmation when functional currency or posted history changes", () => {
  const changeFunctional = validateCurrencySettingsDraft(
    {
      functionalCurrencyCode: "USD",
      presentationCurrencyCode: "USD",
      effectiveDate: "2026-12-01",
      changeReason: "Entity switch",
    },
    sampleSettings
  );

  assert.equal(changeFunctional.valid, true);
  assert.equal(changeFunctional.hasChanges, true);
  assert.equal(changeFunctional.functionalChanged, true);
  assert.equal(changeFunctional.requiresConfirmation, true);

  const invalidIso = validateCurrencySettingsDraft(
    {
      functionalCurrencyCode: "PK",
      presentationCurrencyCode: "USD",
      effectiveDate: "2026-12-01",
      changeReason: "",
    },
    sampleSettings
  );
  assert.equal(invalidIso.valid, false);
  assert.ok(invalidIso.errors.length >= 1);
});

test("validateExchangeRateDraft blocks self-pair, non-positive rate, duplicate pair, and ambiguous inverse pair", () => {
  const selfPair = validateExchangeRateDraft(
    {
      fromCurrency: "PKR",
      toCurrency: "PKR",
      rate: "1",
      effectiveDate: "2026-11-26",
      rateType: "closing",
      source: "SBP",
      reference: "",
    },
    [existingClosingRate]
  );
  assert.equal(selfPair.valid, false);

  const duplicate = validateExchangeRateDraft(
    {
      fromCurrency: "USD",
      toCurrency: "PKR",
      rate: "279.00",
      effectiveDate: "2026-11-26",
      rateType: "closing",
      source: "SBP",
      reference: "",
    },
    [existingClosingRate]
  );
  assert.equal(duplicate.valid, false);
  assert.match(duplicate.errors[0], /Duplicate exchange rate/i);

  const inverseAmbiguous = validateExchangeRateDraft(
    {
      fromCurrency: "PKR",
      toCurrency: "USD",
      rate: "0.00359",
      effectiveDate: "2026-11-26",
      rateType: "closing",
      source: "SBP",
      reference: "",
    },
    [existingClosingRate]
  );
  assert.equal(inverseAmbiguous.valid, false);
  assert.match(inverseAmbiguous.errors[0], /Ambiguous inverse pair/i);

  const allowedDifferentType = validateExchangeRateDraft(
    {
      fromCurrency: "USD",
      toCurrency: "PKR",
      rate: "277.80",
      effectiveDate: "2026-11-26",
      rateType: "transaction",
      source: "SBP",
      reference: "TX-1",
    },
    [existingClosingRate]
  );
  assert.equal(allowedDifferentType.valid, true);

  const allowedEditSameId = validateExchangeRateDraft(
    {
      fromCurrency: "USD",
      toCurrency: "PKR",
      rate: "278.90",
      effectiveDate: "2026-11-26",
      rateType: "closing",
      source: "SBP",
      reference: "Edited",
    },
    [existingClosingRate],
    "fx-1"
  );
  assert.equal(allowedEditSameId.valid, true);
});

test("formatExchangeRateDisplay and describeRateType produce readable IAS 21 labels", () => {
  assert.equal(formatExchangeRateDisplay("USD", "PKR", 278.5), "1 USD = 278.50 USD".replace("USD", "PKR").replace("PKR", "USD") ? "1 USD = 278.50 PKR" : "");
  assert.match(describeRateType("closing"), /Assets & Liabilities/);
  assert.match(describeRateType("transaction"), /Revenue, Expenses & Historical Equity/);
});
