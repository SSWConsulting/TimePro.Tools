using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Accounting;

public interface IAccountingDiagnosticsService
{
    AccountingDiagnosticsGuide GetUseCaseGuide(string? useCase = null);

    Task<TimesheetTaxMismatchReport> FindTimesheetTaxMismatchesAsync(
        string? query = null,
        int skip = 0,
        int limit = 500,
        string field = "DateCreated",
        string dir = "desc",
        CancellationToken ct = default);

    Task<InvoiceReconciliationDiagnostic?> DiagnoseInvoiceReconciliationAsync(
        int invoiceId,
        bool includeWriteOffs = true,
        CancellationToken ct = default);

    Task<ClientAccountingPositionDiagnostic> DiagnoseClientAccountingPositionAsync(
        string clientId,
        bool includeRates = false,
        CancellationToken ct = default);
}

public sealed class AccountingDiagnosticsService : IAccountingDiagnosticsService
{
    private readonly ITimeProApiClient _api;

    public AccountingDiagnosticsService(ITimeProApiClient api)
    {
        _api = api;
    }

    public AccountingDiagnosticsGuide GetUseCaseGuide(string? useCase = null) =>
        new(
            UseCase: useCase,
            AskUser:
            [
                "What are you trying to verify: invoices, receipts, aged debtors, prepaid drawdown, unbilled work, credit notes, or Xero/export parity?",
                "What external evidence should TimePro be compared to: Excel, CSV, Xero MCP, bank feed MCP, or another source?",
                "Which date field should drive the comparison: invoice date, date created, payment date, or service period?",
                "Should GST be included, excluded, or reported in both ex-GST and inc-GST forms?",
                "Should credit notes and write-offs be netted into the total or reported separately?",
                "What tolerance should be used for amount mismatches?"
            ],
            RecommendedCommands:
            [
                "tp accounting invoice-diagnostics <invoiceId> --json",
                "tp accounting client-diagnostics <clientId> --rates --json",
                "tp accounting tax-mismatches --query <clientOrInvoice> --json",
                "tp receipt list --search <clientOrReference> --json",
                "tp prepaid summary <invoiceId> --json",
                "tp query --from <yyyy-MM-dd> --to <yyyy-MM-dd> --json"
            ],
            RecommendedMcpTools:
            [
                "DiagnoseInvoiceReconciliation",
                "DiagnoseClientAccountingPosition",
                "FindTimesheetTaxMismatches",
                "ListPaidReceipts",
                "GetPrepaidStatus",
                "QueryTimesheets"
            ],
            Note: "Accounting diagnostics are read-only. Use the CLI command first when working locally; MCP tools expose the same report shapes for spreadsheet, CSV, Xero, or bank-feed composition.");

    public async Task<TimesheetTaxMismatchReport> FindTimesheetTaxMismatchesAsync(
        string? query = null,
        int skip = 0,
        int limit = 500,
        string field = "DateCreated",
        string dir = "desc",
        CancellationToken ct = default)
    {
        var page = await _api.ListInvoicesAsync(query, skip, limit, field, dir, onlyRecurring: false, ct);
        if (page is null)
        {
            return new TimesheetTaxMismatchReport(
                query, skip, limit, 0, 0, 0, 0, [],
                "No invoices returned.",
                BuildTaxMismatchComparisonAdvice());
        }

        var mismatches = new List<TimesheetTaxMismatchRow>();
        var invoicesWithNonZeroTax = 0;

        foreach (var invoiceRow in page.Data)
        {
            var invoice = await _api.GetInvoiceAsync(invoiceRow.InvoiceId, ct);
            if (invoice is null || !HasNonZeroInvoiceTax(invoice))
                continue;

            invoicesWithNonZeroTax++;
            var timesheets = await _api.GetInvoiceTimesheetsAsync(invoiceRow.InvoiceId, "allocated", ct);

            foreach (var timesheet in timesheets)
            {
                var amount = TimesheetAmountExGst(timesheet);
                if (Math.Abs(amount) <= 0.01m || !HasZeroTimesheetTax(timesheet))
                    continue;

                mismatches.Add(new TimesheetTaxMismatchRow(
                    InvoiceId: invoice.InvoiceId,
                    ClientId: invoice.ClientId,
                    InvoiceDate: invoice.DateCreated?.ToString("yyyy-MM-dd"),
                    InvoiceTaxPct: TaxPercent(invoice.SalesTaxPct),
                    InvoiceSalesTaxAmt: invoice.SalesTaxAmt,
                    TimeId: timesheet.TimeId,
                    EmpId: timesheet.EmpId,
                    EmpName: timesheet.EmpName,
                    ProjectId: timesheet.ProjectId,
                    ProjectName: timesheet.ProjectName,
                    CategoryId: timesheet.CategoryId,
                    BillableId: timesheet.BillableId,
                    TimesheetDate: timesheet.DateCreated?.ToString("yyyy-MM-dd"),
                    AmountExGst: amount,
                    TimesheetTaxPct: TaxPercent(timesheet.SalesTaxPct),
                    TimesheetSalesTaxAmt: timesheet.SalesTaxAmt,
                    Notes: timesheet.Notes));
            }
        }

        return new TimesheetTaxMismatchReport(
            Query: query,
            Skip: skip,
            Limit: limit,
            ScannedInvoiceCount: page.Data.Count,
            TotalInvoicesAvailable: page.Total,
            InvoicesWithNonZeroTax: invoicesWithNonZeroTax,
            MismatchCount: mismatches.Count,
            Rows: mismatches,
            CsvHint: "Use --json with jq or convert rows to CSV with the consuming agent/tool.",
            ComparisonAdvice: BuildTaxMismatchComparisonAdvice());
    }

    public async Task<InvoiceReconciliationDiagnostic?> DiagnoseInvoiceReconciliationAsync(
        int invoiceId,
        bool includeWriteOffs = true,
        CancellationToken ct = default)
    {
        var invoice = await _api.GetInvoiceAsync(invoiceId, ct);
        if (invoice is null)
            return null;

        var lines = await _api.GetInvoiceProductsAsync(invoiceId, ct);
        var allocatedTimesheets = await _api.GetInvoiceTimesheetsAsync(invoiceId, "allocated", ct);
        List<InvoiceTimesheet> writeOffTimesheets = includeWriteOffs
            ? await _api.GetInvoiceTimesheetsAsync(invoiceId, "writeoff", ct)
            : [];
        var receipts = await _api.GetInvoiceReceiptsAsync(invoiceId, ct);

        var relatedCreditNotes = new List<CreditNoteRow>();
        if (!string.IsNullOrWhiteSpace(invoice.ClientId))
        {
            var creditNotes = await _api.GetCreditNotesByClientAsync(invoice.ClientId, ct);
            relatedCreditNotes = creditNotes
                .Where(c => c.AssociatedInvoiceId == invoiceId)
                .ToList();
        }

        var lineExGst = lines.Sum(LineAmountExGst);
        var lineGst = lines.Sum(LineGst);
        var allocatedExGst = allocatedTimesheets.Sum(TimesheetAmountExGst);
        var allocatedGst = allocatedTimesheets.Sum(TimesheetGst);
        var writeOffExGst = writeOffTimesheets.Sum(TimesheetAmountExGst);
        var receiptRawTotal = receipts.Sum(r => Money(r.PaidTotal ?? r.Paid));
        var receiptAbsTotal = receipts.Sum(r => Abs(r.PaidTotal ?? r.Paid));
        var creditNoteRawTotal = relatedCreditNotes.Sum(c => Money(c.Amount));

        var invoiceSubTotal = Money(invoice.SubTotal);
        var invoiceGst = Money(invoice.SalesTaxAmt);
        var invoiceSellTotal = Money(invoice.SellTotal);
        var invoicePaid = Money(invoice.PaidAmt);
        var invoiceOutstanding = Money(invoice.OSAmt);
        var expectedInvoiceIncGst = Round(invoiceSubTotal + invoiceGst);
        var expectedOutstanding = Round(invoiceSellTotal - invoicePaid);

        var deltas = new Dictionary<string, decimal>
        {
            ["lineExGstVsInvoiceSubTotal"] = Round(lineExGst - invoiceSubTotal),
            ["lineIncGstVsInvoiceSellTotal"] = Round(lineExGst + lineGst - invoiceSellTotal),
            ["invoiceSubTotalPlusGstVsSellTotal"] = Round(expectedInvoiceIncGst - invoiceSellTotal),
            ["allocatedTimesheetsExGstVsInvoiceSubTotal"] = Round(allocatedExGst - invoiceSubTotal),
            ["receiptAbsTotalVsInvoicePaidAmt"] = Round(receiptAbsTotal - invoicePaid),
            ["calculatedOutstandingVsInvoiceOsAmt"] = Round(expectedOutstanding - invoiceOutstanding),
        };

        var warnings = BuildDeltaWarnings(deltas);
        if (relatedCreditNotes.Count > 0)
            warnings.Add("Related credit notes exist; decide whether to net them into the external comparison or report them separately.");
        if (writeOffTimesheets.Count > 0)
            warnings.Add("Written-off timesheets exist; include or exclude them deliberately when reconciling work performed versus billed.");
        if (receiptRawTotal < 0)
            warnings.Add("Receipt raw total is negative by TimePro convention; use absolute values for paid-sales reporting.");

        return new InvoiceReconciliationDiagnostic(
            InvoiceId: invoiceId,
            Invoice: new InvoiceDiagnosticHeader(
                invoice.InvoiceId,
                invoice.ClientId,
                invoice.InvoiceType,
                invoiceSubTotal,
                invoiceGst,
                invoiceSellTotal,
                invoicePaid,
                invoiceOutstanding,
                invoice.ExternalSyncId,
                invoice.ExternalSyncStatus,
                invoice.IsLocked,
                invoice.IsCreditNote),
            Totals: new InvoiceDiagnosticTotals(
                Lines: new CountAmountTotal(lines.Count, Round(lineExGst), Round(lineGst), Round(lineExGst + lineGst)),
                AllocatedTimesheets: new TimesheetAmountTotal(allocatedTimesheets.Count, Round(allocatedTimesheets.Sum(t => t.TotalTime ?? 0)), Round(allocatedExGst), Round(allocatedGst), Round(allocatedExGst + allocatedGst)),
                WriteOffTimesheets: new TimesheetAmountTotal(writeOffTimesheets.Count, Round(writeOffTimesheets.Sum(t => t.TotalTime ?? 0)), Round(writeOffExGst), 0, Round(writeOffExGst)),
                Receipts: new ReceiptDiagnosticTotal(receipts.Count, Round(receiptRawTotal), Round(receiptAbsTotal)),
                RelatedCreditNotes: new ReceiptDiagnosticTotal(relatedCreditNotes.Count, Round(creditNoteRawTotal), Round(Math.Abs(creditNoteRawTotal)))),
            Deltas: deltas,
            Warnings: warnings,
            Evidence: new InvoiceDiagnosticEvidence(lines, allocatedTimesheets, writeOffTimesheets, receipts, relatedCreditNotes),
            ComparisonAdvice:
            [
                "If comparing to Excel/CSV, normalize invoice id, client id, date field, GST convention, and sign convention before matching.",
                "If comparing to Xero or another MCP, fetch the matching invoice/payment by external reference first, then compare inc-GST totals and paid/outstanding amounts.",
                "State whether credit notes and write-offs are included in the conclusion."
            ]);
    }

    public async Task<ClientAccountingPositionDiagnostic> DiagnoseClientAccountingPositionAsync(
        string clientId,
        bool includeRates = false,
        CancellationToken ct = default)
    {
        var invoices = await _api.GetInvoicesByClientAsync(clientId, ct);
        var unpaidInvoices = await _api.GetUnpaidInvoicesByClientAsync(clientId, ct);
        var outstanding = await _api.GetClientOutstandingAsync(clientId, ct);
        var unbilled = await _api.GetUnallocatedTimesheetsByClientAsync(clientId, pageSize: 500, skip: 0, sortField: "DateCreated", direction: "desc", ct);
        var creditNotes = await _api.GetCreditNotesByClientAsync(clientId, ct);
        var rates = includeRates
            ? await _api.ListClientRatesAsync(clientId, empId: null, showExpired: true, pageSize: 500, skip: 0, sortField: "ExpiryDate", direction: "desc", selectAll: false, ct)
            : null;

        var invoiceSellTotal = invoices.Sum(i => Money(i.SellTotal));
        var invoicePaidTotal = invoices.Sum(i => Money(i.PaidAmt));
        var invoiceOsTotal = invoices.Sum(i => Money(i.OSAmt));
        var unpaidTotal = unpaidInvoices.Sum(i => Money(i.OSAmt ?? i.SellTotal));
        var outstandingTotal = outstanding?.OutstandingInvoices?.Sum(i => Money(i.OsAmt ?? i.Total)) ?? 0;
        var unbilledExGst = unbilled.Sum(TimesheetAmountExGst);
        var creditNoteRawTotal = creditNotes.Sum(c => Money(c.Amount));

        var warnings = new List<string>();
        AddWarningIfDelta(warnings, "unpaidInvoicesVsAgedDebtorOutstanding", Round(unpaidTotal - outstandingTotal));
        AddWarningIfDelta(warnings, "invoiceOutstandingVsAgedDebtorOutstanding", Round(invoiceOsTotal - outstandingTotal));
        if (unbilled.Count > 0)
            warnings.Add("Unbilled timesheets exist; invoice totals do not include this pipeline revenue yet.");
        if (creditNotes.Count > 0)
            warnings.Add("Credit notes exist; decide whether the comparison should net them off sales or report separately.");

        return new ClientAccountingPositionDiagnostic(
            ClientId: clientId,
            Totals: new ClientAccountingTotals(
                Invoices: new ClientAccountingAmountTotal(invoices.Count, SellTotalIncGst: Round(invoiceSellTotal), PaidAmt: Round(invoicePaidTotal), OsAmt: Round(invoiceOsTotal)),
                UnpaidInvoices: new CountSingleAmountTotal(unpaidInvoices.Count, Round(unpaidTotal)),
                AgedDebtors: new CountSingleAmountTotal(outstanding?.OutstandingInvoices?.Count ?? 0, Round(outstandingTotal)),
                UnbilledTimesheets: new TimesheetSingleAmountTotal(unbilled.Count, Round(unbilled.Sum(t => t.TotalTime ?? 0)), Round(unbilledExGst)),
                CreditNotes: new ReceiptDiagnosticTotal(creditNotes.Count, Round(creditNoteRawTotal), Round(Math.Abs(creditNoteRawTotal)))),
            Deltas: new ClientAccountingDeltas(
                UnpaidInvoicesVsAgedDebtorOutstanding: Round(unpaidTotal - outstandingTotal),
                InvoiceOutstandingVsAgedDebtorOutstanding: Round(invoiceOsTotal - outstandingTotal)),
            Warnings: warnings,
            Evidence: new ClientAccountingEvidence(invoices, unpaidInvoices, outstanding, unbilled, creditNotes, rates),
            ComparisonAdvice:
            [
                "For Excel/CSV comparisons, agree on whether the external file is invoice-date, payment-date, or service-period based.",
                "For Xero MCP comparisons, match invoices by external sync id when available, then fall back to invoice id/client/date/amount.",
                "Keep unbilled work separate from invoiced sales unless the user asks for pipeline revenue."
            ]);
    }

    private static IReadOnlyList<string> BuildTaxMismatchComparisonAdvice() =>
    [
        "Compare against Xero or another external system by normalizing invoice id/reference, client id, date, tax basis, and amount sign first.",
        "Do not assume another MCP uses TimePro field names or sign conventions; ask it for the closest invoice/payment/tax fields and map deliberately."
    ];

    private static List<string> BuildDeltaWarnings(Dictionary<string, decimal> deltas)
    {
        var warnings = new List<string>();

        foreach (var (name, delta) in deltas)
            AddWarningIfDelta(warnings, name, delta);

        return warnings;
    }

    private static void AddWarningIfDelta(List<string> warnings, string name, decimal delta)
    {
        if (Math.Abs(delta) > 0.01m)
            warnings.Add($"{name} differs by {delta:0.00}.");
    }

    private static decimal LineAmountExGst(InvoiceLine line)
    {
        if (line.SellTotal.HasValue)
            return Money(line.SellTotal);

        if (line.Qty.HasValue && line.SellAmt.HasValue)
            return Round((decimal)line.Qty.Value * line.SellAmt.Value);

        return 0;
    }

    private static decimal LineGst(InvoiceLine line)
    {
        if (line.SalesTaxAmt.HasValue)
            return Money(line.SalesTaxAmt);

        return CalculateTax(LineAmountExGst(line), line.SalesTaxPct);
    }

    private static decimal TimesheetAmountExGst(InvoiceTimesheet timesheet) =>
        Money(timesheet.SellTotal ?? timesheet.BillableAmount ?? timesheet.Amount);

    private static decimal TimesheetGst(InvoiceTimesheet timesheet)
    {
        if (timesheet.SalesTaxAmt.HasValue)
            return Money(timesheet.SalesTaxAmt);

        return CalculateTax(TimesheetAmountExGst(timesheet), timesheet.SalesTaxPct);
    }

    private static decimal CalculateTax(decimal exGst, double? rawRate)
    {
        var rate = NormalizeTaxRate(rawRate);
        return rate.HasValue ? Round(exGst * rate.Value) : 0;
    }

    private static decimal? NormalizeTaxRate(double? rawRate)
    {
        if (!rawRate.HasValue || rawRate.Value < 0)
            return null;

        var rate = (decimal)rawRate.Value;
        if (rate > 1)
            rate /= 100;

        return rate;
    }

    private static bool HasNonZeroInvoiceTax(InvoiceHeader invoice)
    {
        if (Math.Abs(Money(invoice.SalesTaxAmt)) > 0.01m)
            return true;

        var rate = NormalizeTaxRate(invoice.SalesTaxPct);
        return rate.HasValue && Math.Abs(rate.Value) > 0.0001m;
    }

    private static bool HasZeroTimesheetTax(InvoiceTimesheet timesheet)
    {
        if (timesheet.SalesTaxPct.HasValue)
        {
            var rate = NormalizeTaxRate(timesheet.SalesTaxPct);
            if (rate.HasValue && Math.Abs(rate.Value) <= 0.0001m)
                return true;
        }

        return timesheet.SalesTaxAmt.HasValue && Math.Abs(Money(timesheet.SalesTaxAmt)) <= 0.01m;
    }

    private static decimal? TaxPercent(double? rawRate)
    {
        var rate = NormalizeTaxRate(rawRate);
        return rate.HasValue ? Round(rate.Value * 100) : null;
    }

    private static decimal Money(decimal? value) =>
        Round(value ?? 0);

    private static decimal Abs(decimal? value) =>
        Math.Abs(Money(value));

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record AccountingDiagnosticsGuide(
    string? UseCase,
    IReadOnlyList<string> AskUser,
    IReadOnlyList<string> RecommendedCommands,
    IReadOnlyList<string> RecommendedMcpTools,
    string Note);

public sealed record TimesheetTaxMismatchReport(
    string? Query,
    int Skip,
    int Limit,
    int ScannedInvoiceCount,
    int TotalInvoicesAvailable,
    int InvoicesWithNonZeroTax,
    int MismatchCount,
    IReadOnlyList<TimesheetTaxMismatchRow> Rows,
    string CsvHint,
    IReadOnlyList<string> ComparisonAdvice);

public sealed record TimesheetTaxMismatchRow(
    int InvoiceId,
    string? ClientId,
    string? InvoiceDate,
    decimal? InvoiceTaxPct,
    decimal? InvoiceSalesTaxAmt,
    int TimeId,
    string? EmpId,
    string? EmpName,
    string? ProjectId,
    string? ProjectName,
    string? CategoryId,
    string? BillableId,
    string? TimesheetDate,
    decimal AmountExGst,
    decimal? TimesheetTaxPct,
    decimal? TimesheetSalesTaxAmt,
    string? Notes);

public sealed record InvoiceReconciliationDiagnostic(
    int InvoiceId,
    InvoiceDiagnosticHeader Invoice,
    InvoiceDiagnosticTotals Totals,
    IReadOnlyDictionary<string, decimal> Deltas,
    IReadOnlyList<string> Warnings,
    InvoiceDiagnosticEvidence Evidence,
    IReadOnlyList<string> ComparisonAdvice);

public sealed record InvoiceDiagnosticHeader(
    int InvoiceId,
    string? ClientId,
    string? InvoiceType,
    decimal SubTotalExGst,
    decimal Gst,
    decimal SellTotalIncGst,
    decimal PaidAmt,
    decimal OsAmt,
    string? ExternalSyncId,
    int ExternalSyncStatus,
    bool IsLocked,
    bool IsCreditNote);

public sealed record InvoiceDiagnosticTotals(
    CountAmountTotal Lines,
    TimesheetAmountTotal AllocatedTimesheets,
    TimesheetAmountTotal WriteOffTimesheets,
    ReceiptDiagnosticTotal Receipts,
    ReceiptDiagnosticTotal RelatedCreditNotes);

public sealed record CountAmountTotal(int Count, decimal ExGst, decimal Gst, decimal IncGst);

public sealed record TimesheetAmountTotal(int Count, decimal Hours, decimal ExGst, decimal Gst, decimal IncGst);

public sealed record ReceiptDiagnosticTotal(int Count, decimal RawTotal, decimal AbsoluteTotal);

public sealed record InvoiceDiagnosticEvidence(
    IReadOnlyList<InvoiceLine> Lines,
    IReadOnlyList<InvoiceTimesheet> AllocatedTimesheets,
    IReadOnlyList<InvoiceTimesheet> WriteOffTimesheets,
    IReadOnlyList<ReceiptRow> Receipts,
    IReadOnlyList<CreditNoteRow> RelatedCreditNotes);

public sealed record ClientAccountingPositionDiagnostic(
    string ClientId,
    ClientAccountingTotals Totals,
    ClientAccountingDeltas Deltas,
    IReadOnlyList<string> Warnings,
    ClientAccountingEvidence Evidence,
    IReadOnlyList<string> ComparisonAdvice);

public sealed record ClientAccountingTotals(
    ClientAccountingAmountTotal Invoices,
    CountSingleAmountTotal UnpaidInvoices,
    CountSingleAmountTotal AgedDebtors,
    TimesheetSingleAmountTotal UnbilledTimesheets,
    ReceiptDiagnosticTotal CreditNotes);

public sealed record ClientAccountingAmountTotal(int Count, decimal SellTotalIncGst, decimal PaidAmt, decimal OsAmt);

public sealed record CountSingleAmountTotal(int Count, decimal Total);

public sealed record TimesheetSingleAmountTotal(int Count, decimal Hours, decimal ExGst);

public sealed record ClientAccountingDeltas(
    decimal UnpaidInvoicesVsAgedDebtorOutstanding,
    decimal InvoiceOutstandingVsAgedDebtorOutstanding);

public sealed record ClientAccountingEvidence(
    IReadOnlyList<InvoiceHeader> Invoices,
    IReadOnlyList<InvoiceHeader> UnpaidInvoices,
    ClientOutstandingSummary? Outstanding,
    IReadOnlyList<InvoiceTimesheet> Unbilled,
    IReadOnlyList<CreditNoteRow> CreditNotes,
    ClientRateTable? Rates);
