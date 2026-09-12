namespace SSW.TimePro.Cli.Features.Leave;

/// <summary>
/// Leave status codes that no longer accept writes. Mirrors the server's LeaveStatus enum.
/// </summary>
public static class LeaveStatusRules
{
    public const int Declined = 5;
    public const int Cancelled = 7;
    public const int PendingCancellation = 8;

    public static bool IsTerminal(int status) =>
        status is Declined or Cancelled or PendingCancellation;
}
