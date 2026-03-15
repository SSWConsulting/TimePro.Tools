using Microsoft.Extensions.DependencyInjection;
using SSW.TimePro.Cli.Features.Auth;
using SSW.TimePro.Cli.Features.Bookings;
using SSW.TimePro.Cli.Features.Tenants;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Features.Users;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using Spectre.Console.Cli;

using ClientSearch = SSW.TimePro.Cli.Features.Clients.SearchCommand;
using ProjectList = SSW.TimePro.Cli.Features.Projects.ListCommand;
using RateGet = SSW.TimePro.Cli.Features.Rates.GetCommand;

// Configure DI
var services = new ServiceCollection();
services.AddSingleton<IConfigService, ConfigService>();
services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
services.AddHttpClient<ITimeProApiClient, TimeProApiClient>();

var registrar = new TypeRegistrar(services);

// Build command tree
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("tp");
    config.SetApplicationVersion("0.1.0");

    // Auth
    config.AddCommand<LoginCommand>("login")
        .WithDescription("Authenticate with a TimePro tenant");
    config.AddCommand<LogoutCommand>("logout")
        .WithDescription("Remove stored credentials");

    // Tenant management
    config.AddBranch("tenant", tenant =>
    {
        tenant.SetDescription("Manage tenants");
        tenant.AddCommand<TenantSetCommand>("set")
            .WithDescription("Switch the active tenant");
        tenant.AddCommand<TenantInfoCommand>("info")
            .WithDescription("Show active tenant details");
        tenant.AddCommand<TenantListCommand>("list")
            .WithDescription("List all stored tenants");
    });

    // Helper to register all timesheet subcommands on a branch
    void RegisterTimesheetCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<GetCommand>("get")
            .WithDescription("View timesheets for a day or week");
        branch.AddCommand<CreateCommand>("create")
            .WithDescription("Create a new timesheet entry");
        branch.AddCommand<UpdateCommand>("update")
            .WithDescription("Update an existing timesheet");
        branch.AddCommand<DeleteCommand>("delete")
            .WithDescription("Delete a timesheet entry");
        branch.AddCommand<SuggestCommand>("suggest")
            .WithDescription("View suggested timesheets");
        branch.AddCommand<AcceptCommand>("accept")
            .WithDescription("Accept a suggested timesheet");
    }

    // Timesheets (with alias)
    config.AddBranch("timesheet", ts =>
    {
        ts.SetDescription("Manage timesheets");
        RegisterTimesheetCommands(ts);
    });

    config.AddBranch("ts", ts =>
    {
        ts.SetDescription("Manage timesheets (alias)");
        RegisterTimesheetCommands(ts);
    });

    // Bookings (with alias)
    config.AddBranch("booking", bk =>
    {
        bk.SetDescription("CRM bookings/appointments");
        bk.AddCommand<ListCommand>("list")
            .WithDescription("List CRM bookings");
    });

    config.AddBranch("bk", bk =>
    {
        bk.SetDescription("CRM bookings (alias)");
        bk.AddCommand<ListCommand>("list")
            .WithDescription("List CRM bookings");
    });

    // Client (with alias)
    config.AddBranch("client", cl =>
    {
        cl.SetDescription("Client operations");
        cl.AddCommand<ClientSearch>("search")
            .WithDescription("Search for clients by name");
    });

    config.AddBranch("cl", cl =>
    {
        cl.SetDescription("Client operations (alias)");
        cl.AddCommand<ClientSearch>("search")
            .WithDescription("Search for clients by name");
    });

    // Project (with alias)
    config.AddBranch("project", pj =>
    {
        pj.SetDescription("Project operations");
        pj.AddCommand<ProjectList>("list")
            .WithDescription("List projects for a client");
    });

    config.AddBranch("proj", pj =>
    {
        pj.SetDescription("Project operations (alias)");
        pj.AddCommand<ProjectList>("list")
            .WithDescription("List projects for a client");
    });

    // Rate
    config.AddBranch("rate", rate =>
    {
        rate.SetDescription("Rate information");
        rate.AddCommand<RateGet>("get")
            .WithDescription("Get client rate for current employee");
    });

    // User
    config.AddBranch("user", user =>
    {
        user.SetDescription("User information");
        user.AddCommand<MeCommand>("me")
            .WithDescription("Show current user info");
    });
});

return await app.RunAsync(args);
