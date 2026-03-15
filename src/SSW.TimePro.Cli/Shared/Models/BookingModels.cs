namespace SSW.TimePro.Cli.Shared.Models;

public class AppointmentItem
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool AllDay { get; set; }
    public string? ClientId { get; set; }
    public string? ProjectId { get; set; }
    public int? IterationId { get; set; }
    public bool Editable { get; set; }
    public int? TimeZoneOffsetInMinutes { get; set; }
}
