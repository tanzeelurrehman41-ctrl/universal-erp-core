import test from 'node:test';
import assert from 'node:assert/strict';
import {
  calculateNextBackoffDelaySeconds,
  isDeliveryExhausted,
  formatDeliveryStatus,
  validateWebhookUrl,
  maskSecret
} from '../src/integrationHelpers.ts';

test('calculateNextBackoffDelaySeconds computes exponential delay up to 5 attempts', () => {
  assert.equal(calculateNextBackoffDelaySeconds(0), 0);
  assert.equal(calculateNextBackoffDelaySeconds(1), 60);  // 2^1 * 30 = 60s
  assert.equal(calculateNextBackoffDelaySeconds(2), 120); // 2^2 * 30 = 120s
  assert.equal(calculateNextBackoffDelaySeconds(3), 240); // 2^3 * 30 = 240s
  assert.equal(calculateNextBackoffDelaySeconds(4), 480); // 2^4 * 30 = 480s
  assert.equal(calculateNextBackoffDelaySeconds(5), -1);  // Exhausted -> dead letter
});

test('isDeliveryExhausted checks max retry limit', () => {
  assert.equal(isDeliveryExhausted(3), false);
  assert.equal(isDeliveryExhausted(4), false);
  assert.equal(isDeliveryExhausted(5), true);
  assert.equal(isDeliveryExhausted(6), true);
});

test('formatDeliveryStatus formats delivery lifecycle statuses', () => {
  assert.equal(formatDeliveryStatus('pending').label, 'Pending Delivery');
  assert.equal(formatDeliveryStatus('delivered').label, 'Delivered (200 OK)');
  assert.equal(formatDeliveryStatus('failed').label, 'Failed (Retrying)');
  assert.equal(formatDeliveryStatus('exhausted').label, 'Dead Letter (Exhausted)');
});

test('validateWebhookUrl accepts valid absolute HTTP and HTTPS URLs', () => {
  assert.equal(validateWebhookUrl('https://api.partner.com/erp/webhooks'), true);
  assert.equal(validateWebhookUrl('http://localhost:8080/events'), true);
  assert.equal(validateWebhookUrl('invalid-url-string'), false);
  assert.equal(validateWebhookUrl('ftp://files.partner.com'), false);
  assert.equal(validateWebhookUrl(''), false);
});

test('maskSecret redacts secret characters for secure UI rendering', () => {
  assert.equal(maskSecret('erp_sec_abcdef1234567890'), 'erp_...7890');
  assert.equal(maskSecret('short'), '********');
  assert.equal(maskSecret(''), '');
});
