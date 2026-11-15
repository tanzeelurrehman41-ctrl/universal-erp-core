namespace UniversalErp.Application.Finance;

public sealed record StatementMatchCandidate(
    Guid StatementLineId,
    int LineNo,
    DateOnly TransactionDate,
    string? ReferenceNumber,
    decimal Amount);

public sealed record GlJournalMatchCandidate(
    Guid JournalLineId,
    Guid JournalId,
    string JournalNo,
    DateOnly JournalDate,
    decimal NetAmount, // Positive if debit, negative if credit
    string? ReferenceNumber);

public sealed record ReconciliationMatch(
    Guid StatementLineId,
    Guid JournalLineId,
    decimal Amount,
    string MatchType);

public static class BankReconciliationEngine
{
    public static (IReadOnlyList<ReconciliationMatch> Matches, decimal UnmatchedStatementTotal, decimal Discrepancy) Reconcile(
        IReadOnlyList<StatementMatchCandidate> statementLines,
        IReadOnlyList<GlJournalMatchCandidate> glLines,
        decimal openingBalance,
        decimal closingBalance,
        decimal glBookBalance)
    {
        var matches = new List<ReconciliationMatch>();
        var unmatchedGl = glLines.ToList();
        decimal matchedStatementSum = 0m;
        decimal totalStatementSum = statementLines.Sum(s => s.Amount);

        foreach (var stmt in statementLines)
        {
            // 1. Exact match by reference number and amount
            var exactRefMatch = unmatchedGl.FirstOrDefault(gl =>
                gl.NetAmount == stmt.Amount &&
                !string.IsNullOrWhiteSpace(stmt.ReferenceNumber) &&
                !string.IsNullOrWhiteSpace(gl.ReferenceNumber) &&
                string.Equals(stmt.ReferenceNumber.Trim(), gl.ReferenceNumber.Trim(), StringComparison.OrdinalIgnoreCase));

            if (exactRefMatch is not null)
            {
                matches.Add(new ReconciliationMatch(stmt.StatementLineId, exactRefMatch.JournalLineId, stmt.Amount, "exact_reference"));
                unmatchedGl.Remove(exactRefMatch);
                matchedStatementSum += stmt.Amount;
                continue;
            }

            // 2. Exact match by amount and close date (within 5 days)
            var amountAndDateMatch = unmatchedGl.FirstOrDefault(gl =>
                gl.NetAmount == stmt.Amount &&
                Math.Abs(stmt.TransactionDate.DayNumber - gl.JournalDate.DayNumber) <= 5);

            if (amountAndDateMatch is not null)
            {
                matches.Add(new ReconciliationMatch(stmt.StatementLineId, amountAndDateMatch.JournalLineId, stmt.Amount, "amount_and_date"));
                unmatchedGl.Remove(amountAndDateMatch);
                matchedStatementSum += stmt.Amount;
                continue;
            }

            // 3. Fallback: exact match by amount
            var amountMatch = unmatchedGl.FirstOrDefault(gl => gl.NetAmount == stmt.Amount);
            if (amountMatch is not null)
            {
                matches.Add(new ReconciliationMatch(stmt.StatementLineId, amountMatch.JournalLineId, stmt.Amount, "amount_only"));
                unmatchedGl.Remove(amountMatch);
                matchedStatementSum += stmt.Amount;
            }
        }

        var unmatchedStatementTotal = totalStatementSum - matchedStatementSum;
        var computedClosingBalance = openingBalance + totalStatementSum;
        var discrepancy = closingBalance - glBookBalance;

        return (matches, unmatchedStatementTotal, discrepancy);
    }
}
