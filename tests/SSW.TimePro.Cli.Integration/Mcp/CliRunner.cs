using Microsoft.Extensions.DependencyInjection;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Cli;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Every class that runs <see cref="CliRunner"/>. It swaps the process-wide Console.Out, so two
/// such classes in parallel steal each other's stdout.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class CliConsoleCollection
{
    public const string Name = "cli-console";
}

/// <summary>
/// Runs the real command tree in-process against a supplied API client, capturing stdout so a
/// parity case can compare a CLI <c>--json</c> document with its MCP counterpart.
/// </summary>
public static class CliRunner
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    public static async Task<Result> RunAsync(
        string[] args, ITimeProApiClient api, IConfigService config, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);
        services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
        services.AddSingleton<LeaveLookup>();
        services.AddSingleton<LeaveListService>();
        services.AddSingleton<LeaveCreateService>();
        services.AddSingleton<LeaveUpdateService>();
        services.AddSingleton<LeaveBalanceImportService>();
        services.AddSingleton<TimesheetUpdateService>();
        services.AddSingleton<TimesheetAcceptService>();

        var jsonRequested = CommandLineErrorHandler.IsJsonRequested(args);
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.SetExceptionHandler((ex, _) => CommandLineErrorHandler.Handle(ex, jsonRequested, args));
            CliConfiguration.Configure(configurator);
        });

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalConsole = AnsiConsole.Console;

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(stdout)
            });

            var exitCode = await app.RunAsync(args, ct);
            return new Result(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
