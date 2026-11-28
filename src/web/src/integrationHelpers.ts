/**
 * Universal ERP - Phase 13 External Integrations & Webhook Helpers
 */

export const MAX_RETRY_ATTEMPTS = 5;
export const BASE_BACKOFF_SECONDS = 30;

/**
 * Calculates exponential backoff delay for delivery retries (2^attempt * baseSeconds).
 */
export function calculateNextBackoffDelaySeconds(attemptCount: number, baseSeconds: number = BASE_BACKOFF_SECONDS): number {
  if (attemptCount <= 0) return 0;
  if (attemptCount >= MAX_RETRY_ATTEMPTS) return -1; // Exhausted -> Dead letter
  const seconds = Math.pow(2, attemptCount) * baseSeconds;
  return Math.min(seconds, 86400); // 24 hours max
}

/**
 * Checks if delivery has exceeded max retry threshold and moved to dead-letter state.
 */
export function isDeliveryExhausted(attemptCount: number, maxAttempts: number = MAX_RETRY_ATTEMPTS): boolean {
  return attemptCount >= maxAttempts;
}

/**
 * Formats delivery status with readable text and badge class.
 */
export function formatDeliveryStatus(status: string): { label: string; badgeClass: string } {
  switch (status.toLowerCase()) {
    case 'pending':
      return { label: 'Pending Delivery', badgeClass: 'badge-pending' };
    case 'delivered':
      return { label: 'Delivered (200 OK)', badgeClass: 'badge-delivered' };
    case 'failed':
      return { label: 'Failed (Retrying)', badgeClass: 'badge-failed' };
    case 'exhausted':
      return { label: 'Dead Letter (Exhausted)', badgeClass: 'badge-exhausted' };
    default:
      return { label: status, badgeClass: 'badge-default' };
  }
}

/**
 * Validates absolute HTTPS or HTTP webhook target URL.
 */
export function validateWebhookUrl(url: string): boolean {
  if (!url || url.trim().length === 0) return false;
  try {
    const parsed = new URL(url.trim());
    return parsed.protocol === 'http:' || parsed.protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * Masks sensitive API keys or webhook signing secrets for safe display.
 */
export function maskSecret(secret: string): string {
  if (!secret) return '';
  if (secret.length <= 8) return '********';
  return `${secret.substring(0, 4)}...${secret.substring(secret.length - 4)}`;
}
