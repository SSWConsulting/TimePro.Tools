using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class ConnectionErrorPresenterTests
{
    [Fact]
    public void BuildContextLines_NamesTenantFileApiUrlAndSwitchCommand()
    {
        var ex = new TimeProConnectionException(
            "Connection refused (localhost:1)",
            tenantFile: "northwind-staging",
            tenantId: "northwind",
            apiUrl: "https://localhost:1/");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var lines = ConnectionErrorPresenter.BuildContextLines(ex, home);

        lines.Should().HaveCount(2);
        lines[0].Should().Be(
            $"Active tenant: northwind-staging ({Path.Combine("~", ".config", "timepro-cli", "tenants", "northwind-staging.json")}, apiUrl https://localhost:1/)");
        lines[1].Should().Contain("tp tenant set");
        lines[1].Should().Contain("tp tenant list");
    }

    [Fact]
    public void BuildContextLines_FallsBackToTenantIdWhenFileIsUnknown()
    {
        var ex = new TimeProConnectionException(
            "No such host is known. (northwind.invalid:443)",
            tenantFile: null,
            tenantId: "northwind",
            apiUrl: "https://northwind.invalid/");

        ConnectionErrorPresenter.BuildContextLines(ex)[0]
            .Should().Be("Active tenant: northwind (apiUrl https://northwind.invalid/)");
    }
}
