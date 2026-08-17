using System.Text;
using Harness.Config;

namespace Harness.Commits;

internal static class CommitTemplate
{
    public static string Render(CommitSettings settings)
    {
        var description = settings.Language == CommitLanguage.Russian
            ? "<краткое описание в инфинитиве>"
            : "<short imperative description>";
        var context = settings.Language == CommitLanguage.Russian
            ? "<почему изменение понадобилось>"
            : "<why this change is needed>";
        var decision = settings.Language == CommitLanguage.Russian
            ? "<принятое решение и существенная причина выбора>"
            : "<the chosen solution and its important rationale>";
        var boundaries = settings.Language == CommitLanguage.Russian
            ? "<что осознанно не входит в изменение>"
            : "<what is intentionally outside this change>";
        var consequences = settings.Language == CommitLanguage.Russian
            ? "<ограничения, риски, миграция или способ отката>"
            : "<constraints, risks, migration, or rollback>";
        var optionalTrailers = settings.Language == CommitLanguage.Russian
            ? "# Необязательные Git trailers; удалите неиспользуемые строки."
            : "# Optional Git trailers; remove unused lines.";
        var breakingChange = settings.Language == CommitLanguage.Russian
            ? "# BREAKING CHANGE: опишите несовместимость и способ миграции"
            : "# BREAKING CHANGE: describe the incompatibility and migration";

        var text = new StringBuilder();
        text.Append("<type>(<scope>): ").Append(description).Append("\n\n")
            .Append(settings.ContextHeading).Append('\n').Append(context).Append("\n\n")
            .Append(settings.DecisionHeading).Append("\n- ").Append(decision).Append("\n\n")
            .Append(settings.BoundariesHeading).Append("\n- ").Append(boundaries).Append("\n\n")
            .Append(settings.ConsequencesHeading).Append("\n- ").Append(consequences).Append("\n\n")
            .Append(optionalTrailers).Append('\n')
            .Append("# Refs: IDP-123\n")
            .Append(breakingChange).Append('\n');
        return text.ToString();
    }
}
