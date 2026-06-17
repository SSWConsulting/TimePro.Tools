using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace SSW.TimePro.Cli.Infrastructure.Output;

/// <summary>
/// Handles output formatting — normal (Spectre markup) vs --json.
/// </summary>
public static class OutputHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Outputs data as JSON (for --json flag) or runs the display action for human output.
    /// </summary>
    public static void Render<T>(T data, bool useJson, Action<T> displayAction)
    {
        if (useJson)
        {
            WriteRawJson(data);
        }
        else
        {
            displayAction(data);
        }
    }

    /// <summary>
    /// Outputs data as formatted JSON.
    /// </summary>
    public static void WriteJson<T>(T data)
    {
        WriteRawJson(data);
    }

    /// <summary>
    /// Writes an error message to stderr.
    /// </summary>
    public static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes a warning message.
    /// </summary>
    public static void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes a success message.
    /// </summary>
    public static void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Writes an info message.
    /// </summary>
    public static void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]{Markup.Escape(message)}[/]");
    }

    private static void WriteRawJson<T>(T data)
    {
        var json = SerializeJson(data);

        // Emit raw JSON to stdout without terminal formatting so it stays
        // machine-readable for tools like jq.
        Console.Out.WriteLine(json);
    }

    internal static string SerializeJson<T>(T data)
    {
        return JsonSerializer.Serialize(data, JsonOptions);
    }
}
