namespace Harness.Commits;

internal enum CommitLanguage
{
    English,
    Russian,
}

internal sealed record CommitSettings(CommitLanguage Language, bool RequireSetup)
{
    public static CommitSettings Default { get; } = new(CommitLanguage.English, RequireSetup: false);

    public string Code => Language == CommitLanguage.Russian ? "ru" : "en";

    public string ContextHeading => Language == CommitLanguage.Russian ? "Контекст:" : "Context:";

    public string DecisionHeading => Language == CommitLanguage.Russian ? "Решение:" : "Decision:";

    public string BoundariesHeading => Language == CommitLanguage.Russian ? "Границы:" : "Boundaries:";

    public string ConsequencesHeading => Language == CommitLanguage.Russian ? "Последствия:" : "Consequences:";
}
