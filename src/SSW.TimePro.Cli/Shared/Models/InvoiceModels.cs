namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Row returned by /api/ClientInvoice/rangepaged (invoice search table).
/// </summary>
public class InvoiceSearchRow
{
    public int InvoiceId { get; set; }
    public DateTime DateCreated { get; set; }
    public string? InvoiceType { get; set; }
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public decimal SellTotal { get; set; }
    public decimal PaidAmt { get; set; }
    public string? ExternalSyncId { get; set; }
    public int ExternalSyncStatus { get; set; }
    public DateTime? ExternalSyncTime { get; set; }
    public string? ExternalSyncType { get; set; }
    public DateTime? LastGeneratedPdfDate { get; set; }
}

/// <summary>
/// Header for a single invoice (GET /api/ClientInvoices/{id} or /api/v2/ClientInvoice/{id}).
/// Mirrors ClientInvoiceDto in the TimePRO backend.
/// </summary>
public class InvoiceHeader
{
    public int InvoiceId { get; set; }
    public string? InvoiceWithCnId { get; set; }
    public string? CategoryId { get; set; }
    public string? CurrencyId { get; set; }
    public double? ExchangeRate { get; set; }
    public string? InvoiceType { get; set; }
    public int Batch { get; set; }
    public string? ClientId { get; set; }
    public string? ProjectId { get; set; }
    public string? ClientRef { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public decimal? SubTotal { get; set; }
    public decimal? SellTotal { get; set; }
    public double? SalesTaxPct { get; set; }
    public decimal? SalesTaxAmt { get; set; }
    public decimal? CostTotal { get; set; }
    public decimal? Margin { get; set; }
    public double? MarginPct { get; set; }
    public decimal? SumOfWrittenOff { get; set; }
    public decimal? TotalMargin { get; set; }
    public double? TotalMarginPct { get; set; }
    public decimal? PaidAmt { get; set; }
    public decimal? OSAmt { get; set; }
    public string? ExportId { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? EmpUpdated { get; set; }
    public string? EmpUpdatedAccountName { get; set; }
    public string? Note { get; set; }
    public string? Month { get; set; }
    public short? CollectionDays { get; set; }
    public string? NoteInternal { get; set; }
    public DateTime? DatePromisedToPay { get; set; }
    public bool IsRecurring { get; set; }
    public bool IsLocked { get; set; }
    public string? ExternalSyncType { get; set; }
    public string? ExternalSyncId { get; set; }
    public int ExternalSyncStatus { get; set; }
    public int? OutstandingDays { get; set; }
    public int PaymentTerms { get; set; }
    public bool IsCreditNote { get; set; }
    public DateTime? CreditNoteDate { get; set; }
}

/// <summary>
/// Invoice line item — row from /api/ClientInvoiceProduct/invoiceID/{id}
/// or /api/v2/ClientInvoice/{id}/products.
/// </summary>
public class InvoiceLine
{
    public int InvoiceProdId { get; set; }
    public int InvoiceId { get; set; }
    public string? SkuId { get; set; }
    public string? SkuName { get; set; }
    public string? ProdName { get; set; }
    public string? CategoryId { get; set; }
    public string? AccountId { get; set; }
    public string? EmpId { get; set; }
    public double? Qty { get; set; }
    public decimal? SellAmt { get; set; }
    public decimal? SellTotal { get; set; }
    public decimal? CostAmt { get; set; }
    public decimal? CostTotal { get; set; }
    public decimal? Margin { get; set; }
    public double? MarginPct { get; set; }
    public decimal? RrpAmt { get; set; }
    public decimal? RrpTotal { get; set; }
    public decimal? SalesTaxAmt { get; set; }
    public double? SalesTaxPct { get; set; }
    public decimal? DiscountPct { get; set; }
    public string? Note { get; set; }
    public string? NoteInternal { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public string? Type { get; set; }
}

/// <summary>
/// Timesheet billed (or written-off) on an invoice.
/// Returned by /api/v2/Timesheets/WithNames/{Allocated|WriteOff|Unallocated}.
/// </summary>
public class InvoiceTimesheet
{
    public int TimeId { get; set; }
    public string? EmpId { get; set; }
    public string? EmpName { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? CategoryId { get; set; }
    public string? BillableId { get; set; }
    public DateTime? DateCreated { get; set; }
    public string? TimeStart { get; set; }
    public string? TimeEnd { get; set; }
    public decimal? TotalTime { get; set; }
    public decimal? Amount { get; set; }
    public decimal? BillableAmount { get; set; }
    public decimal? SellPrice { get; set; }
    public string? Notes { get; set; }
    public int? InvoiceId { get; set; }
}
