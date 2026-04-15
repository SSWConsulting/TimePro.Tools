namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Row in /api/clients/GetClientRates — one configured rate for an employee+client pairing.
/// This is separate from <c>ClientRateResponse</c> (which is a point-in-time lookup for the
/// current employee used by <c>tp rate get</c>).
/// </summary>
public class ClientRateRow
{
    public int? ClientRateId { get; set; }
    public string? EmpId { get; set; }
    public string? EmployeeName { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public decimal? Rate { get; set; }
    public decimal? PrepaidRate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Response envelope from /api/clients/GetClientRates — paged with typed rows.
/// </summary>
public class ClientRateTable
{
    public List<ClientRateRow> Rates { get; set; } = [];
    public int Total { get; set; }
}
