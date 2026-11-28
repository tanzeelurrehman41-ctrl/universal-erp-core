import test from 'node:test';
import assert from 'node:assert/strict';
import {
  parseGs1Barcode,
  formatGs1Barcode,
  parsePrefixedBarcode,
  sequencePickPath,
  calculateReplenishment,
  calculateBinUtilization,
  formatBinLocation,
  type PickItem,
} from '../src/warehouseHelpers.ts';

test('parseGs1Barcode correctly parses composite parenthesized GS1-128 barcode', () => {
  const barcode = '(01)08901234567890(17)261231(10)BATCH-404(21)SN-9988(37)50';
  const result = parseGs1Barcode(barcode);

  assert.equal(result.isValid, true);
  assert.equal(result.gtin, '08901234567890');
  assert.equal(result.expiryDate, '2026-12-31');
  assert.equal(result.lot, 'BATCH-404');
  assert.equal(result.serialNumber, 'SN-9988');
  assert.equal(result.quantity, 50);
});

test('parseGs1Barcode parses net weight AI 3102 with 2 decimal places', () => {
  const barcode = '(01)01234567890128(3102)01550'; // 15.50 kg
  const result = parseGs1Barcode(barcode);

  assert.equal(result.isValid, true);
  assert.equal(result.gtin, '01234567890128');
  assert.equal(result.netWeightKg, 15.5);
});

test('parseGs1Barcode falls back to standard GTIN/UPC when given numeric string', () => {
  const result = parseGs1Barcode('12345678901234');
  assert.equal(result.isValid, true);
  assert.equal(result.gtin, '12345678901234');
});

test('formatGs1Barcode formats composite GS1-128 string', () => {
  const formatted = formatGs1Barcode('8901234567890', 'LOT-99', '2026-12-31', 'SER-001', 25);
  assert.equal(formatted, '(01)08901234567890(17)261231(10)LOT-99(21)SER-001(37)25');
});

test('parsePrefixedBarcode recognizes warehouse Code 128 prefixes', () => {
  const bin = parsePrefixedBarcode('BIN:A-01-02-01');
  assert.equal(bin.isPrefixed, true);
  assert.equal(bin.type, 'bin');
  assert.equal(bin.code, 'A-01-02-01');

  const wave = parsePrefixedBarcode('WAVE:W-2026-001');
  assert.equal(wave.isPrefixed, true);
  assert.equal(wave.type, 'wave');
  assert.equal(wave.code, 'W-2026-001');

  const pkg = parsePrefixedBarcode('PKG:CARTON-88');
  assert.equal(pkg.isPrefixed, true);
  assert.equal(pkg.type, 'package');
  assert.equal(pkg.code, 'CARTON-88');

  const raw = parsePrefixedBarcode('RAW-CODE-123');
  assert.equal(raw.isPrefixed, false);
  assert.equal(raw.type, 'unknown');
  assert.equal(raw.code, 'RAW-CODE-123');
});

test('sequencePickPath alternates directions in serpentine fashion across aisles', () => {
  const items: PickItem[] = [
    // Aisle 1 (odd) -> rack descending
    { id: '1', variantId: 'v1', productName: 'Item A', sku: 'A', binId: 'b1', binCode: 'B1', zoneCode: 'ZONE-A', aisle: '1', rack: '1', shelf: '1', position: '1', quantityRequired: 5 },
    { id: '2', variantId: 'v2', productName: 'Item B', sku: 'B', binId: 'b2', binCode: 'B2', zoneCode: 'ZONE-A', aisle: '1', rack: '4', shelf: '1', position: '1', quantityRequired: 2 },
    // Aisle 2 (even) -> rack ascending
    { id: '3', variantId: 'v3', productName: 'Item C', sku: 'C', binId: 'b3', binCode: 'B3', zoneCode: 'ZONE-A', aisle: '2', rack: '5', shelf: '1', position: '1', quantityRequired: 1 },
    { id: '4', variantId: 'v4', productName: 'Item D', sku: 'D', binId: 'b4', binCode: 'B4', zoneCode: 'ZONE-A', aisle: '2', rack: '2', shelf: '1', position: '1', quantityRequired: 4 },
  ];

  const sequenced = sequencePickPath(items);
  assert.equal(sequenced.length, 4);

  // Aisle 1: Rack 4 descending before Rack 1
  assert.equal(sequenced[0].id, '2'); // Aisle 1, Rack 4
  assert.equal(sequenced[0].pickSequence, 1);
  assert.equal(sequenced[1].id, '1'); // Aisle 1, Rack 1
  assert.equal(sequenced[1].pickSequence, 2);

  // Aisle 2: Rack 2 ascending before Rack 5
  assert.equal(sequenced[2].id, '4'); // Aisle 2, Rack 2
  assert.equal(sequenced[2].pickSequence, 3);
  assert.equal(sequenced[3].id, '3'); // Aisle 2, Rack 5
  assert.equal(sequenced[3].pickSequence, 4);
});

test('calculateReplenishment triggers replenishment rounded up to increment when stock <= reorder', () => {
  // Current: 5, Reorder: 10, Max: 50, Increment: 10, Reserve: 100
  // Deficit = 50 - 5 = 45 -> rounded up to 50
  const result = calculateReplenishment(5, 50, 10, 10, 5, 100);

  assert.equal(result.needsReplenishment, true);
  assert.equal(result.recommendedQuantity, 50);

  // When stock is above reorder point
  const noReplenish = calculateReplenishment(5, 50, 10, 10, 15, 100);
  assert.equal(noReplenish.needsReplenishment, false);
  assert.equal(noReplenish.recommendedQuantity, 0);

  // When reserve is lower than deficit
  const cappedReserve = calculateReplenishment(5, 50, 10, 10, 5, 30);
  assert.equal(cappedReserve.needsReplenishment, true);
  assert.equal(cappedReserve.recommendedQuantity, 30); // Capped at reserve
});

test('calculateBinUtilization calculates percentage accurately', () => {
  assert.equal(calculateBinUtilization(25, 100), 25.0);
  assert.equal(calculateBinUtilization(33, 100), 33.0);
  assert.equal(calculateBinUtilization(0, 50), 0);
  assert.equal(calculateBinUtilization(60, 50), 120.0);
});

test('formatBinLocation formats location hierarchy', () => {
  assert.equal(formatBinLocation('ZONE-A', '01', '02', '03', '01'), 'ZONE-A-01-02-03-01');
  assert.equal(formatBinLocation('MAIN', '01'), 'MAIN-01');
});
