namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Credit note row from /api/creditnote/by-client/{clientId}.
/// </summary>
public class CreditNoteRow
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreditNoteDate { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsLocked { get; set; }
    public decimal? Paid { get; set; }
    public int SyncStatus { get; set; }
    public string? SyncDisplayName { get; set; }
    public bool IsCreditingInvoice { get; set; }
    public int? AssociatedInvoiceId { get; set; }
}

/// <summary>
/// Response from /api/creditnote/by-client/{clientId}/count.
/// </summary>
public class CreditNoteCount
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
}
