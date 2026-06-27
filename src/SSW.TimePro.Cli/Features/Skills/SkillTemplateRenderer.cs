using System.Reflection;

namespace SSW.TimePro.Cli.Features.Skills;

public static class SkillTemplateRenderer
{
    public static string Render(string templateFileName) =>
        Render(templateFileName, new Dictionary<string, string>());

    public static string Render(string templateFileName, IReadOnlyDictionary<string, string> values)
    {
        var template = LoadTemplate(templateFileName);

        foreach (var (key, value) in values)
        {
            template = template.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        if (template.Contains("{{", StringComparison.Ordinal))
            throw new InvalidOperationException($"Skill template '{templateFileName}' has unreplaced placeholders.");

        return template.TrimEnd() + "\n";
    }

    private static string LoadTemplate(string templateFileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".{templateFileName}", StringComparison.Ordinal));

        if (resourceName is null)
        {
            var available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException($"Skill template '{templateFileName}' was not embedded. Available resources: {available}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Skill template '{templateFileName}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
