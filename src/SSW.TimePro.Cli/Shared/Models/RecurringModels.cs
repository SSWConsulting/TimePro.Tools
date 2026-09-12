namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Recurrence cadence. The API serialises this as the ordinal, so the values are fixed by the backend.
/// </summary>
public enum RecurrenceUnit
{
    Year = 0,
    Month = 1,
    Day = 2
}

/// <summary>
/// Row from /api/recurring/invoices/ (recurring invoice list).
/// </summary>
public class RecurringInvoiceRow
{
    public int? Id { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public decimal? SellTotal { get; set; }
    public int CountOfInv { get; set; }
    public RecurrenceUnit Unit { get; set; }
    public string? Note { get; set; }
    public string? NoteInternal { get; set; }
    public DateTime? LastInvEndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>
/// Single recurring invoice template from /api/recurring/invoices/{id}.
/// </summary>
public class RecurringInvoiceDetail
{
    public int? Id { get; set; }
    public string? ClientId { get; set; }
    public decimal? SellAmt { get; set; }
    public decimal? SellTaxAmt { get; set; }
    public decimal? SellTotal { get; set; }
    public int CountOfInv { get; set; }
    public int FirstInvPeriod { get; set; }
    public int SubsequentInvPeriod { get; set; }
    public int LastInvPeriod { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public DateTime? LastInvEndDate { get; set; }
    public DateTime? NextInvoicePeriodStart { get; set; }
    public DateTime? NextInvoicePeriodEnd { get; set; }
    public bool CanGenerateNow { get; set; }
    public string? CannotGenerateReason { get; set; }
    public RecurrenceUnit Unit { get; set; }
    public string? Note { get; set; }
    public string? NoteInternal { get; set; }
    public string? ModifiedBy { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public List<RecurringInvoiceProductRow>? Products { get; set; }
}

/// <summary>
/// Product line on a recurring invoice template.
/// </summary>
public class RecurringInvoiceProductRow
{
    public int? Id { get; set; }
    public int? RecurringInvoiceId { get; set; }
    public string? ProdId { get; set; }
    public string? ProdName { get; set; }
    public string? ProdCatgoryId { get; set; }
    public string? ProdCategoryName { get; set; }
    public decimal? SellAmt { get; set; }
    public decimal? SellTotal { get; set; }
    public double? SalesTaxPct { get; set; }
    public decimal? SalesTaxAmt { get; set; }
    public decimal? CostTotal { get; set; }
    public string? NoteInternal { get; set; }
    public string? Note { get; set; }
    public double? Qty { get; set; }
    public string? ModifiedBy { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
