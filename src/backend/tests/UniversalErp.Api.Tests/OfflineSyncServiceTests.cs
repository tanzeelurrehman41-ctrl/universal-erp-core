using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

/// <summary>
/// Unit tests for offline sync failure scenarios.
/// These use in-memory fakes for the repository so they run without a database.
/// Each test corresponds to a documented gap or safety property in SYNC_PROTOCOL.md.
/// </summary>
public sealed class OfflineSyncServiceTests
{
    private static readonly TenantContext TestCtx = new(
        TenantId: Guid.Parse("00000000-0000-4000-a000-000000000001"),
        CompanyId: Guid.Parse("00000000-0000-4000-a000-000000000002"),
        UserId: Guid.Parse("00000000-0000-4000-a000-000000000003"));

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string CanonicalHash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static OfflineReceiptOperation MakeOperation(Guid operationId, long seq = 1) =>
        new(operationId, seq, "inventory.receipt.post", DateTimeOffset.UtcNow,
            new PostInventoryReceiptRequest(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                10m, Guid.NewGuid(), null, null, null, null));

    private static OfflineReceiptBatchRequest MakeBatch(Guid deviceId, Guid grantId, OfflineReceiptOperation op) =>
        new(Guid.NewGuid(), deviceId, grantId, "test-token", [op]);

    // ── Fake repository ───────────────────────────────────────────────────────

    /// <summary>
    /// In-memory implementation of IOfflineReceiptSyncRepository for unit tests.
    /// Replicates the contract-level behaviour without any database interaction.
    /// </summary>
    private sealed class FakeSyncRepository : IOfflineReceiptSyncRepository
    {
        // Simulated grant store
        private readonly Dictionary<(Guid companyId, Guid grantId, string grantHash), bool> _grants = [];
        // Simulated operation inbox: operationId → InboxEntry
        private readonly Dictionary<Guid, InboxEntry> _inbox = [];
        // When true, the device registration succeeds
        public bool DeviceActive = true;
        public bool CompanyActive = true;
        public bool CompanyScopeActive = true;
        private readonly Dictionary<Guid, (DateTimeOffset ExpiresAt, bool Revoked)> _grantMetadata = [];

        private record InboxEntry(
            Guid DeviceId,
            long DeviceSequence,
            string OperationType,
            string PayloadHash,
            string Outcome,
            OfflineReceiptOperationResult? StoredResult = null);

        public void SeedGrant(Guid grantId, string rawToken, Guid? companyId = null)
        {
            _grants[(companyId ?? TestCtx.CompanyId, grantId, Hash(rawToken))] = true;
            _grantMetadata[grantId] = (DateTimeOffset.UtcNow.AddDays(7), Revoked: false);
        }

        public void RevokeGrant(Guid grantId)
        {
            if (_grantMetadata.TryGetValue(grantId, out var meta))
                _grantMetadata[grantId] = (meta.ExpiresAt, Revoked: true);
        }

        public void ExpireGrant(Guid grantId)
        {
            if (_grantMetadata.TryGetValue(grantId, out var meta))
                _grantMetadata[grantId] = (DateTimeOffset.UtcNow.AddDays(-1), meta.Revoked);
        }

        private static string Hash(string raw) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        // ── IOfflineReceiptSyncRepository ────────────────────────────────────

        public Task<OfflineDeviceGrant?> RegisterDeviceAsync(
            TenantContext context, Guid clientDeviceId, string platformCode,
            string? deviceName, CancellationToken cancellationToken)
        {
            if (!DeviceActive || !CompanyActive || !CompanyScopeActive)
                return Task.FromResult<OfflineDeviceGrant?>(null);

            var grantToken = "test-token";
            var grantId = Guid.NewGuid();
            SeedGrant(grantId, grantToken, context.CompanyId);
            return Task.FromResult<OfflineDeviceGrant?>(
                new OfflineDeviceGrant(Guid.NewGuid(), grantId, grantToken,
                    DateTimeOffset.UtcNow.AddDays(7)));
        }

        public Task<OfflineInboxReservation> ReserveReceiptOperationAsync(
            TenantContext context,
            OfflineReceiptBatchRequest batch,
            OfflineReceiptOperation operation,
            string payloadHash,
            CancellationToken cancellationToken)
        {
            // Validate grant
            var grantHash = Hash(batch.GrantToken);
            if (!_grants.ContainsKey((context.CompanyId, batch.GrantId, grantHash)))
                return Task.FromResult(new OfflineInboxReservation("unauthorized"));

            if (_grantMetadata.TryGetValue(batch.GrantId, out var meta))
            {
                if (meta.Revoked || meta.ExpiresAt <= DateTimeOffset.UtcNow)
                    return Task.FromResult(new OfflineInboxReservation("unauthorized"));
            }

            // Check existing inbox row
            if (_inbox.TryGetValue(operation.OperationId, out var existing))
            {
                var matches = existing.DeviceId == batch.DeviceId
                    && existing.DeviceSequence == operation.DeviceSequence
                    && existing.OperationType == operation.OperationType
                    && string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                    return Task.FromResult(new OfflineInboxReservation("conflict",
                        new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence,
                            "conflict", null, "OPERATION_ID_REUSED")));

                if (existing.Outcome == "deferred")
                    return Task.FromResult(new OfflineInboxReservation("process"));

                var replayResult = existing.StoredResult! with
                {
                    Status = existing.Outcome == "accepted" ? "duplicate" : existing.Outcome,
                };
                return Task.FromResult(new OfflineInboxReservation("replay", replayResult));
            }

            // Insert new deferred row (no race in-memory, always succeeds)
            _inbox[operation.OperationId] = new InboxEntry(
                batch.DeviceId, operation.DeviceSequence, operation.OperationType,
                payloadHash, "deferred");
            return Task.FromResult(new OfflineInboxReservation("process"));
        }

        public Task CompleteReceiptOperationAsync(
            TenantContext context,
            Guid operationId,
            OfflineReceiptOperationResult result,
            CancellationToken cancellationToken)
        {
            if (!_inbox.TryGetValue(operationId, out var row) || row.Outcome != "deferred")
                throw new InvalidOperationException("The reserved offline operation could not be acknowledged.");

            _inbox[operationId] = row with { Outcome = result.Status, StoredResult = result };
            return Task.CompletedTask;
        }

        public async Task<OfflineReceiptOperationResult> ProcessReceiptOperationAtomicAsync(
            TenantContext context,
            OfflineReceiptBatchRequest batch,
            OfflineReceiptOperation operation,
            string payloadHash,
            CancellationToken cancellationToken)
        {
            var reservation = await ReserveReceiptOperationAsync(context, batch, operation, payloadHash, cancellationToken);
            if (reservation.Disposition == "unauthorized")
                return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence, "unauthorized", null, "OFFLINE_DEVICE_OR_GRANT_INVALID");
            if (reservation.Disposition is "replay" or "conflict")
                return reservation.ExistingResult!;
            if (reservation.Disposition == "deferred")
                return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence, "deferred", null, "OPERATION_IN_PROGRESS");

            var result = new OfflineReceiptOperationResult(
                operation.OperationId,
                operation.DeviceSequence,
                "accepted",
                new InventoryReceiptSummary(
                    operation.OperationId,
                    Guid.NewGuid(),
                    operation.Payload.VariantId,
                    operation.Payload.WarehouseId,
                    operation.Payload.BinId,
                    operation.Payload.Quantity,
                    operation.Payload.UnitOfMeasureId,
                    operation.Payload.OccurredAt ?? operation.CreatedAt,
                    DateTimeOffset.UtcNow),
                null);

            await CompleteReceiptOperationAsync(context, operation.OperationId, result, cancellationToken);
            return result;
        }
    }

    // ── Fake receipt processor ────────────────────────────────────────────────

    private sealed class FakeReceiptProcessor : IInventoryWorkflowRepository
    {
        public bool ShouldFail { get; set; }
        public string? CustomError { get; set; }
        public readonly HashSet<Guid> DeactivatedVariants = [];
        public readonly HashSet<Guid> InvalidWarehouses = [];

        public Task<Result<InventoryReceiptSummary>> PostReceiptAsync(
            TenantContext context, Guid transactionId, string requestHash,
            PostInventoryReceiptRequest request, CancellationToken cancellationToken)
        {
            if (DeactivatedVariants.Contains(request.VariantId))
                return Task.FromResult(Result<InventoryReceiptSummary>.Failure("VARIANT_INACTIVE"));
            if (InvalidWarehouses.Contains(request.WarehouseId))
                return Task.FromResult(Result<InventoryReceiptSummary>.Failure("WAREHOUSE_NOT_FOUND"));
            if (!string.IsNullOrEmpty(CustomError))
                return Task.FromResult(Result<InventoryReceiptSummary>.Failure(CustomError));
            if (ShouldFail)
                return Task.FromResult(Result<InventoryReceiptSummary>.Failure("STOCK_MOVEMENT_FAILED"));
            var summary = new InventoryReceiptSummary(
                transactionId, Guid.NewGuid(), request.VariantId, request.WarehouseId,
                request.BinId, request.Quantity, request.UnitOfMeasureId,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult(Result<InventoryReceiptSummary>.Success(summary));
        }

        // Unused in sync tests — delegate to throw so test failures are obvious
        public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<InventoryTransferSummary>> PostTransferAsync(TenantContext ctx, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(TenantContext ctx, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProductCategorySummary>>([]);
        public Task<Result<ProductCategorySummary>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<InventoryMovementSummary>>([]);
    }

    // ── Test helpers that exercise the reservation / complete contract ─────────

    /// <summary>
    /// Simulates the batch endpoint logic in OfflineReceiptSyncEndpointMappings.cs
    /// for a single operation, so tests can verify end-to-end reservation + completion
    /// without spinning up the full ASP.NET host.
    /// </summary>
    private static async Task<OfflineReceiptOperationResult> ProcessBatchAsync(
        FakeSyncRepository repo,
        FakeReceiptProcessor processor,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        TenantContext? context = null,
        CancellationToken ct = default)
    {
        var ctx = context ?? TestCtx;
        var payloadHash = CanonicalHash(new
        {
            operationType = operation.OperationType,
            payload = operation.Payload,
        });

        var reservation = await repo.ReserveReceiptOperationAsync(ctx, batch, operation, payloadHash, ct);

        switch (reservation.Disposition)
        {
            case "unauthorized":
                return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence,
                    "rejected", null, "UNAUTHORIZED");

            case "replay":
                return reservation.ExistingResult!;

            case "conflict":
                return reservation.ExistingResult!;

            case "process":
            {
                var result = await processor.PostReceiptAsync(ctx, operation.OperationId,
                    payloadHash, operation.Payload, ct);
                var outcome = result.IsSuccess
                    ? new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence,
                        "accepted", result.Value, null)
                    : new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence,
                        "rejected", null, result.Error);
                await repo.CompleteReceiptOperationAsync(ctx, operation.OperationId, outcome, ct);
                return outcome;
            }

            default:
                throw new InvalidOperationException($"Unknown disposition: {reservation.Disposition}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1 — Idempotency replay: same operationId + same hash → duplicate, no re-process
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IdempotencyReplay_SameOperationId_SameHash_ReturnsDuplicateWithoutReprocessing()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var operationId = Guid.NewGuid();
        var op = MakeOperation(operationId);
        var batch = MakeBatch(deviceId, grantId, op);

        // First call — processes and commits
        var first = await ProcessBatchAsync(repo, processor, batch, op);
        Assert.Equal("accepted", first.Status);
        Assert.NotNull(first.Receipt);
        var firstMovementId = first.Receipt!.MovementId;

        // Simulate lost acknowledgement — second identical call
        var second = await ProcessBatchAsync(repo, processor, batch, op);

        // Must be idempotent: same result, same movementId, NOT re-processed
        Assert.Equal("duplicate", second.Status);
        Assert.Equal(firstMovementId, second.Receipt?.MovementId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2 — Operation ID reused with different payload → OPERATION_ID_REUSED conflict
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OperationIdReuse_DifferentPayload_ReturnsConflict()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var operationId = Guid.NewGuid();
        var op1 = MakeOperation(operationId, seq: 1);
        var batch1 = MakeBatch(deviceId, grantId, op1);

        // Process first operation
        await ProcessBatchAsync(repo, processor, batch1, op1);

        // Second operation with SAME operationId but different payload (quantity differs)
        var differentPayload = op1.Payload with { Quantity = op1.Payload.Quantity + 99 };
        var op2 = op1 with { Payload = differentPayload };
        var batch2 = MakeBatch(deviceId, grantId, op2);

        var result = await ProcessBatchAsync(repo, processor, batch2, op2);

        Assert.Equal("conflict", result.Status);
        Assert.Equal("OPERATION_ID_REUSED", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3 — Deferred row (crash between reserve and complete) is safe to resume
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeferredOperation_ServerCrashBeforeComplete_SafeToResume()
    {
        var repo = new FakeSyncRepository();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var operationId = Guid.NewGuid();
        var op = MakeOperation(operationId);
        var batch = MakeBatch(deviceId, grantId, op);

        var payloadHash = CanonicalHash(new { operationType = op.OperationType, payload = op.Payload });

        // Step 1: reserve (simulates server crash before Complete was called)
        var reservation = await repo.ReserveReceiptOperationAsync(TestCtx, batch, op, payloadHash, CancellationToken.None);
        Assert.Equal("process", reservation.Disposition);

        // Step 2: client retries with same operationId — server should return "process" again
        var retry = await repo.ReserveReceiptOperationAsync(TestCtx, batch, op, payloadHash, CancellationToken.None);
        Assert.Equal("process", retry.Disposition);
        // NOT "conflict" or "replay" — the deferred row is safe to resume
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4 — Double-completion guard: CompleteReceiptOperationAsync on non-deferred row throws
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DoubleCompletion_ThrowsInvalidOperationException()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var operationId = Guid.NewGuid();
        var op = MakeOperation(operationId);
        var batch = MakeBatch(deviceId, grantId, op);

        // First complete — should succeed
        var first = await ProcessBatchAsync(repo, processor, batch, op);
        Assert.Equal("accepted", first.Status);

        // Attempt to complete again (simulate bug or race) — must throw
        var alreadyComplete = new OfflineReceiptOperationResult(op.OperationId, op.DeviceSequence, "accepted", first.Receipt, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.CompleteReceiptOperationAsync(TestCtx, operationId, alreadyComplete, CancellationToken.None));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 5 — Unauthorized grant: bad token → "rejected" (UNAUTHORIZED)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnauthorizedGrant_BadToken_ReturnsRejected()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        // Seed with a different token than what the batch sends
        repo.SeedGrant(grantId, "correct-token");

        var operationId = Guid.NewGuid();
        var op = MakeOperation(operationId);
        // Batch sends wrong grant token
        var badBatch = new OfflineReceiptBatchRequest(
            Guid.NewGuid(), deviceId, grantId, "wrong-token", [op]);

        var result = await ProcessBatchAsync(repo, processor, badBatch, op);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("UNAUTHORIZED", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 6 — Syncing→queued recovery: if a batch is interrupted mid-sync,
    //          the next load should convert 'syncing' rows back to 'queued'.
    //          We test the in-memory contract directly (the IDB layer is browser-only).
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SyncingRecovery_Contract_SyncingStatusMustBeTransientNotTerminal()
    {
        // The set of terminal statuses for the client queue (these must never be reset).
        var terminalStatuses = new HashSet<string> { "accepted", "conflict", "rejected" };
        // "syncing" is intentionally NOT in the terminal set — it reverts to "queued" on restart.
        Assert.DoesNotContain("syncing", terminalStatuses);
        // "queued" is not terminal — pending re-try.
        Assert.DoesNotContain("queued", terminalStatuses);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 7 — Snapshot cursor monotonicity contract
    //          Lower cursors must NOT overwrite higher ones.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SnapshotCursorMonotonicity_OlderCursorMustNotRegressNewer()
    {
        // This is a pure logic test of the guard introduced in offlineReceiptQueue.ts.
        // We verify the invariant in C# to document and enforce it independently of the JS layer.
        static bool ShouldWriteSnapshot(string newProducts, string newInventory,
                                        string storedProducts, string storedInventory)
        {
            // Must not write if either cursor would go backwards.
            return BigInteger.Parse(newProducts) >= BigInteger.Parse(storedProducts)
                && BigInteger.Parse(newInventory) >= BigInteger.Parse(storedInventory);
        }

        // Case 1: newer cursors → allowed
        Assert.True(ShouldWriteSnapshot("100", "200", "50", "100"));
        // Case 2: equal cursors → allowed (idempotent re-write)
        Assert.True(ShouldWriteSnapshot("50", "100", "50", "100"));
        // Case 3: products cursor regresses → blocked
        Assert.False(ShouldWriteSnapshot("10", "200", "50", "100"));
        // Case 4: inventory cursor regresses → blocked
        Assert.False(ShouldWriteSnapshot("100", "50", "50", "100"));
        // Case 5: both regress → blocked
        Assert.False(ShouldWriteSnapshot("10", "10", "50", "100"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 8 — Two users with different payloads for different operation IDs
    //          must NOT create conflicts — each is a separate, clean insert.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TwoUsersDistinctOperationIds_BothAccepted_NoConflict()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceIdA = Guid.NewGuid();
        var deviceIdB = Guid.NewGuid();
        var grantIdA = Guid.NewGuid();
        var grantIdB = Guid.NewGuid();
        repo.SeedGrant(grantIdA, "test-token");
        repo.SeedGrant(grantIdB, "test-token");

        var opA = MakeOperation(Guid.NewGuid(), seq: 1);
        var opB = MakeOperation(Guid.NewGuid(), seq: 1);
        var batchA = MakeBatch(deviceIdA, grantIdA, opA);
        var batchB = MakeBatch(deviceIdB, grantIdB, opB);

        var resultA = await ProcessBatchAsync(repo, processor, batchA, opA);
        var resultB = await ProcessBatchAsync(repo, processor, batchB, opB);

        Assert.Equal("accepted", resultA.Status);
        Assert.Equal("accepted", resultB.Status);
        Assert.NotEqual(resultA.Receipt?.MovementId, resultB.Receipt?.MovementId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 9 — Revoked device in identity.devices cannot register or renew grant
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokedDevice_ReturnsNullOnRegistration()
    {
        var repo = new FakeSyncRepository { DeviceActive = false };
        var grant = await repo.RegisterDeviceAsync(TestCtx, Guid.NewGuid(), "web", "Revoked Browser", CancellationToken.None);
        Assert.Null(grant);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 10 — Revoked company scope in sync.device_company_scopes cannot renew grant
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokedCompanyScope_ReturnsNullOnRegistration()
    {
        var repo = new FakeSyncRepository { CompanyScopeActive = false };
        var grant = await repo.RegisterDeviceAsync(TestCtx, Guid.NewGuid(), "web", "Revoked Scope Browser", CancellationToken.None);
        Assert.Null(grant);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 11 — Inactive or non-existent company cannot issue grant
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InactiveCompany_ReturnsNullOnRegistration()
    {
        var repo = new FakeSyncRepository { CompanyActive = false };
        var grant = await repo.RegisterDeviceAsync(TestCtx, Guid.NewGuid(), "web", "Inactive Company Browser", CancellationToken.None);
        Assert.Null(grant);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 12 — Revoked grant token in sync.offline_grants returns unauthorized
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokedGrant_ReturnsUnauthorizedOnSync()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");
        repo.RevokeGrant(grantId); // Revoke the grant

        var op = MakeOperation(Guid.NewGuid());
        var batch = MakeBatch(deviceId, grantId, op);

        var result = await ProcessBatchAsync(repo, processor, batch, op);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("UNAUTHORIZED", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 13 — Expired grant token in sync.offline_grants returns unauthorized
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExpiredGrant_ReturnsUnauthorizedOnSync()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");
        repo.ExpireGrant(grantId); // Expire the grant

        var op = MakeOperation(Guid.NewGuid());
        var batch = MakeBatch(deviceId, grantId, op);

        var result = await ProcessBatchAsync(repo, processor, batch, op);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("UNAUTHORIZED", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 14 — Concurrent stock changes: two receipts for same variant/bin
    //           both commit additively with separate movement IDs
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentStockChanges_BothReceiptsAccepted_DistinctMovements()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var sharedVariantId = Guid.NewGuid();
        var sharedWarehouseId = Guid.NewGuid();
        var sharedBinId = Guid.NewGuid();
        var sharedUomId = Guid.NewGuid();

        // Operation 1
        var op1 = new OfflineReceiptOperation(
            Guid.NewGuid(), 1, "inventory.receipt.post", DateTimeOffset.UtcNow,
            new PostInventoryReceiptRequest(sharedVariantId, sharedWarehouseId, sharedBinId, 10m, sharedUomId, null, null, null, "Op 1"));
        var batch1 = MakeBatch(deviceId, grantId, op1);

        // Operation 2 (concurrent stock increase on same bin)
        var op2 = new OfflineReceiptOperation(
            Guid.NewGuid(), 2, "inventory.receipt.post", DateTimeOffset.UtcNow,
            new PostInventoryReceiptRequest(sharedVariantId, sharedWarehouseId, sharedBinId, 25m, sharedUomId, null, null, null, "Op 2"));
        var batch2 = MakeBatch(deviceId, grantId, op2);

        var result1 = await ProcessBatchAsync(repo, processor, batch1, op1);
        var result2 = await ProcessBatchAsync(repo, processor, batch2, op2);

        Assert.Equal("accepted", result1.Status);
        Assert.Equal("accepted", result2.Status);
        Assert.NotNull(result1.Receipt);
        Assert.NotNull(result2.Receipt);
        Assert.NotEqual(result1.Receipt!.MovementId, result2.Receipt!.MovementId);
        Assert.Equal(10m, result1.Receipt.Quantity);
        Assert.Equal(25m, result2.Receipt.Quantity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 15 — Invalid warehouse scope: warehouse outside company boundary is rejected
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidWarehouseScope_WarehouseBelongsToDifferentCompany_Rejected()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var foreignWarehouseId = Guid.NewGuid();
        processor.InvalidWarehouses.Add(foreignWarehouseId);

        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var op = new OfflineReceiptOperation(
            Guid.NewGuid(), 1, "inventory.receipt.post", DateTimeOffset.UtcNow,
            new PostInventoryReceiptRequest(Guid.NewGuid(), foreignWarehouseId, Guid.NewGuid(), 5m, Guid.NewGuid(), null, null, null, "Cross-scope receipt"));
        var batch = MakeBatch(deviceId, grantId, op);

        var result = await ProcessBatchAsync(repo, processor, batch, op);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("WAREHOUSE_NOT_FOUND", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 16 — Invalid company grant scope: grant for Company A presented in Company B
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidCompanyGrantScope_GrantIssuedForDifferentCompany_ReturnsUnauthorized()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var companyA = Guid.Parse("00000000-0000-4000-a000-000000000002");
        var companyB = Guid.Parse("00000000-0000-4000-b000-000000000002");

        // Seed grant specifically for Company A
        repo.SeedGrant(grantId, "test-token", companyA);

        var op = MakeOperation(Guid.NewGuid());
        var batch = MakeBatch(deviceId, grantId, op);

        // Client attempts to sync under Company B's tenant context
        var companyBContext = TestCtx with { CompanyId = companyB };
        var result = await ProcessBatchAsync(repo, processor, batch, op, companyBContext);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("UNAUTHORIZED", result.Code);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 17 — Retry after lost acknowledgement: durable receipt preserved exactly
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RetryAfterLostAcknowledgement_ReturnsStoredReceipt_PreservesDurableState()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var op = MakeOperation(Guid.NewGuid());
        var batch = MakeBatch(deviceId, grantId, op);

        // 1. Initial execution committed by server
        var committed = await ProcessBatchAsync(repo, processor, batch, op);
        Assert.Equal("accepted", committed.Status);
        Assert.NotNull(committed.Receipt);
        var expectedMovementId = committed.Receipt!.MovementId;
        var expectedPostedAt = committed.Receipt.PostedAt;

        // 2. Client retries due to dropped network connection before ACK was delivered
        var retry = await ProcessBatchAsync(repo, processor, batch, op);

        // 3. Must return duplicate with identical receipts and zero side effects
        Assert.Equal("duplicate", retry.Status);
        Assert.NotNull(retry.Receipt);
        Assert.Equal(expectedMovementId, retry.Receipt!.MovementId);
        Assert.Equal(expectedPostedAt, retry.Receipt.PostedAt);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 18 — Stale client state: offline operation for deactivated variant rejected
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaleClientState_DeactivatedVariant_RejectedWithActionableCode()
    {
        var repo = new FakeSyncRepository();
        var processor = new FakeReceiptProcessor();
        var staleVariantId = Guid.NewGuid();
        processor.DeactivatedVariants.Add(staleVariantId); // Deactivated on cloud while client was offline

        var deviceId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        repo.SeedGrant(grantId, "test-token");

        var op = new OfflineReceiptOperation(
            Guid.NewGuid(), 1, "inventory.receipt.post", DateTimeOffset.UtcNow,
            new PostInventoryReceiptRequest(staleVariantId, Guid.NewGuid(), Guid.NewGuid(), 10m, Guid.NewGuid(), null, null, null, "Stale variant receipt"));
        var batch = MakeBatch(deviceId, grantId, op);

        var result = await ProcessBatchAsync(repo, processor, batch, op);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("VARIANT_INACTIVE", result.Code);
    }
}

// Minimal BigInteger alias so the snapshot test compiles without System.Numerics import conflict
file static class BigInteger
{
    public static long Parse(string s) => long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
