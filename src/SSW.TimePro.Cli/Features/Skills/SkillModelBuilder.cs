using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Skills;

/// <summary>
/// Builds <see cref="SkillContentModel"/>s from config/detection.
/// The body prose comes from <see cref="SkillBodyBuilder"/>; <see cref="SkillRenderer"/>
/// serializes the model to the unified agent skill format.
/// </summary>
public static class SkillModelBuilder
{
    public const string TimesheetsName = "timepro-timesheets";
    public const string AccountingName = "timepro-accounting-cli";

    private const string TimesheetsDescription =
        "Manage SSW TimePro timesheets with the tp CLI — view/accept/create entries, repo mappings, bookings, leave, and daily scrum. Use when entering, fixing, or reviewing timesheets.";

    private const string AccountingDescription =
        "Explore SSW TimePro financial data via the tp CLI (read-only) — invoices with line items, billed timesheets, credit notes, receipts, sale products, client rates, aged debtors, unbilled time, recurring invoices, prepaid drawdowns and client billable-work threshold reports. Use for accountant-style questions. For raw HTTP/curl access (when tp isn't installed), use the timepro-accounting skill instead.";

    /// <summary>
    /// Builds the timesheets skill model. Deterministic read-only commands are
    /// rendered as a portable "run these first" block by <see cref="SkillRenderer"/>.
    /// </summary>
    public static SkillContentModel BuildTimesheets(
        TenantConfig? tenant,
        GlobalConfig global,
        RepoMappingEntry? repoMapping,
        string? ghRepoSlug)
    {
        var body = SkillBodyBuilder.BuildTimesheetsBody(
            tenant, global, repoMapping, ghRepoSlug);

        return new SkillContentModel(
            Name: TimesheetsName,
            Description: TimesheetsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(sl *)"],
            Prefetch: SkillBodyBuilder.TimesheetsPrefetch,
            Body: body);
    }

    /// <summary>
    /// Builds the accounting skill model. The accounting flows are exploratory,
    /// not deterministic, so no prefetch commands are emitted.
    /// </summary>
    public static SkillContentModel BuildAccounting(TenantConfig? tenant) =>
        new(
            Name: AccountingName,
            Description: AccountingDescription,
            AllowedTools: ["Bash(tp *)"],
            Prefetch: [],
            Body: SkillBodyBuilder.BuildAccountingBody(tenant));
}
