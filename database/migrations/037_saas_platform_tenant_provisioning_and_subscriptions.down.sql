-- Migration 037 Down: Rollback SaaS Platform, Tenant Provisioning & Subscription Management

BEGIN;

-- Drop newly created tables
DROP TABLE IF EXISTS platform.saas_platform_alerts CASCADE;
DROP TABLE IF EXISTS platform.support_access_grants CASCADE;
DROP TABLE IF EXISTS platform.subscription_credit_notes CASCADE;
DROP TABLE IF EXISTS platform.subscription_payments CASCADE;
DROP TABLE IF EXISTS platform.subscription_invoice_lines CASCADE;
DROP TABLE IF EXISTS platform.subscription_invoices CASCADE;
DROP TABLE IF EXISTS platform.tenant_billing_credits CASCADE;
DROP TABLE IF EXISTS platform.billing_tax_rules CASCADE;
DROP TABLE IF EXISTS platform.tenant_usage_aggregations CASCADE;
DROP TABLE IF EXISTS platform.tenant_invitations CASCADE;
DROP TABLE IF EXISTS platform.plan_pricing CASCADE;

-- Drop added columns from subscriptions
ALTER TABLE platform.subscriptions
  DROP COLUMN IF EXISTS current_price,
  DROP COLUMN IF EXISTS overage_policy,
  DROP COLUMN IF EXISTS overage_rate,
  DROP COLUMN IF EXISTS last_billed_at,
  DROP COLUMN IF EXISTS next_billing_at,
  DROP COLUMN IF EXISTS proration_credit;

-- Revert subscriptions status check constraint
ALTER TABLE platform.subscriptions DROP CONSTRAINT IF EXISTS subscriptions_status_check;
ALTER TABLE platform.subscriptions ADD CONSTRAINT subscriptions_status_check 
  CHECK (status = ANY (ARRAY['trial'::text, 'active'::text, 'past_due'::text, 'grace'::text, 'suspended'::text, 'cancelled'::text, 'expired'::text]));

-- Drop added columns from subscription_plans
ALTER TABLE platform.subscription_plans
  DROP COLUMN IF EXISTS tier_rank,
  DROP COLUMN IF EXISTS is_public;

-- Drop added columns from tenants
ALTER TABLE platform.tenants
  DROP COLUMN IF EXISTS suspended_at,
  DROP COLUMN IF EXISTS suspension_reason,
  DROP COLUMN IF EXISTS archived_at,
  DROP COLUMN IF EXISTS metadata;

-- Revert tenants status check constraint
ALTER TABLE platform.tenants DROP CONSTRAINT IF EXISTS tenants_status_check;
ALTER TABLE platform.tenants ADD CONSTRAINT tenants_status_check 
  CHECK (status = ANY (ARRAY['pending'::text, 'active'::text, 'suspended'::text, 'closed'::text]));

COMMIT;
