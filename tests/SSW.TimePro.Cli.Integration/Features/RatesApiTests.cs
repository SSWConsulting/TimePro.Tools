using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Shared.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// WireMock-backed tests for the rate endpoints used by the rate-aware timesheet flow.
/// Field names use the server's PascalCase shape (case-insensitive deserialize); the placeholder
/// client is Northwind (NWIND).
/// </summary>
public class RatesApiTests : TestBase
{
    [Fact]
    public async Task InitializeClientRate_ReturnsDefaultAndPreviousRates()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/InitializeClientRate")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "DefaultRate": 200, "DefaultPrepaidRate": 185,
                  "PreviousRate": 325, "PreviousPrepaidRate": 300,
                  "EmpID": "TST", "Employee": "Test User (TST)",
                  "ClientID": "NWIND", "Client": "Northwind Traders (NWIND)"
                }
                """));

        var init = await ApiClient.InitializeClientRateAsync("TST", "NWIND", TestContext.Current.CancellationToken);

        init.Should().NotBeNull();
        init!.PreviousRate.Should().Be(325);
        init.PreviousPrepaidRate.Should().Be(300);
        init.DefaultRate.Should().Be(200);
        init.Client.Should().Be("Northwind Traders (NWIND)");
    }

    [Fact]
    public async Task SaveClientRate_PostsModel_OmittingNullRateIdOnCreate()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveClientRate")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var model = new SaveClientRateModel
        {
            ClientRateId = null, // create
            EmpId = "TST",
            ClientId = "NWIND",
            Rate = 325m,
            PrepaidRate = 300m,
            ExpiryDate = new DateTime(2026, 12, 26)
        };

        await ApiClient.SaveClientRateAsync(model, TestContext.Current.CancellationToken);

        var call = WireMock.LogEntries
            .Single(e => e.RequestMessage!.AbsolutePath == "/api/Timesheets/SaveClientRate");
        using var doc = JsonDocument.Parse(call.RequestMessage!.Body!);
        doc.RootElement.GetProperty("ClientId").GetString().Should().Be("NWIND");
        doc.RootElement.GetProperty("EmpId").GetString().Should().Be("TST");
        doc.RootElement.GetProperty("Rate").GetDecimal().Should().Be(325m);
        // null ClientRateId is dropped (WhenWritingNull) so the server treats it as a create
        doc.RootElement.TryGetProperty("ClientRateId", out _).Should().BeFalse();
    }
}
