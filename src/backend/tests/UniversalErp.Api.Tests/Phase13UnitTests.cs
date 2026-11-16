using System;
using System.Collections.Generic;
using System.Linq;
using UniversalErp.Application.Consolidations;
using UniversalErp.Application.Integrations;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase13UnitTests
{
    private readonly ConsolidationEliminationEngine _eliminationEngine = new();
    private readonly WebhookSigningEngine _signingEngine = new();

    // ── 1. Consolidation Elimination Engine Unit Tests ─────────────────────────

    [Fact]
    public void CalculateEliminations_WithReciprocalBalances_GeneratesBalancedEliminations()
    {
        var runId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var balancePairs = new List<ConsolidationEliminationEngine.IntercompanyBalancePair>
        {
            new(companyA, companyB, DueFromAmount: 50000m, DueToAmount: 50000m)
        };

        var result = _eliminationEngine.CalculateEliminations(
            runId,
            balancePairs,
            new List<ConsolidationEliminationEngine.IntercompanyTradingVolume>(),
            new List<ConsolidationEliminationEngine.UnrealizedInventoryStock>()
        );

        Assert.True(result.IsBalanced);
        Assert.Equal(50000m, result.TotalIntercompanyBalanceEliminated);
        Assert.Equal(50000m, result.TotalDebits);
        Assert.Equal(50000m, result.TotalCredits);

        var drLine = result.Lines.Single(l => l.DebitAmountPkr > 0);
        var crLine = result.Lines.Single(l => l.CreditAmountPkr > 0);

        Assert.Equal("2200", drLine.AccountCode);
        Assert.Equal("1300", crLine.AccountCode);
    }

    [Fact]
    public void CalculateEliminations_WithTradingVolume_EliminatesSalesAndCogs()
    {
        var runId = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();

        var tradingVolumes = new List<ConsolidationEliminationEngine.IntercompanyTradingVolume>
        {
            new(seller, buyer, TotalSalesAmount: 120000m)
        };

        var result = _eliminationEngine.CalculateEliminations(
            runId,
            new List<ConsolidationEliminationEngine.IntercompanyBalancePair>(),
            tradingVolumes,
            new List<ConsolidationEliminationEngine.UnrealizedInventoryStock>()
        );

        Assert.True(result.IsBalanced);
        Assert.Equal(120000m, result.TotalRevenueEliminated);
        Assert.Equal(120000m, result.TotalDebits);
        Assert.Equal(120000m, result.TotalCredits);

        var drLine = result.Lines.Single(l => l.DebitAmountPkr > 0);
        var crLine = result.Lines.Single(l => l.CreditAmountPkr > 0);

        Assert.Equal("4000", drLine.AccountCode); // Dr Sales Revenue
        Assert.Equal("5000", crLine.AccountCode); // Cr Cost of Goods Sold
    }

    [Fact]
    public void CalculateEliminations_WithUnrealizedInventoryProfit_ReducesGroupInventoryAsset()
    {
        var runId = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();

        // 100 units transferred at PKR 150 each, original cost PKR 100 each -> markup PKR 50/unit
        // 40 units remain held at buyer warehouse -> Unrealized profit = 40 * 50 = PKR 2,000
        var heldStocks = new List<ConsolidationEliminationEngine.UnrealizedInventoryStock>
        {
            new(seller, buyer, QuantityHeld: 40m, UnitCost: 100m, UnitTransferPrice: 150m)
        };

        var result = _eliminationEngine.CalculateEliminations(
            runId,
            new List<ConsolidationEliminationEngine.IntercompanyBalancePair>(),
            new List<ConsolidationEliminationEngine.IntercompanyTradingVolume>(),
            heldStocks,
            eliminateUnrealizedProfit: true
        );

        Assert.True(result.IsBalanced);
        Assert.Equal(2000m, result.TotalUnrealizedProfitEliminated);
        Assert.Equal(2000m, result.TotalDebits);
        Assert.Equal(2000m, result.TotalCredits);

        var drLine = result.Lines.Single(l => l.DebitAmountPkr > 0);
        var crLine = result.Lines.Single(l => l.CreditAmountPkr > 0);

        Assert.Equal("5000", drLine.AccountCode); // Dr COGS
        Assert.Equal("1200", crLine.AccountCode); // Cr Inventory Asset
    }

    [Fact]
    public void CalculateEliminations_FullConglomerateScenario_VerifiesZeroVariance()
    {
        var runId = Guid.NewGuid();
        var coA = Guid.NewGuid();
        var coB = Guid.NewGuid();

        var balancePairs = new List<ConsolidationEliminationEngine.IntercompanyBalancePair>
        {
            new(coA, coB, DueFromAmount: 85000m, DueToAmount: 85000m)
        };
        var trades = new List<ConsolidationEliminationEngine.IntercompanyTradingVolume>
        {
            new(coA, coB, TotalSalesAmount: 250000m)
        };
        var held = new List<ConsolidationEliminationEngine.UnrealizedInventoryStock>
        {
            new(coA, coB, QuantityHeld: 50m, UnitCost: 200m, UnitTransferPrice: 250m) // 50 * 50 = 2,500
        };

        var result = _eliminationEngine.CalculateEliminations(runId, balancePairs, trades, held, true);

        Assert.True(result.IsBalanced);
        Assert.Equal(85000m, result.TotalIntercompanyBalanceEliminated);
        Assert.Equal(250000m, result.TotalRevenueEliminated);
        Assert.Equal(2500m, result.TotalUnrealizedProfitEliminated);
        Assert.Equal(85000m + 250000m + 2500m, result.TotalDebits);
        Assert.Equal(result.TotalDebits, result.TotalCredits);
        Assert.Equal(6, result.Lines.Count); // 2 balance + 2 revenue + 2 unrealized
    }

    // ── 2. Webhook Signing Engine Unit Tests ────────────────────────────────────

    [Fact]
    public void ComputeSignature_ValidInputs_GeneratesDeterministicHmacSha256()
    {
        const string payload = "{\"event\":\"invoice.posted\",\"invoiceNumber\":\"INV-001\"}";
        const string secret = "super-secret-hmac-key-for-testing-12345";

        var sig1 = _signingEngine.ComputeSignature(payload, secret);
        var sig2 = _signingEngine.ComputeSignature(payload, secret);

        Assert.StartsWith("sha256=", sig1);
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void VerifySignature_HeaderMatches_ReturnsTrue()
    {
        const string payload = "{\"event\":\"inventory.movement\"}";
        const string secret = "crypto-key-98765";
        var timestamp = DateTimeOffset.UtcNow;

        var (sigHeader, tsHeader) = _signingEngine.ComputeHeaders(payload, secret, timestamp);
        var isValid = _signingEngine.VerifySignature(payload, secret, sigHeader, tsHeader);

        Assert.True(isValid);

        // Tampered payload fails
        var tampered = _signingEngine.VerifySignature(payload + "tampered", secret, sigHeader, tsHeader);
        Assert.False(tampered);
    }

    [Fact]
    public void SecretGeneration_GeneratesCryptographicallyUniqueHash()
    {
        var secret1 = _signingEngine.GenerateSecret();
        var secret2 = _signingEngine.GenerateSecret();

        Assert.NotEqual(secret1, secret2);
        Assert.Equal(64, secret1.Length); // 32 bytes hex

        var hash = _signingEngine.HashSecret(secret1);
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(secret1, hash);
    }

    [Fact]
    public void CalculateNextRetry_ExponentialBackoffProgression_ExhaustsAfter5Attempts()
    {
        var now = DateTimeOffset.UtcNow;

        // Attempt 1: 2^1 * 30 = 60s
        var retry1 = _signingEngine.CalculateNextRetry(1, now);
        Assert.NotNull(retry1);
        Assert.Equal(60, (int)(retry1.Value - now).TotalSeconds);

        // Attempt 2: 2^2 * 30 = 120s
        var retry2 = _signingEngine.CalculateNextRetry(2, now);
        Assert.NotNull(retry2);
        Assert.Equal(120, (int)(retry2.Value - now).TotalSeconds);

        // Attempt 3: 2^3 * 30 = 240s
        var retry3 = _signingEngine.CalculateNextRetry(3, now);
        Assert.NotNull(retry3);
        Assert.Equal(240, (int)(retry3.Value - now).TotalSeconds);

        // Attempt 4: 2^4 * 30 = 480s
        var retry4 = _signingEngine.CalculateNextRetry(4, now);
        Assert.NotNull(retry4);
        Assert.Equal(480, (int)(retry4.Value - now).TotalSeconds);

        // Attempt 5: Exhausted -> returns null (dead-letter)
        var retry5 = _signingEngine.CalculateNextRetry(5, now);
        Assert.Null(retry5);
    }
}
