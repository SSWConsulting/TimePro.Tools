using System.Text;

namespace SSW.TimePro.Cli.Features.Skills;

public sealed record PrefetchCommand(string Command, string Purpose);

public sealed record SkillDefinition(string Name, int Version, string? FeatureKey);

public sealed record SkillContentModel(
    string Name,
    int Version,
    string Description,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<PrefetchCommand> Prefetch,
    string Body);

public static class SkillRenderer
{
    public static string RelativePath(string skillName) =>
        $"skills/{skillName}/SKILL.md";

    public static string Render(SkillContentModel model)
    {
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"name: {model.Name}");
        sb.AppendLine($"description: {model.Description}");
        if (model.AllowedTools.Count > 0)
            sb.AppendLine($"allowed-tools: {string.Join(", ", model.AllowedTools)}");
        sb.AppendLine("---");
        sb.AppendLine();

        if (model.Prefetch.Count > 0)
        {
            sb.AppendLine("## Run these first");
            sb.AppendLine("Run these read-only commands before you start, and read their output:");
            sb.AppendLine();
            sb.AppendLine("```bash");
            foreach (var pf in model.Prefetch)
                sb.AppendLine($"{pf.Command}    # {pf.Purpose}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.Append(model.Body);
        return sb.ToString();
    }
}
