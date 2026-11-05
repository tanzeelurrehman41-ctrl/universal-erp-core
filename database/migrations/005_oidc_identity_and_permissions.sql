-- Map externally authenticated OIDC identities to provisioned ERP users.
-- Subject values are accepted only after issuer/signature/audience checks in the API.

CREATE TABLE identity.external_identities (
  issuer text NOT NULL,
  subject text NOT NULL,
  user_id uuid NOT NULL REFERENCES identity.users(id),
  linked_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (issuer, subject),
  UNIQUE (issuer, user_id),
  CHECK (length(issuer) BETWEEN 1 AND 2048),
  CHECK (length(subject) BETWEEN 1 AND 1024)
);

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.product.view', 'View the product catalog.', 'inventory'),
  ('inventory.product.create', 'Create products in the tenant catalog.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

-- The least-privileged API can resolve a signed-in subject and verify its live
-- memberships and permission assignments. It cannot create identity links or grants.
GRANT USAGE ON SCHEMA platform, identity, organization, "authorization" TO erp_app;
GRANT SELECT ON identity.external_identities TO erp_app;
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT (tenant_id, user_id, status) ON identity.tenant_memberships TO erp_app;
GRANT SELECT (tenant_id, company_id, user_id, id, status)
  ON identity.company_memberships TO erp_app;
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT (tenant_id, id, status) ON organization.companies TO erp_app;
GRANT SELECT (tenant_id, membership_id, role_id, effective_from, effective_until)
  ON "authorization".membership_role_assignments TO erp_app;
GRANT SELECT (tenant_id, role_id, permission_code, scope_type)
  ON "authorization".role_permissions TO erp_app;
