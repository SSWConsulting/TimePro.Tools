using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Reads the IL of an MCP tool class to find which tools still call a given type's members
/// themselves. Constructor inspection cannot answer this: the shared services are static helpers
/// that take the API client, so a migrated class still holds the dependency to pass it on.
/// </summary>
public static class ToolIlScanner
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static readonly Regex OwningMethod = new(@"<([^>]+)>", RegexOptions.Compiled);

    private const BindingFlags AnyMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// The tool methods on <paramref name="toolType"/> whose own code (including its async state
    /// machine and lambdas) calls a member declared on <paramref name="dependency"/>.
    /// </summary>
    public static IReadOnlySet<string> MethodsCalling(Type toolType, Type dependency)
    {
        var toolMethods = McpToolInventory.Methods(toolType).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var callers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in Types(toolType))
        {
            foreach (var method in type.GetMethods(AnyMember).Cast<MethodBase>().Concat(type.GetConstructors(AnyMember)))
            {
                if (!CallsInto(method, dependency))
                    continue;

                // Fail closed: an unattributable caller would otherwise let a private helper reach
                // the API client with no tool name for the allowlist to catch.
                callers.Add(Owner(method, toolMethods)
                    ?? throw new InvalidOperationException(
                        $"{method.DeclaringType!.Name}.{method.Name} calls {dependency.Name} but "
                        + "belongs to no MCP tool method. Move the call into a shared service, or "
                        + "inline it into the tool so the allowlist can name it."));
            }
        }

        return callers;
    }

    private static IEnumerable<Type> Types(Type toolType) =>
        new[] { toolType }.Concat(toolType.GetNestedTypes(AnyMember));

    /// <summary>Attributes compiler-generated state machines and lambdas back to their tool method.</summary>
    private static string? Owner(MethodBase method, IReadOnlySet<string> toolMethods)
    {
        if (toolMethods.Contains(method.Name))
            return method.Name;

        foreach (var candidate in OwningMethod.Matches(method.DeclaringType!.Name + " " + method.Name)
                     .Select(m => m.Groups[1].Value))
            if (toolMethods.Contains(candidate))
                return candidate;

        return null;
    }

    private static bool CallsInto(MethodBase method, Type dependency)
    {
        var il = SafeBody(method);
        if (il is null)
            return false;

        var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : null;

        for (var i = 0; i < il.Length;)
        {
            short code = il[i];
            i++;
            if (code == 0xFE)
            {
                code = (short)(0xFE00 | il[i]);
                i++;
            }

            if (!Opcodes.TryGetValue(code, out var opcode))
                throw new InvalidOperationException(
                    $"Unknown IL opcode 0x{code:X} in {method.DeclaringType?.Name}.{method.Name}; "
                    + "the scanner would silently stop finding calls.");

            if (opcode.OperandType is OperandType.InlineMethod
                && Resolve(method.Module, BitConverter.ToInt32(il, i), typeArgs, methodArgs) is { } member
                && member.DeclaringType == dependency)
                return true;

            i += OperandSize(opcode, il, i);
        }

        return false;
    }

    private static byte[]? SafeBody(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MemberInfo? Resolve(Module module, int token, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return module.ResolveMember(token, typeArgs, methodArgs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int OperandSize(OpCode opcode, byte[] il, int operandStart) => opcode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, operandStart)),
        _ => 4
    };
}
