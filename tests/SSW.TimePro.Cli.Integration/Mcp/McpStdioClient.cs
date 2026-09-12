using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Minimal JSON-RPC-over-stdio client that drives the real <c>tp mcp</c> child process.
/// Kept deliberately dumb: it correlates ids, skips notifications, and drains stderr so a
/// crashing server surfaces its output instead of hanging the test.
/// </summary>
public sealed class McpStdioClient : IAsyncDisposable
{
    private const string ProtocolVersion = "2024-11-05";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly StringBuilder _stderr = new();
    private readonly Queue<JsonObject> _pending = new();
    private int _nextId = 1;

    private McpStdioClient(Process process)
    {
        _process = process;
    }

    public string Stderr => _stderr.ToString();

    public static async Task<McpStdioClient> StartAsync(
        string configRoot,
        IEnumerable<string>? extraArgs = null,
        CancellationToken ct = default)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        info.ArgumentList.Add(CliBinary.Path);
        info.ArgumentList.Add("mcp");
        foreach (var arg in extraArgs ?? [])
            info.ArgumentList.Add(arg);

        info.Environment["TIMEPRO_CLI_CONFIG_DIR"] = configRoot;
        info.Environment["DOTNET_ENVIRONMENT"] = "Production";
        info.Environment["NO_COLOR"] = "1";

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("Failed to start the MCP host process.");

        var client = new McpStdioClient(process);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (client._stderr) client._stderr.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        await client.InitializeAsync(ct);
        return client;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "tp-mcp-harness",
                ["version"] = "1.0.0"
            }
        }, ct);

        await NotifyAsync("notifications/initialized", ct);
    }

    /// <summary>Reads the whole paginated tool list.</summary>
    public async Task<List<JsonObject>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = new List<JsonObject>();
        string? cursor = null;

        do
        {
            var parameters = new JsonObject();
            if (cursor is not null)
                parameters["cursor"] = cursor;

            var result = await RequestAsync("tools/list", parameters, ct);
            foreach (var tool in result["tools"]!.AsArray())
                tools.Add((JsonObject)tool!.DeepClone());

            cursor = result["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null);

        return tools;
    }

    public async Task<JsonObject> CallToolAsync(
        string name, JsonObject arguments, CancellationToken ct = default)
    {
        return await RequestAsync("tools/call", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments
        }, ct);
    }

    public async Task<JsonObject> RequestAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        var id = _nextId++;
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
            request["params"] = parameters;

        await WriteAsync(request, ct);

        var response = await ReadResponseAsync(id, ct);
        if (response.TryGetPropertyValue("error", out var error))
            throw new InvalidOperationException(
                $"{method} failed: {error!.ToJsonString()}{Environment.NewLine}stderr:{Environment.NewLine}{Stderr}");

        return (JsonObject)response["result"]!;
    }

    private async Task NotifyAsync(string method, CancellationToken ct) =>
        await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, ct);

    private async Task WriteAsync(JsonObject frame, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(frame.ToJsonString().AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    private async Task<JsonObject> ReadResponseAsync(int id, CancellationToken ct)
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            var queued = _pending.Dequeue();
            if (Matches(queued, id))
                return queued;
            _pending.Enqueue(queued);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadTimeout);

        while (true)
        {
            string? line;
            try
            {
                line = await _process.StandardOutput.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for response {id}.{Environment.NewLine}stderr:{Environment.NewLine}{Stderr}");
            }

            if (line is null)
                throw new InvalidOperationException(
                    $"MCP host exited before answering request {id}.{Environment.NewLine}stderr:{Environment.NewLine}{Stderr}");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonObject frame;
            try
            {
                frame = (JsonObject)JsonNode.Parse(line)!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    $"Non-protocol output on the MCP stdout stream: {line}");
            }

            if (Matches(frame, id))
                return frame;

            // Server-initiated notifications and out-of-order responses.
            if (frame.ContainsKey("id"))
                _pending.Enqueue(frame);
        }
    }

    private static bool Matches(JsonObject frame, int id) =>
        frame.TryGetPropertyValue("id", out var value)
        && value is not null
        && value.GetValueKind() == JsonValueKind.Number
        && value.GetValue<int>() == id;

    public async ValueTask DisposeAsync()
    {
        try
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(5000))
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
        await Task.CompletedTask;
    }
}
