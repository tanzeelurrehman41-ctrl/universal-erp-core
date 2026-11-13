import sys
import re
import pglast

def validate_all():
    files = [
        "database/cloud_cutover/26_seed_demo_finance_and_gl_accounts.sql",
        "database/cloud_cutover/27_requeue_dead_letter_outbox_events.sql",
        "database/cloud_cutover/28_verify_purchasing_outbox_processing.sql",
        "database/cloud_cutover/29_inspect_finance_workspace_verification.sql",
        "database/cloud_cutover/30_inspect_inventory_valuation_reconciliation.sql",
        "database/cloud_cutover/31_inspect_sales_workspace_preconditions.sql",
        "database/cloud_cutover/32_apply_migration_025_sales_permissions.sql",
        "database/cloud_cutover/33_propose_demo_sales_operator.sql",
        "database/cloud_cutover/34_apply_and_verify_sales_authorization.sql",
        "database/cloud_cutover/35_verify_sales_and_pos_execution.sql",
        "database/cloud_cutover/36_verify_sales_authorization_rollback_state.sql",
        "database/cloud_cutover/37_inspect_sales_and_pos_state.sql",
        "database/cloud_cutover/38_propose_fifo_layer_backfill.sql",
        "database/cloud_cutover/39_verify_fifo_valuation_state.sql",
        "database/cloud_cutover/40_inspect_duplicate_layers_and_allocations.sql",
        "database/migrations/025_register_sales_permissions.sql",
        "database/migrations/025_register_sales_permissions.down.sql",
        "database/migrations/026_fifo_valuation_layers.sql",
        "database/migrations/026_fifo_valuation_layers.down.sql"
    ]

    all_passed = True

    for sql_path in files:
        print(f"\n=======================================================")
        print(f"Validating {sql_path} with PostgreSQL libpg_query...")
        print(f"=======================================================")
        with open(sql_path, "r", encoding="utf-8") as f:
            content = f.read()

        # Remove psql-specific meta commands (\echo, etc.) for pure SQL AST parsing
        sql_clean = re.sub(r'^\\[a-zA-Z]+.*$', '', content, flags=re.MULTILINE)

        try:
            stmts = pglast.parse_sql(sql_clean)
            print(f"[PASS] Top-level parse successful. Parsed {len(stmts)} top-level statements.")
            for idx, stmt in enumerate(stmts):
                print(f"  Stmt {idx + 1}: {stmt.stmt.__class__.__name__}")
        except Exception as e:
            print(f"[FAIL] Top-level parse failed: {e}")
            all_passed = False

    # Deep verification of 27_requeue_dead_letter_outbox_events.sql
    with open("database/cloud_cutover/27_requeue_dead_letter_outbox_events.sql", "r", encoding="utf-8") as f:
        c27 = f.read()

    do_match = re.search(r'DO\s+\$\$(.*?)\$\$;', c27, re.DOTALL)
    do_body = do_match.group(1)

    cte_pattern = re.search(r'(WITH\s+updated\s+AS\s*\(.*?\)\s*INSERT\s+INTO\s+tmp_requeued_events.*?;)', do_body, re.DOTALL)
    if cte_pattern:
        sample_sql = cte_pattern.group(1).replace("v_tenant_id", "'22222222-2222-4222-8222-222222222222'::uuid") \
                                          .replace("v_company_id", "'22222222-2222-4222-8222-222222222223'::uuid") \
                                          .replace("v_grn_id_1", "'41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid") \
                                          .replace("v_grn_id_2", "'07da9829-4327-4545-9bf5-117ef50fd756'::uuid")
        try:
            cte_parsed = pglast.parse_sql(sample_sql)
            print(f"\n[PASS] Data-modifying CTE + INSERT in Script 27 parsed into valid PostgreSQL AST ({cte_parsed[0].stmt.__class__.__name__}).")
        except Exception as e:
            print(f"[FAIL] Data-modifying CTE failed: {e}")
            all_passed = False

    # Invariants for Script 27
    invariants = [
        ("v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;", "Target Tenant DEMO-CLOUD"),
        ("v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;", "Target Company CLOUD-LOGISTICS"),
        ("v_grn_id_1 uuid := '41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid;", "Target GRN-2024-01"),
        ("v_grn_id_2 uuid := '07da9829-4327-4545-9bf5-117ef50fd756'::uuid;", "Target GRN-2024-03"),
        ("event_type = 'grn.posted'", "Event Type grn.posted"),
        ("status = 'dead_letter'", "Dead-letter status filter"),
        ("published_at IS NULL", "Unpublished filter"),
        ("GET DIAGNOSTICS v_requeue_count = ROW_COUNT;", "Diagnostics ROW_COUNT capture"),
        ("IF v_requeue_count <> 2 THEN", "Exact-two-events assertion"),
    ]

    for needle, desc in invariants:
        if needle in c27:
            print(f"[PASS] Confirmed invariant: {desc}")
        else:
            print(f"[FAIL] Missing invariant: {desc}")
            all_passed = False

    return all_passed

if __name__ == "__main__":
    success = validate_all()
    sys.exit(0 if success else 1)
