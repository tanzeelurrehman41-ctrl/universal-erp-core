-- Down Migration: 025_register_sales_permissions.down.sql
BEGIN;

DELETE FROM "authorization".permissions
WHERE permission_code IN (
  'sales.customer.view',
  'sales.customer.create',
  'sales.order.view',
  'sales.order.create',
  'sales.order.confirm',
  'sales.invoice.view',
  'sales.invoice.post'
);

DELETE FROM public.schema_migrations
WHERE version = '025_register_sales_permissions';

COMMIT;
