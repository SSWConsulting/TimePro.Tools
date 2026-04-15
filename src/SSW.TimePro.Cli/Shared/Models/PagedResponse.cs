namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Generic paged API response: { total, data[] }.
/// Used by /api/ClientInvoice/rangepaged, /api/receipting/PaidReceiptsPaged, and similar.
/// </summary>
public class PagedResponse<T>
{
    public int Total { get; set; }
    public List<T> Data { get; set; } = [];
}
