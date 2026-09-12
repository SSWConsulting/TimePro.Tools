using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Mcp.Tools;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// The registered tool surface, read from the attributes rather than a hand-kept list, so a new
/// tool immediately shows up as an uncovered case.
/// </summary>
public static class McpToolInventory
{
    public static IReadOnlyList<Type> DefaultToolTypes { get; } =
        [typeof(TimesheetMcpTools), typeof(LookupMcpTools), typeof(LeaveMcpTools)];

    public static Type AccountingToolType => typeof(AccountingMcpTools);

    public static IReadOnlyList<string> DefaultMethodNames { get; } =
        DefaultToolTypes.SelectMany(MethodNamesOn).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> AccountingMethodNames { get; } =
        MethodNamesOn(AccountingToolType).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> AllMethodNames { get; } =
        DefaultMethodNames.Concat(AccountingMethodNames).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> DefaultToolNames { get; } =
        DefaultMethodNames.Select(WireName).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> AccountingToolNames { get; } =
        AccountingMethodNames.Select(WireName).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> AllToolNames { get; } =
        AllMethodNames.Select(WireName).Order(StringComparer.Ordinal).ToList();

    public static IEnumerable<MethodInfo> Methods(Type toolType) =>
        toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    public static IEnumerable<MethodInfo> AllMethods =>
        DefaultToolTypes.Append(AccountingToolType).SelectMany(Methods);

    /// <summary>
    /// The SDK publishes <c>AcceptSuggestedTimesheet</c> as <c>accept_suggested_timesheet</c>;
    /// clients bind to the wire name, so every cross-check goes through here.
    /// </summary>
    public static string WireName(string methodName)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < methodName.Length; i++)
        {
            var c = methodName[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> MethodNamesOn(Type toolType) =>
        Methods(toolType).Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name ?? m.Name);
}
