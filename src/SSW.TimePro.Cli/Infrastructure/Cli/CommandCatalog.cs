namespace SSW.TimePro.Cli.Infrastructure.Cli;

public sealed record CommandNode(string Name, IReadOnlyList<CommandNode> Children)
{
    public bool IsBranch => Children.Count > 0;

    public CommandNode? Child(string name) =>
        Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Mirror of the command tree registered in <c>Program.cs</c>. Spectre does not expose its
/// model outside a command, so unknown-command hints are built from this copy; new commands
/// must be added here too.
/// </summary>
public static class CommandCatalog
{
    private static readonly string[] TimesheetCommands =
        ["get", "create", "update", "delete", "suggest", "accept", "export", "check", "copy"];

    private static readonly string[] ClientCommands = ["search", "outstanding", "billable-work"];
    private static readonly string[] ProjectCommands = ["list", "recent"];
    private static readonly string[] InvoiceCommands = ["list", "get", "lines", "timesheets", "receipts"];
    private static readonly string[] ReceiptCommands = ["list", "get", "outstanding"];
    private static readonly string[] ProductCommands = ["list", "get", "discounts"];
    private static readonly string[] LocationCommands = ["info", "set"];

    public static CommandNode Root { get; } = new("tp",
    [
        Leaf("login"),
        Leaf("logout"),
        Branch("tenant", "set", "info", "list"),
        Leaf("feature"),
        Leaf("check-update"),
        Leaf("check-version"),
        Leaf("whats-new"),
        Branch("timesheet", TimesheetCommands),
        Branch("ts", TimesheetCommands),
        Branch("booking", "list"),
        Branch("bk", "list"),
        LeaveBranch("leave"),
        LeaveBranch("lv"),
        Branch("client", ClientCommands),
        Branch("cl", ClientCommands),
        Branch("project", ProjectCommands),
        Branch("proj", ProjectCommands),
        Branch("rate", "get", "list", "recommend", "create", "update"),
        Branch("iteration", "list"),
        Branch("iter", "list"),
        Branch("location", LocationCommands),
        Branch("loc", LocationCommands),
        Branch("map", "set", "list", "remove", "detect"),
        Branch("skills", "create", "ignore-version"),
        Branch("accounting", "guide"),
        Branch("acct", "guide"),
        Branch("developer", "guide"),
        Branch("dev", "guide"),
        Branch("invoice", InvoiceCommands),
        Branch("inv", InvoiceCommands),
        Branch("receipt", ReceiptCommands),
        Branch("rcpt", ReceiptCommands),
        Branch("creditnote", "list"),
        Branch("cn", "list"),
        Branch("product", ProductCommands),
        Branch("prod", ProductCommands),
        Branch("recurring", "list", "get"),
        Branch("prepaid", "summary", "status"),
        Branch("unbilled", "list"),
        Branch("user", "me", "list", "get"),
        Branch("blog", "list"),
        Leaf("info"),
        Leaf("summary"),
        Leaf("report"),
        Leaf("query"),
        Leaf("scrum"),
        Leaf("mcp"),
    ]);

    private static CommandNode Leaf(string name) => new(name, []);

    private static CommandNode Branch(string name, params string[] children) =>
        new(name, [.. children.Select(Leaf)]);

    private static CommandNode LeaveBranch(string name) =>
        new(name,
        [
            Leaf("list"),
            Leaf("create"),
            Leaf("update"),
            Leaf("cancel"),
            Leaf("balance"),
            Branch("balances", "status", "import"),
        ]);
}
