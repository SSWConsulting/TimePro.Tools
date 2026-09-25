using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Users;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Users;

public class StaffDirectoryTests
{
    [Fact]
    public async Task ListAsync_KeepsOnlyCategorisedStaffOutsideExcludedCategories()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var ct = TestContext.Current.CancellationToken;

        api.ListUsersAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new EmployeeSummary { EmpId = "BOB", Name = "Bob Northwind" },
            new EmployeeSummary { EmpId = "OFF", Name = "Olive Northwind" },
            new EmployeeSummary { EmpId = "OAE", Name = "Oscar Northwind" },
            new EmployeeSummary { EmpId = "CON", Name = "Connie Northwind" },
            new EmployeeSummary { EmpId = "TIM", Name = "Tim Northwind" },
            new EmployeeSummary { EmpId = "SVC", Name = "Northwind Service" },
            new EmployeeSummary { EmpId = "ZZR", Name = "zzRita zzNorthwind" }
        ]);

        Detail(api, "BOB", "PM-E");
        Detail(api, "OFF", "O");
        Detail(api, "OAE", "oa-e");
        Detail(api, "CON", "EXCON");
        Detail(api, "TIM", "WE");
        Detail(api, "SVC", null);

        var staff = await StaffDirectory.ListAsync(api, ct);

        staff.Select(s => s.EmpId).Should().Equal("BOB");
        await api.DidNotReceive().GetUserAsync("ZZR", Arg.Any<CancellationToken>());
    }

    private static void Detail(ITimeProApiClient api, string empId, string? categoryId) =>
        api.GetUserAsync(empId, Arg.Any<CancellationToken>())
            .Returns(new EmployeeDetail { EmpId = empId, CategoryId = categoryId });
}
