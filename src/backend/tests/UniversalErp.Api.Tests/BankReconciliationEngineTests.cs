using UniversalErp.Application.Finance;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class BankReconciliationEngineTests
{
    [Fact]
    public void Reconcile_MatchesExactReferenceAndAmount()
    {
        var stmtLines = new List<StatementMatchCandidate>
        {
            new(Guid.NewGuid(), 1, new DateOnly(2026, 11, 5), "CHQ-1001", 5000m),
            new(Guid.NewGuid(), 2, new DateOnly(2026, 11, 10), "DEP-2002", -12000m)
        };

        var glLines = new List<GlJournalMatchCandidate>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "JRN-01", new DateOnly(2026, 11, 4), 5000m, "CHQ-1001"),
            new(Guid.NewGuid(), Guid.NewGuid(), "JRN-02", new DateOnly(2026, 11, 12), -12000m, "DEP-2002")
        };

        var (matches, unmatchedStmt, discrepancy) = BankReconciliationEngine.Reconcile(
            stmtLines, glLines, 0m, -7000m, -7000m);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal("exact_reference", m.MatchType));
        Assert.Equal(0m, unmatchedStmt);
        Assert.Equal(0m, discrepancy);
    }

    [Fact]
    public void Reconcile_MatchesAmountAndDate_WhenReferenceDiffersOrMissing()
    {
        var stmtLines = new List<StatementMatchCandidate>
        {
            new(Guid.NewGuid(), 1, new DateOnly(2026, 11, 5), null, 3500m)
        };

        var glLines = new List<GlJournalMatchCandidate>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "JRN-03", new DateOnly(2026, 11, 7), 3500m, "POS-DAILY")
        };

        var (matches, unmatchedStmt, discrepancy) = BankReconciliationEngine.Reconcile(
            stmtLines, glLines, 1000m, 4500m, 4500m);

        Assert.Single(matches);
        Assert.Equal("amount_and_date", matches[0].MatchType);
        Assert.Equal(3500m, matches[0].Amount);
        Assert.Equal(0m, unmatchedStmt);
        Assert.Equal(0m, discrepancy);
    }

    [Fact]
    public void Reconcile_ReportsUnmatchedStatementsAndDiscrepancy()
    {
        var stmtLines = new List<StatementMatchCandidate>
        {
            new(Guid.NewGuid(), 1, new DateOnly(2026, 11, 5), "REF-A", 1000m),
            new(Guid.NewGuid(), 2, new DateOnly(2026, 11, 6), "REF-B", 2500m) // Unmatched
        };

        var glLines = new List<GlJournalMatchCandidate>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "JRN-01", new DateOnly(2026, 11, 5), 1000m, "REF-A")
        };

        var (matches, unmatchedStmt, discrepancy) = BankReconciliationEngine.Reconcile(
            stmtLines, glLines, 10000m, 13500m, 11000m);

        Assert.Single(matches);
        Assert.Equal(2500m, unmatchedStmt);
        Assert.Equal(2500m, discrepancy); // Closing bank (13500) - GL (11000) = 2500
    }
}
