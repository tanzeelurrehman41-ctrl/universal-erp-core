-- Migration Down: 024_register_purchasing_permissions.down.sql
-- Module: purchasing

BEGIN;

DELETE FROM "authorization".permissions
WHERE permission_code IN (
  'purchasing.supplier.view',
  'purchasing.supplier.create',
  'purchasing.po.view',
  'purchasing.po.create',
  'purchasing.po.confirm',
  'purchasing.grn.view',
  'purchasing.grn.post'
);

DELETE FROM public.schema_migrations
WHERE version = '024_register_purchasing_permissions';

COMMIT;
