namespace Harness.Config;

/// <summary>
/// Answers the two questions a path-scoped check asks about one file: is it analyzed at
/// all, and by which numbers. The global settings are the starting point; the settings of
/// every covering override land on top in declaration order, and one covering off-entry
/// excludes the file regardless of order.
/// </summary>
internal static class OverrideResolution
{
    public static string EverythingExcluded { get; } =
        $"every analyzable file is excluded for this check by 'overrides' in {HarnessConfig.FileName}";

    public static bool Disables(HarnessConfig? config, string checkId, string path)
        => config is not null
            && config.Overrides.Any(entry => entry.Off && entry.Covers(checkId, path));

    public static CommentSettings CommentsFor(HarnessConfig? config, string checkId, string path)
        => Resolve(config, checkId, path, config?.Settings.Comments ?? CommentSettings.Default,
            (settings, name, value) => settings.With(name, value));

    public static MaintainabilitySettings MaintainabilityFor(HarnessConfig? config, string checkId, string path)
        => Resolve(config, checkId, path, config?.Settings.Maintainability ?? MaintainabilitySettings.Default,
            (settings, name, value) => settings.With(name, value));

    public static CohesionSettings CohesionFor(HarnessConfig? config, string checkId, string path)
        => Resolve(config, checkId, path, config?.Settings.Cohesion ?? CohesionSettings.Default,
            (settings, name, value) => settings.With(name, value));

    private static TSettings Resolve<TSettings>(
        HarnessConfig? config,
        string checkId,
        string path,
        TSettings settings,
        Func<TSettings, string, int, TSettings> apply)
    {
        if (config is null)
        {
            return settings;
        }

        foreach (var entry in config.Overrides)
        {
            if (entry.Off || !entry.Covers(checkId, path))
            {
                continue;
            }

            foreach (var (name, value) in entry.Settings)
            {
                settings = apply(settings, name, value);
            }
        }

        return settings;
    }
}
