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

/// <summary>
/// Body for POST /api/Timesheets/SaveClientRate. A null <see cref="ClientRateId"/> creates a new
/// rate; a set one updates it. The API defaults a null <see cref="ExpiryDate"/> to 12 months out.
/// </summary>
public class SaveClientRateModel
{
    public int? ClientRateId { get; set; }
    public string EmpId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public decimal? PrepaidRate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Response from GET /api/Timesheets/InitializeClientRate — the building blocks the Angular
/// rate dialog uses: the employee's default rate plus the latest client-specific rate (returned
/// regardless of expiry). Used to recommend a rate when no active one exists.
/// </summary>
public class ClientRateInit
{
    // Nullable: the API returns null for these when no previous/default rate exists.
    public decimal? DefaultRate { get; set; }
    public decimal? DefaultPrepaidRate { get; set; }
    public decimal? PreviousRate { get; set; }
    public decimal? PreviousPrepaidRate { get; set; }
    public string? EmpId { get; set; }
    public string? Employee { get; set; }
    public string? ClientId { get; set; }
    public string? Client { get; set; }
}
