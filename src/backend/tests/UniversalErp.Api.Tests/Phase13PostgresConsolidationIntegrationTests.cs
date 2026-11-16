using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Branches;
using UniversalErp.Application.Consolidations;
using UniversalErp.Application.Integrations;
using UniversalErp.Infrastructure.Branches;
using UniversalErp.Infrastructure.Consolidations;
using UniversalErp.Infrastructure.Integrations;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase13PostgresConsolidationIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    [Fact]
    public async Task Phase13_FullMultiBranchAndConsolidation_PostgresIntegrationTest()
    {
        if (string.IsNullOrWhiteSpace(StagingConnectionString)) return;

        var ct = CancellationToken.None;
        var tenantId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var fiscalYearIdA = Guid.NewGuid();
        var fiscalPeriodIdA = Guid.NewGuid();
        var fiscalYearIdB = Guid.NewGuid();
        var fiscalPeriodIdB = Guid.NewGuid();

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString);
        var branchRepo = new PostgresBranchesRepository(dataSource);
        var branchService = new BranchService(branchRepo);

        var eliminationEngine = new ConsolidationEliminationEngine();
        var consolidationRepo = new PostgresConsolidationsRepository(dataSource, eliminationEngine);
        var consolidationService = new ConsolidationService(consolidationRepo);

        var signingEngine = new WebhookSigningEngine();
        var integrationsRepo = new PostgresIntegrationsRepository(dataSource, signingEngine);
        var integrationsService = new IntegrationsService(integrationsRepo, signingEngine);

        var acc1200A = Guid.NewGuid();
        var acc1300A = Guid.NewGuid();
        var acc4000A = Guid.NewGuid();
        var acc5000A = Guid.NewGuid();

        var acc1200B = Guid.NewGuid();
        var acc2200B = Guid.NewGuid();
        var acc4000B = Guid.NewGuid();
        var acc5000B = Guid.NewGuid();

        var whA = Guid.NewGuid();
        var binA = Guid.NewGuid();
        var whB = Guid.NewGuid();
        var binB = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var varId = Guid.NewGuid();
        var uomId = Guid.NewGuid();

        // ── 1. Seed Tenant, Multi-Company Foundation, Fiscal Periods & Accounts ───
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = $"SELECT set_config('app.tenant_id', '{tenantId}', true);";
            await cmd.ExecuteNonQueryAsync(ct);

            var tcode = $"T-{tenantId.ToString("N")[..8]}";
            var ccodeA = $"CO-A-{tenantId.ToString("N")[..6]}";
            var ccodeB = $"CO-B-{tenantId.ToString("N")[..6]}";

            cmd.CommandText = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tId, @tcode, 'Phase 13 Conglomerate Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES
                (@tId, @cA, @ccA, 'Phase 13 Manufacturing Co A', 'PK', 'PKR', 'Asia/Karachi', 'active'),
                (@tId, @cB, @ccB, 'Phase 13 Distribution Co B', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uId, @email, 'Phase 13 Admin', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO finance.currencies (code, name, minor_unit, is_active)
                VALUES ('PKR', 'Pakistani Rupee', 2, true)
                ON CONFLICT (code) DO NOTHING;

                -- Fiscal Year & Periods for Company A
                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tId, @cA, @fyA, 'FY2026-A', '2026-07-01', '2027-06-30', 'open')
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tId, @cA, @fpA, @fyA, 1, 'Period 1 A', '2026-07-01', '2026-07-31', 'open')
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                -- Fiscal Year & Periods for Company B
                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tId, @cB, @fyB, 'FY2026-B', '2026-07-01', '2027-06-30', 'open')
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tId, @cB, @fpB, @fyB, 1, 'Period 1 B', '2026-07-01', '2026-07-31', 'open')
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                -- Chart of Accounts for Company A
                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_active)
                VALUES
                (@tId, @cA, @a1200A, '1200', 'Inventory Asset', 'asset', 'debit', true),
                (@tId, @cA, @a1300A, '1300', 'Due-From Affiliates (Receivable)', 'asset', 'debit', true),
                (@tId, @cA, @a4000A, '4000', 'Sales Revenue', 'revenue', 'credit', true),
                (@tId, @cA, @a5000A, '5000', 'Cost of Goods Sold', 'expense', 'debit', true)
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                -- Chart of Accounts for Company B
                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_active)
                VALUES
                (@tId, @cB, @a1200B, '1200', 'Inventory Asset', 'asset', 'debit', true),
                (@tId, @cB, @a2200B, '2200', 'Due-To Affiliates (Payable)', 'liability', 'credit', true),
                (@tId, @cB, @a4000B, '4000', 'Sales Revenue', 'revenue', 'credit', true),
                (@tId, @cB, @a5000B, '5000', 'Cost of Goods Sold', 'expense', 'debit', true)
                ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

                -- Product & UOM
                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tId, @uomId, 'EA', 'Each', 'count', 0)
                ON CONFLICT (tenant_id, uom_id) DO NOTHING;

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tId, @prodId, 'PROD-P13-001', 'Conglomerate Industrial Widget', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, product_id, variant_id, sku, variant_name, base_uom_id, is_active)
                VALUES (@tId, @prodId, @varId, 'SKU-P13-001', 'Standard Widget', @uomId, true)
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;
                """;

            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("tcode", tcode);
            cmd.Parameters.AddWithValue("cA", companyA);
            cmd.Parameters.AddWithValue("ccA", ccodeA);
            cmd.Parameters.AddWithValue("cB", companyB);
            cmd.Parameters.AddWithValue("ccB", ccodeB);
            cmd.Parameters.AddWithValue("uId", userId);
            cmd.Parameters.AddWithValue("email", $"p13_admin_{Guid.NewGuid():N}@example.com");
            cmd.Parameters.AddWithValue("fyA", fiscalYearIdA);
            cmd.Parameters.AddWithValue("fpA", fiscalPeriodIdA);
            cmd.Parameters.AddWithValue("fyB", fiscalYearIdB);
            cmd.Parameters.AddWithValue("fpB", fiscalPeriodIdB);
            cmd.Parameters.AddWithValue("a1200A", acc1200A);
            cmd.Parameters.AddWithValue("a1300A", acc1300A);
            cmd.Parameters.AddWithValue("a4000A", acc4000A);
            cmd.Parameters.AddWithValue("a5000A", acc5000A);
            cmd.Parameters.AddWithValue("a1200B", acc1200B);
            cmd.Parameters.AddWithValue("a2200B", acc2200B);
            cmd.Parameters.AddWithValue("a4000B", acc4000B);
            cmd.Parameters.AddWithValue("a5000B", acc5000B);
            cmd.Parameters.AddWithValue("uomId", uomId);
            cmd.Parameters.AddWithValue("prodId", prodId);
            cmd.Parameters.AddWithValue("varId", varId);

            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ── 2. Scenario 2: Multi-Branch Hierarchy ──────────────────────────────────
        var hqBranch = await branchService.CreateBranchAsync(tenantId, companyA, new CreateBranchRequest(
            "BR-HQ-01", "Corporate Headquarters", "headquarters", "Karachi", "Sindh"
        ), ct);

        var plantBranch = await branchService.CreateBranchAsync(tenantId, companyA, new CreateBranchRequest(
            "BR-PLANT-01", "Port Qasim Manufacturing Plant", "plant", "Karachi", "Sindh",
            ParentBranchId: hqBranch.Id
        ), ct);

        var salesBranch = await branchService.CreateBranchAsync(tenantId, companyA, new CreateBranchRequest(
            "BR-SALES-01", "Lahore Regional Sales Office", "sales_office", "Lahore", "Punjab",
            ParentBranchId: hqBranch.Id
        ), ct);

        var hierarchy = await branchService.GetHierarchyAsync(tenantId, companyA, ct);
        Assert.Single(hierarchy);
        Assert.Equal(hqBranch.Id, hierarchy[0].Branch.Id);
        Assert.Equal(2, hierarchy[0].Children.Count);

        // ── 3. Scenario 3: Branch Isolation & Context Switching ────────────────────
        var switchResult = await branchService.SwitchBranchAsync(tenantId, companyA, plantBranch.Id, ct);
        Assert.Equal(plantBranch.Id, switchResult.BranchId);
        Assert.Equal("BR-PLANT-01", switchResult.BranchCode);
        Assert.Contains(plantBranch.Id.ToString(), switchResult.TokenContext);

        // ── 4. Scenario 4: Branch Scoping on Warehouses & Bins ──────────────────────
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active, branch_id)
                VALUES
                (@tId, @cA, @whA, 'WH-A-01', 'Company A Plant Warehouse', true, @brPlant),
                (@tId, @cB, @whB, 'WH-B-01', 'Company B Central Hub', true, NULL)
                ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active)
                VALUES
                (@tId, @cA, @whA, @binA, 'BIN-A-01', 'storage', true),
                (@tId, @cB, @whB, @binB, 'BIN-B-01', 'storage', true)
                ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("cA", companyA);
            cmd.Parameters.AddWithValue("cB", companyB);
            cmd.Parameters.AddWithValue("whA", whA);
            cmd.Parameters.AddWithValue("binA", binA);
            cmd.Parameters.AddWithValue("whB", whB);
            cmd.Parameters.AddWithValue("binB", binB);
            cmd.Parameters.AddWithValue("brPlant", plantBranch.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ── 5. Scenario 6: Intercompany Relationship Establishment ─────────────────
        var relationship = await consolidationService.CreateRelationshipAsync(tenantId, new CreateIntercompanyRelationshipRequest(
            FromCompanyId: companyA,
            ToCompanyId: companyB,
            DueToAccountId: acc2200B,
            DueFromAccountId: acc1300A,
            ClearingCurrencyCode: "PKR",
            TransferPricingRule: "cost_plus",
            MarkupPercentage: 20.000m
        ), ct);

        Assert.Equal(companyA, relationship.FromCompanyId);
        Assert.Equal(companyB, relationship.ToCompanyId);
        Assert.Equal("cost_plus", relationship.TransferPricingRule);
        Assert.Equal(20.000m, relationship.MarkupPercentage);

        // ── 6. Scenario 7: Seed FIFO Cost Layer in Company A & Create Transfer ─────
        var sourceLayerId = Guid.NewGuid();
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO erp_inventory.inventory_cost_layer (
                    tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id,
                    status_code, source_document_type, source_document_id, original_qty, remaining_qty,
                    unit_cost, currency_code
                )
                VALUES (
                    @tId, @cA, @layerId, @varId, @whA, @binA,
                    'available', 'initial_receipt', @layerId, 50, 50,
                    100.0000, 'PKR'
                );
                """;
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("cA", companyA);
            cmd.Parameters.AddWithValue("layerId", sourceLayerId);
            cmd.Parameters.AddWithValue("varId", varId);
            cmd.Parameters.AddWithValue("whA", whA);
            cmd.Parameters.AddWithValue("binA", binA);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Create Intercompany Transfer in Draft
        // Transfer 30 EA: unit cost PKR 100, transfer price PKR 120 (+20% markup) -> Total PKR 3,600
        var transfer = await consolidationService.CreateTransferAsync(tenantId, new CreateIntercompanyTransferRequest(
            TransferNumber: "ICT-TEST-001",
            FromCompanyId: companyA,
            ToCompanyId: companyB,
            DispatchDate: new DateOnly(2026, 7, 15),
            CurrencyCode: "PKR",
            FromBranchId: plantBranch.Id,
            Lines: new List<IntercompanyTransferLineRequest>
            {
                new(
                    ProductId: prodId,
                    Quantity: 30m,
                    UnitCostPkr: 100m,
                    TransferPricePkr: 120m,
                    SourceLayerId: sourceLayerId,
                    SourceWarehouseId: whA,
                    SourceBinId: binA,
                    DestWarehouseId: whB,
                    DestBinId: binB
                )
            }
        ), ct);

        Assert.Equal("draft", transfer.Status);
        Assert.Equal(3600m, transfer.TotalAmountPkr);
        Assert.Single(transfer.Lines);

        // ── 7. Scenario 8: Dispatch Transfer & Verify Sender Paired GL Journal ─────
        var dispatchedTransfer = await consolidationService.DispatchTransferAsync(tenantId, transfer.Id, userId, ct);
        Assert.Equal("dispatched", dispatchedTransfer.Status);
        Assert.NotNull(dispatchedTransfer.FromGlJournalId);

        // Verify remaining qty in sender FIFO layer decreased from 50 to 20
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE layer_id = @layerId;";
            cmd.Parameters.AddWithValue("layerId", sourceLayerId);
            var remaining = Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct));
            Assert.Equal(20m, remaining);
        }

        // ── 8. Scenario 9: Receive Transfer & Verify Receiver Paired GL Journal ────
        var receivedTransfer = await consolidationService.ReceiveTransferAsync(tenantId, transfer.Id, userId, new ReceiveIntercompanyTransferRequest(
            ReceiptDate: new DateOnly(2026, 7, 20),
            Lines: new List<IntercompanyTransferReceiptLine>
            {
                new(dispatchedTransfer.Lines[0].Id, 30m, whB, binB)
            }
        ), ct);

        Assert.Equal("received", receivedTransfer.Status);
        Assert.NotNull(receivedTransfer.ToGlJournalId);

        // Verify receiver Model A FIFO layer created in Company B
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT remaining_qty, unit_cost FROM erp_inventory.inventory_cost_layer WHERE company_id = @cB AND source_document_id = @tId;";
            cmd.Parameters.AddWithValue("cB", companyB);
            cmd.Parameters.AddWithValue("tId", transfer.Id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct));
            Assert.Equal(30m, reader.GetDecimal(0));
            Assert.Equal(120m, reader.GetDecimal(1));
        }

        // ── 9. Scenario 13 & 14: Financial Consolidation Run & Automated Eliminations
        var consolidationRun = await consolidationService.ExecuteConsolidationAsync(tenantId, userId, new ExecuteConsolidationRequest(
            RunCode: "CONS-2026-STAGING-001",
            FiscalPeriodId: fiscalPeriodIdA,
            HoldingCompanyId: null,
            ParticipatingCompanyIds: new List<Guid> { companyA, companyB },
            ReportingCurrencyCode: "PKR",
            EliminateUnrealizedProfit: true
        ), ct);

        Assert.Equal("completed", consolidationRun.Status);
        Assert.Equal(7200m, consolidationRun.TotalIntercompanyEliminationsPkr); // 3,600 balance + 3,600 sales eliminated
        Assert.Equal(600m, consolidationRun.UnrealizedProfitEliminationPkr); // 30 EA held * PKR 20 markup = PKR 600

        // Verify elimination lines are balanced
        var totalDr = consolidationRun.EliminationLines.Sum(l => l.DebitAmountPkr);
        var totalCr = consolidationRun.EliminationLines.Sum(l => l.CreditAmountPkr);
        Assert.True(totalDr > 0);
        Assert.Equal(totalDr, totalCr);

        // Verify Consolidated Financial Statement
        var statement = await consolidationService.GetConsolidatedStatementAsync(tenantId, consolidationRun.Id, "balance_sheet", ct);
        Assert.True(statement.IsBalanced);
        Assert.NotEmpty(statement.Lines);

        // ── 10. Scenario 15: Duplicate Run Code Rejection ───────────────────────────
        await Assert.ThrowsAnyAsync<Exception>(() => consolidationService.ExecuteConsolidationAsync(tenantId, userId, new ExecuteConsolidationRequest(
            RunCode: "CONS-2026-STAGING-001", // Duplicate!
            FiscalPeriodId: fiscalPeriodIdA,
            HoldingCompanyId: null,
            ParticipatingCompanyIds: new List<Guid> { companyA, companyB }
        ), ct));

        // ── 11. Scenario 16: External Integrations Hub ──────────────────────────────
        // 1. Create Webhook Subscription
        var webhook = await integrationsService.CreateWebhookSubscriptionAsync(tenantId, companyA, new CreateWebhookSubscriptionRequest(
            EventType: "intercompany.transfer.dispatched",
            TargetUrl: "https://api.logistics-partner.example.com/webhooks"
        ), ct);

        Assert.NotNull(webhook.PlaintextSecret);
        Assert.True(webhook.PlaintextSecret.Length >= 32);

        // 2. Rotate Webhook Secret
        var rotated = await integrationsService.RotateWebhookSecretAsync(tenantId, webhook.Subscription.Id, ct);
        Assert.NotEqual(webhook.PlaintextSecret, rotated.NewPlaintextSecret);

        // 3. Enqueue Outbound Delivery & Sign with HMAC-SHA256
        const string eventPayload = "{\"event\":\"intercompany.transfer.dispatched\",\"transferNumber\":\"ICT-TEST-001\"}";
        var delivery = await integrationsService.EnqueueEventDeliveryAsync(tenantId, new EnqueueEventRequest(
            SubscriptionId: webhook.Subscription.Id,
            EventType: "intercompany.transfer.dispatched",
            EventPayloadJson: eventPayload
        ), ct);

        Assert.Equal("pending", delivery.DeliveryStatus);
        Assert.Equal(0, delivery.AttemptCount);

        var (sigHeader, tsHeader) = signingEngine.ComputeHeaders(eventPayload, rotated.NewPlaintextSecret, DateTimeOffset.UtcNow);
        Assert.StartsWith("sha256=", sigHeader);
        Assert.True(signingEngine.VerifySignature(eventPayload, rotated.NewPlaintextSecret, sigHeader, tsHeader));

        // 4. Simulate Failure & Exponential Backoff Progression
        var failed1 = await integrationsService.RecordDeliveryAttemptAsync(tenantId, delivery.Id, 503, "Service Unavailable", succeeded: false, ct);
        Assert.Equal("failed", failed1.DeliveryStatus);
        Assert.Equal(1, failed1.AttemptCount);
        Assert.NotNull(failed1.NextRetryAt);

        // Progressively fail until attempt 5 (Dead Letter Exhaustion)
        await integrationsService.RecordDeliveryAttemptAsync(tenantId, delivery.Id, 503, "Service Unavailable", succeeded: false, ct); // 2
        await integrationsService.RecordDeliveryAttemptAsync(tenantId, delivery.Id, 503, "Service Unavailable", succeeded: false, ct); // 3
        await integrationsService.RecordDeliveryAttemptAsync(tenantId, delivery.Id, 503, "Service Unavailable", succeeded: false, ct); // 4
        var deadLetter = await integrationsService.RecordDeliveryAttemptAsync(tenantId, delivery.Id, 503, "Service Unavailable", succeeded: false, ct); // 5

        Assert.Equal("exhausted", deadLetter.DeliveryStatus);
        Assert.Equal(5, deadLetter.AttemptCount);
        Assert.Null(deadLetter.NextRetryAt); // No automatic retry

        // 5. Authorized Manual Retry
        var retried = await integrationsService.RetryDeliveryAsync(tenantId, delivery.Id, ct);
        Assert.Equal("pending", retried.DeliveryStatus);

        // 6. API Partner Registration
        var partner = await integrationsService.CreateApiPartnerAsync(tenantId, companyA, new CreateApiPartnerRequest(
            PartnerName: "Apex Logistics Partner",
            Permissions: new List<string> { "intercompany.transfer.create", "intercompany.transfer.receive" }
        ), ct);

        Assert.StartsWith("erp_live_", partner.PlaintextApiKey);
        Assert.Equal("Apex Logistics Partner", partner.Partner.PartnerName);

        // Revoke partner
        var revoked = await integrationsService.RevokeApiPartnerAsync(tenantId, partner.Partner.Id, ct);
        Assert.True(revoked);

        // Multi-tenant isolation test: querying with another tenant returns 0
        var otherTenantId = Guid.NewGuid();
        var otherPartners = await integrationsService.GetApiPartnersAsync(otherTenantId, companyA, ct);
        Assert.Empty(otherPartners);
    }
}
