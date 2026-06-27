using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Skills;

/// <summary>
/// Builds <see cref="SkillContentModel"/>s from config/detection.
/// The body prose comes from packaged Markdown templates via <see cref="SkillBodyBuilder"/>;
/// <see cref="SkillRenderer"/> serializes the model to the unified agent skill format.
/// </summary>
public static class SkillModelBuilder
{
    public const string TimesheetsName = "timepro-timesheets";
    public const string AccountingName = "timepro-accounting-cli";
    public const string TenantSetupName = "timepro-tenant-setup";
    public const string DeveloperDiagnosticsName = "timepro-dev-diagnostics";
    public const string DeveloperTimesheetDiagnosticsName = "timepro-dev-timesheet-diagnostics";
    public const string DeveloperFinanceDiagnosticsName = "timepro-dev-finance-diagnostics";
    public const string EnvironmentCompareName = "timepro-env-compare";

    private const string TimesheetsDescription =
        "Manage SSW TimePro timesheets with the tp CLI — view/accept/create entries, repo mappings, bookings, leave, and daily scrum. Use when entering, fixing, or reviewing timesheets.";

    private const string AccountingDescription =
        "Explore SSW TimePro financial data via the tp CLI (read-only) — invoices with line items, billed timesheets, credit notes, receipts, sale products, client rates, aged debtors, unbilled time, recurring invoices, prepaid drawdowns and client billable-work threshold reports. Use for accountant-style questions. For raw HTTP/curl access (when tp isn't installed), use the timepro-accounting skill instead.";

    private const string TenantSetupDescription =
        "Set up and switch TimePro tenant profiles with the tp CLI, including switching the active session to ssw-staging and using process-local --tenant/--env overrides without changing the active tenant.";

    private const string DeveloperDiagnosticsDescription =
        "Developer workflow for reproducing, diagnosing, and verifying TimePro bugs across local, staging, and production with the tp CLI first, environment-aware safety rules, App Insights follow-up, and explicit permission before non-read-only production actions.";

    private const string DeveloperTimesheetDiagnosticsDescription =
        "Developer workflow for diagnosing TimePro suggested-timesheet, CRM booking, and saved-timesheet bugs with CLI-first evidence, App Insights follow-up, and production read-only safety boundaries.";

    private const string DeveloperFinanceDiagnosticsDescription =
        "Developer workflow for diagnosing TimePro invoice, credit note, client rate, prepaid, tax, and external-sync bugs with CLI-first evidence, accounting MCP pointers, and production read-only safety boundaries.";

    private const string EnvironmentCompareDescription =
        "Compare TimePro tenant and environment behaviour for consistency across local, staging, and production using read-only tp CLI evidence, normalized JSON diffs, and clear production safety boundaries.";

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

    public static SkillContentModel BuildTenantSetup() =>
        new(
            Name: TenantSetupName,
            Description: TenantSetupDescription,
            AllowedTools: ["Bash(tp *)"],
            Prefetch: SkillBodyBuilder.TenantSetupPrefetch,
            Body: SkillBodyBuilder.BuildTenantSetupBody());

    public static SkillContentModel BuildDeveloperDiagnostics() =>
        new(
            Name: DeveloperDiagnosticsName,
            Description: DeveloperDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperDiagnosticsBody());

    public static SkillContentModel BuildDeveloperTimesheetDiagnostics() =>
        new(
            Name: DeveloperTimesheetDiagnosticsName,
            Description: DeveloperTimesheetDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperTimesheetDiagnosticsBody());

    public static SkillContentModel BuildDeveloperFinanceDiagnostics() =>
        new(
            Name: DeveloperFinanceDiagnosticsName,
            Description: DeveloperFinanceDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperFinanceDiagnosticsBody());

    public static SkillContentModel BuildEnvironmentCompare() =>
        new(
            Name: EnvironmentCompareName,
            Description: EnvironmentCompareDescription,
            AllowedTools: ["Bash(tp *)", "Bash(jq *)", "Bash(diff *)"],
            Prefetch: SkillBodyBuilder.EnvironmentComparePrefetch,
            Body: SkillBodyBuilder.BuildEnvironmentCompareBody());
}
