using System.Text;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Skills;

/// <summary>
/// Builds generated skill bodies from packaged Markdown templates.
/// Keep long prose in Features/Skills/Templates/*.md.
/// </summary>
public static class SkillBodyBuilder
{
    public static IReadOnlyList<PrefetchCommand> TimesheetsPrefetch { get; } =
    [
        new("tp info --json", "CLI health, active tenant, user, and update status"),
        new("tp project recent --json", "ranked likely projects + repo paths (start here)"),
        new("tp ts get --week --json", "current week's entries + suggestions"),
        new("tp bk list --week --json", "CRM bookings for the week"),
        new("tp loc info --json", "location defaults / WFH days"),
    ];

    public static IReadOnlyList<PrefetchCommand> TenantSetupPrefetch { get; } =
    [
        new("tp tenant list", "configured tenant profiles; no secret values"),
        new("tp tenant info", "active tenant profile before changing anything"),
    ];

    public static IReadOnlyList<PrefetchCommand> DeveloperDiagnosticsPrefetch { get; } =
    [
        new("tp tenant list", "configured tenant profiles; no secret values"),
        new("tp tenant info --json", "active tenant/environment before reproducing"),
        new("tp user me --json", "current tenant-profile employee"),
    ];

    public static IReadOnlyList<PrefetchCommand> EnvironmentComparePrefetch { get; } =
    [
        new("tp tenant list", "configured tenant profiles; no secret values"),
        new("tp tenant info --json", "active tenant/environment context"),
    ];

    public static string BuildTimesheetsBody(
        TenantConfig? tenant,
        GlobalConfig global,
        RepoMappingEntry? repoMapping,
        string? ghRepoSlug) =>
        SkillTemplateRenderer.Render(
            "timepro-timesheets.md",
            new Dictionary<string, string>
            {
                ["GITHUB_CONTEXT_COMMANDS"] = BuildGitHubContextCommands(ghRepoSlug),
                ["PROJECT_CONTEXT"] = BuildProjectContext(repoMapping, ghRepoSlug),
                ["CURRENT_CONFIGURATION"] = BuildTimesheetsCurrentConfiguration(tenant),
                ["LOCATION_DEFAULTS"] = BuildLocationDefaults(global),
            });

    public static string BuildAccountingBody(TenantConfig? tenant) =>
        SkillTemplateRenderer.Render(
            "timepro-accounting-cli.md",
            new Dictionary<string, string>
            {
                ["CURRENT_CONFIGURATION"] = BuildAccountingCurrentConfiguration(tenant),
            });

    public static string BuildTenantSetupBody() =>
        SkillTemplateRenderer.Render("timepro-tenant-setup.md");

    public static string BuildDeveloperDiagnosticsBody() =>
        SkillTemplateRenderer.Render("timepro-dev-diagnostics.md");

    public static string BuildDeveloperTimesheetDiagnosticsBody() =>
        SkillTemplateRenderer.Render("timepro-dev-timesheet-diagnostics.md");

    public static string BuildDeveloperFinanceDiagnosticsBody() =>
        SkillTemplateRenderer.Render("timepro-dev-finance-diagnostics.md");

    public static string BuildEnvironmentCompareBody() =>
        SkillTemplateRenderer.Render("timepro-env-compare.md");

    private static string BuildGitHubContextCommands(string? ghRepoSlug)
    {
        var sb = new StringBuilder();

        if (ghRepoSlug is not null)
        {
            sb.AppendLine("# Open issues assigned to me");
            sb.AppendLine($"gh issue list --repo {ghRepoSlug} --assignee @me --state open --json number,title");
            sb.AppendLine();
            sb.AppendLine("# My recently merged PRs (check dates to match to days)");
            sb.AppendLine($"gh pr list --repo {ghRepoSlug} --author @me --state merged --limit 20 --json number,title,mergedAt");
            sb.AppendLine();
            sb.AppendLine("# My open PRs (in-progress work)");
            sb.AppendLine($"gh pr list --repo {ghRepoSlug} --author @me --state open --json number,title,headRefName");
        }
        else
        {
            sb.AppendLine("# Open issues assigned to me (run from repo root)");
            sb.AppendLine("gh issue list --assignee @me --state open --json number,title");
            sb.AppendLine();
            sb.AppendLine("# My recently merged PRs");
            sb.AppendLine("gh pr list --author @me --state merged --limit 20 --json number,title,mergedAt");
            sb.AppendLine();
            sb.AppendLine("# My open PRs (in-progress work)");
            sb.AppendLine("gh pr list --author @me --state open --json number,title,headRefName");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildProjectContext(RepoMappingEntry? repoMapping, string? ghRepoSlug)
    {
        if (repoMapping is null && ghRepoSlug is null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Project Context");

        if (repoMapping is not null)
        {
            sb.AppendLine($"- Client: `{repoMapping.ClientId}`");
            sb.AppendLine($"- Project: `{repoMapping.ProjectId}`");
            if (!string.IsNullOrEmpty(repoMapping.ProjectName))
                sb.AppendLine($"- Project Name: {repoMapping.ProjectName}");
            if (!string.IsNullOrEmpty(repoMapping.CategoryId))
                sb.AppendLine($"- Category: `{repoMapping.CategoryId}`");
        }

        if (ghRepoSlug is not null)
            sb.AppendLine($"- GitHub: `{ghRepoSlug}`");

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string BuildTimesheetsCurrentConfiguration(TenantConfig? tenant)
    {
        if (tenant is null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Current Configuration");
        sb.AppendLine($"- Tenant: `{tenant.TenantId}`");
        sb.AppendLine($"- Employee: `{tenant.EmployeeId}`");
        sb.AppendLine($"- API: `{tenant.ApiUrl}`");
        sb.AppendLine("- Repo mappings: `~/.config/timepro-cli/repo-mappings.json`");

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string BuildAccountingCurrentConfiguration(TenantConfig? tenant)
    {
        if (tenant is null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Current Configuration");
        sb.AppendLine($"- Tenant: `{tenant.TenantId}`");
        sb.AppendLine($"- API: `{tenant.ApiUrl}`");

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string BuildLocationDefaults(GlobalConfig global)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Location Defaults");
        sb.AppendLine("Valid location IDs: `SSW` (At My Company), `Home` (At Home), `Client` (At Client), `Travel`, `Other`");
        sb.AppendLine();
        sb.AppendLine("Common aliases are resolved automatically: Office -> SSW, WFH -> Home, Onsite -> Client");
        sb.AppendLine();

        if (global.WfhDays.Count > 0)
        {
            sb.AppendLine($"- WFH days: {string.Join(", ", global.WfhDays)}");
            sb.AppendLine($"- Default: {global.DefaultLocation}");
            sb.AppendLine("- Location is auto-applied when creating timesheets based on the day");
        }
        else
        {
            sb.AppendLine($"- Default: {global.DefaultLocation}");
            sb.AppendLine("- No WFH days configured. Use `tp loc set Home --day Mon,Tue` to set them.");
        }

        return sb.ToString().TrimEnd() + "\n";
    }
}
