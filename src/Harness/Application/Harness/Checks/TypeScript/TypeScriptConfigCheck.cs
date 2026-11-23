using System.Text.Json;
using Harness.Languages;
using Harness.Repository;

namespace Harness.Checks.TypeScript;

/// <summary>
/// Audits tracked TypeScript configurations from the Git index so TypeScript sources cannot
/// live outside a tsconfig.json or silently disable strict typechecking.
/// </summary>
internal sealed class TypeScriptConfigCheck : IRepositoryCheck
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly EvidenceFile[] SourceFiles =
    [
        new("*.ts"),
        new("*.tsx"),
        new("*.mts"),
        new("*.cts"),
    ];

    private static readonly EvidenceFile[] ConfigFiles =
    [
        new("*.json"),
    ];

    public string Id => Language.TypeScript.Qualify(Group);

    public string Group => "tsconfig";

    public string Applicability => Language.TypeScript.Key;

    public IReadOnlyList<EvidenceFile> Evidence => [.. SourceFiles, .. ConfigFiles];

    public string Summary => "TypeScript sources are covered by strict tsconfig.json";

    public string Explanation => TypeScriptConfigExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var sources = SourceFiles
            .SelectMany(context.Tracked)
            .Where(entry => !IsDeclarationFile(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        if (sources.Count == 0)
        {
            return CheckEvaluation.NotApplicable("no TypeScript sources in the repository");
        }

        var configs = ConfigFiles
            .SelectMany(context.Tracked)
            .Where(entry => IsTsConfigFile(FileNameOf(entry.Path)))
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var findings = new List<Finding>();
        CheckCoverage(sources, configs, findings);

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var config in configs)
        {
            var (text, failure) = context.Repository.ReadTrackedText(config);
            if (text is null)
            {
                return CheckEvaluation.Incomplete(failure ?? $"Could not read '{config.Path}'.");
            }

            texts[config.Path] = text;
        }

        foreach (var config in configs)
        {
            InspectConfig(config.Path, texts, findings);
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0
                ? $"{sources.Count} TypeScript source(s) covered by {configs.Count} strict tsconfig file(s)"
                : null);
    }

    private static bool IsTsConfigFile(string fileName)
        => fileName.Equals("tsconfig.json", StringComparison.Ordinal)
            || (fileName.StartsWith("tsconfig.", StringComparison.Ordinal)
                && fileName.EndsWith(".json", StringComparison.Ordinal));

    private static bool IsDeclarationFile(string path)
        => path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase);

    private static void CheckCoverage(
        List<TrackedEntry> sources,
        List<TrackedEntry> configs,
        List<Finding> findings)
    {
        var configDirs = configs
            .Where(entry => FileNameOf(entry.Path).Equals("tsconfig.json", StringComparison.Ordinal))
            .Select(entry => DirectoryOf(entry.Path))
            .ToHashSet(StringComparer.Ordinal);

        var reportedDirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (HasCoveringConfig(source.Path, configDirs))
            {
                continue;
            }

            var directory = DirectoryOf(source.Path);
            if (reportedDirs.Add(directory))
            {
                findings.Add(new Finding(
                    FindingSeverity.Blocking,
                    source.Path,
                    "no tracked tsconfig.json covers this TypeScript source (add tsconfig.json with compilerOptions.strict: true)"));
            }
        }
    }

    private static bool HasCoveringConfig(string path, HashSet<string> configDirs)
    {
        var current = DirectoryOf(path);
        while (true)
        {
            if (configDirs.Contains(current))
            {
                return true;
            }

            if (current.Length == 0)
            {
                return false;
            }

            var slash = current.LastIndexOf('/');
            current = slash < 0 ? string.Empty : current[..slash];
        }
    }

    private static void InspectConfig(
        string path,
        Dictionary<string, string> texts,
        List<Finding> findings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(texts[path], JsonOptions);
        }
        catch (JsonException error)
        {
            findings.Add(new Finding(FindingSeverity.Blocking, path, $"is not valid JSON: {error.Message}"));
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new Finding(FindingSeverity.Blocking, path, "must be a JSON object"));
                return;
            }

            if (IsSolutionReferenceConfig(document.RootElement))
            {
                return;
            }
        }

        var (strict, noImplicitAny, strictNullChecks, extendsExternal) =
            ResolveEffectiveFlags(path, texts, new HashSet<string>(StringComparer.Ordinal));

        if (strict == false)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "sets compilerOptions.strict to false; enable strict mode"));
        }
        else if (strict is null && !extendsExternal)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "does not enable compilerOptions.strict: true"));
        }

        if (noImplicitAny == false)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "sets compilerOptions.noImplicitAny to false; do not weaken strict mode"));
        }

        if (strictNullChecks == false)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "sets compilerOptions.strictNullChecks to false; do not weaken strict mode"));
        }
    }

    private static bool IsSolutionReferenceConfig(JsonElement root)
    {
        var hasOptions = root.TryGetProperty("compilerOptions", out var options)
            && options.ValueKind == JsonValueKind.Object
            && options.EnumerateObject().Any();
        if (hasOptions || root.TryGetProperty("include", out _))
        {
            return false;
        }

        var hasEmptyFiles = root.TryGetProperty("files", out var files)
            && files.ValueKind == JsonValueKind.Array
            && files.GetArrayLength() == 0;
        var hasReferences = root.TryGetProperty("references", out var refs)
            && refs.ValueKind == JsonValueKind.Array
            && refs.GetArrayLength() > 0;
        return hasEmptyFiles && hasReferences;
    }

    private static (bool? Strict, bool? NoImplicitAny, bool? StrictNullChecks, bool ExtendsExternal)
        ResolveEffectiveFlags(string path, Dictionary<string, string> texts, HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out var text))
        {
            return (null, null, null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(text, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null, false);
            }

            bool? strict = null;
            bool? noImplicitAny = null;
            bool? strictNullChecks = null;
            var extendsExternal = false;

            if (document.RootElement.TryGetProperty("extends", out var extendsElement)
                && extendsElement.ValueKind == JsonValueKind.String)
            {
                var target = extendsElement.GetString();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (target.StartsWith("./", StringComparison.Ordinal) || target.StartsWith("../", StringComparison.Ordinal))
                    {
                        var resolved = ResolveRelativePath(DirectoryOf(path), target, texts);
                        if (resolved is not null)
                        {
                            (strict, noImplicitAny, strictNullChecks, extendsExternal) =
                                ResolveEffectiveFlags(resolved, texts, visited);
                        }
                    }
                    else
                    {
                        extendsExternal = true;
                    }
                }
            }

            if (document.RootElement.TryGetProperty("compilerOptions", out var options)
                && options.ValueKind == JsonValueKind.Object)
            {
                strict = ReadBool(options, "strict") ?? strict;
                noImplicitAny = ReadBool(options, "noImplicitAny") ?? noImplicitAny;
                strictNullChecks = ReadBool(options, "strictNullChecks") ?? strictNullChecks;
            }

            return (strict, noImplicitAny, strictNullChecks, extendsExternal);
        }
        catch (JsonException)
        {
            return (null, null, null, false);
        }
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? ResolveRelativePath(string directory, string target, Dictionary<string, string> texts)
    {
        var segments = directory.Length == 0
            ? new List<string>()
            : [.. directory.Split('/')];
        foreach (var part in target.Split('/'))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(part);
            }
        }

        var candidate = string.Join('/', segments);
        if (texts.ContainsKey(candidate))
        {
            return candidate;
        }

        var withJson = candidate + ".json";
        return texts.ContainsKey(withJson) ? withJson : null;
    }

    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}
