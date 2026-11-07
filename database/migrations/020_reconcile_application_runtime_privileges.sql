-- Migration 020: Reconcile Application Runtime Privileges for erp_app
-- Authoritative reconciliation of least-privileged access for sales orders, POS checkout,
-- purchasing workflow, and public subscription plans exploration.
-- Preserves Forced Row-Level Security (RLS) and strict tenant/company isolation.

-- 1. erp_sales schema grants
GRANT USAGE ON SCHEMA erp_sales TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_sales.customer TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_sales.sales_order TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.outbox_event TO erp_app;

-- 2. erp_purchasing schema grants
GRANT USAGE ON SCHEMA erp_purchasing TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.supplier TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note_line TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;

-- 3. platform schema grants (public plan exploration catalog)
GRANT USAGE ON SCHEMA platform TO erp_app;
GRANT SELECT ON platform.subscription_plans TO erp_app;
GRANT SELECT ON platform.plan_features TO erp_app;

-- 4. Fail-closed defense: Explicitly ensure DELETE, TRUNCATE, REFERENCES, TRIGGER remain strictly denied
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_sales FROM erp_app;
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_purchasing FROM erp_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON platform.subscription_plans, platform.plan_features FROM erp_app;
