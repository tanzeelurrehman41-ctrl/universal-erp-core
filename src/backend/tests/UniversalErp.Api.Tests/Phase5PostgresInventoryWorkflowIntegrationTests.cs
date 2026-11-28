using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Inventory;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase5PostgresInventoryWorkflowIntegrationTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    private sealed class StaticTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    [Fact]
    public async Task Phase5_CompleteInventoryWorkflow_E2E_Lifecycle_Passes()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);

        // Verify connection
        try
        {
            await using var testConn = await dataSource.OpenConnectionAsync();
        }
        catch
        {
            // If local postgres staging container is offline, skip integration
            return;
        }

        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var accessor = new StaticTenantContextAccessor(context);
        var repo = new PostgresInventoryWorkflowRepository(dataSource);
        var service = new InventoryWorkflowService(accessor, repo);

        // 1. Seed tenant, company, warehouse, bin, UOM, and product variant
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            await using var tx = await conn.BeginTransactionAsync();
            var tCode = $"T-{tenantId.ToString()[..8].ToUpperInvariant()}";
            var cCode = $"C-{companyId.ToString()[..8].ToUpperInvariant()}";

            // Seed platform.tenants
            await using (var seedTenant = new NpgsqlCommand(
                """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tid, @tcode, 'Integration Test Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT DO NOTHING;
                """, conn, tx))
            {
                seedTenant.Parameters.AddWithValue("tid", tenantId);
                seedTenant.Parameters.AddWithValue("tcode", tCode);
                await seedTenant.ExecuteNonQueryAsync();
            }

            // Seed organization.companies
            await using (var seedComp = new NpgsqlCommand(
                """
                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tid, @cid, @ccode, 'Integration Test Company', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT DO NOTHING;
                """, conn, tx))
            {
                seedComp.Parameters.AddWithValue("tid", tenantId);
                seedComp.Parameters.AddWithValue("cid", companyId);
                seedComp.Parameters.AddWithValue("ccode", cCode);
                await seedComp.ExecuteNonQueryAsync();
            }

            // Seed erp_inventory.stock_status
            await using (var seedStatus = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES
                    (@tid, 'available', 'Available Stock', true, true),
                    (@tid, 'quarantine', 'Quarantine Stock', false, false),
                    (@tid, 'damaged', 'Damaged Stock', false, false)
                ON CONFLICT DO NOTHING;
                """, conn, tx))
            {
                seedStatus.Parameters.AddWithValue("tid", tenantId);
                await seedStatus.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        // 2. Create UOM, Variant, Warehouse, Bin via service
        var uomRes = await service.CreateUnitAsync(new CreateUnitOfMeasureRequest("EA", "Each", "count", 0), CancellationToken.None);
        Assert.True(uomRes.IsSuccess);
        var uomId = uomRes.Value!.UnitOfMeasureId;

        var whRes = await service.CreateWarehouseAsync(new CreateWarehouseRequest("WH-TEST", "Main Test Warehouse"), CancellationToken.None);
        Assert.True(whRes.IsSuccess);
        var whId = whRes.Value!.WarehouseId;

        var binRes = await service.CreateBinAsync(whId, new CreateStockBinRequest("BIN-01", "storage"), CancellationToken.None);
        Assert.True(binRes.IsSuccess);
        var binId = binRes.Value!.BinId;

        // Seed product
        var productId = Guid.NewGuid();
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            await using var tx = await conn.BeginTransactionAsync();
            await using var insProd = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tid, @pid, 'PROD-TEST', 'Test Product', 'stock', true)
                ON CONFLICT DO NOTHING;
                """,
                conn, tx);
            insProd.Parameters.AddWithValue("tid", tenantId);
            insProd.Parameters.AddWithValue("pid", productId);
            await insProd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        var varRes = await service.CreateVariantAsync(new CreateProductVariantRequest(productId, "SKU-TEST-01", "Variant Test 1", uomId), CancellationToken.None);
        Assert.True(varRes.IsSuccess);
        var variantId = varRes.Value!.VariantId;

        // 3. Receive initial inventory (30 EA)
        var receiptTx = Guid.NewGuid();
        var recRes = await service.PostReceiptAsync(receiptTx, new PostInventoryReceiptRequest(
            VariantId: variantId,
            WarehouseId: whId,
            BinId: binId,
            Quantity: 30m,
            UnitOfMeasureId: uomId,
            SourceDocumentType: "purchase_order",
            SourceDocumentId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            Note: "Initial stocking"), CancellationToken.None);
        Assert.True(recRes.IsSuccess);

        // Seed FIFO layer (migration 026) for the 30 EA at PKR 45.00
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            await using var tx = await conn.BeginTransactionAsync();
            await using var insLayer = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id,
                     status_code, source_document_type, received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tid, @cid, gen_random_uuid(), @varId, @whId, @binId,
                     'available', 'grn', now(), 30, 30, 45.00, 'PKR');
                """, conn, tx);
            insLayer.Parameters.AddWithValue("tid", tenantId);
            insLayer.Parameters.AddWithValue("cid", companyId);
            insLayer.Parameters.AddWithValue("varId", variantId);
            insLayer.Parameters.AddWithValue("whId", whId);
            insLayer.Parameters.AddWithValue("binId", binId);
            await insLayer.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        // 4. Test ATP before reservation: Physical = 30, Reserved = 0, Valued = 30 -> ATP = 30
        var atpBefore = await service.GetAtpAsync(variantId, whId, binId, CancellationToken.None);
        Assert.Single(atpBefore);
        Assert.Equal(30m, atpBefore[0].PhysicalOnHand);
        Assert.Equal(0m, atpBefore[0].ActiveReservedQty);
        Assert.Equal(30m, atpBefore[0].AvailableToPromiseQty);
        Assert.Equal(30m, atpBefore[0].SellableAtpQty);

        // 5. Create reservation of 10 EA
        var resTx = Guid.NewGuid();
        var resResult = await service.CreateReservationAsync(resTx, new CreateStockReservationRequest(
            VariantId: variantId,
            WarehouseId: whId,
            BinId: binId,
            Quantity: 10m,
            SourceDocumentType: "sales_order",
            SourceDocumentId: Guid.NewGuid(),
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(2),
            Note: "Reserved for test order"), CancellationToken.None);
        Assert.True(resResult.IsSuccess);
        var reservationId = resResult.Value!.ReservationId;
        Assert.Equal("active", resResult.Value.ReservationStatus);

        // 6. Test ATP after reservation: Physical = 30, Reserved = 10 -> ATP = 20
        var atpAfter = await service.GetAtpAsync(variantId, whId, binId, CancellationToken.None);
        Assert.Equal(20m, atpAfter[0].AvailableToPromiseQty);
        Assert.Equal(20m, atpAfter[0].SellableAtpQty);

        // 7. Attempting to reserve 25 EA (which exceeds remaining ATP 20) must fail
        var overRes = await service.CreateReservationAsync(Guid.NewGuid(), new CreateStockReservationRequest(
            VariantId: variantId,
            WarehouseId: whId,
            BinId: binId,
            Quantity: 25m,
            SourceDocumentType: "sales_order",
            SourceDocumentId: null,
            ExpiresAt: null,
            Note: "Over reservation attempt"), CancellationToken.None);
        Assert.False(overRes.IsSuccess);
        Assert.Contains("INSUFFICIENT_AVAILABLE_TO_PROMISE", overRes.Error);

        // 8. Create pick list, record picked qty, and dispatch
        var pickTx = Guid.NewGuid();
        var plResult = await service.CreatePickListAsync(pickTx, new CreatePickListRequest(
            WarehouseId: whId,
            SourceDocumentType: "sales_order",
            SourceDocumentId: null,
            Lines: [
                new CreatePickLineRequest(variantId, binId, 10m, reservationId, "Pick line 1")
            ],
            Note: "Pick list test"), CancellationToken.None);
        Assert.True(plResult.IsSuccess);
        var pickListId = plResult.Value!.PickListId;
        var pickLineId = plResult.Value.Lines[0].PickLineId;

        // Record picking 10 EA
        var recPickResult = await service.RecordPickLinesAsync(pickListId, [
            new RecordPickLineRequest(pickLineId, 10m, "All 10 EA picked")
        ], CancellationToken.None);
        Assert.True(recPickResult.IsSuccess);
        Assert.Equal("picked", recPickResult.Value!.Status);

        // Dispatch pick list -> consumes FIFO layer (10 EA * 45 = 450 PKR)
        var dispatchResult = await service.DispatchPickListAsync(pickListId, CancellationToken.None);
        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(10m, dispatchResult.Value!.TotalDispatchedQty);
        Assert.Equal(450m, dispatchResult.Value.TotalFifoCost);
        Assert.Single(dispatchResult.Value.CostAllocations);

        // Verify reservation is fulfilled
        var reservations = await service.ListReservationsAsync(variantId, whId, null, CancellationToken.None);
        var updatedRes = reservations.First(r => r.ReservationId == reservationId);
        Assert.Equal("fulfilled", updatedRes.ReservationStatus);
        Assert.Equal(10m, updatedRes.FulfilledQty);

        // Verify remaining stock is 20 EA
        var atpPostDispatch = await service.GetAtpAsync(variantId, whId, binId, CancellationToken.None);
        Assert.Equal(20m, atpPostDispatch[0].PhysicalOnHand);
        Assert.Equal(20m, atpPostDispatch[0].AvailableToPromiseQty);

        // 9. Physical Stock Count Session (Cycle Count)
        var countTx = Guid.NewGuid();
        var countSessionResult = await service.CreateCountSessionAsync(countTx, new CreateCountSessionRequest(
            WarehouseId: whId,
            CountType: "cycle",
            Note: "Cycle count test"), CancellationToken.None);
        Assert.True(countSessionResult.IsSuccess);
        var sessionId = countSessionResult.Value!.SessionId;

        // Record count: found 22 EA (a gain of +2 EA)
        var recordCountResult = await service.RecordCountLinesAsync(sessionId, [
            new RecordCountLineRequest(variantId, binId, 22m, "Found 2 extra units")
        ], CancellationToken.None);
        Assert.True(recordCountResult.IsSuccess);

        // Approve count session -> posts reconciliation adjustment and creates new FIFO layer
        var approveCountResult = await service.ApproveCountSessionAsync(sessionId, CancellationToken.None);
        Assert.True(approveCountResult.IsSuccess);
        Assert.Equal(2m, approveCountResult.Value!.TotalNetVarianceQty);

        // Verify physical stock is now 22 EA
        var atpPostCount = await service.GetAtpAsync(variantId, whId, binId, CancellationToken.None);
        Assert.Equal(22m, atpPostCount[0].PhysicalOnHand);

        // 10. Customer Return (return 3 EA at PKR 45)
        var returnTx = Guid.NewGuid();
        var returnResult = await service.CreateStockReturnAsync(returnTx, new CreateStockReturnRequest(
            ReturnType: "customer_return",
            SourceDocumentType: "sales_invoice",
            SourceDocumentId: null,
            WarehouseId: whId,
            Lines: [
                new CreateStockReturnLineRequest(variantId, binId, 3m, 45m, "available", "Customer returned goods")
            ],
            Note: "Customer return test"), CancellationToken.None);
        Assert.True(returnResult.IsSuccess);
        var returnId = returnResult.Value!.ReturnId;

        var processReturnResult = await service.ProcessStockReturnAsync(returnId, CancellationToken.None);
        Assert.True(processReturnResult.IsSuccess);
        Assert.Equal(3m, processReturnResult.Value!.TotalReturnedQty);

        // Verify physical stock is now 25 EA (22 + 3)
        var atpPostReturn = await service.GetAtpAsync(variantId, whId, binId, CancellationToken.None);
        Assert.Equal(25m, atpPostReturn[0].PhysicalOnHand);

        // 11. Movement history & Valuation Report
        var history = await service.GetBinMovementHistoryAsync(whId, binId, variantId, 50, CancellationToken.None);
        Assert.NotEmpty(history);

        var valReport = await service.GetInventoryValuationReportAsync(CancellationToken.None);
        Assert.NotNull(valReport);
        Assert.True(valReport.TotalActiveLayers >= 1);
    }
}
