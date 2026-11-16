/**
 * Universal ERP — Internationalization & Locale Utility Engine (i18n)
 * Handles RTL layout direction, localized number/date conventions,
 * country-specific identifier validations (EIN, SSN, VAT, TRN, NTN, etc.),
 * postal code validation, international address formatting, and local business calendars.
 */

export type LayoutDirection = "ltr" | "rtl";

export const RTL_LANGUAGES = ["ar", "ur", "fa", "he", "ps", "sd", "ug", "yi"];

/**
 * Checks whether a given locale or language code uses Right-to-Left (RTL) script.
 */
export function isRtlLocale(locale = "en-US"): boolean {
  if (!locale) return false;
  const lang = locale.toLowerCase().split(/[-_]/)[0];
  return RTL_LANGUAGES.includes(lang);
}

/**
 * Returns HTML direction ("rtl" or "ltr") for a given locale.
 */
export function getLayoutDirection(locale = "en-US"): LayoutDirection {
  return isRtlLocale(locale) ? "rtl" : "ltr";
}

export type IdentifierValidationResult = {
  isValid: boolean;
  countryCode: string;
  idType: string;
  formattedId: string;
  error?: string;
};

/**
 * Validates country-specific statutory identification numbers.
 */
export function validateTaxOrNationalId(
  countryCode: string,
  id: string,
  idTypePreference?: string
): IdentifierValidationResult {
  const country = (countryCode || "").trim().toUpperCase();
  const rawId = (id || "").trim();
  const cleanId = rawId.replace(/[\s-]/g, "");

  if (!rawId) {
    return {
      isValid: false,
      countryCode: country,
      idType: idTypePreference || "unknown",
      formattedId: "",
      error: "Identifier cannot be empty.",
    };
  }

  switch (country) {
    case "US": {
      // US Employer Identification Number (EIN) or Social Security Number (SSN)
      if (idTypePreference?.toUpperCase() === "SSN" || (!idTypePreference && /^\d{9}$/.test(cleanId) && rawId.includes("-") && rawId.indexOf("-") === 3)) {
        const isSsn = /^\d{9}$/.test(cleanId);
        return {
          isValid: isSsn,
          countryCode: "US",
          idType: "SSN",
          formattedId: isSsn ? `${cleanId.slice(0, 3)}-${cleanId.slice(3, 5)}-${cleanId.slice(5)}` : rawId,
          error: isSsn ? undefined : "Invalid US Social Security Number (format: XXX-XX-XXXX).",
        };
      }
      const isEin = /^\d{9}$/.test(cleanId);
      return {
        isValid: isEin,
        countryCode: "US",
        idType: "EIN",
        formattedId: isEin ? `${cleanId.slice(0, 2)}-${cleanId.slice(2)}` : rawId,
        error: isEin ? undefined : "Invalid US EIN (format: XX-XXXXXXX, 9 digits).",
      };
    }

    case "GB": {
      // UK VAT, UTR (Unique Taxpayer Reference), or NINO (National Insurance Number)
      if (idTypePreference?.toUpperCase() === "NINO" || /^[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D]$/i.test(cleanId)) {
        const isNino = /^[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D]$/i.test(cleanId);
        return {
          isValid: isNino,
          countryCode: "GB",
          idType: "NINO",
          formattedId: cleanId.toUpperCase(),
          error: isNino ? undefined : "Invalid UK National Insurance Number format (e.g. QQ123456C).",
        };
      }
      if (idTypePreference?.toUpperCase() === "UTR" || (/^\d{10}$/.test(cleanId) && idTypePreference)) {
        const isUtr = /^\d{10}$/.test(cleanId);
        return {
          isValid: isUtr,
          countryCode: "GB",
          idType: "UTR",
          formattedId: cleanId,
          error: isUtr ? undefined : "Invalid UK UTR (must be 10 digits).",
        };
      }
      // UK VAT: GB + 9 or 12 digits, or just 9/12 digits
      const vatWithoutGb = cleanId.replace(/^GB/i, "");
      const isVat = /^\d{9}$/.test(vatWithoutGb) || /^\d{12}$/.test(vatWithoutGb);
      return {
        isValid: isVat,
        countryCode: "GB",
        idType: "VAT",
        formattedId: isVat ? `GB${vatWithoutGb}` : rawId,
        error: isVat ? undefined : "Invalid UK VAT registration number (9 or 12 digits).",
      };
    }

    case "DE":
    case "FR":
    case "IT":
    case "ES":
    case "NL":
    case "IE":
    case "BE":
    case "AT":
    case "SE":
    case "PL": {
      // EU Member State VAT Number validation
      const prefix = cleanId.slice(0, 2).toUpperCase();
      const numPart = prefix === country ? cleanId.slice(2) : cleanId;
      let valid = false;

      if (country === "DE") valid = /^\d{9}$/.test(numPart);
      else if (country === "FR") valid = /^[A-Z0-9]{2}\d{9}$/i.test(numPart);
      else if (country === "IT") valid = /^\d{11}$/.test(numPart);
      else if (country === "ES") valid = /^[A-Z0-9]\d{7}[A-Z0-9]$/i.test(numPart);
      else if (country === "NL") valid = /^\d{9}B\d{2}$/i.test(numPart);
      else if (country === "IE") valid = /^\d{7}[A-W][A-I]?$/i.test(numPart) || /^\d[A-Z\d\*\+]\d{5}[A-W]$/i.test(numPart);
      else if (country === "BE") valid = /^\d{10}$/.test(numPart);
      else if (country === "AT") valid = /^U\d{8}$/i.test(numPart);
      else if (country === "SE") valid = /^\d{12}$/.test(numPart);
      else if (country === "PL") valid = /^\d{10}$/.test(numPart);
      else valid = /^[A-Z0-9]{8,12}$/i.test(numPart);

      return {
        isValid: valid,
        countryCode: country,
        idType: "EU_VAT",
        formattedId: valid ? `${country}${numPart.toUpperCase()}` : rawId,
        error: valid ? undefined : `Invalid EU VAT number format for ${country}.`,
      };
    }

    case "AE": {
      // UAE Tax Registration Number (TRN): 15 digits starting with 100
      const isTrn = /^100\d{12}$/.test(cleanId);
      return {
        isValid: isTrn,
        countryCode: "AE",
        idType: "TRN",
        formattedId: cleanId,
        error: isTrn ? undefined : "Invalid UAE TRN (must be 15 digits starting with 100).",
      };
    }

    case "SA": {
      // Saudi Arabia VAT/TIN: 15 digits starting and ending with 3
      const isSaudiVat = /^3\d{13}3$/.test(cleanId);
      return {
        isValid: isSaudiVat,
        countryCode: "SA",
        idType: "VAT_TIN",
        formattedId: cleanId,
        error: isSaudiVat ? undefined : "Invalid Saudi VAT/TIN (must be 15 digits starting and ending with 3).",
      };
    }

    case "PK": {
      // Pakistan: NTN (7 digits + 1 check digit), STRN (13 digits), CNIC (13 digits)
      if (idTypePreference?.toUpperCase() === "CNIC" || cleanId.length === 13) {
        const isCnic = /^\d{13}$/.test(cleanId);
        return {
          isValid: isCnic,
          countryCode: "PK",
          idType: "CNIC",
          formattedId: isCnic ? `${cleanId.slice(0, 5)}-${cleanId.slice(5, 12)}-${cleanId.slice(12)}` : rawId,
          error: isCnic ? undefined : "Invalid Pakistani CNIC (13 digits required).",
        };
      }
      const isNtn = /^\d{7,8}$/.test(cleanId);
      return {
        isValid: isNtn,
        countryCode: "PK",
        idType: "NTN",
        formattedId: isNtn && cleanId.length === 8 ? `${cleanId.slice(0, 7)}-${cleanId.slice(7)}` : cleanId,
        error: isNtn ? undefined : "Invalid Pakistani NTN (7 digits + 1 check digit required).",
      };
    }

    case "CA": {
      // Canadian Business Number (BN / GST): 9 digits optionally followed by RT0001
      const isBn = /^\d{9}(RT\d{4})?$/i.test(cleanId);
      return {
        isValid: isBn,
        countryCode: "CA",
        idType: "BN_GST",
        formattedId: cleanId.toUpperCase(),
        error: isBn ? undefined : "Invalid Canadian Business Number (9 digits or 9 digits + RT0001).",
      };
    }

    case "AU": {
      // Australian Business Number (ABN): 11 digits
      const isAbn = /^\d{11}$/.test(cleanId);
      return {
        isValid: isAbn,
        countryCode: "AU",
        idType: "ABN",
        formattedId: isAbn ? `${cleanId.slice(0, 2)} ${cleanId.slice(2, 5)} ${cleanId.slice(5, 8)} ${cleanId.slice(8)}` : rawId,
        error: isAbn ? undefined : "Invalid Australian Business Number (ABN must be 11 digits).",
      };
    }

    case "IN": {
      // India GSTIN: 15 alphanumeric (e.g. 27AAAAA0000A1Z5)
      const isGstin = /^\d{2}[A-Z]{5}\d{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$/i.test(cleanId);
      return {
        isValid: isGstin,
        countryCode: "IN",
        idType: "GSTIN",
        formattedId: cleanId.toUpperCase(),
        error: isGstin ? undefined : "Invalid Indian GSTIN (15-character alphanumeric format required).",
      };
    }

    default: {
      const isValid = cleanId.length >= 4;
      return {
        isValid,
        countryCode: country,
        idType: "TAX_ID",
        formattedId: cleanId,
        error: isValid ? undefined : "Identifier too short (minimum 4 characters).",
      };
    }
  }
}

/**
 * Validates international postal codes by country.
 */
export function validatePostalCode(countryCode: string, postalCode: string): boolean {
  const code = (postalCode || "").trim();
  const country = (countryCode || "").trim().toUpperCase();

  switch (country) {
    case "US":
      // 5 digits or ZIP+4 (XXXXX or XXXXX-XXXX)
      return /^\d{5}(-\d{4})?$/.test(code);
    case "GB":
      // UK Postcode (e.g. SW1A 1AA, EC1A 1BB, W1A 0AX)
      return /^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$/i.test(code);
    case "CA":
      // Canadian Postal Code (A1A 1A1)
      return /^[A-CEGHJ-NPR-TVXY]\d[A-CEGHJ-NPR-TVXY]\s*\d[A-CEGHJ-NPR-TVXY]\d$/i.test(code);
    case "AU":
      // Australian Postcode (4 digits)
      return /^\d{4}$/.test(code);
    case "DE":
    case "FR":
    case "IT":
    case "ES":
      // 5 digits
      return /^\d{5}$/.test(code);
    case "NL":
      // Dutch Postcode (4 digits + 2 letters)
      return /^\d{4}\s*[A-Z]{2}$/i.test(code);
    case "IE":
      // Irish Eircode (e.g. D02 X285)
      return /^[A-Z0-9]{3}\s*[A-Z0-9]{4}$/i.test(code);
    case "PK":
    case "IN":
      // 5 or 6 digits
      return /^\d{5,6}$/.test(code);
    case "AE":
    case "SA":
      // P.O. Box or 5-digit National Address code
      return code.length >= 3;
    default:
      return code.length >= 3;
  }
}

export type AddressComponents = {
  line1: string;
  line2?: string;
  city: string;
  stateOrProvince?: string;
  postalCode?: string;
  countryName: string;
};

/**
 * Formats multi-line postal addresses according to national standards.
 */
export function formatAddress(countryCode: string, address: AddressComponents): string {
  const country = (countryCode || "").trim().toUpperCase();
  const lines: string[] = [address.line1];
  if (address.line2) lines.push(address.line2);

  if (country === "US" || country === "CA" || country === "AU") {
    // City, State ZIP
    const stateZip = [address.stateOrProvince, address.postalCode].filter(Boolean).join(" ");
    const cityStateZip = [address.city, stateZip].filter(Boolean).join(", ");
    lines.push(cityStateZip);
  } else if (country === "GB") {
    // City / Postcode on separate lines
    lines.push(address.city);
    if (address.stateOrProvince) lines.push(address.stateOrProvince);
    if (address.postalCode) lines.push(address.postalCode.toUpperCase());
  } else if (["DE", "FR", "IT", "ES", "NL", "BE", "AT"].includes(country)) {
    // Postcode City
    const postCodeCity = [address.postalCode, address.city].filter(Boolean).join(" ");
    lines.push(postCodeCity);
  } else {
    // General fallback
    const loc = [address.city, address.stateOrProvince, address.postalCode].filter(Boolean).join(", ");
    lines.push(loc);
  }

  lines.push(address.countryName);
  return lines.filter(Boolean).join("\n");
}

export type BusinessCalendarConfig = {
  countryCode: string;
  workDays: number[]; // 0=Sunday, 1=Monday, ..., 6=Saturday
  weekendDays: number[];
  standardWeeklyHours: number;
};

/**
 * Gets standard business calendar and weekend definition for a country.
 */
export function getBusinessCalendar(countryCode: string): BusinessCalendarConfig {
  const country = (countryCode || "").trim().toUpperCase();

  switch (country) {
    case "SA":
      // Saudi Arabia: Sunday-Thursday working days, Friday/Saturday weekend
      return {
        countryCode: "SA",
        workDays: [0, 1, 2, 3, 4], // Sun, Mon, Tue, Wed, Thu
        weekendDays: [5, 6],       // Fri, Sat
        standardWeeklyHours: 48,
      };

    case "AE":
      // UAE: Monday-Friday working week (since Jan 2022 Federal reform), Saturday/Sunday weekend
      return {
        countryCode: "AE",
        workDays: [1, 2, 3, 4, 5],
        weekendDays: [6, 0],
        standardWeeklyHours: 40,
      };

    case "PK":
      // Pakistan: Monday-Friday or Monday-Saturday (commercial sector standard 48 hrs)
      return {
        countryCode: "PK",
        workDays: [1, 2, 3, 4, 5, 6],
        weekendDays: [0],
        standardWeeklyHours: 48,
      };

    default:
      // Standard Western: Monday-Friday, Saturday/Sunday weekend
      return {
        countryCode: country,
        workDays: [1, 2, 3, 4, 5],
        weekendDays: [6, 0],
        standardWeeklyHours: 40,
      };
  }
}
