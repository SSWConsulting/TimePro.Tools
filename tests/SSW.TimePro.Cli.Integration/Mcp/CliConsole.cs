using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// One xUnit collection for the harness classes that run the real command tree through
/// <see cref="CliRunner"/>. It redirects the process-wide <c>Console.Out</c>, so two of them
/// running concurrently would capture each other's output.
/// </summary>
[CollectionDefinition(Collection)]
public sealed class CliConsole
{
    public const string Collection = "cli-console";
}
