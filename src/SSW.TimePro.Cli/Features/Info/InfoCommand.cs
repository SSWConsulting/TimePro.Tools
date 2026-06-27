using System.ComponentModel;
using SSW.TimePro.Cli.Features.Updates;
using SSW.TimePro.Cli.Infrastructure;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Info;

[Description("Show CLI version, tenant, user, config, and update status")]
public class InfoCommand : AsyncCommand<InfoCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }

        [CommandOption("--no-update-check")]
        [Description("Skip the GitHub release check")]
        public bool NoUpdateCheck { get; set; }
    }

    public InfoCommand(IConfigService config)
    {
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();
        var activeTenant = _config.LoadActiveTenantConfig();
        var tenants = _config.ListTenants();
        var repoMappings = _config.LoadRepoMappings();

        UpdateCheckResult? updateCheck = null;
        if (!settings.NoUpdateCheck)
        {
            updateCheck = await UpdateCheckService.CheckLatestReleaseAsync(cancellationToken);
            if (AppMetadataCommandLine.RecordUpdateCheck(_config, updateCheck))
                global = _config.LoadGlobalConfig();
        }

        var summary = new CliInfoSummary(
            Version: BuildInfo.Version,
            Commit: BuildInfo.Commit,
            InstalledVersion: new InstalledVersionSummary(
                Version: global.Version.Version,
                PreviousVersion: global.Version.PreviousVersion,
                InstalledAt: global.Version.InstalledAt,
                LastUpdateCheckedAt: global.Version.LastUpdateCheckedAt,
                LastUpdateCheckedVersion: global.Version.LastUpdateCheckedVersion),
            Config: new ConfigSummary(
                ActiveTenant: global.ActiveTenant,
                TenantCount: tenants.Count,
                RepoMappingCount: repoMappings.Count,
                DefaultLocation: global.DefaultLocation,
                WfhDays: global.WfhDays),
            Tenant: activeTenant?.ToSummary(),
            Update: BuildUpdateSummary(updateCheck, global.Version));

        OutputHelper.Render(summary, settings.Json, RenderHuman);
        return 0;
    }

    private static UpdateSummary BuildUpdateSummary(
        UpdateCheckResult? updateCheck,
        InstalledVersionConfig versionState)
    {
        if (updateCheck is null)
        {
            return new UpdateSummary(
                Status: "skipped",
                CurrentVersion: BuildInfo.Version,
                LatestVersion: versionState.LastUpdateCheckedVersion,
                UpdateAvailable: false,
                CheckedAt: versionState.LastUpdateCheckedAt,
                ReleaseUrl: versionState.LastUpdateCheckedVersion is null
                    ? null
                    : GitHubReleaseClient.ReleaseUrlFor(versionState.LastUpdateCheckedVersion),
                ErrorMessage: null,
                InstallMacLinux: null,
                InstallWindows: null);
        }

        return new UpdateSummary(
            Status: StatusText(updateCheck.Status),
            CurrentVersion: updateCheck.CurrentVersion,
            LatestVersion: updateCheck.LatestVersion,
            UpdateAvailable: updateCheck.UpdateAvailable,
            CheckedAt: updateCheck.CheckedAt,
            ReleaseUrl: updateCheck.ReleaseUrl,
            ErrorMessage: updateCheck.ErrorMessage,
            InstallMacLinux: updateCheck.UpdateAvailable
                ? $"curl -fsSL {GitHubReleaseClient.InstallScriptUrl} | bash"
                : null,
            InstallWindows: updateCheck.UpdateAvailable
                ? $"irm {GitHubReleaseClient.InstallPowerShellUrl} | iex"
                : null);
    }

    private static string StatusText(UpdateCheckStatus status) =>
        status switch
        {
            UpdateCheckStatus.Skipped => "skipped",
            UpdateCheckStatus.DevelopmentBuild => "development-build",
            UpdateCheckStatus.UpToDate => "up-to-date",
            UpdateCheckStatus.UpdateAvailable => "update-available",
            UpdateCheckStatus.Error => "error",
            _ => "unknown"
        };

    private static void RenderHuman(CliInfoSummary info)
    {
        var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
        table.AddRow("[bold]Version[/]", Markup.Escape($"{info.Version}+{info.Commit}"));
        table.AddRow("[bold]Installed[/]", Markup.Escape(info.InstalledVersion.Version ?? "not recorded"));
        table.AddRow("[bold]Previous version[/]", Markup.Escape(info.InstalledVersion.PreviousVersion ?? "none recorded"));
        table.AddRow("[bold]Installed at[/]", Markup.Escape(FormatDate(info.InstalledVersion.InstalledAt)));
        table.AddRow("[bold]Last update check[/]", Markup.Escape(FormatLastCheck(info)));
        table.AddRow("[bold]Active tenant[/]", Markup.Escape(info.Config.ActiveTenant ?? "none"));
        table.AddRow("[bold]Employee[/]", Markup.Escape(FormatEmployee(info.Tenant)));
        table.AddRow("[bold]API URL[/]", Markup.Escape(info.Tenant?.ApiUrl ?? "none"));
        table.AddRow("[bold]Environment[/]", info.Tenant is null
            ? "none"
            : info.Tenant.IsProduction ? "[red]Production[/]" : "[green]Staging/Dev[/]");
        table.AddRow("[bold]Tenants[/]", info.Config.TenantCount.ToString());
        table.AddRow("[bold]Repo mappings[/]", info.Config.RepoMappingCount.ToString());
        table.AddRow("[bold]Default location[/]", Markup.Escape(info.Config.DefaultLocation));
        table.AddRow("[bold]WFH days[/]", Markup.Escape(info.Config.WfhDays.Count == 0
            ? "none"
            : string.Join(", ", info.Config.WfhDays)));
        table.AddRow("[bold]Update status[/]", Markup.Escape(info.Update.Status));

        if (!string.IsNullOrWhiteSpace(info.Update.LatestVersion))
            table.AddRow("[bold]Latest version[/]", Markup.Escape(info.Update.LatestVersion));
        if (!string.IsNullOrWhiteSpace(info.Update.ReleaseUrl))
            table.AddRow("[bold]Release notes[/]", Markup.Escape(info.Update.ReleaseUrl));
        if (!string.IsNullOrWhiteSpace(info.Update.ErrorMessage))
            table.AddRow("[bold]Update check error[/]", Markup.Escape(info.Update.ErrorMessage));

        AnsiConsole.Write(table);

        if (info.Update.UpdateAvailable)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Update available.[/]");
            AnsiConsole.MarkupLine($"macOS/Linux: [grey]{Markup.Escape(info.Update.InstallMacLinux!)}[/]");
            AnsiConsole.MarkupLine($"Windows:     [grey]{Markup.Escape(info.Update.InstallWindows!)}[/]");
        }
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value is null ? "not recorded" : value.Value.ToString("u");

    private static string FormatLastCheck(CliInfoSummary info)
    {
        if (info.Update.CheckedAt is not null)
            return $"{info.Update.CheckedAt:u} (latest {info.Update.LatestVersion ?? "unknown"})";

        if (info.InstalledVersion.LastUpdateCheckedAt is not null)
            return $"{info.InstalledVersion.LastUpdateCheckedAt:u} (latest {info.InstalledVersion.LastUpdateCheckedVersion ?? "unknown"})";

        return "not recorded";
    }

    private static string FormatEmployee(TenantConfigSummary? tenant)
    {
        if (tenant is null)
            return "none";

        var name = string.IsNullOrWhiteSpace(tenant.EmployeeName) ? "unknown" : tenant.EmployeeName;
        var empId = string.IsNullOrWhiteSpace(tenant.EmployeeId) ? "unknown" : tenant.EmployeeId;
        return $"{name} ({empId})";
    }
}

public sealed record CliInfoSummary(
    string Version,
    string Commit,
    InstalledVersionSummary InstalledVersion,
    ConfigSummary Config,
    TenantConfigSummary? Tenant,
    UpdateSummary Update);

public sealed record InstalledVersionSummary(
    string? Version,
    string? PreviousVersion,
    DateTimeOffset? InstalledAt,
    DateTimeOffset? LastUpdateCheckedAt,
    string? LastUpdateCheckedVersion);

public sealed record ConfigSummary(
    string? ActiveTenant,
    int TenantCount,
    int RepoMappingCount,
    string DefaultLocation,
    IReadOnlyList<string> WfhDays);

public sealed record UpdateSummary(
    string Status,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    DateTimeOffset? CheckedAt,
    string? ReleaseUrl,
    string? ErrorMessage,
    string? InstallMacLinux,
    string? InstallWindows);
