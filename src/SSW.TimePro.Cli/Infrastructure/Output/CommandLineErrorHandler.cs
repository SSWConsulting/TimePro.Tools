using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Infrastructure.Output;

/// <summary>
/// Turns Spectre parse/binding failures into the same contract the commands themselves honour:
/// the JSON error envelope on stdout when <c>--json</c> was asked for, human text on stderr otherwise.
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
        args.Any(arg => arg is "--json" || arg.StartsWith("--json=", StringComparison.Ordinal));

    public static int Handle(Exception exception, bool jsonRequested)
    {
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
}
