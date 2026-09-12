using System.Text.Json.Serialization;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Rates;

/// <summary>
/// The <c>rate get --json</c> shape. Every key is written even when null so a hit and a miss
/// parse identically; the global serializer omits nulls, hence the per-property overrides.
/// </summary>
public sealed record RateLookupResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required bool Found { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string ClientId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string Date { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EmpId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EmployeeName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ClientName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? Rate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? PrepaidRate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? ClientRateId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ExpiryDate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Notes { get; init; }

    public static RateLookupResult From(string clientId, DateOnly date, ClientRateResponse? rate) => new()
    {
        Found = rate is not null,
        ClientId = clientId,
        Date = date.ToString("yyyy-MM-dd"),
        EmpId = rate?.EmpId,
        EmployeeName = rate?.EmployeeName,
        ClientName = rate?.ClientName,
        Rate = rate?.Rate,
        PrepaidRate = rate?.PrepaidRate,
        ClientRateId = rate?.ClientRateId,
        ExpiryDate = rate?.ExpiryDate,
        Notes = rate?.Notes
    };
}
