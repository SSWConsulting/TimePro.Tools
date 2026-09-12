using SSW.TimePro.Cli.Infrastructure.ApiClient;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Infrastructure.Output;

/// <summary>
/// Turns Spectre parse/binding failures, and connection failures escaping a command, into the same
/// contract the commands themselves honour: the JSON error envelope on stdout when <c>--json</c> was
/// asked for, human text on stderr otherwise.
/// </summary>
public static class CommandLineErrorHandler
{
    public const int CommandLineErrorExitCode = 1;
    private const int UnexpectedErrorExitCode = -1;

    /// <summary>
    /// Whether <c>--json</c> appears anywhere in the raw arguments. Parse failures happen before
    /// settings binding, so the flag can only be read off argv.
    /// </summary>
    public static bool IsJsonRequested(IEnumerable<string> args) =>
        args.Any(IsJsonFlag);

    private static bool IsJsonFlag(string arg)
    {
        if (arg is "--json")
            return true;

        if (!arg.StartsWith("--json=", StringComparison.Ordinal))
            return false;

        var value = arg["--json=".Length..];
        return !bool.TryParse(value, out var enabled) || enabled;
    }

    public static int Handle(Exception exception, bool jsonRequested)
    {
        if (FindConnectionFailure(exception) is { } connectionFailure)
            return HandleConnectionFailure(connectionFailure, jsonRequested);

        var commandLineError = exception as CommandAppException;

        if (jsonRequested)
        {
            OutputHelper.WriteJsonError(
                exception.Message,
                detail: commandLineError is null ? exception.ToString() : null);
        }
        else if (commandLineError is not null)
        {
            if (commandLineError.Pretty is not null)
                OutputHelper.WriteErrorRenderable(commandLineError.Pretty);
            else
                OutputHelper.WriteError(commandLineError.Message);
        }
        else
        {
            OutputHelper.WriteErrorException(exception);
        }

        return commandLineError is not null ? CommandLineErrorExitCode : UnexpectedErrorExitCode;
    }

    private static int HandleConnectionFailure(TimeProConnectionException failure, bool jsonRequested)
    {
        if (jsonRequested)
        {
            OutputHelper.WriteJsonError(
                failure.Message,
                detail: ConnectionErrorPresenter.BuildDetail(failure),
                tenant: failure.TenantFile ?? failure.TenantId,
                apiUrl: failure.ApiUrl);
        }

        OutputHelper.WriteError(failure.Message);
        foreach (var line in ConnectionErrorPresenter.BuildContextLines(failure))
            OutputHelper.WriteErrorDetail(line);

        return CommandLineErrorExitCode;
    }

    private static TimeProConnectionException? FindConnectionFailure(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is TimeProConnectionException connectionFailure)
                return connectionFailure;

            exception = exception.InnerException;
        }

        return null;
    }
}
