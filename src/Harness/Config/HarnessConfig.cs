using System.Text.Json;
using Harness.Checks;
using Harness.Git;

namespace Harness.Config;

/// <summary>How a repository answered one question of the harness frame.</summary>
internal enum DeclarationKind
{
    /// <summary>Declared with an address the harness can hold against Git evidence.</summary>
    Proven,

    /// <summary>Declared present with a justification, but without an address to verify.</summary>
    Asserted,

    /// <summary>Declared absent with a justification.</summary>
    Absent,

    /// <summary>Declared to be a question this repository is not answerable for.</summary>
    NotApplicable,
}

/// <param name="Paths">Repository-relative addresses that prove the declaration; empty unless proven.</param>
/// <param name="Reason">Why, in the repository's own words. Required for every form but <see cref="DeclarationKind.Proven"/>.</param>
internal sealed record Declaration(
    string Key,
    DeclarationKind Kind,
    IReadOnlyList<string> Paths,
    string? Reason);

/// <summary>How strictly the run treats one check, over and above what the check concluded.</summary>
internal enum CheckPolicy
{
    /// <summary>The check's own severity stands.</summary>
    Default,

    /// <summary>A readiness gap becomes a violation: the repository has committed to this.</summary>
    Required,

    /// <summary>Violations are reported without failing the run.</summary>
    Advisory,

    /// <summary>The check does not run at all.</summary>
    Off,
}

/// <param name="Check">Check or group identifier the exception applies to.</param>
/// <param name="Location">Repository-relative path, or directory prefix, the exception covers.</param>
/// <param name="Reason">Why this is accepted. Never optional: an unexplained exception is not one.</param>
internal sealed record Suppression(string Check, string Location, string Reason);

/// <summary>
/// The repository's own statement of the harness frame: what quality machinery it owns and
/// where the proof of that lives, how strictly each check is treated, and which findings it
/// has consciously accepted and why.
/// </summary>
/// <remarks>
/// The config declares policy and answers; it never supplies facts. Every address it gives
/// is checked against Git-tracked evidence, and a declaration that contradicts what Git
/// shows is a violation rather than an override. That division is what keeps one file from
/// becoming a way to declare a repository green.
/// </remarks>
internal sealed class HarnessConfig
{
    public const string FileName = ".harness.json";

    private static readonly string[] TopLevelKeys = ["version", "declarations", "policy", "suppress"];

    /// <summary>The only schema version this harness reads.</summary>
    private const int SupportedVersion = 1;

    private HarnessConfig(
        IReadOnlyDictionary<string, Declaration> declarations,
        IReadOnlyDictionary<string, CheckPolicy> policy,
        IReadOnlyList<Suppression> suppressions)
    {
        Declarations = declarations;
        Policy = policy;
        Suppressions = suppressions;
    }

    /// <summary>Answers keyed by declaration key, without the `declaration.` prefix.</summary>
    public IReadOnlyDictionary<string, Declaration> Declarations { get; }

    /// <summary>Policy keyed by check or group identifier.</summary>
    public IReadOnlyDictionary<string, CheckPolicy> Policy { get; }

    public IReadOnlyList<Suppression> Suppressions { get; }

    public Declaration? Declared(string key)
        => Declarations.TryGetValue(key, out var declaration) ? declaration : null;

    /// <summary>
    /// Policy for one check. A check identifier outranks its group, so a repository can set
    /// a group-wide policy and still say something different about one member.
    /// </summary>
    public CheckPolicy PolicyFor(string checkId, string group)
    {
        if (Policy.TryGetValue(checkId, out var byId))
        {
            return byId;
        }

        return Policy.TryGetValue(group, out var byGroup) ? byGroup : CheckPolicy.Default;
    }

    /// <summary>
    /// Reads and fully validates the tracked config. An untracked config does not exist for
    /// the harness, the same as any untracked file: what verifies a repository has to be
    /// part of it. Every failure names what to fix rather than degrading to a default,
    /// because a frame that silently assumes an answer is not a frame.
    /// </summary>
    public static (HarnessConfig? Config, string? Failure) Load(
        GitRepository repository,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        var entry = repository.TrackedEntries.FirstOrDefault(candidate => candidate.Path == FileName);
        if (entry is null)
        {
            return (null, $"'{FileName}' is not tracked in this repository, so nothing about the harness frame "
                + $"can be established.{Environment.NewLine}{Template}");
        }

        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure!);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException exception)
        {
            return (null, $"'{FileName}' is not readable as JSON ({exception.Message}).");
        }

        using (document)
        {
            return Read(document.RootElement, checks);
        }
    }

    private static (HarnessConfig? Config, string? Failure) Read(
        JsonElement root,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("the document is not a JSON object");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!TopLevelKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                return Invalid($"'{property.Name}' is not a key this harness reads "
                    + $"(expected {string.Join(", ", TopLevelKeys)})");
            }
        }

        if (root.TryGetProperty("version", out var version))
        {
            if (version.ValueKind != JsonValueKind.Number || version.GetInt32() != SupportedVersion)
            {
                return Invalid($"'version' must be {SupportedVersion}");
            }
        }

        var (declarations, declarationFailure) = ReadDeclarations(root, DeclarationKeys(checks));
        if (declarations is null)
        {
            return (null, declarationFailure);
        }

        var selectors = Selectors(checks);
        var (policy, policyFailure) = ReadPolicy(root, selectors);
        if (policy is null)
        {
            return (null, policyFailure);
        }

        var (suppressions, suppressionFailure) = ReadSuppressions(root, selectors);
        return suppressions is null
            ? (null, suppressionFailure)
            : (new HarnessConfig(declarations, policy, suppressions), null);
    }

    private static (Dictionary<string, Declaration>? Declarations, string? Failure) ReadDeclarations(
        JsonElement root,
        IReadOnlyList<string> knownKeys)
    {
        var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        if (!root.TryGetProperty("declarations", out var declared))
        {
            return (declarations, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure("'declarations' must be an object"));
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!knownKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, Failure($"'declarations.{property.Name}' is not a question this harness asks "
                    + $"(expected {string.Join(", ", knownKeys)})"));
            }

            var (declaration, failure) = ReadDeclaration(property.Name, property.Value);
            if (declaration is null)
            {
                return (null, failure);
            }

            declarations[property.Name] = declaration;
        }

        return (declarations, null);
    }

    /// <summary>
    /// One answer. The four forms are mutually exclusive on purpose: a repository that
    /// gives an address and also claims absence has not answered, it has contradicted
    /// itself, and the harness will not choose which half to believe.
    /// </summary>
    private static (Declaration? Declaration, string? Failure) ReadDeclaration(string key, JsonElement value)
    {
        var at = $"declarations.{key}";
        if (value.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure($"'{at}' must be an object"));
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("paths" or "present" or "applicable" or "reason"))
            {
                return (null, Failure($"'{at}.{property.Name}' is not a key this harness reads "
                    + "(expected paths, present, applicable, reason)"));
            }
        }

        var reason = ReadString(value, "reason");
        var hasPaths = value.TryGetProperty("paths", out var paths);
        var hasPresent = value.TryGetProperty("present", out var present);

        if (value.TryGetProperty("applicable", out var applicable)
            && applicable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, Failure($"'{at}.applicable' must be true or false"));
        }

        var notApplicable = applicable.ValueKind == JsonValueKind.False;

        if (notApplicable)
        {
            if (hasPaths || hasPresent)
            {
                return (null, Failure($"'{at}' declares the question not applicable and also answers it"));
            }

            return string.IsNullOrWhiteSpace(reason)
                ? (null, Failure($"'{at}' needs a non-empty 'reason' saying why the question does not apply"))
                : (new Declaration(key, DeclarationKind.NotApplicable, [], reason), null);
        }

        if (hasPaths && hasPresent)
        {
            return (null, Failure($"'{at}' gives both 'paths' and 'present'; an address is already an answer"));
        }

        if (hasPaths)
        {
            var (addresses, failure) = ReadPaths(at, paths);
            return addresses is null
                ? (null, failure)
                : (new Declaration(key, DeclarationKind.Proven, addresses, reason), null);
        }

        if (!hasPresent || present.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, Failure($"'{at}' must give 'paths', or 'present' as true or false, "
                + "or 'applicable' as false"));
        }

        // An address needs no words; a claim without one does. This is where the harness
        // asks for the sentence a reviewer would have asked for anyway.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (null, Failure($"'{at}' answers without an address, so it needs a non-empty 'reason'"));
        }

        return (
            new Declaration(
                key,
                present.ValueKind == JsonValueKind.True ? DeclarationKind.Asserted : DeclarationKind.Absent,
                [],
                reason),
            null);
    }

    private static (List<string>? Paths, string? Failure) ReadPaths(string at, JsonElement paths)
    {
        if (paths.ValueKind != JsonValueKind.Array)
        {
            return (null, Failure($"'{at}.paths' must be an array of repository-relative paths"));
        }

        var addresses = new List<string>();
        foreach (var path in paths.EnumerateArray())
        {
            var value = path.ValueKind == JsonValueKind.String ? path.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, Failure($"'{at}.paths' must hold non-empty strings"));
            }

            addresses.Add(value.Trim().TrimEnd('/'));
        }

        return addresses.Count == 0
            ? (null, Failure($"'{at}.paths' is empty; declare 'present' instead of an empty address list"))
            : (addresses, null);
    }

    private static (Dictionary<string, CheckPolicy>? Policy, string? Failure) ReadPolicy(
        JsonElement root,
        IReadOnlyList<string> selectors)
    {
        var policy = new Dictionary<string, CheckPolicy>(StringComparer.Ordinal);
        if (!root.TryGetProperty("policy", out var declared))
        {
            return (policy, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure("'policy' must be an object"));
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!selectors.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, Failure($"'policy.{property.Name}' is not a check or group this harness ships"));
            }

            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            var parsed = value switch
            {
                "required" => CheckPolicy.Required,
                "advisory" => CheckPolicy.Advisory,
                "off" => CheckPolicy.Off,
                _ => CheckPolicy.Default,
            };

            if (parsed == CheckPolicy.Default)
            {
                return (null, Failure($"'policy.{property.Name}' must be required, advisory or off"));
            }

            policy[property.Name] = parsed;
        }

        return (policy, null);
    }

    private static (List<Suppression>? Suppressions, string? Failure) ReadSuppressions(
        JsonElement root,
        IReadOnlyList<string> selectors)
    {
        var suppressions = new List<Suppression>();
        if (!root.TryGetProperty("suppress", out var declared))
        {
            return (suppressions, null);
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            return (null, Failure("'suppress' must be an array"));
        }

        var index = 0;
        foreach (var element in declared.EnumerateArray())
        {
            var at = $"suppress[{index++}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                return (null, Failure($"'{at}' must be an object"));
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is not ("check" or "location" or "reason"))
                {
                    return (null, Failure($"'{at}.{property.Name}' is not a key this harness reads "
                        + "(expected check, location, reason)"));
                }
            }

            var check = ReadString(element, "check");
            var location = ReadString(element, "location");
            var reason = ReadString(element, "reason");

            if (string.IsNullOrWhiteSpace(check) || !selectors.Contains(check, StringComparer.Ordinal))
            {
                return (null, Failure($"'{at}.check' must name a check or group this harness ships"));
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                return (null, Failure($"'{at}.location' must be a non-empty repository-relative path"));
            }

            // The whole point of a named exception is the sentence that justifies it. A
            // suppression without one is an invisible exception, which is the thing this
            // mechanism exists to prevent.
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (null, Failure($"'{at}.reason' must say why this finding is accepted"));
            }

            suppressions.Add(new Suppression(check.Trim(), location.Trim().TrimEnd('/'), reason.Trim()));
        }

        return (suppressions, null);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Declaration keys are exactly the questions the shipped declaration checks ask.</summary>
    private static List<string> DeclarationKeys(IReadOnlyList<IRepositoryCheck> checks)
        => checks
            .Where(check => check.Group == DeclarationGroup)
            .Select(check => check.Id[(DeclarationGroup.Length + 1)..])
            .ToList();

    public const string DeclarationGroup = "declaration";

    private static List<string> Selectors(IReadOnlyList<IRepositoryCheck> checks)
        => checks.Select(check => check.Id)
            .Concat(checks.Select(check => check.Group))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static (HarnessConfig?, string?) Invalid(string detail) => (null, Failure(detail));

    private static string Failure(string detail) => $"'{FileName}' is not a valid harness frame: {detail}.";

    /// <summary>
    /// The smallest config that answers everything, shown whenever there is none. A reader
    /// who has never seen this file should not have to find documentation to start.
    /// </summary>
    public static string Template =>
        """
        A minimal .harness.json, committed at the repository root:

          {
            "version": 1,
            "declarations": {
              "tests.unit": { "paths": ["tests/Unit"] },
              "tests.integration": { "present": false, "reason": "no external dependencies yet" },
              "tests.architecture": { "present": false, "reason": "planned" },
              "format": { "paths": [".editorconfig"] },
              "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
              "build": { "paths": ["Repository.sln"] },
              "typecheck": { "applicable": false, "reason": "no web stack" }
            }
          }

        Run `harness explain <check-id>` for what one declaration means and how it is verified.
        """;
}
