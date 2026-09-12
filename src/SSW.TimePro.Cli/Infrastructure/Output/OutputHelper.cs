using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Rendering;
using SSW.TimePro.Cli.Infrastructure.ApiClient;

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

    // Compact options for the machine-readable error envelope emitted on the
    // --json path. The core keys (code/message/detail) are always present (null when absent) so
    // the shape is stable for agents parsing stdout; `recovery` is an optional extra, present only
    // on errors that carry a recovery recipe.
    private static readonly JsonSerializerOptions ErrorEnvelopeOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // A dedicated console bound to stderr so error/warning markup never lands on
    // stdout (which would corrupt JSON an agent is parsing on the --json path).
    private static readonly IAnsiConsole ErrorConsole = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });

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
    /// Writes an error message to stderr (so it never corrupts JSON on stdout).
    /// </summary>
    public static void WriteError(string message)
    {
        ErrorConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes a pre-rendered error (e.g. Spectre's parse-error diagram) to stderr.
    /// </summary>
    public static void WriteErrorRenderable(IRenderable renderable)
    {
        ErrorConsole.Write(renderable);
    }

    /// <summary>
    /// Writes an unexpected exception, with stack trace, to stderr.
    /// </summary>
    public static void WriteErrorException(Exception exception)
    {
        ErrorConsole.WriteException(exception);
    }

    /// <summary>
    /// Writes an unprefixed follow-up line to stderr, for context under an error.
    /// </summary>
    public static void WriteErrorDetail(string message)
    {
        ErrorConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Reports a failed API call on whichever path the caller is using, including the
    /// detail parsed out of the server response body.
    /// </summary>
    public static void WriteApiError(ApiException exception, bool useJson)
    {
        var detail = ApiErrorParser.ExtractDetail(exception.ResponseBody);

        if (useJson)
        {
            WriteJsonError(
                $"API error: {exception.Message}",
                exception.StatusCode,
                detail,
                requestId: exception.RequestId);
            return;
        }

        WriteError(detail is null
            ? $"API error ({exception.StatusCode}): {exception.Message}"
            : $"API error ({exception.StatusCode}): {exception.Message} - {detail}");

        WriteRequestId(exception.RequestId);
    }

    /// <summary>
    /// Writes the correlation id under an error so a user can quote it to whoever reads the
    /// server logs. Silent when there is no id (a failure before any HTTP attempt).
    /// </summary>
    public static void WriteRequestId(string? requestId)
    {
        if (!string.IsNullOrWhiteSpace(requestId))
            WriteErrorDetail($"request id: {requestId}");
    }

    /// <summary>
    /// Writes a warning message to stderr (so it never corrupts JSON on stdout).
    /// </summary>
    public static void WriteWarning(string message)
    {
        ErrorConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes a compact, machine-readable error envelope to <b>stdout</b> for the
    /// <c>--json</c> path: <c>{"error":{"code":&lt;code|null&gt;,"message":...,"detail":&lt;detail|null&gt;}}</c>.
    /// This keeps stdout valid JSON for an agent even when an API call fails.
    /// </summary>
    public static void WriteJsonError(
        string message,
        int? code = null,
        string? detail = null,
        object? recovery = null,
        string? tenant = null,
        string? apiUrl = null,
        string? requestId = null)
    {
        var envelope = new ErrorEnvelope(new ErrorPayload(code, message, detail, recovery, tenant, apiUrl, requestId));
        Console.Out.WriteLine(JsonSerializer.Serialize(envelope, ErrorEnvelopeOptions));
    }

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] ErrorPayload Error);

    private sealed record ErrorPayload(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("detail")] string? Detail,
        // Optional, omitted when absent: a machine-actionable recovery recipe an agent can follow.
        [property: JsonPropertyName("recovery")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Recovery = null,
        // Optional, present on connection failures: which tenant config produced the URL that failed.
        [property: JsonPropertyName("tenant")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tenant = null,
        [property: JsonPropertyName("apiUrl")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApiUrl = null,
        // Optional, present once an HTTP attempt was made: matches x-timepro-client-request-id.
        [property: JsonPropertyName("requestId")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null);

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
