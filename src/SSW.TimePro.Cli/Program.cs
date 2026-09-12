using Microsoft.Extensions.DependencyInjection;
using SSW.TimePro.Cli.Features.Accounting;
using SSW.TimePro.Cli.Features.Auth;
using SSW.TimePro.Cli.Features.Bookings;
using SSW.TimePro.Cli.Features.FeatureFlags;
using SSW.TimePro.Cli.Features.Info;
using SSW.TimePro.Cli.Features.Tenants;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Features.Updates;
using SSW.TimePro.Cli.Infrastructure;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Cli;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Spectre.Console.Cli;


var configService = new ConfigService();
var featureFlags = FeatureFlagCommandLineInterceptor.ExtractCommandLineOptions(args);
FeatureFlagCommandLineInterceptor.EnableRequestedFeatures(configService, featureFlags.EnableFeatures);

var verbose = VerboseCommandLineInterceptor.ExtractCommandLineOptions(featureFlags.Args);
ClientContext.Verbose = verbose.Verbose;

var tenantOverride = TenantOverrideResolver.ExtractCommandLineOptions(verbose.Args);
if (tenantOverride.Error is not null)
{
    OutputHelper.WriteError(tenantOverride.Error);
    return 1;
}

var jsonRequested = CommandLineErrorHandler.IsJsonRequested(tenantOverride.Args);
var isHelpOrVersionRequest = tenantOverride.Args.Any(arg => arg is "--help" or "-h" or "--version");
if (!isHelpOrVersionRequest)
    VersionStateService.RecordInstalledVersion(configService, BuildInfo.Version, DateTimeOffset.UtcNow);

if (AppMetadataCommandLine.IsMetadataRequest(tenantOverride.Args))
    return await AppMetadataCommandLine.ExecuteAsync(tenantOverride.Args, configService, CancellationToken.None);

TenantConfig? overrideTenant = null;
string? tenantOverrideError = null;
if (!isHelpOrVersionRequest)
{
    overrideTenant = TenantOverrideResolver.ResolveTenantOverride(
        configService,
        tenantOverride.Options,
        out tenantOverrideError);
}

if (tenantOverrideError is not null)
{
    if (jsonRequested)
        OutputHelper.WriteJsonError(tenantOverrideError);

    OutputHelper.WriteError(tenantOverrideError);
    return 1;
}

if (overrideTenant is not null)
    configService.SetActiveTenantOverride(overrideTenant);

var invocation = ClientContext.BeginCli(CommandPathResolver.Resolve(tenantOverride.Args));

if (verbose.Verbose && configService.LoadActiveTenantConfig() is { } verboseTenant)
{
    var host = Uri.TryCreate(verboseTenant.ApiUrl, UriKind.Absolute, out var verboseUri)
        ? verboseUri.Host
        : verboseTenant.ApiUrl;
    Console.Error.WriteLine($"api host: {host} (tenant {verboseTenant.ConfigName ?? verboseTenant.TenantId})");
}

// Configure DI
var services = new ServiceCollection();
services.AddSingleton<IConfigService>(configService);
services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
services.AddHttpClient<ITimeProApiClient, TimeProApiClient>();
services.AddSingleton<SSW.TimePro.Cli.Features.Leave.LeaveCreateService>();
services.AddSingleton<SSW.TimePro.Cli.Features.Leave.LeaveUpdateService>();
services.AddSingleton<SSW.TimePro.Cli.Features.Leave.LeaveBalanceImportService>();
services.AddSingleton<TimesheetUpdateService>();
services.AddSingleton<TimesheetAcceptService>();

var registrar = new TypeRegistrar(services);

// Build command tree
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetExceptionHandler((ex, _) => CommandLineErrorHandler.Handle(ex, jsonRequested, tenantOverride.Args));
    CliConfiguration.Configure(config);
});

var started = System.Diagnostics.Stopwatch.StartNew();
var exitCode = await app.RunAsync(tenantOverride.Args);
started.Stop();

CommandLog.Record(
    invocation,
    configService.LoadGlobalConfig,
    configService.LoadActiveTenantConfig,
    started.ElapsedMilliseconds,
    exitCode);

return exitCode;
