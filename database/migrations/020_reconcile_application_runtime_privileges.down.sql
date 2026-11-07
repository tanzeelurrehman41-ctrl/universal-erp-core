-- Migration 020 Rollback: Revert Application Runtime Privileges for erp_app
REVOKE SELECT ON platform.plan_features FROM erp_app;
REVOKE SELECT ON platform.subscription_plans FROM erp_app;

REVOKE ALL ON erp_purchasing.goods_received_note_line FROM erp_app;
REVOKE ALL ON erp_purchasing.goods_received_note FROM erp_app;
GRANT SELECT ON erp_purchasing.goods_received_note TO erp_app;

REVOKE ALL ON erp_purchasing.purchase_order_line FROM erp_app;
REVOKE ALL ON erp_purchasing.purchase_order FROM erp_app;
REVOKE ALL ON erp_purchasing.supplier FROM erp_app;

REVOKE ALL ON erp_sales.outbox_event FROM erp_app;
REVOKE ALL ON erp_sales.sales_invoice_line FROM erp_app;
REVOKE ALL ON erp_sales.sales_invoice FROM erp_app;
REVOKE ALL ON erp_sales.sales_order_line FROM erp_app;
REVOKE ALL ON erp_sales.sales_order FROM erp_app;
REVOKE UPDATE ON erp_sales.customer FROM erp_app;
