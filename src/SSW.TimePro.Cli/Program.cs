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
using LeaveList = SSW.TimePro.Cli.Features.Leave.ListCommand;
using LeaveCreate = SSW.TimePro.Cli.Features.Leave.CreateCommand;
using LeaveCancel = SSW.TimePro.Cli.Features.Leave.CancelCommand;
using LocationInfo = SSW.TimePro.Cli.Features.Location.InfoCommand;
using LocationSet = SSW.TimePro.Cli.Features.Location.SetCommand;
using MapSet = SSW.TimePro.Cli.Features.RepoMap.SetCommand;
using MapList = SSW.TimePro.Cli.Features.RepoMap.ListCommand;
using MapRemove = SSW.TimePro.Cli.Features.RepoMap.RemoveCommand;
using MapDetect = SSW.TimePro.Cli.Features.RepoMap.DetectCommand;
using SkillsCreate = SSW.TimePro.Cli.Features.Skills.CreateCommand;
using McpHost = SSW.TimePro.Cli.Features.Mcp.McpHostCommand;

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
        branch.AddCommand<ExportCommand>("export")
            .WithDescription("Export timesheets to CSV");
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

    // Leave (with alias)
    void RegisterLeaveCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<LeaveList>("list")
            .WithDescription("List leave entries");
        branch.AddCommand<LeaveCreate>("create")
            .WithDescription("Create a leave request");
        branch.AddCommand<LeaveCancel>("cancel")
            .WithDescription("Cancel a leave request");
    }

    config.AddBranch("leave", lv =>
    {
        lv.SetDescription("Manage leave/EasyLeave");
        RegisterLeaveCommands(lv);
    });

    config.AddBranch("lv", lv =>
    {
        lv.SetDescription("Manage leave (alias)");
        RegisterLeaveCommands(lv);
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

    // Location (with alias)
    config.AddBranch("location", loc =>
    {
        loc.SetDescription("Location and WFH settings");
        loc.AddCommand<LocationInfo>("info")
            .WithDescription("Show location defaults");
        loc.AddCommand<LocationSet>("set")
            .WithDescription("Set WFH day defaults");
    });

    config.AddBranch("loc", loc =>
    {
        loc.SetDescription("Location settings (alias)");
        loc.AddCommand<LocationInfo>("info")
            .WithDescription("Show location defaults");
        loc.AddCommand<LocationSet>("set")
            .WithDescription("Set WFH day defaults");
    });

    // Repo mapping
    config.AddBranch("map", map =>
    {
        map.SetDescription("Repository-to-project mappings");
        map.AddCommand<MapSet>("set")
            .WithDescription("Map a repo path to a client/project");
        map.AddCommand<MapList>("list")
            .WithDescription("List repo mappings");
        map.AddCommand<MapRemove>("remove")
            .WithDescription("Remove a repo mapping");
        map.AddCommand<MapDetect>("detect")
            .WithDescription("Detect mapping for current directory");
    });

    // Skills
    config.AddBranch("skills", skills =>
    {
        skills.SetDescription("Agent skill file management");
        skills.AddCommand<SkillsCreate>("create")
            .WithDescription("Generate agent skill files");
    });

    // User
    config.AddBranch("user", user =>
    {
        user.SetDescription("User information");
        user.AddCommand<MeCommand>("me")
            .WithDescription("Show current user info");
    });

    // MCP
    config.AddCommand<McpHost>("mcp")
        .WithDescription("Start MCP server (stdio transport)");
});

return await app.RunAsync(args);
