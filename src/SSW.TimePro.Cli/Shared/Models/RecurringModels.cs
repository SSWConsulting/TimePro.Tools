namespace SSW.TimePro.Cli.Shared.Models;

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
    public string? Unit { get; set; }
    public string? Note { get; set; }
    public string? NoteInternal { get; set; }
    public DateTime? LastInvEndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>
/// Single recurring invoice template from /api/recurring/invoices/{id}.
/// Shape varies across backend versions; keeping it loose.
/// </summary>
public class RecurringInvoiceDetail
{
    public int Id { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? Unit { get; set; }
    public int? Interval { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTime? NextInvoiceDate { get; set; }
    public DateTime? LastInvEndDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? SellTotal { get; set; }
    public decimal? TaxRate { get; set; }
    public string? Note { get; set; }
    public string? NoteInternal { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
    public List<RecurringInvoiceProductRow>? Products { get; set; }
}

/// <summary>
/// Recurring product row from /api/recurring/GetProducts/{clientId}.
/// </summary>
public class RecurringInvoiceProductRow
{
    public int? Id { get; set; }
    public int? RecurringInvoiceId { get; set; }
    public string? SkuId { get; set; }
    public string? SkuName { get; set; }
    public string? ProductName { get; set; }
    public double? Qty { get; set; }
    public decimal? SellAmt { get; set; }
    public decimal? SellTotal { get; set; }
    public string? Note { get; set; }
}
