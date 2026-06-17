using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Auth;

[Description("Authenticate with a TimePro tenant")]
public class LoginCommand : AsyncCommand<LoginCommand.Settings>
{
    private readonly IConfigService _config;
    private readonly ITimeProApiClient _api;
    private readonly DefaultTenantProvider _tenantProvider;

    public class Settings : CommandSettings
    {
        [CommandOption("--tenant <TENANT>")]
        [Description("Tenant ID (e.g., ssw)")]
        public string Tenant { get; set; } = string.Empty;

        [CommandOption("--token <TOKEN>")]
        [Description("API token (will prompt if not provided)")]
        public string? Token { get; set; }

        [CommandOption("--api-url <URL>")]
        [Description("API base URL (default: https://api.sswtimepro.com)")]
        public string? ApiUrl { get; set; }
    }

    public LoginCommand(IConfigService config, ITimeProApiClient api, ITenantProvider tenantProvider)
    {
        _config = config;
        _api = api;
        _tenantProvider = (DefaultTenantProvider)tenantProvider;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Tenant))
        {
            OutputHelper.WriteError("--tenant is required");
            return 1;
        }

        var tenant = _config.LoadTenantConfig(settings.Tenant) ?? new TenantConfig
        {
            TenantId = settings.Tenant.ToLowerInvariant()
        };

        if (!string.IsNullOrEmpty(settings.ApiUrl))
            tenant.ApiUrl = settings.ApiUrl;

        // Prompt for token if not provided
        var token = settings.Token;
        if (string.IsNullOrEmpty(token))
        {
            AnsiConsole.MarkupLine($"[blue]To get your API token, visit:[/]");
            AnsiConsole.MarkupLine($"  [link]{tenant.GetTokenPageUrl()}[/]");
            AnsiConsole.WriteLine();
            token = AnsiConsole.Prompt(
                new TextPrompt<string>("Paste your API token:")
                    .Secret());
        }

        tenant.ApiKey = token;

        // Temporarily override the tenant provider so API calls use the new credentials
        _tenantProvider.SetOverride(tenant);

        try
        {
            // Auto-detect employee ID
            var empIdResponse = await _api.GetEmployeeIdAsync(CancellationToken.None);
            tenant.EmployeeId = empIdResponse?.EmpId;

            // Get user name
            var user = await _api.GetCurrentUserAsync(CancellationToken.None);
            tenant.EmployeeName = $"{user?.FirstName} {user?.LastName}".Trim();

            // Save tenant config
            _config.SaveTenantConfig(tenant);

            // Set as active tenant
            var global = _config.LoadGlobalConfig();
            global.ActiveTenant = tenant.TenantId;
            _config.SaveGlobalConfig(global);

            OutputHelper.WriteSuccess(
                $"Logged in as {tenant.EmployeeId} ({tenant.EmployeeName}) on tenant '{tenant.TenantId}'");
            AnsiConsole.MarkupLine($"  API: [dim]{Markup.Escape(tenant.ApiUrl)}[/]");

            return 0;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            OutputHelper.WriteError("Authentication failed. Check your API token.");
            return 1;
        }
        catch (Exception ex)
        {
            OutputHelper.WriteError($"Login failed: {ex.Message}");
            return 1;
        }
        finally
        {
            _tenantProvider.ClearOverride();
        }
    }
}
