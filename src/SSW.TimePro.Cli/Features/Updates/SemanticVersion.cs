namespace SSW.TimePro.Cli.Features.Updates;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        var metadataIndex = normalized.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
            normalized = normalized[..metadataIndex];

        var parts = normalized.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 4)
            return false;

        if (!int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor))
            return false;

        var patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            return false;

        if (parts.Length == 4
            && (!int.TryParse(parts[3], out var revision) || revision != 0))
            return false;

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public static bool IsDevelopmentVersion(string? value)
    {
        if (!TryParse(value, out var version))
            return false;

        return version.Patch == 0;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
            return minor;

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
