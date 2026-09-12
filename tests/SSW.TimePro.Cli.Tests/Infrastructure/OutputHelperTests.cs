using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using System.Text.Json;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class OutputHelperTests
{
    [Fact]
    public void SerializeJson_WritesValidJson_ForLongStringsWithControlCharacters()
    {
        var payload = new
        {
            note = string.Join(" ", Enumerable.Repeat("Build the Northwind Checkout API", 8))
                   + "\nTabbed\tvalue"
        };

        var output = OutputHelper.SerializeJson(payload);

        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("note").GetString().Should().Be(payload.note);
        output.Should().Contain("\\n");
        output.Should().Contain("\\t");
    }

    [Fact]
    public void WriteJsonError_EmitsParseableEnvelope_WithCodeAndDetail()
    {
        var output = CaptureStdout(() => OutputHelper.WriteJsonError("API error: boom", 500, "duplicate entry"));

        // Must be a single compact line of valid JSON on stdout.
        output.Should().NotContain("\n", because: "the envelope is compact (single line)");

        using var doc = JsonDocument.Parse(output);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(500);
        error.GetProperty("message").GetString().Should().Be("API error: boom");
        error.GetProperty("detail").GetString().Should().Be("duplicate entry");
    }

    [Fact]
    public void WriteJsonError_EmitsNullCodeAndDetail_WhenOmitted()
    {
        var output = CaptureStdout(() => OutputHelper.WriteJsonError("Not logged in."));

        using var doc = JsonDocument.Parse(output);
        var error = doc.RootElement.GetProperty("error");

        // The shape is stable: code/detail are always present, null when absent.
        error.GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("message").GetString().Should().Be("Not logged in.");
    }

    [Fact]
    public void WriteApiError_AddsRequestId_WithoutDroppingCoreKeys()
    {
        var failure = new ApiException(404, "TimePro API returned 404 Not Found", "{\"detail\":\"no such invoice\"}", "req-abc-123");

        var output = CaptureStdout(() => OutputHelper.WriteApiError(failure, useJson: true));

        using var doc = JsonDocument.Parse(output);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("requestId").GetString().Should().Be("req-abc-123");
        error.GetProperty("code").GetInt32().Should().Be(404);
        error.GetProperty("message").GetString().Should().Contain("404");
        error.GetProperty("detail").GetString().Should().Be("no such invoice");
    }

    [Fact]
    public void WriteJsonError_OmitsRequestId_WhenNoHttpAttemptWasMade()
    {
        var output = CaptureStdout(() => OutputHelper.WriteJsonError("Not logged in."));

        using var doc = JsonDocument.Parse(output);
        doc.RootElement.GetProperty("error").TryGetProperty("requestId", out _).Should().BeFalse();
    }

    [Fact]
    public void WriteJsonError_AddsTenantAndApiUrl_WithoutDroppingCoreKeys()
    {
        var output = CaptureStdout(() => OutputHelper.WriteJsonError(
            "Connection refused (localhost:1)",
            tenant: "northwind-staging",
            apiUrl: "https://localhost:1/"));

        using var doc = JsonDocument.Parse(output);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("message").GetString().Should().Be("Connection refused (localhost:1)");
        error.GetProperty("tenant").GetString().Should().Be("northwind-staging");
        error.GetProperty("apiUrl").GetString().Should().Be("https://localhost:1/");
    }

    [Fact]
    public void WriteJsonError_OmitsTenantKeys_WhenNotAConnectionFailure()
    {
        var output = CaptureStdout(() => OutputHelper.WriteJsonError("API error: boom", 500));

        using var doc = JsonDocument.Parse(output);
        var error = doc.RootElement.GetProperty("error");
        error.TryGetProperty("tenant", out _).Should().BeFalse();
        error.TryGetProperty("apiUrl", out _).Should().BeFalse();
    }

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString().Trim();
    }
}
