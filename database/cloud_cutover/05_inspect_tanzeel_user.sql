-- Universal ERP -- Read-Only Inspection for Tanzeel Staging Account
-- Target Database: Neon PostgreSQL (neondb)
-- Firebase Project: universal-erp-staging
-- Email: tanzeelurrehman41@gmail.com
-- UID: nnnmCg7nT0cehV14o5APFFtY2s53
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

\echo '=========================================================================='
\echo '1. Database Identity Verification'
\echo '=========================================================================='
SELECT current_database() AS database_name,
       current_user AS connected_user,
       inet_server_addr() AS server_ip,
       version() AS postgres_version;

\echo '=========================================================================='
\echo '2. Target Demo Tenant & Company Status'
\echo '=========================================================================='
SELECT t.id AS tenant_id,
       t.tenant_code,
       t.display_name AS tenant_name,
       t.status AS tenant_status,
       c.id AS company_id,
       c.company_code,
       c.name AS company_name,
       c.status AS company_status
FROM platform.tenants t
LEFT JOIN organization.companies c 
       ON c.tenant_id = t.id 
      AND c.id = '22222222-2222-4222-8222-222222222223'::uuid
WHERE t.id = '22222222-2222-4222-8222-222222222222'::uuid;

\echo '=========================================================================='
\echo '3. Existing identity.users Matching tanzeelurrehman41@gmail.com or Tanzeel'
\echo '=========================================================================='
SELECT id,
       email_normalized,
       display_name,
       status,
       preferred_language_code,
       mfa_required,
       created_at,
       updated_at
FROM identity.users
WHERE email_normalized = 'tanzeelurrehman41@gmail.com'
   OR display_name ILIKE '%tanzeel%';

\echo '=========================================================================='
\echo '4. Existing External Identity Mappings (by UID or Target User)'
\echo '=========================================================================='
SELECT ext.issuer,
       ext.subject,
       ext.user_id,
       u.email_normalized,
       u.display_name,
       ext.linked_at
FROM identity.external_identities ext
LEFT JOIN identity.users u ON u.id = ext.user_id
WHERE ext.subject = 'nnnmCg7nT0cehV14o5APFFtY2s53'
   OR u.email_normalized = 'tanzeelurrehman41@gmail.com'
   OR u.display_name ILIKE '%tanzeel%';

\echo '=========================================================================='
\echo '5. Tenant Memberships for Target User'
\echo '=========================================================================='
SELECT tm.id AS membership_id,
       tm.tenant_id,
       t.tenant_code,
       t.display_name AS tenant_name,
       tm.user_id,
       u.email_normalized,
       tm.status,
       tm.joined_at,
       tm.created_at
FROM identity.tenant_memberships tm
JOIN platform.tenants t ON t.id = tm.tenant_id
JOIN identity.users u ON u.id = tm.user_id
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com'
   OR u.display_name ILIKE '%tanzeel%';

\echo '=========================================================================='
\echo '6. Company Memberships for Target User'
\echo '=========================================================================='
SELECT cm.id AS membership_id,
       cm.tenant_id,
       cm.company_id,
       c.company_code,
       c.name AS company_name,
       cm.user_id,
       u.email_normalized,
       cm.status,
       cm.created_at
FROM identity.company_memberships cm
JOIN organization.companies c 
  ON c.id = cm.company_id 
 AND c.tenant_id = cm.tenant_id
JOIN identity.users u ON u.id = cm.user_id
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com'
   OR u.display_name ILIKE '%tanzeel%';

\echo '=========================================================================='
\echo '7. Conflict Diagnostic Check'
\echo '=========================================================================='
SELECT 
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM identity.external_identities 
            WHERE issuer = 'https://securetoken.google.com/universal-erp-staging' 
              AND subject = 'nnnmCg7nT0cehV14o5APFFtY2s53'
              AND user_id NOT IN (
                  SELECT id FROM identity.users 
                  WHERE email_normalized = 'tanzeelurrehman41@gmail.com' 
                     OR display_name ILIKE '%tanzeel%'
              )
        ) THEN 'CONFLICT: UID nnnmCg7nT0cehV14o5APFFtY2s53 is linked to an unrelated user!'
        WHEN EXISTS (
            SELECT 1 FROM identity.external_identities ext
            JOIN identity.users u ON u.id = ext.user_id
            WHERE ext.issuer = 'https://securetoken.google.com/universal-erp-staging'
              AND ext.subject <> 'nnnmCg7nT0cehV14o5APFFtY2s53'
              AND (u.email_normalized = 'tanzeelurrehman41@gmail.com' OR u.display_name ILIKE '%tanzeel%')
        ) THEN 'CONFLICT: Tanzeel ERP user already has a different external identity subject!'
        ELSE 'CLEAN: No conflicting identity mappings found. Safe to link.'
    END AS conflict_diagnostic;

\echo '=========================================================================='
\echo '8. Current Workspace Context Lookup for UID nnnmCg7nT0cehV14o5APFFtY2s53'
\echo '=========================================================================='
SELECT * FROM identity.list_user_company_contexts(
    'https://securetoken.google.com/universal-erp-staging',
    'nnnmCg7nT0cehV14o5APFFtY2s53'
);
