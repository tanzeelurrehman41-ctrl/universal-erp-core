-- Universal ERP — Migration 045: Controlled Production Launch, Regional Data Residency Enforcement & Customer Pilot Setup
-- Establishes refined regional nodes (AU Sydney/Melbourne, India Mumbai/Hyderabad, UK London, UAE vs Bahrain, Saudi In-Kingdom),
-- fail-closed data residency policies, and isolated commercial pilot tenant environment.

-- 1. Refine Regional Deployment Nodes
INSERT INTO platform.regional_deployment_nodes (
    region_code, region_name, cloud_provider, node_role, data_residency_zones,
    endpoint_url, health_status, latency_ms
) VALUES
    -- Australia: Dedicated In-Country Nodes
    ('ap-southeast-2', 'AWS Sydney (Australia SE-2 Primary)', 'aws', 'PRIMARY_ACTIVE', ARRAY['AU', 'AU_NSW', 'AU_VIC', 'AU_QLD', 'AU_WA', 'AU_SA', 'AU_TAS', 'AU_ACT'], 'https://api-au-sydney.universal-erp.internal', 'HEALTHY', 14),
    ('ap-southeast-4', 'AWS Melbourne (Australia SE-4 Standby)', 'aws', 'READ_REPLICA', ARRAY['AU', 'AU_VIC', 'AU_STANDBY'], 'https://api-au-melbourne.universal-erp.internal', 'HEALTHY', 16),

    -- India: RBI-Compliant Regulated Payment & Transaction Data Gateways
    ('ap-south-1', 'AWS Mumbai (India South-1 Payment Data Gateway)', 'aws', 'PRIMARY_ACTIVE', ARRAY['IN_PAYMENTS', 'IN', 'IN_MH', 'IN_DL'], 'https://api-in-mumbai.universal-erp.internal', 'HEALTHY', 19),
    ('ap-south-2', 'AWS Hyderabad (India South-2 Standby Gateway)', 'aws', 'READ_REPLICA', ARRAY['IN_PAYMENTS', 'IN_STANDBY'], 'https://api-in-hyderabad.universal-erp.internal', 'HEALTHY', 22),

    -- United Kingdom: Dedicated UK GDPR / DPA 2018 Node
    ('eu-west-2', 'AWS London (UK West-2 Primary)', 'aws', 'PRIMARY_ACTIVE', ARRAY['GB', 'UK'], 'https://api-uk-london.universal-erp.internal', 'HEALTHY', 15),

    -- UAE: Dedicated UAE Node (Strictly distinct from Bahrain)
    ('me-central-1-uae', 'AWS UAE (Dubai Me-Central-1 Primary)', 'aws', 'PRIMARY_ACTIVE', ARRAY['AE', 'UAE'], 'https://api-uae-dubai.universal-erp.internal', 'HEALTHY', 18),

    -- Bahrain: Dedicated Bahrain Node
    ('me-south-1-bhr', 'AWS Bahrain (Me-South-1 Primary)', 'aws', 'READ_REPLICA', ARRAY['BH', 'BAHRAIN'], 'https://api-bh-manama.universal-erp.internal', 'HEALTHY', 20),

    -- Saudi Arabia: In-Kingdom Sovereign Cloud Node (Roadmap / Pending Certification)
    ('me-central-2-ksa', 'Saudi Arabia Sovereign Node (Riyadh / Dammam - Certification Pending)', 'aws', 'DR_STANDBY', ARRAY['SA', 'KSA'], 'https://api-ksa-riyadh.universal-erp.internal', 'DRAINING', 30)
ON CONFLICT (region_code) DO UPDATE SET
    region_name = EXCLUDED.region_name,
    data_residency_zones = EXCLUDED.data_residency_zones,
    endpoint_url = EXCLUDED.endpoint_url,
    health_status = EXCLUDED.health_status,
    latency_ms = EXCLUDED.latency_ms;

-- 2. Data Residency Enforcement Policy Registry
CREATE TABLE IF NOT EXISTS platform.tenant_residency_enforcement_policies (
    policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    country_code VARCHAR(2) NOT NULL UNIQUE,
    policy_name VARCHAR(100) NOT NULL,
    regulatory_framework VARCHAR(100) NOT NULL,
    mandatory_in_country_hosting BOOLEAN NOT NULL DEFAULT false,
    provisioning_allowed BOOLEAN NOT NULL DEFAULT true,
    suspension_reason TEXT,
    allowed_primary_regions TEXT[] NOT NULL,
    allowed_failover_regions TEXT[] NOT NULL,
    cross_border_disaster_recovery_allowed BOOLEAN NOT NULL DEFAULT false,
    special_conditions JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_residency_policy_country ON platform.tenant_residency_enforcement_policies(country_code);

-- Seed Fail-Closed Residency Policies
INSERT INTO platform.tenant_residency_enforcement_policies (
    country_code, policy_name, regulatory_framework, mandatory_in_country_hosting,
    provisioning_allowed, suspension_reason, allowed_primary_regions, allowed_failover_regions,
    cross_border_disaster_recovery_allowed, special_conditions
) VALUES
    ('AU', 'Australia APRA CPS 234 & Privacy Act Policy', 'APRA CPS 234 / Australian Privacy Principles', true,
     true, NULL, ARRAY['ap-southeast-2'], ARRAY['ap-southeast-4'],
     false, '{"regulated_data": ["financial", "customer_pii", "audit_trail"], "cross_border_transfer_safeguards": "Strictly restricted to Australian territory"}'::jsonb),

    ('IN', 'India RBI Payment Data & DPDP Policy', 'RBI Master Direction on Storage of Payment System Data & DPDP Act 2023', false,
     true, NULL, ARRAY['ap-south-1', 'us-east-1'], ARRAY['ap-south-2', 'eu-central-1'],
     false, '{"regulated_payment_data_in_country": true, "payment_regions": ["ap-south-1", "ap-south-2"], "general_erp_data_transfer_allowed": true}'::jsonb),

    ('SA', 'Saudi Arabia SAMA CSF & NCA ECC Policy', 'SAMA Cybersecurity Framework & NCA ECC-1:2018 / CCC-1:2020', true,
     false, 'Non-compliant: In-kingdom Saudi Arabia sovereign hosting (Riyadh/Dammam) is pending regulatory compliance audit and formal certification. Tenant provisioning is fail-closed disabled.',
     ARRAY['me-central-2-ksa'], ARRAY[]::text[],
     false, '{"in_kingdom_mandatory": true, "nca_ecc_compliance_required": true}'::jsonb),

    ('AE', 'UAE Federal Data Protection Law Policy', 'UAE Federal Decree-Law No. 45/2021', true,
     true, NULL, ARRAY['me-central-1-uae'], ARRAY['eu-central-1'],
     false, '{"distinct_from_bahrain": true, "telecom_tdra_compliance": true}'::jsonb),

    ('GB', 'United Kingdom GDPR & DPA 2018 Policy', 'UK General Data Protection Regulation & Data Protection Act 2018', true,
     true, NULL, ARRAY['eu-west-2'], ARRAY['eu-central-1'],
     false, '{"uk_adequacy_regime": true, "international_data_transfer_agreement_idta_required_for_non_eea": true}'::jsonb),

    ('DE', 'Germany BDSG & EU GDPR Policy', 'EU GDPR Chapter V & Bundesdatenschutzgesetz (BDSG)', true,
     true, NULL, ARRAY['eu-central-1'], ARRAY['eu-west-2'],
     false, '{"schrems_ii_safeguards": true, "unauthorized_foreign_recovery_prevented": true}'::jsonb),

    ('FR', 'France CNIL & EU GDPR Policy', 'EU GDPR Chapter V & Loi Informatique et Libertés', true,
     true, NULL, ARRAY['eu-central-1'], ARRAY['eu-west-2'],
     false, '{"schrems_ii_safeguards": true}'::jsonb),

    ('CA', 'Canada PIPEDA & Provincial Privacy Policy', 'PIPEDA & Quebec Law 25 / BC FIPPA / Alberta PIPA', false,
     true, NULL, ARRAY['us-east-1'], ARRAY['eu-central-1'],
     true, '{"mandatory_privacy_impact_assessment": "Required for public sector or health workloads", "cross_border_processing_disclosed": true}'::jsonb),

    ('US', 'United States HIPAA / SOC 2 Policy', 'HIPAA / SOC 2 Type II / NIST SP 800-53', false,
     true, NULL, ARRAY['us-east-1'], ARRAY['eu-central-1'],
     true, '{"baa_available": true, "fedramp_moderate_alignment": true}'::jsonb)
ON CONFLICT (country_code) DO UPDATE SET
    policy_name = EXCLUDED.policy_name,
    regulatory_framework = EXCLUDED.regulatory_framework,
    mandatory_in_country_hosting = EXCLUDED.mandatory_in_country_hosting,
    provisioning_allowed = EXCLUDED.provisioning_allowed,
    suspension_reason = EXCLUDED.suspension_reason,
    allowed_primary_regions = EXCLUDED.allowed_primary_regions,
    allowed_failover_regions = EXCLUDED.allowed_failover_regions,
    cross_border_disaster_recovery_allowed = EXCLUDED.cross_border_disaster_recovery_allowed,
    special_conditions = EXCLUDED.special_conditions,
    updated_at = now();

-- 3. Fail-Closed Tenant Provisioning & Disaster Recovery Routing Trigger Function
CREATE OR REPLACE FUNCTION platform.enforce_tenant_residency_rules()
RETURNS TRIGGER AS $$
DECLARE
    v_policy RECORD;
BEGIN
    SELECT * INTO v_policy
    FROM platform.tenant_residency_enforcement_policies
    WHERE country_code = NEW.home_country_code;

    IF FOUND THEN
        -- Rule 1: Fail-closed block if provisioning is disabled for this country (e.g. Saudi Arabia pending in-kingdom sovereign cloud)
        IF NOT v_policy.provisioning_allowed THEN
            RAISE EXCEPTION '[FAIL-CLOSED DATA RESIDENCY] Tenant provisioning is blocked for country %: %',
                NEW.home_country_code, v_policy.suspension_reason;
        END IF;

        -- Rule 2: Ensure primary region is an approved primary hosting region
        IF NOT (NEW.primary_region_code = ANY(v_policy.allowed_primary_regions)) THEN
            RAISE EXCEPTION '[FAIL-CLOSED DATA RESIDENCY] Primary region % is not permitted for country %. Approved regions: %',
                NEW.primary_region_code, NEW.home_country_code, array_to_string(v_policy.allowed_primary_regions, ', ');
        END IF;

        -- Rule 3: Ensure standby/failover region does not violate cross-border restrictions
        IF NEW.standby_region_code IS NOT NULL AND NOT (NEW.standby_region_code = ANY(v_policy.allowed_failover_regions)) THEN
            RAISE EXCEPTION '[FAIL-CLOSED DATA RESIDENCY] Failover/Standby region % is not permitted for country %. Approved failover regions: %',
                NEW.standby_region_code, NEW.home_country_code, array_to_string(v_policy.allowed_failover_regions, ', ');
        END IF;

        -- Rule 4: Prevent unauthorized cross-border replication when prohibited
        IF NEW.cross_border_replication_allowed AND NOT v_policy.cross_border_disaster_recovery_allowed THEN
            RAISE EXCEPTION '[FAIL-CLOSED DATA RESIDENCY] Cross-border data replication is strictly forbidden for country % under %',
                NEW.home_country_code, v_policy.regulatory_framework;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_enforce_tenant_residency_rules ON platform.tenant_data_residency_routes;
CREATE TRIGGER trg_enforce_tenant_residency_rules
    BEFORE INSERT OR UPDATE ON platform.tenant_data_residency_routes
    FOR EACH ROW
    EXECUTE FUNCTION platform.enforce_tenant_residency_rules();

-- 4. Commercial Customer Pilot Environment Setup
-- Pilot Tenant: PILOT-ALPHA-2026
INSERT INTO platform.tenants (
    id, tenant_code, display_name, legal_name, status,
    default_country_code, default_currency_code, default_timezone, default_language_code,
    metadata
) VALUES (
    'a0000000-0000-0000-0000-000000000001',
    'PILOT-ALPHA-2026',
    'Universal Commercial Pilot Client A',
    'Universal Commercial Pilot Enterprise LLC',
    'active',
    'US',
    'USD',
    'America/New_York',
    'en',
    jsonb_build_object(
        'is_pilot', true,
        'pilot_phase', 'Phase 18 Controlled Launch',
        'pilot_cohort', 'Alpha Pilot 2026',
        'payment_mode', 'sandbox',
        'live_payments_allowed', false,
        'live_payroll_allowed', false,
        'outbox_mode', 'isolated_demo_email',
        'max_branches', 5,
        'max_users', 25,
        'model_a_fifo_valuation_active', true
    )
) ON CONFLICT (tenant_code) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    metadata = EXCLUDED.metadata;

-- Pilot Residency Route
INSERT INTO platform.tenant_data_residency_routes (
    route_id, tenant_id, home_country_code, primary_region_code,
    standby_region_code, compliance_regime, cross_border_replication_allowed
) VALUES (
    'b0000000-0000-0000-0000-000000000001',
    'a0000000-0000-0000-0000-000000000001',
    'US',
    'us-east-1',
    'eu-central-1',
    'HIPAA',
    true
) ON CONFLICT (tenant_id) DO NOTHING;

-- Pilot Subscription Plan & Entitlements
INSERT INTO platform.tenant_entitlements (
    tenant_id, feature_code, enabled, quantity_limit, source_type, effective_from
) VALUES
    ('a0000000-0000-0000-0000-000000000001', 'core_sales', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'core_purchasing', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'inventory_fifo', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'manufacturing_work_orders', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'finance_general_ledger', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'enterprise_hr_payroll_demo', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'multi_branch_operations', true, 5, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'intercompany_consolidation', true, NULL, 'plan', now()),
    ('a0000000-0000-0000-0000-000000000001', 'saas_subscriptions', true, NULL, 'plan', now());

-- 5. Pilot Safety Guardrails (Strict Sandbox Enforcement)
CREATE OR REPLACE FUNCTION platform.enforce_pilot_guardrails()
RETURNS TRIGGER AS $$
DECLARE
    v_is_pilot BOOLEAN;
BEGIN
    SELECT COALESCE((metadata->>'is_pilot')::boolean, false) INTO v_is_pilot
    FROM platform.tenants
    WHERE id = NEW.tenant_id;

    IF v_is_pilot THEN
        -- Ensure only sandbox payments are accepted
        IF NEW.payment_provider NOT IN ('sandbox', 'test_dummy', 'demo_raast', 'demo_ach') THEN
            RAISE EXCEPTION '[PILOT SAFETY GUARDRAIL] Pilot tenant % is strictly restricted to sandbox payment providers. Attempted provider: %',
                NEW.tenant_id, NEW.payment_provider;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_enforce_pilot_payments ON platform.subscription_payments;
CREATE TRIGGER trg_enforce_pilot_payments
    BEFORE INSERT OR UPDATE ON platform.subscription_payments
    FOR EACH ROW
    EXECUTE FUNCTION platform.enforce_pilot_guardrails();
