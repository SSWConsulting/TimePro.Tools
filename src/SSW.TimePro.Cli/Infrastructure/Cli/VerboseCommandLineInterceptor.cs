namespace SSW.TimePro.Cli.Infrastructure.Cli;

public sealed record VerboseParseResult(string[] Args, bool Verbose);

/// <summary>
/// Strips <c>--verbose</c> from argv before Spectre parses it, so it works on every command
/// without each settings class declaring it (the same approach as <c>--tenant</c>).
/// </summary>
public static class VerboseCommandLineInterceptor
{
    public static VerboseParseResult ExtractCommandLineOptions(string[] args)
    {
        var filtered = new List<string>(args.Length);
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                filtered.AddRange(args[i..]);
                break;
            }

            if (arg == "--verbose")
            {
                verbose = true;
                continue;
            }

            if (arg.StartsWith("--verbose=", StringComparison.Ordinal))
            {
                var value = arg["--verbose=".Length..];
                verbose = !bool.TryParse(value, out var enabled) || enabled;
                continue;
            }

            filtered.Add(arg);
        }

        return new VerboseParseResult(filtered.ToArray(), verbose);
    }
}
