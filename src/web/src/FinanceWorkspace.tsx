import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  calculateStraightLineDepreciation,
  calculateReconciliationDiscrepancy,
  computeFinancialStatementSummary,
  formatCurrencyPkr,
  formatMoney,
  type FinancialStatementSummary,
} from "./financeHelpers";

export type Account = {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  currencyCode?: string | null;
  isControlAccount: boolean;
  allowManualPosting: boolean;
  isActive: boolean;
};

export type FiscalPeriod = {
  periodId: string;
  fiscalYearId: string;
  periodNo: number;
  name: string;
  startsOn: string;
  endsOn: string;
  status: string;
  reopenedAt?: string | null;
  reopenReason?: string | null;
};

export type JournalLine = {
  lineNo: number;
  accountId: string;
  accountCode: string;
  accountName: string;
  description?: string | null;
  transactionCurrency: string;
  transactionAmount: number;
  debitFunctional: number;
  creditFunctional: number;
};

export type Journal = {
  journalId: string;
  journalNo: string;
  journalDate: string;
  fiscalPeriodId: string;
  description: string;
  functionalCurrency: string;
  sourceModule: string;
  sourceType: string;
  sourceId: string;
  status: string;
  postedAt?: string | null;
  totalDebit: number;
  totalCredit: number;
  lines: JournalLine[];
};

export type TrialBalanceRow = {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  totalDebit: number;
  totalCredit: number;
  netBalance: number;
};

export type BankAccount = {
  bankAccountId: string;
  accountNumber: string;
  accountName: string;
  bankName: string;
  branchName?: string | null;
  swiftBic?: string | null;
  iban?: string | null;
  currencyCode: string;
  accountType: string;
  glAccountId: string;
  glAccountCode: string;
  glAccountName: string;
  currentBalance: number;
  isActive: boolean;
  createdAt: string;
};

export type BankStatementLine = {
  lineId: string;
  lineNo: number;
  transactionDate: string;
  valueDate?: string | null;
  referenceNumber?: string | null;
  description: string;
  amount: number;
  balanceAfter?: number | null;
  reconciledStatus: string;
  matchedJournalLineId?: string | null;
  matchedAt?: string | null;
};

export type BankStatement = {
  statementId: string;
  bankAccountId: string;
  bankAccountName: string;
  statementNumber: string;
  statementDate: string;
  openingBalance: number;
  closingBalance: number;
  status: string;
  importedAt: string;
  reconciledAt?: string | null;
  lines: BankStatementLine[];
};

export type TreasuryPayment = {
  paymentId: string;
  paymentNumber: string;
  paymentType: string;
  paymentDate: string;
  bankAccountId: string;
  bankAccountName: string;
  destinationBankAccountId?: string | null;
  amount: number;
  currencyCode: string;
  paymentMethod: string;
  referenceNumber?: string | null;
  payeePayerName: string;
  glJournalId?: string | null;
  status: string;
  createdBy: string;
  createdAt: string;
};

export type TaxCode = {
  taxCodeId: string;
  code: string;
  name: string;
  taxType: string;
  ratePercent: number;
  glPayableAccountId: string;
  glPayableAccountCode: string;
  glReceivableAccountId?: string | null;
  isActive: boolean;
};

export type TaxLiability = {
  totalSalesAmount: number;
  totalPurchasesAmount: number;
  totalOutputTax: number;
  totalInputTax: number;
  netTaxPayable: number;
};

export type TaxFiling = {
  filingId: string;
  filingNumber: string;
  periodStart: string;
  periodEnd: string;
  taxType: string;
  totalTaxableAmount: number;
  outputTaxAmount: number;
  inputTaxAmount: number;
  netTaxPayable: number;
  status: string;
  filedAt?: string | null;
  glSettlementJournalId?: string | null;
  notes?: string | null;
};

export type RecurringSchedule = {
  scheduleId: string;
  scheduleName: string;
  scheduleType: string;
  frequency: string;
  startDate: string;
  endDate?: string | null;
  totalAmount: number;
  periodAmount: number;
  debitAccountId: string;
  debitAccountCode: string;
  debitAccountName: string;
  creditAccountId: string;
  creditAccountCode: string;
  creditAccountName: string;
  periodsCount: number;
  postedPeriodsCount: number;
  nextRunDate: string;
  autoReverseAccrual: boolean;
  status: string;
  descriptionTemplate?: string | null;
};

export type FixedAsset = {
  assetId: string;
  assetCode: string;
  assetName: string;
  assetCategory: string;
  acquisitionDate: string;
  acquisitionCost: number;
  salvageValue: number;
  usefulLifeMonths: number;
  depreciationMethod: string;
  assetGlAccountId: string;
  assetGlAccountCode: string;
  accumulatedDeprAccountId: string;
  accumulatedDeprAccountCode: string;
  depreciationExpenseAccountId: string;
  depreciationExpenseAccountCode: string;
  currentBookValue: number;
  accumulatedDepreciation: number;
  status: string;
};

export type AssetDepreciationRun = {
  runId: string;
  runNumber: string;
  runDate: string;
  fiscalPeriodId: string;
  assetId: string;
  assetCode: string;
  assetName: string;
  depreciationAmount: number;
  glJournalId?: string | null;
  status: string;
  createdAt: string;
};

export type PeriodChecklistItem = {
  itemId: string;
  fiscalPeriodId: string;
  taskCode: string;
  taskName: string;
  isCompleted: boolean;
  completedAt?: string | null;
  notes?: string | null;
};

export type AuditLog = {
  logId: string;
  eventType: string;
  entityName: string;
  entityId: string;
  actorUserId: string;
  description: string;
  createdAt: string;
};

export type ControlDashboard = {
  totalAccounts: number;
  totalGlDebits: number;
  totalGlCredits: number;
  glNetVariance: number;
  isGlBalanced: boolean;
  activeBankAccountsCount: number;
  totalCashAndBankBalances: number;
  unreconciledStatementsCount: number;
  activeFixedAssetsCount: number;
  totalFixedAssetBookValue: number;
  activeRecurringSchedulesCount: number;
  openFiscalPeriodsCount: number;
  estimatedNetTaxPayable: number;
};

export type MissingExchangeRateDiagnostic = {
  requiredFor: string;
  currencyPair: string;
  rateType: string;
  requiredDate: string;
  reason: string;
};

export type PresentationStatementLine = {
  accountCode: string;
  accountName: string;
  accountType: string;
  functionalBalance: number;
  presentationBalance?: number | null;
  appliedRate?: number | null;
  rateEffectiveDate?: string | null;
  rateTypeUsed: string;
  translationStatus: string;
};

export type PresentationStatementsResponse = {
  asOfDate: string;
  functionalCurrencyCode: string;
  presentationCurrencyCode: string;
  sameCurrency: boolean;
  translationMethod: string;
  translationStatus: string;
  closingRate?: number | null;
  closingRateDirection?: string | null;
  closingRateEffectiveDate?: string | null;
  closingRateSource?: string | null;
  totalAssetsFunctional?: number | null;
  totalAssetsPresentation?: number | null;
  totalLiabilitiesFunctional?: number | null;
  totalLiabilitiesPresentation?: number | null;
  equityBeforeOciFunctional?: number | null;
  equityBeforeOciPresentation?: number | null;
  totalRevenueFunctional?: number | null;
  totalRevenuePresentation?: number | null;
  totalCostOfSalesFunctional?: number | null;
  totalCostOfSalesPresentation?: number | null;
  totalOperatingExpensesFunctional?: number | null;
  totalOperatingExpensesPresentation?: number | null;
  totalExpensesFunctional?: number | null;
  totalExpensesPresentation?: number | null;
  netIncomeFunctional?: number | null;
  netIncomePresentation?: number | null;
  translationDifferenceOciPresentation?: number | null;
  totalLiabilitiesAndEquityIncludingOciPresentation?: number | null;
  isBalanceSheetBalancedPresentation?: boolean | null;
  balanceSheetDiscrepancyPresentation?: number | null;
  balanceSheetLines: PresentationStatementLine[];
  incomeStatementLines: PresentationStatementLine[];
  missingRates: MissingExchangeRateDiagnostic[];
};

type Tab =
  | "gl"
  | "post-journal"
  | "banking"
  | "reconciliation"
  | "treasury"
  | "tax"
  | "recurring"
  | "assets"
  | "periods"
  | "reports"
  | "audit";

type NewJournalLine = {
  accountId: string;
  description: string;
  debit: string;
  credit: string;
  transactionCurrency?: string;
  exchangeRate?: string;
};

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
};

export default function FinanceWorkspace({ enabled, workspace }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>("gl");
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  // Domain state
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [periods, setPeriods] = useState<FiscalPeriod[]>([]);
  const [journals, setJournals] = useState<Journal[]>([]);
  const [trialBalance, setTrialBalance] = useState<TrialBalanceRow[]>([]);
  const [presentationAsOfDate, setPresentationAsOfDate] = useState(new Date().toISOString().slice(0, 10));
  const [presentationIncomeRateMethod, setPresentationIncomeRateMethod] = useState<"transaction" | "average">("transaction");
  const [presentationStatements, setPresentationStatements] = useState<PresentationStatementsResponse | null>(null);
  const [presentationError, setPresentationError] = useState("");
  const [bankAccounts, setBankAccounts] = useState<BankAccount[]>([]);
  const [statements, setStatements] = useState<BankStatement[]>([]);
  const [payments, setPayments] = useState<TreasuryPayment[]>([]);
  const [taxCodes, setTaxCodes] = useState<TaxCode[]>([]);
  const [taxLiability, setTaxLiability] = useState<TaxLiability | null>(null);
  const [taxFilings, setTaxFilings] = useState<TaxFiling[]>([]);
  const [recurringSchedules, setRecurringSchedules] = useState<RecurringSchedule[]>([]);
  const [fixedAssets, setFixedAssets] = useState<FixedAsset[]>([]);
  const [deprRuns, setDeprRuns] = useState<AssetDepreciationRun[]>([]);
  const [checklist, setChecklist] = useState<PeriodChecklistItem[]>([]);
  const [dashboard, setDashboard] = useState<ControlDashboard | null>(null);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);

  // Selected period for forms & checklist
  const [selectedPeriodId, setSelectedPeriodId] = useState("");

  // New Account form state
  const [newCode, setNewCode] = useState("");
  const [newName, setNewName] = useState("");
  const [newType, setNewType] = useState("expense");
  const [newBalance, setNewBalance] = useState("debit");

  // New Journal form state
  const [journalNo, setJournalNo] = useState(`JRN-${Date.now().toString().slice(-6)}`);
  const [journalDate, setJournalDate] = useState(new Date().toISOString().slice(0, 10));
  const [journalDescription, setJournalDescription] = useState("");
  const [journalLines, setJournalLines] = useState<NewJournalLine[]>([
    { accountId: "", description: "", debit: "0", credit: "0" },
    { accountId: "", description: "", debit: "0", credit: "0" },
  ]);

  // New Bank Account form state
  const [newBankNum, setNewBankNum] = useState("");
  const [newBankAccName, setNewBankAccName] = useState("");
  const [newBankName, setNewBankName] = useState("");
  const [newBankType, setNewBankType] = useState("checking");
  const [newBankGlId, setNewBankGlId] = useState("");

  // New Bank Statement Import form state
  const [stmtBankId, setStmtBankId] = useState("");
  const [stmtNumber, setStmtNumber] = useState(`STMT-${Date.now().toString().slice(-6)}`);
  const [stmtDate, setStmtDate] = useState(new Date().toISOString().slice(0, 10));
  const [stmtOpenBal, setStmtOpenBal] = useState("0");
  const [stmtCloseBal, setStmtCloseBal] = useState("0");
  const [stmtLineDesc, setStmtLineDesc] = useState("");
  const [stmtLineAmt, setStmtLineAmt] = useState("0");
  const [stmtLineRef, setStmtLineRef] = useState("");

  // New Treasury Payment form state
  const [pmtNumber, setPmtNumber] = useState(`PMT-${Date.now().toString().slice(-6)}`);
  const [pmtType, setPmtType] = useState("vendor_disbursement");
  const [pmtDate, setPmtDate] = useState(new Date().toISOString().slice(0, 10));
  const [pmtBankId, setPmtBankId] = useState("");
  const [pmtAmount, setPmtAmount] = useState("0");
  const [pmtMethod, setPmtMethod] = useState("wire");
  const [pmtPayee, setPmtPayee] = useState("");
  const [pmtRef, setPmtRef] = useState("");
  const [pmtSubledgerId, setPmtSubledgerId] = useState("");

  // Tax forms
  const [taxCodeStr, setTaxCodeStr] = useState("");
  const [taxNameStr, setTaxNameStr] = useState("");
  const [taxTypeStr, setTaxTypeStr] = useState("vat");
  const [taxRateStr, setTaxRateStr] = useState("18");
  const [taxPayableGlId, setTaxPayableGlId] = useState("");
  const [filingPeriodStart, setFilingPeriodStart] = useState(new Date().toISOString().slice(0, 10));
  const [filingPeriodEnd, setFilingPeriodEnd] = useState(new Date().toISOString().slice(0, 10));

  // Fixed Asset form state
  const [assetCode, setAssetCode] = useState("");
  const [assetName, setAssetName] = useState("");
  const [assetCat, setAssetCat] = useState("machinery");
  const [assetCost, setAssetCost] = useState("0");
  const [assetSalvage, setAssetSalvage] = useState("0");
  const [assetLifeMonths, setAssetLifeMonths] = useState("60");
  const [assetGlId, setAssetGlId] = useState("");
  const [accumDeprGlId, setAccumDeprGlId] = useState("");
  const [deprExpenseGlId, setDeprExpenseGlId] = useState("");

  // Recurring Schedule form state
  const [schedName, setSchedName] = useState("");
  const [schedType, setSchedType] = useState("accrual");
  const [schedFreq, setSchedFreq] = useState("monthly");
  const [schedTotalAmt, setSchedTotalAmt] = useState("120000");
  const [schedPeriodAmt, setSchedPeriodAmt] = useState("10000");
  const [schedDebitGlId, setSchedDebitGlId] = useState("");
  const [schedCreditGlId, setSchedCreditGlId] = useState("");
  const [schedPeriodsCount, setSchedPeriodsCount] = useState("12");

  const loadAll = async () => {
    if (!enabled) return;
    setLoading(true);
    setError("");
    try {
      const [
        accRes,
        perRes,
        jrnRes,
        tbRes,
        bankRes,
        stmtRes,
        pmtRes,
        taxCRes,
        taxLiabRes,
        taxFRes,
        schedRes,
        assetRes,
        dashRes,
        auditRes,
      ] = await Promise.all([
        apiFetch("/api/v1/finance/accounts", {}, workspace),
        apiFetch("/api/v1/finance/periods", {}, workspace),
        apiFetch("/api/v1/finance/journals", {}, workspace),
        apiFetch("/api/v1/finance/trial-balance", {}, workspace),
        apiFetch("/api/v1/finance/bank-accounts", {}, workspace),
        apiFetch("/api/v1/finance/bank-statements", {}, workspace),
        apiFetch("/api/v1/finance/treasury/payments", {}, workspace),
        apiFetch("/api/v1/finance/tax/codes", {}, workspace),
        apiFetch("/api/v1/finance/tax/liability-summary", {}, workspace),
        apiFetch("/api/v1/finance/tax/filings", {}, workspace),
        apiFetch("/api/v1/finance/recurring-schedules", {}, workspace),
        apiFetch("/api/v1/finance/fixed-assets", {}, workspace),
        apiFetch("/api/v1/finance/dashboard/controls", {}, workspace),
        apiFetch("/api/v1/finance/audit-logs", {}, workspace),
      ]);

      if (accRes.ok) {
        const accList = (await accRes.json()) as Account[];
        setAccounts(accList);
        if (accList.length > 0) {
          if (!newBankGlId) setNewBankGlId(accList[0].accountId);
          if (!pmtSubledgerId) setPmtSubledgerId(accList[0].accountId);
          if (!taxPayableGlId) setTaxPayableGlId(accList[0].accountId);
          if (!assetGlId) setAssetGlId(accList[0].accountId);
          if (!accumDeprGlId) setAccumDeprGlId(accList[0].accountId);
          if (!deprExpenseGlId) setDeprExpenseGlId(accList[0].accountId);
          if (!schedDebitGlId) setSchedDebitGlId(accList[0].accountId);
          if (!schedCreditGlId) setSchedCreditGlId(accList[0].accountId);
        }
      }
      if (perRes.ok) {
        const perList = (await perRes.json()) as FiscalPeriod[];
        setPeriods(perList);
        if (perList.length > 0 && !selectedPeriodId) {
          const openPeriod = perList.find((p) => p.status === "open") ?? perList[0];
          setSelectedPeriodId(openPeriod.periodId);
        }
      }
      if (jrnRes.ok) setJournals((await jrnRes.json()) as Journal[]);
      if (tbRes.ok) setTrialBalance((await tbRes.json()) as TrialBalanceRow[]);
      if (bankRes.ok) {
        const bList = (await bankRes.json()) as BankAccount[];
        setBankAccounts(bList);
        if (bList.length > 0) {
          if (!stmtBankId) setStmtBankId(bList[0].bankAccountId);
          if (!pmtBankId) setPmtBankId(bList[0].bankAccountId);
        }
      }
      if (stmtRes.ok) setStatements((await stmtRes.json()) as BankStatement[]);
      if (pmtRes.ok) setPayments((await pmtRes.json()) as TreasuryPayment[]);
      if (taxCRes.ok) setTaxCodes((await taxCRes.json()) as TaxCode[]);
      if (taxLiabRes.ok) setTaxLiability((await taxLiabRes.json()) as TaxLiability);
      if (taxFRes.ok) setTaxFilings((await taxFRes.json()) as TaxFiling[]);
      if (schedRes.ok) setRecurringSchedules((await schedRes.json()) as RecurringSchedule[]);
      if (assetRes.ok) setFixedAssets((await assetRes.json()) as FixedAsset[]);
      if (dashRes.ok) setDashboard((await dashRes.json()) as ControlDashboard);
      if (auditRes.ok) setAuditLogs((await auditRes.json()) as AuditLog[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load finance data.");
    } finally {
      setLoading(false);
    }
  };

  const functionalCurrency = (
    workspace?.functionalCurrencyCode ||
    workspace?.baseCurrencyCode ||
    journals[0]?.functionalCurrency ||
    "PKR"
  ).trim().toUpperCase();
  const fmt = (amount: number | null | undefined, currency: string = functionalCurrency): string =>
    formatMoney(amount, currency);

  const loadPresentationStatements = async (
    asOfDate: string = presentationAsOfDate,
    incomeRateMethod: "transaction" | "average" = presentationIncomeRateMethod
  ) => {
    if (!enabled || !workspace) return;
    setPresentationError("");
    try {
      const res = await apiFetch(
        `/api/v1/finance/financial-statements/presentation?asOfDate=${encodeURIComponent(asOfDate)}&incomeRateMethod=${encodeURIComponent(incomeRateMethod)}`,
        {},
        workspace
      );
      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        setPresentationStatements(null);
        setPresentationError(errJson.detail || errJson.error || `Presentation statements unavailable (HTTP ${res.status})`);
        return;
      }
      setPresentationStatements((await res.json()) as PresentationStatementsResponse);
    } catch (err) {
      setPresentationStatements(null);
      setPresentationError(err instanceof Error ? err.message : "Failed to load IAS 21 presentation statements.");
    }
  };

  useEffect(() => {
    void loadAll();
  }, [enabled, workspace]);

  useEffect(() => {
    if (!enabled || !workspace) return;
    void loadPresentationStatements(presentationAsOfDate, presentationIncomeRateMethod);
  }, [enabled, workspace, presentationAsOfDate, presentationIncomeRateMethod]);

  // Load checklist whenever selectedPeriodId changes
  useEffect(() => {
    if (!selectedPeriodId) return;
    const fetchChecklist = async () => {
      try {
        const res = await apiFetch(`/api/v1/finance/periods/${selectedPeriodId}/checklist`, {}, workspace);
        if (res.ok) setChecklist((await res.json()) as PeriodChecklistItem[]);
      } catch {
        // silent
      }
    };
    void fetchChecklist();
  }, [selectedPeriodId, workspace]);

  // ── Actions ─────────────────────────────────────────────────────────────────

  const handleCreateAccount = async (e: FormEvent) => {
    e.preventDefault();
    setMessage("");
    setError("");
    try {
      const res = await apiFetch("/api/v1/finance/accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accountCode: newCode.trim().toUpperCase(),
          accountName: newName.trim(),
          accountType: newType,
          normalBalance: newBalance,
          currencyCode: functionalCurrency,
          isControlAccount: false,
          allowManualPosting: true,
        }),
      }, workspace);

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.title || `Account creation failed (HTTP ${res.status})`);
      }
      setMessage(`Account ${newCode} created successfully!`);
      setNewCode("");
      setNewName("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating account.");
    }
  };

  const handlePostJournal = async (e: FormEvent) => {
    e.preventDefault();
    const deb = journalLines.reduce((sum, l) => sum + (parseFloat(l.debit) || 0), 0);
    const cred = journalLines.reduce((sum, l) => sum + (parseFloat(l.credit) || 0), 0);
    if (Math.abs(deb - cred) >= 0.001 || deb <= 0) {
      setError("Journal is unbalanced or debits/credits are zero.");
      return;
    }
    setMessage("");
    setError("");
    const idempotencyKey = crypto.randomUUID();
    try {
      const lines = journalLines.map((l) => {
        const d = parseFloat(l.debit) || 0;
        const c = parseFloat(l.credit) || 0;
        const fxRate = Math.max(0.000001, parseFloat(l.exchangeRate || "1") || 1);
        const lineCurr = (l.transactionCurrency || functionalCurrency).trim().toUpperCase();
        const funcAmt = d > 0 ? d : c;
        const txAmt = lineCurr === functionalCurrency ? funcAmt : Math.round((funcAmt / fxRate) * 100) / 100;
        return {
          accountId: l.accountId,
          description: l.description.trim() || journalDescription.trim(),
          transactionCurrency: lineCurr,
          transactionAmount: txAmt,
          exchangeRate: lineCurr === functionalCurrency ? 1 : fxRate,
          debitFunctional: d,
          creditFunctional: c,
        };
      });

      const res = await apiFetch("/api/v1/finance/journals", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify({
          journalNo: journalNo.trim().toUpperCase(),
          journalDate,
          fiscalPeriodId: selectedPeriodId,
          description: journalDescription.trim(),
          functionalCurrency,
          sourceModule: "manual",
          sourceType: "general",
          sourceId: idempotencyKey,
          lines,
        }),
      }, workspace);

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.title || `Journal posting failed (HTTP ${res.status})`);
      }
      setMessage(`Journal ${journalNo} posted in ${functionalCurrency} successfully!`);
      setJournalNo(`JRN-${Date.now().toString().slice(-6)}`);
      setJournalDescription("");
      void loadAll();
      void loadPresentationStatements();
      setActiveTab("gl");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error posting journal.");
    }
  };

  const handleReverseJournal = async (journalId: string, currentNo: string) => {
    const reason = window.prompt(`Enter audit reason for reversing journal ${currentNo}:`, "Correction of posting");
    if (!reason) return;
    try {
      const res = await apiFetch(`/api/v1/finance/journals/${journalId}/reverse`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason }),
      }, workspace);
      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.title || "Reversal failed");
      }
      setMessage(`Journal ${currentNo} reversed successfully! Opposing journal created.`);
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to reverse journal");
    }
  };

  const handleCreateBankAccount = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch("/api/v1/finance/bank-accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accountNumber: newBankNum.trim(),
          accountName: newBankAccName.trim(),
          bankName: newBankName.trim(),
          currencyCode: functionalCurrency,
          accountType: newBankType,
          glAccountId: newBankGlId,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Failed to create bank account");
      setMessage(`Bank account ${newBankAccName} created!`);
      setNewBankNum("");
      setNewBankAccName("");
      setNewBankName("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating bank account");
    }
  };

  const handleImportBankStatement = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const amt = parseFloat(stmtLineAmt) || 0;
      const res = await apiFetch("/api/v1/finance/bank-statements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          bankAccountId: stmtBankId,
          statementNumber: stmtNumber,
          statementDate: stmtDate,
          openingBalance: parseFloat(stmtOpenBal) || 0,
          closingBalance: parseFloat(stmtCloseBal) || 0,
          lines: [
            {
              lineNo: 1,
              transactionDate: stmtDate,
              referenceNumber: stmtLineRef || undefined,
              description: stmtLineDesc || "Bank statement entry",
              amount: amt,
            },
          ],
        }),
      }, workspace);
      if (!res.ok) throw new Error("Statement import failed");
      setMessage(`Statement ${stmtNumber} imported!`);
      setStmtNumber(`STMT-${Date.now().toString().slice(-6)}`);
      setStmtLineDesc("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error importing statement");
    }
  };

  const handleReconcileStatement = async (statementId: string) => {
    try {
      const res = await apiFetch(`/api/v1/finance/bank-statements/${statementId}/reconcile`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ note: "Auto-reconciled by operator" }),
      }, workspace);
      if (!res.ok) throw new Error("Reconciliation failed");
      const result = await res.json();
      setMessage(`Reconciliation complete! Matched lines: ${result.matchedLinesCount}/${result.totalLinesCount}. Discrepancy: ${functionalCurrency} ${result.discrepancy}`);
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reconciliation failed");
    }
  };

  const handleCreateTreasuryPayment = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch("/api/v1/finance/treasury/payments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          paymentNumber: pmtNumber,
          paymentType: pmtType,
          paymentDate: pmtDate,
          bankAccountId: pmtBankId,
          amount: parseFloat(pmtAmount) || 0,
          currencyCode: functionalCurrency,
          paymentMethod: pmtMethod,
          payeePayerName: pmtPayee,
          referenceNumber: pmtRef || undefined,
          targetSubledgerId: pmtSubledgerId || undefined,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Treasury payment failed");
      setMessage(`Treasury payment ${pmtNumber} executed and posted to GL!`);
      setPmtNumber(`PMT-${Date.now().toString().slice(-6)}`);
      setPmtPayee("");
      setPmtAmount("0");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Treasury payment error");
    }
  };

  const handleCreateTaxCode = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch("/api/v1/finance/tax/codes", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          code: taxCodeStr.trim().toUpperCase(),
          name: taxNameStr.trim(),
          taxType: taxTypeStr,
          ratePercent: parseFloat(taxRateStr) || 0,
          glPayableAccountId: taxPayableGlId,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Create tax code failed");
      setMessage(`Tax code ${taxCodeStr} created!`);
      setTaxCodeStr("");
      setTaxNameStr("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating tax code");
    }
  };

  const handleCreateTaxFiling = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const fNum = `TAX-${Date.now().toString().slice(-6)}`;
      const res = await apiFetch("/api/v1/finance/tax/filings", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          filingNumber: fNum,
          periodStart: filingPeriodStart,
          periodEnd: filingPeriodEnd,
          taxType: "vat",
          notes: "Generated from Finance Workspace",
        }),
      }, workspace);
      if (!res.ok) throw new Error("Create tax filing failed");
      setMessage(`Tax filing ${fNum} generated!`);
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating tax filing");
    }
  };

  const handleSettleTaxFiling = async (filingId: string) => {
    if (bankAccounts.length === 0) {
      setError("No bank account available for tax settlement");
      return;
    }
    try {
      const res = await apiFetch(`/api/v1/finance/tax/filings/${filingId}/settle`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          bankAccountId: bankAccounts[0].bankAccountId,
          paymentReference: `TAX-PAY-${Date.now().toString().slice(-4)}`,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Tax settlement failed");
      setMessage("Tax filing settled and GL payment journal posted!");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error settling tax filing");
    }
  };

  const handleCreateRecurringSchedule = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch("/api/v1/finance/recurring-schedules", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          scheduleName: schedName.trim(),
          scheduleType: schedType,
          frequency: schedFreq,
          startDate: new Date().toISOString().slice(0, 10),
          totalAmount: parseFloat(schedTotalAmt) || 0,
          periodAmount: parseFloat(schedPeriodAmt) || 0,
          debitAccountId: schedDebitGlId,
          creditAccountId: schedCreditGlId,
          periodsCount: parseInt(schedPeriodsCount, 10) || 12,
          autoReverseAccrual: schedType === "accrual",
        }),
      }, workspace);
      if (!res.ok) throw new Error("Failed to create recurring schedule");
      setMessage(`Recurring schedule ${schedName} created!`);
      setSchedName("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating schedule");
    }
  };

  const handlePostRecurringPeriod = async (scheduleId: string) => {
    try {
      const res = await apiFetch(`/api/v1/finance/recurring-schedules/${scheduleId}/post-period`, {
        method: "POST",
      }, workspace);
      if (!res.ok) throw new Error("Failed to post recurring period");
      setMessage("Recurring schedule period posted to GL!");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error posting recurring period");
    }
  };

  const handleCreateFixedAsset = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch("/api/v1/finance/fixed-assets", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          assetCode: assetCode.trim().toUpperCase(),
          assetName: assetName.trim(),
          assetCategory: assetCat,
          acquisitionDate: new Date().toISOString().slice(0, 10),
          acquisitionCost: parseFloat(assetCost) || 0,
          salvageValue: parseFloat(assetSalvage) || 0,
          usefulLifeMonths: parseInt(assetLifeMonths, 10) || 60,
          depreciationMethod: "straight_line",
          assetGlAccountId: assetGlId,
          accumulatedDeprAccountId: accumDeprGlId,
          depreciationExpenseAccountId: deprExpenseGlId,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Failed to create fixed asset");
      setMessage(`Fixed asset ${assetCode} registered!`);
      setAssetCode("");
      setAssetName("");
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating asset");
    }
  };

  const handleExecuteDepreciationRun = async () => {
    if (!selectedPeriodId) {
      setError("Please select a fiscal period first");
      return;
    }
    try {
      const res = await apiFetch("/api/v1/finance/fixed-assets/depreciation-runs", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          runDate: new Date().toISOString().slice(0, 10),
          fiscalPeriodId: selectedPeriodId,
        }),
      }, workspace);
      if (!res.ok) throw new Error("Depreciation run failed");
      const list = (await res.json()) as AssetDepreciationRun[];
      setDeprRuns(list);
      setMessage(`Depreciation run executed! ${list.length} assets depreciated with GL journals posted.`);
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error running depreciation");
    }
  };

  const handlePeriodAction = async (action: "soft-close" | "close" | "reopen", periodId: string) => {
    try {
      let body: any = {};
      if (action === "reopen") {
        const reason = window.prompt("Enter authorized reason for reopening fiscal period:", "CFO authorized post-audit adjustment");
        if (!reason) return;
        body = { reason };
      } else {
        body = { note: `Action executed by operator at ${new Date().toISOString()}` };
      }

      const res = await apiFetch(`/api/v1/finance/periods/${periodId}/${action}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      }, workspace);
      if (!res.ok) throw new Error(`Period ${action} failed`);
      setMessage(`Fiscal period successfully updated (${action})!`);
      void loadAll();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Error on period ${action}`);
    }
  };

  const handleToggleChecklist = async (item: PeriodChecklistItem) => {
    try {
      const res = await apiFetch(`/api/v1/finance/periods/${item.fiscalPeriodId}/checklist/${item.itemId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          isCompleted: !item.isCompleted,
          notes: item.notes || "Updated via workspace",
        }),
      }, workspace);
      if (!res.ok) throw new Error("Failed to update checklist item");
      setChecklist((prev) =>
        prev.map((c) => (c.itemId === item.itemId ? { ...c, isCompleted: !c.isCompleted } : c))
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Checklist update error");
    }
  };

  // ── Financial Statements calculation ─────────────────────────────────────────
  const financialSummary: FinancialStatementSummary = computeFinancialStatementSummary(trialBalance);

  return (
    <section className="catalog-panel" aria-label="Finance & Treasury Management">
      <div className="panel-header">
        <div>
          <h2>Finance & Treasury Management (Phase 8)</h2>
          <p>
            General Ledger, Cash & Banking, Statements & Reconciliation, Treasury Payments, Tax Engine,
            Fixed Assets & Depreciation, Fiscal Periods & Close, Audit Trail & Financial Statements.
          </p>
        </div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "0.4rem" }}>
          <button type="button" className={activeTab === "gl" ? "active-tab" : ""} onClick={() => setActiveTab("gl")}>
            General Ledger ({journals.length})
          </button>
          <button type="button" className={activeTab === "post-journal" ? "active-tab" : ""} onClick={() => setActiveTab("post-journal")}>
            + Post Journal
          </button>
          <button type="button" className={activeTab === "banking" ? "active-tab" : ""} onClick={() => setActiveTab("banking")}>
            Bank Accounts ({bankAccounts.length})
          </button>
          <button type="button" className={activeTab === "reconciliation" ? "active-tab" : ""} onClick={() => setActiveTab("reconciliation")}>
            Reconciliation ({statements.length})
          </button>
          <button type="button" className={activeTab === "treasury" ? "active-tab" : ""} onClick={() => setActiveTab("treasury")}>
            Treasury ({payments.length})
          </button>
          <button type="button" className={activeTab === "tax" ? "active-tab" : ""} onClick={() => setActiveTab("tax")}>
            Tax Engine ({taxCodes.length})
          </button>
          <button type="button" className={activeTab === "recurring" ? "active-tab" : ""} onClick={() => setActiveTab("recurring")}>
            Recurring ({recurringSchedules.length})
          </button>
          <button type="button" className={activeTab === "assets" ? "active-tab" : ""} onClick={() => setActiveTab("assets")}>
            Fixed Assets ({fixedAssets.length})
          </button>
          <button type="button" className={activeTab === "periods" ? "active-tab" : ""} onClick={() => setActiveTab("periods")}>
            Fiscal Periods ({periods.length})
          </button>
          <button type="button" className={activeTab === "reports" ? "active-tab" : ""} onClick={() => setActiveTab("reports")}>
            Statements & Reports
          </button>
          <button type="button" className={activeTab === "audit" ? "active-tab" : ""} onClick={() => setActiveTab("audit")}>
            Audit & Controls
          </button>
        </div>
      </div>

      {loading && <p className="catalog-message">Loading financial ledgers & treasury state…</p>}
      {message && <p className="catalog-message" style={{ color: "#166534", fontWeight: 600 }}>{message}</p>}
      {error && <p className="catalog-message" style={{ color: "#991b1b", fontWeight: 600 }}>{error}</p>}

      {/* ── 1. General Ledger & Chart of Accounts ─────────────────────────── */}
      {activeTab === "gl" && (
        <div>
          <form onSubmit={handleCreateAccount} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Add New Ledger Account</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Account Code
                <input required placeholder="e.g. 1030" value={newCode} onChange={(e) => setNewCode(e.target.value)} />
              </label>
              <label>
                Account Name
                <input required placeholder="e.g. Petty Cash" value={newName} onChange={(e) => setNewName(e.target.value)} />
              </label>
              <label>
                Category
                <select value={newType} onChange={(e) => setNewType(e.target.value)}>
                  <option value="asset">Asset</option>
                  <option value="liability">Liability</option>
                  <option value="equity">Equity</option>
                  <option value="revenue">Revenue</option>
                  <option value="expense">Expense</option>
                </select>
              </label>
              <label>
                Normal Balance
                <select value={newBalance} onChange={(e) => setNewBalance(e.target.value)}>
                  <option value="debit">Debit</option>
                  <option value="credit">Credit</option>
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Create Account</button>
              </div>
            </div>
          </form>

          <h4>Journal Ledger & Drill-down</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem", marginBottom: "1.5rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Journal No</th>
                <th style={{ padding: "0.5rem" }}>Date</th>
                <th style={{ padding: "0.5rem" }}>Description</th>
                <th style={{ padding: "0.5rem" }}>Source</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Total Debit</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Total Credit</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
                <th style={{ padding: "0.5rem" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {journals.map((j) => (
                <tr key={j.journalId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{j.journalNo}</td>
                  <td style={{ padding: "0.5rem" }}>{j.journalDate}</td>
                  <td style={{ padding: "0.5rem" }}>{j.description}</td>
                  <td style={{ padding: "0.5rem" }}>{j.sourceModule} ({j.sourceType})</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(j.totalDebit, j.functionalCurrency || functionalCurrency)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(j.totalCredit, j.functionalCurrency || functionalCurrency)}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{
                      padding: "0.2rem 0.5rem",
                      borderRadius: "4px",
                      fontSize: "0.75rem",
                      background: j.status === "posted" ? "#dcfce7" : "#f1f5f9",
                      color: j.status === "posted" ? "#166534" : "#475569",
                    }}>
                      {j.status}
                    </span>
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    {j.status === "posted" && !j.journalNo.startsWith("REV-") && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem", background: "#fef2f2", color: "#991b1b", border: "1px solid #fecaca" }}
                        onClick={() => handleReverseJournal(j.journalId, j.journalNo)}
                      >
                        Reverse
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 2. Post Journal Entry Tab ─────────────────────────────────────── */}
      {activeTab === "post-journal" && (
        <form onSubmit={handlePostJournal} className="inventory-form">
          <h4>Post Balanced Double-Entry Journal</h4>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "0.75rem", marginBottom: "1rem" }}>
            <label>
              Journal Number
              <input required value={journalNo} onChange={(e) => setJournalNo(e.target.value)} />
            </label>
            <label>
              Date
              <input type="date" required value={journalDate} onChange={(e) => setJournalDate(e.target.value)} />
            </label>
            <label>
              Fiscal Period
              <select value={selectedPeriodId} onChange={(e) => setSelectedPeriodId(e.target.value)}>
                {periods.map((p) => (
                  <option key={p.periodId} value={p.periodId}>
                    {p.name} ({p.status})
                  </option>
                ))}
              </select>
            </label>
            <label style={{ gridColumn: "span 2" }}>
              Description
              <input required placeholder="Reason for journal posting" value={journalDescription} onChange={(e) => setJournalDescription(e.target.value)} />
            </label>
          </div>

          <div style={{ marginBottom: "1rem" }}>
            <h5>Journal Lines (Minimum 2 Balanced Lines Required — Functional Currency: {functionalCurrency})</h5>
            {journalLines.map((line, idx) => (
              <div key={idx} style={{ display: "grid", gridTemplateColumns: "2fr 2fr 0.7fr 0.8fr 1fr 1fr auto", gap: "0.5rem", marginBottom: "0.5rem" }}>
                <select
                  value={line.accountId}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].accountId = e.target.value;
                    setJournalLines(next);
                  }}
                  required
                >
                  <option value="">Select Account</option>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName} ({a.accountType})
                    </option>
                  ))}
                </select>
                <input
                  placeholder="Line description"
                  value={line.description}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].description = e.target.value;
                    setJournalLines(next);
                  }}
                />
                <input
                  placeholder={functionalCurrency}
                  maxLength={3}
                  value={line.transactionCurrency ?? functionalCurrency}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].transactionCurrency = e.target.value.toUpperCase();
                    setJournalLines(next);
                  }}
                  title="Transaction currency (defaults to functional currency)"
                />
                <input
                  type="number"
                  step="0.000001"
                  min="0.000001"
                  placeholder="FX 1.0"
                  value={line.exchangeRate ?? "1"}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].exchangeRate = e.target.value;
                    setJournalLines(next);
                  }}
                  title="Exchange rate to functional currency"
                />
                <input
                  type="number"
                  step="0.01"
                  placeholder={`Debit (${functionalCurrency})`}
                  value={line.debit}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].debit = e.target.value;
                    if (parseFloat(e.target.value) > 0) next[idx].credit = "0";
                    setJournalLines(next);
                  }}
                />
                <input
                  type="number"
                  step="0.01"
                  placeholder={`Credit (${functionalCurrency})`}
                  value={line.credit}
                  onChange={(e) => {
                    const next = [...journalLines];
                    next[idx].credit = e.target.value;
                    if (parseFloat(e.target.value) > 0) next[idx].debit = "0";
                    setJournalLines(next);
                  }}
                />
                {journalLines.length > 2 && (
                  <button type="button" onClick={() => setJournalLines(journalLines.filter((_, i) => i !== idx))}>
                    ✕
                  </button>
                )}
              </div>
            ))}
            <button
              type="button"
              style={{ fontSize: "0.8rem", padding: "0.3rem 0.6rem", marginTop: "0.5rem" }}
              onClick={() =>
                setJournalLines([
                  ...journalLines,
                  { accountId: "", description: "", debit: "0", credit: "0", transactionCurrency: functionalCurrency, exchangeRate: "1" },
                ])
              }
            >
              + Add Line
            </button>
          </div>

          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderTop: "2px solid #e2e8f0", paddingTop: "0.75rem" }}>
            <div>
              <strong>Debits:</strong> {fmt(journalLines.reduce((s, l) => s + (parseFloat(l.debit) || 0), 0))} |{" "}
              <strong>Credits:</strong> {fmt(journalLines.reduce((s, l) => s + (parseFloat(l.credit) || 0), 0))}
            </div>
            <button type="submit">Post to General Ledger ({functionalCurrency})</button>
          </div>
        </form>
      )}

      {/* ── 3. Bank Accounts ──────────────────────────────────────────────── */}
      {activeTab === "banking" && (
        <div>
          <form onSubmit={handleCreateBankAccount} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Register Bank Account</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Account Number / IBAN
                <input required placeholder="PK99MEZN00..." value={newBankNum} onChange={(e) => setNewBankNum(e.target.value)} />
              </label>
              <label>
                Account Title
                <input required placeholder="Meezan Corporate" value={newBankAccName} onChange={(e) => setNewBankAccName(e.target.value)} />
              </label>
              <label>
                Bank Name
                <input required placeholder="Meezan Bank Ltd" value={newBankName} onChange={(e) => setNewBankName(e.target.value)} />
              </label>
              <label>
                Account Type
                <select value={newBankType} onChange={(e) => setNewBankType(e.target.value)}>
                  <option value="checking">Checking</option>
                  <option value="savings">Savings</option>
                  <option value="cash_drawer">Cash Drawer</option>
                  <option value="clearing">Clearing</option>
                </select>
              </label>
              <label>
                GL Ledger Account
                <select value={newBankGlId} onChange={(e) => setNewBankGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Create Bank Account</button>
              </div>
            </div>
          </form>

          <h4>Active Bank Accounts</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Account Name</th>
                <th style={{ padding: "0.5rem" }}>Bank</th>
                <th style={{ padding: "0.5rem" }}>Account Number</th>
                <th style={{ padding: "0.5rem" }}>GL Link</th>
                <th style={{ padding: "0.5rem" }}>Type</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Current Balance</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {bankAccounts.map((b) => (
                <tr key={b.bankAccountId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{b.accountName}</td>
                  <td style={{ padding: "0.5rem" }}>{b.bankName}</td>
                  <td style={{ padding: "0.5rem" }}>{b.accountNumber}</td>
                  <td style={{ padding: "0.5rem" }}>{b.glAccountCode} - {b.glAccountName}</td>
                  <td style={{ padding: "0.5rem" }}>{b.accountType}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>{fmt(b.currentBalance, b.currencyCode || functionalCurrency)}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{ padding: "0.2rem 0.5rem", borderRadius: "4px", fontSize: "0.75rem", background: b.isActive ? "#dcfce7" : "#fee2e2" }}>
                      {b.isActive ? "active" : "inactive"}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 4. Reconciliation ─────────────────────────────────────────────── */}
      {activeTab === "reconciliation" && (
        <div>
          <form onSubmit={handleImportBankStatement} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Import Bank Statement</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Bank Account
                <select value={stmtBankId} onChange={(e) => setStmtBankId(e.target.value)}>
                  {bankAccounts.map((b) => (
                    <option key={b.bankAccountId} value={b.bankAccountId}>
                      {b.accountName} ({b.bankName})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Statement Number
                <input required value={stmtNumber} onChange={(e) => setStmtNumber(e.target.value)} />
              </label>
              <label>
                Date
                <input type="date" required value={stmtDate} onChange={(e) => setStmtDate(e.target.value)} />
              </label>
              <label>
                Opening Balance
                <input type="number" step="0.01" value={stmtOpenBal} onChange={(e) => setStmtOpenBal(e.target.value)} />
              </label>
              <label>
                Closing Balance
                <input type="number" step="0.01" value={stmtCloseBal} onChange={(e) => setStmtCloseBal(e.target.value)} />
              </label>
              <label>
                Line Description
                <input placeholder="e.g. Online Transfer" value={stmtLineDesc} onChange={(e) => setStmtLineDesc(e.target.value)} />
              </label>
              <label>
                Line Amount
                <input type="number" step="0.01" value={stmtLineAmt} onChange={(e) => setStmtLineAmt(e.target.value)} />
              </label>
              <label>
                Line Reference
                <input placeholder="e.g. TXN-1234" value={stmtLineRef} onChange={(e) => setStmtLineRef(e.target.value)} />
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Import Statement</button>
              </div>
            </div>
          </form>

          <h4>Imported Statements & Auto-Reconciliation</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Statement No</th>
                <th style={{ padding: "0.5rem" }}>Bank Account</th>
                <th style={{ padding: "0.5rem" }}>Date</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Opening</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Closing</th>
                <th style={{ padding: "0.5rem" }}>Lines</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
                <th style={{ padding: "0.5rem" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {statements.map((s) => (
                <tr key={s.statementId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{s.statementNumber}</td>
                  <td style={{ padding: "0.5rem" }}>{s.bankAccountName}</td>
                  <td style={{ padding: "0.5rem" }}>{s.statementDate}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(s.openingBalance)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(s.closingBalance)}</td>
                  <td style={{ padding: "0.5rem" }}>{s.lines.length} lines</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{
                      padding: "0.2rem 0.5rem",
                      borderRadius: "4px",
                      fontSize: "0.75rem",
                      background: s.status === "reconciled" ? "#dcfce7" : "#fef9c3",
                      color: s.status === "reconciled" ? "#166534" : "#854d0e",
                    }}>
                      {s.status}
                    </span>
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    {s.status !== "reconciled" && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                        onClick={() => handleReconcileStatement(s.statementId)}
                      >
                        Auto-Reconcile
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 5. Treasury Payments ─────────────────────────────────────────── */}
      {activeTab === "treasury" && (
        <div>
          <form onSubmit={handleCreateTreasuryPayment} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Execute Treasury Payment & Post GL Journal</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Payment Number
                <input required value={pmtNumber} onChange={(e) => setPmtNumber(e.target.value)} />
              </label>
              <label>
                Type
                <select value={pmtType} onChange={(e) => setPmtType(e.target.value)}>
                  <option value="vendor_disbursement">Vendor Disbursement (AP)</option>
                  <option value="customer_collection">Customer Collection (AR)</option>
                  <option value="inter_bank_transfer">Inter-Bank Transfer</option>
                  <option value="direct_expense">Direct Expense</option>
                </select>
              </label>
              <label>
                Date
                <input type="date" required value={pmtDate} onChange={(e) => setPmtDate(e.target.value)} />
              </label>
              <label>
                Bank Account
                <select value={pmtBankId} onChange={(e) => setPmtBankId(e.target.value)}>
                  {bankAccounts.map((b) => (
                    <option key={b.bankAccountId} value={b.bankAccountId}>
                      {b.accountName} ({b.bankName})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Amount ({functionalCurrency})
                <input type="number" step="0.01" required value={pmtAmount} onChange={(e) => setPmtAmount(e.target.value)} />
              </label>
              <label>
                Method
                <select value={pmtMethod} onChange={(e) => setPmtMethod(e.target.value)}>
                  <option value="wire">Wire</option>
                  <option value="ach">ACH</option>
                  <option value="cheque">Cheque</option>
                  <option value="cash">Cash</option>
                  <option value="internal">Internal Transfer</option>
                  <option value="card">Card</option>
                </select>
              </label>
              <label>
                Payee / Payer Name
                <input required placeholder="Beneficiary name" value={pmtPayee} onChange={(e) => setPmtPayee(e.target.value)} />
              </label>
              <label>
                Reference / Cheque No
                <input placeholder="e.g. CHQ-9900" value={pmtRef} onChange={(e) => setPmtRef(e.target.value)} />
              </label>
              <label>
                Target Subledger / GL Account
                <select value={pmtSubledgerId} onChange={(e) => setPmtSubledgerId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Execute Payment</button>
              </div>
            </div>
          </form>

          <h4>Treasury Payments Ledger</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Payment No</th>
                <th style={{ padding: "0.5rem" }}>Type</th>
                <th style={{ padding: "0.5rem" }}>Date</th>
                <th style={{ padding: "0.5rem" }}>Bank</th>
                <th style={{ padding: "0.5rem" }}>Payee / Payer</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Amount</th>
                <th style={{ padding: "0.5rem" }}>Method</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {payments.map((p) => (
                <tr key={p.paymentId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{p.paymentNumber}</td>
                  <td style={{ padding: "0.5rem" }}>{p.paymentType}</td>
                  <td style={{ padding: "0.5rem" }}>{p.paymentDate}</td>
                  <td style={{ padding: "0.5rem" }}>{p.bankAccountName}</td>
                  <td style={{ padding: "0.5rem" }}>{p.payeePayerName}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>{fmt(p.amount, p.currencyCode || functionalCurrency)}</td>
                  <td style={{ padding: "0.5rem" }}>{p.paymentMethod}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{ padding: "0.2rem 0.5rem", borderRadius: "4px", fontSize: "0.75rem", background: "#dcfce7", color: "#166534" }}>
                      {p.status}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 6. Tax Engine ─────────────────────────────────────────────────── */}
      {activeTab === "tax" && (
        <div>
          {taxLiability && (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "1rem", marginBottom: "1.5rem" }}>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Taxable Sales</span>
                <h4>{fmt(taxLiability.totalSalesAmount)}</h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Output Tax (Collected)</span>
                <h4 style={{ color: "#0284c7" }}>{fmt(taxLiability.totalOutputTax)}</h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Input Tax (Paid)</span>
                <h4 style={{ color: "#16a34a" }}>{fmt(taxLiability.totalInputTax)}</h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Net Tax Payable / (Credit)</span>
                <h4 style={{ color: taxLiability.netTaxPayable > 0 ? "#dc2626" : "#16a34a" }}>
                  {fmt(taxLiability.netTaxPayable)}
                </h4>
              </div>
            </div>
          )}

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1.5rem", marginBottom: "1.5rem" }}>
            <form onSubmit={handleCreateTaxCode} className="inventory-form">
              <h4>Define Tax Code</h4>
              <label>
                Code
                <input required placeholder="GST-18" value={taxCodeStr} onChange={(e) => setTaxCodeStr(e.target.value)} />
              </label>
              <label>
                Name
                <input required placeholder="Standard Sales Tax 18%" value={taxNameStr} onChange={(e) => setTaxNameStr(e.target.value)} />
              </label>
              <label>
                Tax Type
                <select value={taxTypeStr} onChange={(e) => setTaxTypeStr(e.target.value)}>
                  <option value="vat">VAT / Sales Tax</option>
                  <option value="sales_tax_output">Sales Tax Output</option>
                  <option value="withholding_tax_input">Withholding Tax Input</option>
                  <option value="customs">Customs Duty</option>
                </select>
              </label>
              <label>
                Rate (%)
                <input type="number" step="0.01" required value={taxRateStr} onChange={(e) => setTaxRateStr(e.target.value)} />
              </label>
              <label>
                Payable GL Account
                <select value={taxPayableGlId} onChange={(e) => setTaxPayableGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <button type="submit" style={{ marginTop: "0.5rem" }}>Save Tax Code</button>
            </form>

            <form onSubmit={handleCreateTaxFiling} className="inventory-form">
              <h4>Generate Periodic Tax Filing</h4>
              <label>
                Period Start
                <input type="date" required value={filingPeriodStart} onChange={(e) => setFilingPeriodStart(e.target.value)} />
              </label>
              <label>
                Period End
                <input type="date" required value={filingPeriodEnd} onChange={(e) => setFilingPeriodEnd(e.target.value)} />
              </label>
              <button type="submit" style={{ marginTop: "0.5rem" }}>Generate Tax Return</button>
            </form>
          </div>

          <h4>Tax Filings & Settlements</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Filing No</th>
                <th style={{ padding: "0.5rem" }}>Period</th>
                <th style={{ padding: "0.5rem" }}>Type</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Output Tax</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Input Tax</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Net Payable</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
                <th style={{ padding: "0.5rem" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {taxFilings.map((f) => (
                <tr key={f.filingId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{f.filingNumber}</td>
                  <td style={{ padding: "0.5rem" }}>{f.periodStart} to {f.periodEnd}</td>
                  <td style={{ padding: "0.5rem" }}>{f.taxType}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(f.outputTaxAmount)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(f.inputTaxAmount)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>{fmt(f.netTaxPayable)}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{
                      padding: "0.2rem 0.5rem",
                      borderRadius: "4px",
                      fontSize: "0.75rem",
                      background: f.status === "settled" ? "#dcfce7" : "#fee2e2",
                      color: f.status === "settled" ? "#166534" : "#991b1b",
                    }}>
                      {f.status}
                    </span>
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    {f.status !== "settled" && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                        onClick={() => handleSettleTaxFiling(f.filingId)}
                      >
                        Settle & Post GL
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 7. Recurring Schedules & Accruals ──────────────────────────────── */}
      {activeTab === "recurring" && (
        <div>
          <form onSubmit={handleCreateRecurringSchedule} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Create Recurring Schedule / Accrual / Prepayment</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Schedule Name
                <input required placeholder="Office Rent Accrual" value={schedName} onChange={(e) => setSchedName(e.target.value)} />
              </label>
              <label>
                Schedule Type
                <select value={schedType} onChange={(e) => setSchedType(e.target.value)}>
                  <option value="accrual">Accrual (Auto-Reverse)</option>
                  <option value="prepayment">Prepayment Amortization</option>
                  <option value="recurring_expense">Recurring Expense</option>
                  <option value="amortization">Amortization</option>
                </select>
              </label>
              <label>
                Frequency
                <select value={schedFreq} onChange={(e) => setSchedFreq(e.target.value)}>
                  <option value="monthly">Monthly</option>
                  <option value="quarterly">Quarterly</option>
                  <option value="annual">Annual</option>
                </select>
              </label>
              <label>
                Total Contract Amount
                <input type="number" step="0.01" required value={schedTotalAmt} onChange={(e) => setSchedTotalAmt(e.target.value)} />
              </label>
              <label>
                Period Amount
                <input type="number" step="0.01" required value={schedPeriodAmt} onChange={(e) => setSchedPeriodAmt(e.target.value)} />
              </label>
              <label>
                Periods Count
                <input type="number" required value={schedPeriodsCount} onChange={(e) => setSchedPeriodsCount(e.target.value)} />
              </label>
              <label>
                Debit Account
                <select value={schedDebitGlId} onChange={(e) => setSchedDebitGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Credit Account
                <select value={schedCreditGlId} onChange={(e) => setSchedCreditGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Create Schedule</button>
              </div>
            </div>
          </form>

          <h4>Active Recurring Schedules</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Name</th>
                <th style={{ padding: "0.5rem" }}>Type</th>
                <th style={{ padding: "0.5rem" }}>Freq</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Period Amount</th>
                <th style={{ padding: "0.5rem" }}>Progress</th>
                <th style={{ padding: "0.5rem" }}>Next Run</th>
                <th style={{ padding: "0.5rem" }}>Debit Account</th>
                <th style={{ padding: "0.5rem" }}>Credit Account</th>
                <th style={{ padding: "0.5rem" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {recurringSchedules.map((s) => (
                <tr key={s.scheduleId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{s.scheduleName}</td>
                  <td style={{ padding: "0.5rem" }}>{s.scheduleType}</td>
                  <td style={{ padding: "0.5rem" }}>{s.frequency}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>{fmt(s.periodAmount)}</td>
                  <td style={{ padding: "0.5rem" }}>{s.postedPeriodsCount} / {s.periodsCount}</td>
                  <td style={{ padding: "0.5rem" }}>{s.nextRunDate}</td>
                  <td style={{ padding: "0.5rem" }}>{s.debitAccountCode}</td>
                  <td style={{ padding: "0.5rem" }}>{s.creditAccountCode}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <button
                      type="button"
                      style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                      onClick={() => handlePostRecurringPeriod(s.scheduleId)}
                    >
                      Post Period
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 8. Fixed Assets & Depreciation ─────────────────────────────────── */}
      {activeTab === "assets" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
            <h4>Fixed Asset Register</h4>
            <button
              type="button"
              style={{ background: "#2563eb", color: "#fff", padding: "0.5rem 1rem", borderRadius: "6px" }}
              onClick={handleExecuteDepreciationRun}
            >
              ▶ Run Monthly Depreciation
            </button>
          </div>

          <form onSubmit={handleCreateFixedAsset} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Add New Fixed Asset</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Asset Code
                <input required placeholder="AST-001" value={assetCode} onChange={(e) => setAssetCode(e.target.value)} />
              </label>
              <label>
                Asset Name
                <input required placeholder="Delivery Van" value={assetName} onChange={(e) => setAssetName(e.target.value)} />
              </label>
              <label>
                Category
                <select value={assetCat} onChange={(e) => setAssetCat(e.target.value)}>
                  <option value="equipment">Equipment</option>
                  <option value="machinery">Machinery</option>
                  <option value="vehicles">Vehicles</option>
                  <option value="buildings">Buildings</option>
                  <option value="it_hardware">IT Hardware</option>
                  <option value="furniture">Furniture</option>
                </select>
              </label>
              <label>
                Cost ({functionalCurrency})
                <input type="number" step="0.01" required value={assetCost} onChange={(e) => setAssetCost(e.target.value)} />
              </label>
              <label>
                Salvage Value
                <input type="number" step="0.01" value={assetSalvage} onChange={(e) => setAssetSalvage(e.target.value)} />
              </label>
              <label>
                Useful Life (Months)
                <input type="number" required value={assetLifeMonths} onChange={(e) => setAssetLifeMonths(e.target.value)} />
              </label>
              <label>
                Asset GL (1500)
                <select value={assetGlId} onChange={(e) => setAssetGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Accumulated Depr GL (1510)
                <select value={accumDeprGlId} onChange={(e) => setAccumDeprGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Depr Expense GL (6050)
                <select value={deprExpenseGlId} onChange={(e) => setDeprExpenseGlId(e.target.value)}>
                  {accounts.map((a) => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.accountCode} - {a.accountName}
                    </option>
                  ))}
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Register Asset</button>
              </div>
            </div>
          </form>

          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Asset Code</th>
                <th style={{ padding: "0.5rem" }}>Name</th>
                <th style={{ padding: "0.5rem" }}>Category</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Cost</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Accumulated</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Current Book Value</th>
                <th style={{ padding: "0.5rem" }}>Method</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {fixedAssets.map((a) => (
                <tr key={a.assetId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{a.assetCode}</td>
                  <td style={{ padding: "0.5rem" }}>{a.assetName}</td>
                  <td style={{ padding: "0.5rem" }}>{a.assetCategory}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(a.acquisitionCost)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(a.accumulatedDepreciation)}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600, color: "#0284c7" }}>
                    {fmt(a.currentBookValue)}
                  </td>
                  <td style={{ padding: "0.5rem" }}>{a.depreciationMethod}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{ padding: "0.2rem 0.5rem", borderRadius: "4px", fontSize: "0.75rem", background: "#dcfce7", color: "#166534" }}>
                      {a.status}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 9. Fiscal Periods & Month-End Close ─────────────────────────────── */}
      {activeTab === "periods" && (
        <div>
          <h4>Fiscal Periods & Closing Control</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem", marginBottom: "1.5rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Period</th>
                <th style={{ padding: "0.5rem" }}>Start Date</th>
                <th style={{ padding: "0.5rem" }}>End Date</th>
                <th style={{ padding: "0.5rem" }}>Status</th>
                <th style={{ padding: "0.5rem" }}>Reopened Audit Info</th>
                <th style={{ padding: "0.5rem" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {periods.map((p) => (
                <tr key={p.periodId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{p.name} (P{p.periodNo})</td>
                  <td style={{ padding: "0.5rem" }}>{p.startsOn}</td>
                  <td style={{ padding: "0.5rem" }}>{p.endsOn}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span style={{
                      padding: "0.2rem 0.5rem",
                      borderRadius: "4px",
                      fontSize: "0.75rem",
                      background: p.status === "open" ? "#dcfce7" : p.status === "soft_closed" ? "#fef9c3" : "#fee2e2",
                      color: p.status === "open" ? "#166534" : p.status === "soft_closed" ? "#854d0e" : "#991b1b",
                    }}>
                      {p.status}
                    </span>
                  </td>
                  <td style={{ padding: "0.5rem", fontSize: "0.8rem", color: "#64748b" }}>
                    {p.reopenReason ? `Reopened: ${p.reopenReason}` : "—"}
                  </td>
                  <td style={{ padding: "0.5rem", display: "flex", gap: "0.3rem" }}>
                    {p.status === "open" && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.4rem" }}
                        onClick={() => handlePeriodAction("soft-close", p.periodId)}
                      >
                        Soft-Close
                      </button>
                    )}
                    {(p.status === "open" || p.status === "soft_closed") && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.4rem", background: "#ef4444", color: "#fff" }}
                        onClick={() => handlePeriodAction("close", p.periodId)}
                      >
                        Hard-Close
                      </button>
                    )}
                    {p.status === "closed" && (
                      <button
                        type="button"
                        style={{ fontSize: "0.75rem", padding: "0.2rem 0.4rem", background: "#f59e0b", color: "#fff" }}
                        onClick={() => handlePeriodAction("reopen", p.periodId)}
                      >
                        Reopen (Audit)
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {checklist.length > 0 && (
            <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <h4>Period Close Checklist ({checklist.filter((c) => c.isCompleted).length}/{checklist.length} Completed)</h4>
              <ul style={{ listStyle: "none", padding: 0, margin: "0.5rem 0 0 0" }}>
                {checklist.map((item) => (
                  <li key={item.itemId} style={{ display: "flex", alignItems: "center", gap: "0.5rem", padding: "0.4rem 0" }}>
                    <input
                      type="checkbox"
                      checked={item.isCompleted}
                      onChange={() => handleToggleChecklist(item)}
                    />
                    <span style={{ textDecoration: item.isCompleted ? "line-through" : "none", color: item.isCompleted ? "#64748b" : "#0f172a" }}>
                      <strong>[{item.taskCode}]</strong> {item.taskName}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* ── 10. Financial Statements & Reports ─────────────────────────────── */}
      {activeTab === "reports" && (
        <div style={{ display: "grid", gap: "1.5rem" }}>
          <div>
            <h4 style={{ marginBottom: "0.75rem" }}>
              Functional / Accounting Currency Financial Statements ({functionalCurrency})
            </h4>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1.5rem" }}>
              {/* Balance Sheet Card */}
              <div style={{ padding: "1.5rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <h4>Balance Sheet ({functionalCurrency})</h4>
                  <span style={{
                    padding: "0.25rem 0.5rem",
                    borderRadius: "4px",
                    fontSize: "0.75rem",
                    fontWeight: 600,
                    background: financialSummary.isBalanced ? "#dcfce7" : "#fee2e2",
                    color: financialSummary.isBalanced ? "#166534" : "#991b1b",
                  }}>
                    {financialSummary.isBalanced ? "✓ Equation Balanced" : "⚠ Variance Detected"}
                  </span>
                </div>
                <div style={{ marginTop: "1rem", fontSize: "0.9rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0" }}>
                    <span>Total Assets</span>
                    <strong>{fmt(financialSummary.totalAssets)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0" }}>
                    <span>Total Liabilities</span>
                    <strong>{fmt(financialSummary.totalLiabilities)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0" }}>
                    <span>Owner's Equity</span>
                    <strong>{fmt(financialSummary.totalEquity)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0", color: "#0284c7" }}>
                    <span>Current Period Net Income</span>
                    <strong>{fmt(financialSummary.netIncome)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.5rem 0", borderTop: "2px solid #cbd5e1", marginTop: "0.5rem" }}>
                    <span>Total Liabilities & Equity</span>
                    <strong>{fmt(financialSummary.totalLiabilities + financialSummary.totalEquity + financialSummary.netIncome)}</strong>
                  </div>
                </div>
              </div>

              {/* Income Statement Card */}
              <div style={{ padding: "1.5rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <h4>Income Statement (P&L · {functionalCurrency})</h4>
                <div style={{ marginTop: "1rem", fontSize: "0.9rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0", color: "#16a34a" }}>
                    <span>Operating Revenue</span>
                    <strong>{fmt(financialSummary.totalRevenue)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.3rem 0", color: "#dc2626" }}>
                    <span>Total Expenses & COGS</span>
                    <strong>{fmt(financialSummary.totalExpenses)}</strong>
                  </div>
                  <div style={{ display: "flex", justifyContent: "space-between", padding: "0.5rem 0", borderTop: "2px solid #cbd5e1", marginTop: "1.5rem" }}>
                    <span style={{ fontWeight: 600 }}>Net Profit / (Loss)</span>
                    <strong style={{ fontSize: "1.1rem", color: financialSummary.netIncome >= 0 ? "#16a34a" : "#dc2626" }}>
                      {fmt(financialSummary.netIncome)}
                    </strong>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Presentation Currency Financial Statements (IAS 21) */}
          <div style={{ padding: "1.5rem", background: "#ffffff", borderRadius: "8px", border: "1px solid #cbd5e1" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "0.75rem", marginBottom: "1rem" }}>
              <div>
                <h4 style={{ margin: 0 }}>
                  Presentation Currency Reports ({presentationStatements?.presentationCurrencyCode || workspace?.presentationCurrencyCode || workspace?.functionalCurrencyCode || workspace?.baseCurrencyCode || "PKR"}) · IAS 21 Translation
                </h4>
                <p style={{ margin: "4px 0 0 0", fontSize: "0.8rem", color: "#475569" }}>
                  Assets & Liabilities use Closing Rate; Income & Expenses use Transaction-date (or explicit Average) Rate; Equity uses Historical Transaction Rate; translation differences post to OCI.
                </p>
              </div>
              <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
                <input
                  type="date"
                  value={presentationAsOfDate}
                  onChange={(e) => setPresentationAsOfDate(e.target.value)}
                  style={{ padding: "0.35rem 0.5rem", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                />
                <select
                  value={presentationIncomeRateMethod}
                  onChange={(e) => setPresentationIncomeRateMethod(e.target.value as "transaction" | "average")}
                  style={{ padding: "0.35rem 0.5rem", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                >
                  <option value="transaction">Transaction-date P&L Rate</option>
                  <option value="average">Average Period P&L Rate</option>
                </select>
                <button
                  type="button"
                  onClick={() => void loadPresentationStatements(presentationAsOfDate, presentationIncomeRateMethod)}
                  style={{ padding: "0.35rem 0.75rem" }}
                >
                  Refresh Translation
                </button>
              </div>
            </div>

            {presentationError && (
              <p style={{ color: "#991b1b", background: "#fef2f2", padding: "0.75rem", borderRadius: "6px" }}>
                {presentationError}
              </p>
            )}

            {presentationStatements && (
              <>
                <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", marginBottom: "1rem", fontSize: "0.8rem" }}>
                  <span style={{ padding: "0.25rem 0.6rem", borderRadius: "4px", background: "#f1f5f9" }}>
                    <strong>Status:</strong> {presentationStatements.translationStatus}
                  </span>
                  <span style={{ padding: "0.25rem 0.6rem", borderRadius: "4px", background: "#f1f5f9" }}>
                    <strong>Closing Rate:</strong>{" "}
                    {presentationStatements.closingRate
                      ? `${presentationStatements.closingRate} (${presentationStatements.closingRateDirection} · ${presentationStatements.closingRateEffectiveDate} · ${presentationStatements.closingRateSource})`
                      : "Unavailable"}
                  </span>
                </div>

                {presentationStatements.missingRates.length > 0 && (
                  <div style={{ background: "#fffbeb", border: "1px solid #f59e0b", borderRadius: "6px", padding: "0.75rem 1rem", marginBottom: "1rem", fontSize: "0.85rem", color: "#92400e" }}>
                    <strong>Presentation Translation Unavailable for Missing Rates (No 1.0 Fallback Applied):</strong>
                    <ul style={{ margin: "0.4rem 0 0 1.2rem" }}>
                      {presentationStatements.missingRates.map((m, i) => (
                        <li key={i}>
                          [{m.rateType}] {m.currencyPair} on {m.requiredDate} ({m.requiredFor}): {m.reason}
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1.5rem", marginBottom: "1rem" }}>
                  <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "6px", border: "1px solid #e2e8f0", fontSize: "0.875rem" }}>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                      <strong>Translated Balance Sheet ({presentationStatements.presentationCurrencyCode})</strong>
                      <span style={{
                        padding: "0.15rem 0.45rem",
                        borderRadius: "4px",
                        fontSize: "0.72rem",
                        background: presentationStatements.isBalanceSheetBalancedPresentation ? "#dcfce7" : "#fef3c7",
                        color: presentationStatements.isBalanceSheetBalancedPresentation ? "#166534" : "#92400e",
                      }}>
                        {presentationStatements.translationStatus.startsWith("UNAVAILABLE")
                          ? "Unavailable (Missing Rate)"
                          : presentationStatements.isBalanceSheetBalancedPresentation
                          ? "✓ Balanced with OCI"
                          : "Check OCI / Variance"}
                      </span>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Total Assets</span>
                      <strong>{fmt(presentationStatements.totalAssetsPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Total Liabilities</span>
                      <strong>{fmt(presentationStatements.totalLiabilitiesPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Equity (Historical Rate, pre-OCI)</span>
                      <strong>{fmt(presentationStatements.equityBeforeOciPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Translated Net Income</span>
                      <strong>{fmt(presentationStatements.netIncomePresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0", color: "#0369a1" }}>
                      <span>OCI — Foreign Currency Translation Difference</span>
                      <strong>{fmt(presentationStatements.translationDifferenceOciPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.4rem 0", borderTop: "1px solid #cbd5e1", marginTop: "0.4rem" }}>
                      <span>Total Liabilities, Equity & OCI</span>
                      <strong>{fmt(presentationStatements.totalLiabilitiesAndEquityIncludingOciPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                  </div>

                  <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "6px", border: "1px solid #e2e8f0", fontSize: "0.875rem" }}>
                    <strong style={{ display: "block", marginBottom: "0.5rem" }}>
                      Translated Income Statement ({presentationStatements.presentationCurrencyCode})
                    </strong>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Revenue ({fmt(presentationStatements.totalRevenueFunctional)})</span>
                      <strong>{fmt(presentationStatements.totalRevenuePresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Cost of Sales ({fmt(presentationStatements.totalCostOfSalesFunctional)})</span>
                      <strong>{fmt(presentationStatements.totalCostOfSalesPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.25rem 0" }}>
                      <span>Operating Expenses ({fmt(presentationStatements.totalOperatingExpensesFunctional)})</span>
                      <strong>{fmt(presentationStatements.totalOperatingExpensesPresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", padding: "0.4rem 0", borderTop: "1px solid #cbd5e1", marginTop: "0.4rem" }}>
                      <span>Translated Net Income</span>
                      <strong>{fmt(presentationStatements.netIncomePresentation, presentationStatements.presentationCurrencyCode)}</strong>
                    </div>
                  </div>
                </div>

                {(presentationStatements.balanceSheetLines.length > 0 || presentationStatements.incomeStatementLines.length > 0) && (
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.8rem" }}>
                    <thead>
                      <tr style={{ background: "#f1f5f9", textAlign: "left" }}>
                        <th style={{ padding: "0.4rem" }}>Account</th>
                        <th style={{ padding: "0.4rem" }}>Type</th>
                        <th style={{ padding: "0.4rem", textAlign: "right" }}>Functional ({presentationStatements.functionalCurrencyCode})</th>
                        <th style={{ padding: "0.4rem" }}>IAS 21 Rule</th>
                        <th style={{ padding: "0.4rem", textAlign: "right" }}>Applied Rate</th>
                        <th style={{ padding: "0.4rem", textAlign: "right" }}>Presentation ({presentationStatements.presentationCurrencyCode})</th>
                      </tr>
                    </thead>
                    <tbody>
                      {[...presentationStatements.balanceSheetLines, ...presentationStatements.incomeStatementLines].map((line, i) => (
                        <tr key={`${line.accountCode}-${i}`} style={{ borderBottom: "1px solid #e2e8f0" }}>
                          <td style={{ padding: "0.4rem" }}>{line.accountCode} — {line.accountName}</td>
                          <td style={{ padding: "0.4rem" }}>{line.accountType}</td>
                          <td style={{ padding: "0.4rem", textAlign: "right" }}>{fmt(line.functionalBalance, presentationStatements.functionalCurrencyCode)}</td>
                          <td style={{ padding: "0.4rem" }}>{line.rateTypeUsed}</td>
                          <td style={{ padding: "0.4rem", textAlign: "right" }}>{line.appliedRate ?? "Unavailable"}</td>
                          <td style={{ padding: "0.4rem", textAlign: "right", fontWeight: 600 }}>
                            {line.presentationBalance !== null && line.presentationBalance !== undefined
                              ? fmt(line.presentationBalance, presentationStatements.presentationCurrencyCode)
                              : "Unavailable"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </>
            )}
          </div>

          <div>
            <h4>Trial Balance Detail ({functionalCurrency})</h4>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
              <thead>
                <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                  <th style={{ padding: "0.5rem" }}>Account</th>
                  <th style={{ padding: "0.5rem" }}>Name</th>
                  <th style={{ padding: "0.5rem" }}>Type</th>
                  <th style={{ padding: "0.5rem", textAlign: "right" }}>Debits</th>
                  <th style={{ padding: "0.5rem", textAlign: "right" }}>Credits</th>
                  <th style={{ padding: "0.5rem", textAlign: "right" }}>Net Balance</th>
                </tr>
              </thead>
              <tbody>
                {trialBalance.map((r) => (
                  <tr key={r.accountId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                    <td style={{ padding: "0.5rem", fontWeight: 600 }}>{r.accountCode}</td>
                    <td style={{ padding: "0.5rem" }}>{r.accountName}</td>
                    <td style={{ padding: "0.5rem" }}>{r.accountType}</td>
                    <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(r.totalDebit)}</td>
                    <td style={{ padding: "0.5rem", textAlign: "right" }}>{fmt(r.totalCredit)}</td>
                    <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>{fmt(r.netBalance)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── 11. Audit Logs & Financial Dashboard ───────────────────────────── */}
      {activeTab === "audit" && (
        <div>
          {dashboard && (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "1rem", marginBottom: "1.5rem" }}>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>GL Balanced Status</span>
                <h4 style={{ color: dashboard.isGlBalanced ? "#16a34a" : "#dc2626" }}>
                  {dashboard.isGlBalanced ? "BALANCED (0 Var)" : "UNBALANCED"}
                </h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Total Cash & Bank</span>
                <h4>{fmt(dashboard.totalCashAndBankBalances)}</h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Fixed Asset Book Value</span>
                <h4>{fmt(dashboard.totalFixedAssetBookValue)}</h4>
              </div>
              <div style={{ padding: "1rem", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "0.75rem", color: "#64748b" }}>Open Fiscal Periods</span>
                <h4>{dashboard.openFiscalPeriodsCount}</h4>
              </div>
            </div>
          )}

          <h4>Financial Event Audit Trail</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                <th style={{ padding: "0.5rem" }}>Timestamp</th>
                <th style={{ padding: "0.5rem" }}>Event</th>
                <th style={{ padding: "0.5rem" }}>Entity</th>
                <th style={{ padding: "0.5rem" }}>Entity ID</th>
                <th style={{ padding: "0.5rem" }}>Description</th>
              </tr>
            </thead>
            <tbody>
              {auditLogs.map((log) => (
                <tr key={log.logId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem" }}>{new Date(log.createdAt).toLocaleString()}</td>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}>{log.eventType}</td>
                  <td style={{ padding: "0.5rem" }}>{log.entityName}</td>
                  <td style={{ padding: "0.5rem", fontSize: "0.75rem", color: "#64748b" }}>{log.entityId}</td>
                  <td style={{ padding: "0.5rem" }}>{log.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
