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
    public const int CurrentSkillVersion = 6;

    private const string TimesheetsDescription =
        "Use when entering, fixing, accepting or reviewing TimePro timesheets, repo-to-project mappings, or daily scrum notes with the tp CLI.";

    private const string AccountingDescription =
        "Use for accountant questions about TimePro invoices, receipts, rates, prepaid balances or reconciliation, and for Xero leave-balance imports. Read-only apart from the import. Without tp installed, use timepro-accounting instead.";

    private const string TenantSetupDescription =
        "Use when logging in to TimePro, switching the active tenant, or running one command against another tenant or environment with --tenant/--env.";

    private const string DeveloperDiagnosticsDescription =
        "Use when reproducing or diagnosing a TimePro bug across local, staging and production, starting with tp CLI evidence before App Insights.";

    private const string DeveloperTimesheetDiagnosticsDescription =
        "Use when diagnosing a TimePro suggested-timesheet, CRM booking, or saved-timesheet bug.";

    private const string DeveloperFinanceDiagnosticsDescription =
        "Use when diagnosing a TimePro invoice, credit note, client rate, prepaid, tax, or external-sync bug.";

    private const string EnvironmentCompareDescription =
        "Use when checking whether TimePro local, staging and production return the same data or behaviour for the same read-only command.";

    public static IReadOnlyList<SkillDefinition> Catalog { get; } =
    [
        new(TimesheetsName, CurrentSkillVersion, null),
        new(TenantSetupName, CurrentSkillVersion, null),
        new(AccountingName, CurrentSkillVersion, FeatureCatalog.Accounting),
        new(DeveloperDiagnosticsName, CurrentSkillVersion, FeatureCatalog.Developer),
        new(DeveloperTimesheetDiagnosticsName, CurrentSkillVersion, FeatureCatalog.Developer),
        new(DeveloperFinanceDiagnosticsName, CurrentSkillVersion, FeatureCatalog.Developer),
        new(EnvironmentCompareName, CurrentSkillVersion, FeatureCatalog.Developer),
    ];

    public static SkillDefinition? FindDefinition(string name) =>
        Catalog.FirstOrDefault(skill => skill.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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
            Version: CurrentSkillVersion,
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
            Version: CurrentSkillVersion,
            Description: AccountingDescription,
            AllowedTools: ["Bash(tp *)"],
            Prefetch: [],
            Body: SkillBodyBuilder.BuildAccountingBody(tenant));

    public static SkillContentModel BuildTenantSetup() =>
        new(
            Name: TenantSetupName,
            Version: CurrentSkillVersion,
            Description: TenantSetupDescription,
            AllowedTools: ["Bash(tp *)"],
            Prefetch: SkillBodyBuilder.TenantSetupPrefetch,
            Body: SkillBodyBuilder.BuildTenantSetupBody());

    public static SkillContentModel BuildDeveloperDiagnostics() =>
        new(
            Name: DeveloperDiagnosticsName,
            Version: CurrentSkillVersion,
            Description: DeveloperDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperDiagnosticsBody());

    public static SkillContentModel BuildDeveloperTimesheetDiagnostics() =>
        new(
            Name: DeveloperTimesheetDiagnosticsName,
            Version: CurrentSkillVersion,
            Description: DeveloperTimesheetDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperTimesheetDiagnosticsBody());

    public static SkillContentModel BuildDeveloperFinanceDiagnostics() =>
        new(
            Name: DeveloperFinanceDiagnosticsName,
            Version: CurrentSkillVersion,
            Description: DeveloperFinanceDiagnosticsDescription,
            AllowedTools: ["Bash(tp *)", "Bash(az monitor app-insights query *)", "Bash(jq *)"],
            Prefetch: SkillBodyBuilder.DeveloperDiagnosticsPrefetch,
            Body: SkillBodyBuilder.BuildDeveloperFinanceDiagnosticsBody());

    public static SkillContentModel BuildEnvironmentCompare() =>
        new(
            Name: EnvironmentCompareName,
            Version: CurrentSkillVersion,
            Description: EnvironmentCompareDescription,
            AllowedTools: ["Bash(tp *)", "Bash(jq *)", "Bash(diff *)"],
            Prefetch: SkillBodyBuilder.EnvironmentComparePrefetch,
            Body: SkillBodyBuilder.BuildEnvironmentCompareBody());
}
