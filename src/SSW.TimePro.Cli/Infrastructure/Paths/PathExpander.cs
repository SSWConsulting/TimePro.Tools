namespace SSW.TimePro.Cli.Infrastructure.Paths;

/// <summary>
/// Expands portable path shorthand before paths are resolved or compared.
/// </summary>
public static class PathExpander
{
    /// <summary>
    /// Expands a leading current-user home segment (<c>~</c>, <c>~/</c>, or <c>~\</c>).
    /// Embedded tildes and named-user forms such as <c>~someone</c> are left unchanged.
    /// </summary>
    public static string ExpandHomeDirectory(string path)
    {
        var trimmedPath = path.Trim();
        if (trimmedPath != "~"
            && !trimmedPath.StartsWith("~/", StringComparison.Ordinal)
            && !trimmedPath.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return trimmedPath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            return trimmedPath;

        if (trimmedPath.Length == 1)
            return userProfile;

        var relativePath = trimmedPath[2..]
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(userProfile, relativePath);
    }
}
