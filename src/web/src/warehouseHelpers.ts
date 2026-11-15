export interface ParsedGs1 {
  raw: string;
  gtin?: string;
  lot?: string;
  expiryDate?: string; // YYYY-MM-DD
  serialNumber?: string;
  netWeightKg?: number;
  quantity?: number;
  isValid: boolean;
  errorMessage?: string;
}

export interface PickItem {
  id: string;
  variantId: string;
  productName: string;
  sku: string;
  binId: string;
  binCode: string;
  zoneCode: string;
  aisle: string;
  rack: string;
  shelf: string;
  position: string;
  quantityRequired: number;
  quantityPicked?: number;
  status?: string;
  pickSequence?: number;
}

export interface ReplenishmentResult {
  needsReplenishment: boolean;
  recommendedQuantity: number;
  currentStock: number;
  reorderPoint: number;
  maxCapacity: number;
  reserveAvailable: number;
  reason: string;
}

export interface PrefixedBarcode {
  type: 'bin' | 'product' | 'wave' | 'package' | 'work_order' | 'pallet' | 'serial' | 'unknown';
  code: string;
  raw: string;
  isPrefixed: boolean;
}

/**
 * Parses GS1-128 barcode strings containing Application Identifiers (AIs).
 * Supports both parenthesized syntax e.g. (01)12345678901234(10)LOT123 and raw concatenated formats.
 */
export function parseGs1Barcode(barcode: string): ParsedGs1 {
  const result: ParsedGs1 = {
    raw: barcode,
    isValid: false,
  };

  const trimmed = barcode ? barcode.trim() : '';
  if (!trimmed) {
    result.errorMessage = 'Barcode is empty';
    return result;
  }

  // Parenthesized format parsing: e.g. (01)12345678901234(17)261231(10)LOT100
  if (trimmed.startsWith('(')) {
    const aiPattern = /\((\d{2,4})\)([^()]+)/g;
    let match: RegExpExecArray | null;
    let matchCount = 0;

    while ((match = aiPattern.exec(trimmed)) !== null) {
      matchCount++;
      const ai = match[1];
      const val = match[2];

      switch (ai) {
        case '01': // GTIN (14 digits)
          result.gtin = val.padStart(14, '0');
          break;
        case '10': // Batch / Lot
          result.lot = val;
          break;
        case '17': // Expiry (YYMMDD)
          if (val.length === 6) {
            const yy = parseInt(val.substring(0, 2), 10);
            const mm = val.substring(2, 4);
            const dd = val.substring(4, 6);
            const fullYear = yy >= 50 ? 1900 + yy : 2000 + yy;
            result.expiryDate = `${fullYear}-${mm}-${dd}`;
          } else {
            result.expiryDate = val;
          }
          break;
        case '21': // Serial
          result.serialNumber = val;
          break;
        case '37': // Quantity / Count
          result.quantity = parseInt(val, 10);
          break;
        default:
          if (ai.startsWith('310')) {
            // Net weight in kg: AI 310x where x is decimals (e.g. 3102 = 2 decimals)
            const dec = parseInt(ai.charAt(3), 10);
            const num = parseFloat(val);
            result.netWeightKg = dec > 0 ? num / Math.pow(10, dec) : num;
          }
          break;
      }
    }

    if (matchCount > 0) {
      result.isValid = true;
      return result;
    }
  }

  // Fallback: check if 14-digit GTIN or 12/13-digit UPC/EAN
  if (/^\d{8,14}$/.test(trimmed)) {
    result.gtin = trimmed.padStart(14, '0');
    result.isValid = true;
    return result;
  }

  result.errorMessage = 'Unsupported or unrecognized GS1 barcode format';
  return result;
}

/**
 * Formats GS1 data into parenthesized human-readable GS1-128 string.
 */
export function formatGs1Barcode(
  gtin: string,
  lot?: string,
  expiry?: string, // YYYY-MM-DD or YYMMDD
  serial?: string,
  count?: number
): string {
  let output = '';
  if (gtin) {
    const cleanGtin = gtin.replace(/\D/g, '').padStart(14, '0');
    output += `(01)${cleanGtin}`;
  }
  if (expiry) {
    let cleanExp = expiry.replace(/\D/g, '');
    if (cleanExp.length === 8) {
      // YYYYMMDD -> YYMMDD
      cleanExp = cleanExp.substring(2);
    }
    output += `(17)${cleanExp}`;
  }
  if (lot) {
    output += `(10)${lot.trim()}`;
  }
  if (serial) {
    output += `(21)${serial.trim()}`;
  }
  if (count !== undefined && count > 0) {
    output += `(37)${Math.floor(count)}`;
  }
  return output;
}

/**
 * Recognizes warehouse Code 128 prefix format: e.g. "BIN:A-01-02", "PROD:SKU-100", "WAVE:W-2026-001".
 */
export function parsePrefixedBarcode(barcode: string): PrefixedBarcode {
  const trimmed = barcode ? barcode.trim() : '';
  const colonIdx = trimmed.indexOf(':');

  if (colonIdx > 0) {
    const prefix = trimmed.substring(0, colonIdx).toUpperCase();
    const code = trimmed.substring(colonIdx + 1).trim();

    switch (prefix) {
      case 'BIN':
      case 'LOC':
        return { type: 'bin', code, raw: trimmed, isPrefixed: true };
      case 'PROD':
      case 'SKU':
      case 'ITEM':
        return { type: 'product', code, raw: trimmed, isPrefixed: true };
      case 'WAVE':
        return { type: 'wave', code, raw: trimmed, isPrefixed: true };
      case 'PKG':
      case 'BOX':
      case 'CARTON':
        return { type: 'package', code, raw: trimmed, isPrefixed: true };
      case 'WO':
        return { type: 'work_order', code, raw: trimmed, isPrefixed: true };
      case 'PALLET':
      case 'PLT':
        return { type: 'pallet', code, raw: trimmed, isPrefixed: true };
      case 'SER':
      case 'SN':
        return { type: 'serial', code, raw: trimmed, isPrefixed: true };
      default:
        return { type: 'unknown', code, raw: trimmed, isPrefixed: true };
    }
  }

  return { type: 'unknown', code: trimmed, raw: trimmed, isPrefixed: false };
}

/**
 * Sequences picking items into an optimal warehouse serpentine (S-shape) path:
 * 1. Groups by Zone ascending.
 * 2. Groups by Aisle ascending.
 * 3. Even aisles: traverse Rack ascending, Shelf ascending, Position ascending.
 * 4. Odd aisles: traverse Rack descending, Shelf descending, Position descending.
 * 5. Reassigns 1-based sequential pickSequence numbers.
 */
export function sequencePickPath(items: PickItem[]): PickItem[] {
  if (!items || items.length === 0) return [];

  const copy = [...items];

  copy.sort((a, b) => {
    // 1. Zone
    const zoneDiff = (a.zoneCode || '').localeCompare(b.zoneCode || '');
    if (zoneDiff !== 0) return zoneDiff;

    // 2. Aisle
    const aisleA = parseInt(a.aisle || '0', 10);
    const aisleB = parseInt(b.aisle || '0', 10);
    if (!isNaN(aisleA) && !isNaN(aisleB) && aisleA !== aisleB) {
      return aisleA - aisleB;
    }
    const aisleDiff = (a.aisle || '').localeCompare(b.aisle || '');
    if (aisleDiff !== 0) return aisleDiff;

    // Determine direction for this aisle (even = ascending, odd = descending)
    const isEvenAisle = isNaN(aisleA) ? true : aisleA % 2 === 0;

    // 3. Rack
    const rackA = parseInt(a.rack || '0', 10);
    const rackB = parseInt(b.rack || '0', 10);
    if (!isNaN(rackA) && !isNaN(rackB) && rackA !== rackB) {
      return isEvenAisle ? rackA - rackB : rackB - rackA;
    }
    const rackDiff = (a.rack || '').localeCompare(b.rack || '');
    if (rackDiff !== 0) {
      return isEvenAisle ? rackDiff : -rackDiff;
    }

    // 4. Shelf
    const shelfA = parseInt(a.shelf || '0', 10);
    const shelfB = parseInt(b.shelf || '0', 10);
    if (!isNaN(shelfA) && !isNaN(shelfB) && shelfA !== shelfB) {
      return isEvenAisle ? shelfA - shelfB : shelfB - shelfA;
    }
    const shelfDiff = (a.shelf || '').localeCompare(b.shelf || '');
    if (shelfDiff !== 0) {
      return isEvenAisle ? shelfDiff : -shelfDiff;
    }

    // 5. Position
    const posA = parseInt(a.position || '0', 10);
    const posB = parseInt(b.position || '0', 10);
    if (!isNaN(posA) && !isNaN(posB) && posA !== posB) {
      return isEvenAisle ? posA - posB : posB - posA;
    }
    return (a.position || '').localeCompare(b.position || '');
  });

  return copy.map((item, idx) => ({
    ...item,
    pickSequence: idx + 1,
  }));
}

/**
 * Calculates replenishment recommendation based on min, max, reorder threshold, increment, and available reserve.
 */
export function calculateReplenishment(
  min: number,
  max: number,
  reorder: number,
  increment: number,
  current: number,
  reserveAvailable: number
): ReplenishmentResult {
  const safeCurrent = Math.max(0, current);
  const safeMin = Math.max(0, min);
  const safeMax = Math.max(safeMin, max);
  const safeReorder = Math.max(safeMin, reorder);
  const safeIncrement = increment > 0 ? increment : 1;
  const safeReserve = Math.max(0, reserveAvailable);

  if (safeCurrent > safeReorder) {
    return {
      needsReplenishment: false,
      recommendedQuantity: 0,
      currentStock: safeCurrent,
      reorderPoint: safeReorder,
      maxCapacity: safeMax,
      reserveAvailable: safeReserve,
      reason: `Current stock (${safeCurrent}) is above reorder threshold (${safeReorder}).`,
    };
  }

  const deficit = safeMax - safeCurrent;
  if (deficit <= 0) {
    return {
      needsReplenishment: false,
      recommendedQuantity: 0,
      currentStock: safeCurrent,
      reorderPoint: safeReorder,
      maxCapacity: safeMax,
      reserveAvailable: safeReserve,
      reason: 'Bin is already at maximum capacity.',
    };
  }

  // Round up to nearest increment
  let replenishmentQty = Math.ceil(deficit / safeIncrement) * safeIncrement;

  if (safeReserve <= 0) {
    return {
      needsReplenishment: true,
      recommendedQuantity: 0,
      currentStock: safeCurrent,
      reorderPoint: safeReorder,
      maxCapacity: safeMax,
      reserveAvailable: 0,
      reason: `Deficit of ${deficit} units detected, but no reserve stock is available.`,
    };
  }

  // Cap at reserve stock available
  replenishmentQty = Math.min(replenishmentQty, safeReserve);

  return {
    needsReplenishment: true,
    recommendedQuantity: replenishmentQty,
    currentStock: safeCurrent,
    reorderPoint: safeReorder,
    maxCapacity: safeMax,
    reserveAvailable: safeReserve,
    reason: `Replenish ${replenishmentQty} units to restore pick face to capacity (${safeMax}).`,
  };
}

/**
 * Computes volumetric or unit utilization percentage.
 */
export function calculateBinUtilization(currentUnits: number, maxUnits?: number): number {
  if (!maxUnits || maxUnits <= 0) return 0;
  if (currentUnits <= 0) return 0;
  const percent = (currentUnits / maxUnits) * 100;
  return Math.round(percent * 10) / 10;
}

/**
 * Formats a hierarchical warehouse location identifier.
 */
export function formatBinLocation(
  zone: string,
  aisle?: string,
  rack?: string,
  shelf?: string,
  position?: string
): string {
  const parts = [zone.trim()];
  if (aisle) parts.push(aisle.trim());
  if (rack) parts.push(rack.trim());
  if (shelf) parts.push(shelf.trim());
  if (position) parts.push(position.trim());
  return parts.join('-');
}
