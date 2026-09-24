using System.Text.Json;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>Resolves imports using only tracked tsconfig and package manifests.</summary>
internal sealed class TypeScriptResolver
{
    private static readonly string[] Extensions = [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];
    private static readonly string[] Assets = [".scss", ".sass", ".less", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".woff", ".woff2"];
    private static readonly string[] PackageConditions = ["default", "import", "require", "types", "node"];
    private readonly IRepository repository;
    private readonly Dictionary<string, TrackedEntry> tracked;
    private readonly Dictionary<string, Config?> configs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> dependencies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> packages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> packageExports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> imports = new(StringComparer.Ordinal);
    private readonly HashSet<string> packageDirectories = new(StringComparer.Ordinal);
    private readonly List<string> failures = [];

    public TypeScriptResolver(IRepository repository)
    {
        this.repository = repository;
        tracked = repository.TrackedEntries.Where(entry => !entry.IsSymbolicLink).ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var path in tracked.Keys.Where(path => Path.GetFileName(path) is "tsconfig.json" or "jsconfig.json"))
        {
            ReadConfig(path, []);
        }

        foreach (var entry in tracked.Values.Where(entry => Path.GetFileName(entry.Path) == "package.json"))
        {
            ReadPackage(entry);
        }
    }

    public IReadOnlyList<string> Failures => failures;

    public string? Resolve(string from, string specifier, out bool external)
    {
        external = false;
        var clean = specifier.Split('?')[0];
        if (IsAsset(specifier, clean) || IsBuiltin(clean))
        {
            external = true;
            return null;
        }

        if (clean.StartsWith("./", StringComparison.Ordinal) || clean.StartsWith("../", StringComparison.Ordinal))
        {
            return Probe(Join(DirectoryOf(from), clean));
        }

        var owner = packageDirectories
            .Where(candidate => candidate.Length == 0 || from.StartsWith(candidate + "/", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Length).FirstOrDefault();
        if (clean.StartsWith('#'))
        {
            return ResolvePackageAlias(owner, clean);
        }

        if (ResolveConfig(from, clean) is { } configured)
        {
            return configured;
        }

        if (ResolveWorkspacePackage(clean) is { } workspace)
        {
            return workspace;
        }

        external = owner is not null && dependencies.TryGetValue(owner, out var declared)
            && declared.Any(name => clean == name || clean.StartsWith(name + "/", StringComparison.Ordinal));
        return null;
    }

    private static bool IsAsset(string specifier, string clean)
        => clean != specifier || clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || Assets.Any(extension => clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsBuiltin(string specifier)
        => specifier.StartsWith("node:", StringComparison.Ordinal)
            || Builtins.Any(name => specifier == name || specifier.StartsWith(name + "/", StringComparison.Ordinal));

    private string? ResolvePackageAlias(string? owner, string specifier)
    {
        if (owner is null || !imports.TryGetValue(owner, out var aliases))
        {
            return null;
        }

        if (aliases.TryGetValue(specifier, out var target))
        {
            return Probe(target);
        }

        foreach (var (pattern, alias) in aliases)
        {
            var star = pattern.IndexOf('*');
            if (star >= 0 && specifier.StartsWith(pattern[..star], StringComparison.Ordinal)
                && specifier.EndsWith(pattern[(star + 1)..], StringComparison.Ordinal))
            {
                var captured = specifier[star..(specifier.Length - (pattern.Length - star - 1))];
                return Probe(alias.Replace("*", captured, StringComparison.Ordinal));
            }
        }

        return null;
    }

    private string? ResolveConfig(string from, string specifier)
    {
        var config = SelectConfig(from);
        if (config is null)
        {
            return null;
        }

        foreach (var (pattern, targets) in config.Paths.OrderByDescending(item => item.Key.Length))
        {
            var star = pattern.IndexOf('*');
            if (star < 0 && pattern != specifier)
            {
                continue;
            }

            if (star >= 0 && (!specifier.StartsWith(pattern[..star], StringComparison.Ordinal)
                || !specifier.EndsWith(pattern[(star + 1)..], StringComparison.Ordinal)))
            {
                continue;
            }

            var captured = star < 0 ? "" : specifier[star..(specifier.Length - (pattern.Length - star - 1))];
            foreach (var target in targets)
            {
                if (Probe(Join(config.BaseUrl, target.Replace("*", captured, StringComparison.Ordinal))) is { } path)
                {
                    return path;
                }
            }
        }

        return config.HasBaseUrl ? Probe(Join(config.BaseUrl, specifier)) : null;
    }

    private string? ResolveWorkspacePackage(string specifier)
    {
        foreach (var (name, root) in packages.OrderByDescending(pair => pair.Key.Length))
        {
            if (specifier != name && !specifier.StartsWith(name + "/", StringComparison.Ordinal))
            {
                continue;
            }

            var subpath = specifier == name ? "." : "./" + specifier[(name.Length + 1)..];
            if (packageExports.TryGetValue(name, out var exports))
            {
                foreach (var (pattern, target) in exports)
                {
                    var star = pattern.IndexOf('*');
                    if (star < 0 && pattern != subpath)
                    {
                        continue;
                    }

                    if (star >= 0 && (!subpath.StartsWith(pattern[..star], StringComparison.Ordinal)
                        || !subpath.EndsWith(pattern[(star + 1)..], StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var captured = star < 0 ? "" : subpath[star..(subpath.Length - (pattern.Length - star - 1))];
                    if (Probe(Join(root, target.Replace("*", captured, StringComparison.Ordinal))) is { } exported)
                    {
                        return exported;
                    }
                }
            }

            if (Probe(Join(root, specifier == name ? "" : specifier[(name.Length + 1)..])) is { } packagePath)
            {
                return packagePath;
            }
        }

        return null;
    }

    private Config? SelectConfig(string file)
    {
        return configs.Values.Where(config => config is not null)
            .Select(config => config!)
            .Where(config => config.Files.Contains(file) || config.Include.Any(prefix => Includes(file, prefix)))
            .OrderByDescending(config => config.Files.Contains(file) ? int.MaxValue
                : config.Include.Where(prefix => Includes(file, prefix)).Max(prefix => prefix.Length))
            .FirstOrDefault();
    }

    private static bool Includes(string file, string prefix)
        => prefix.Length == 0 || file == prefix || file.StartsWith(prefix + "/", StringComparison.Ordinal);

    private Config? ReadConfig(string path, HashSet<string> visiting)
    {
        if (configs.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(path)) { failures.Add($"{path}: cyclic extends"); return null; }
        if (!tracked.TryGetValue(path, out var entry)) { failures.Add($"{path}: extends is not tracked"); return null; }
        var (text, failure) = repository.ReadTrackedText(entry);
        if (text is null) { failures.Add($"{path}: {failure}"); return null; }
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var directory = DirectoryOf(path);
            Config? parent = null;
            if (GetString(root, "extends") is { } extends)
            {
                parent = ReadConfig(Join(directory, extends.EndsWith(".json", StringComparison.Ordinal) ? extends : extends + ".json"), visiting);
            }

            var options = GetObject(root, "compilerOptions");
            var declaredBaseUrl = GetString(options, "baseUrl");
            var baseUrl = declaredBaseUrl is not null ? Join(directory, declaredBaseUrl) : parent?.BaseUrl ?? directory;
            var hasBaseUrl = declaredBaseUrl is not null || parent?.HasBaseUrl == true;
            var paths = parent?.Paths.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (GetObject(options, "paths") is { } pathMap)
            {
                foreach (var property in pathMap.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        paths[property.Name] = property.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString()!).ToArray();
                    }
                }
            }

            var files = ReadStrings(root, "files").Select(value => Join(directory, value)).ToHashSet(StringComparer.Ordinal);
            var include = ReadStrings(root, "include").Select(value => Join(directory, value.Split('*')[0])).ToArray();
            if (include.Length == 0 && files.Count == 0 && !Has(root, "references") && !Has(root, "files"))
            {
                include = [directory];
            }

            if (root.TryGetProperty("references", out var references) && references.ValueKind == JsonValueKind.Array)
            {
                foreach (var reference in references.EnumerateArray())
                {
                    if (GetString(reference, "path") is { } referencePath)
                    {
                        var candidate = Join(directory, referencePath);
                        ReadConfig(candidate.EndsWith(".json", StringComparison.Ordinal) ? candidate : Join(candidate, "tsconfig.json"), visiting);
                    }
                }
            }

            var config = new Config(directory, baseUrl, hasBaseUrl, paths, files, include);
            configs[path] = config;
            return config;
        }
        catch (JsonException ex) { failures.Add($"{path}: {ex.Message}"); return null; }
    }

    private void ReadPackage(TrackedEntry entry)
    {
        var (text, _) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var directory = DirectoryOf(entry.Path);
            packageDirectories.Add(directory);
            dependencies[directory] = new HashSet<string>(StringComparer.Ordinal);
            imports[directory] = new Dictionary<string, string>(StringComparer.Ordinal);
            if (GetString(root, "name") is { } name)
            {
                packages[name] = directory;
                packageExports[name] = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root.TryGetProperty("exports", out var exported))
                {
                    if (PackageTarget(exported) is { } main)
                    {
                        packageExports[name]["."] = main;
                    }
                    else if (exported.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in exported.EnumerateObject())
                        {
                            if (PackageTarget(property.Value) is { } target)
                            {
                                packageExports[name][property.Name] = target;
                            }
                        }
                    }
                }
            }

            foreach (var key in new[] { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (GetObject(root, key) is { } list)
                {
                    foreach (var property in list.EnumerateObject())
                    {
                        dependencies[directory].Add(property.Name);
                    }
                }
            }

            if (GetObject(root, "imports") is { } aliases)
            {
                foreach (var property in aliases.EnumerateObject())
                {
                    if (PackageTarget(property.Value) is { } target)
                    {
                        imports[directory][property.Name] = Join(directory, target);
                    }
                }
            }
        }
        catch (JsonException) { failures.Add($"{entry.Path}: invalid package manifest"); }
    }

    private string? Probe(string path)
    {
        var candidates = new List<string> { path };
        if (path.EndsWith(".js", StringComparison.Ordinal))
        {
            candidates.Add(path[..^3] + ".ts");
        }
        else if (path.EndsWith(".jsx", StringComparison.Ordinal))
        {
            candidates.Add(path[..^4] + ".tsx");
        }
        else if (path.EndsWith(".mjs", StringComparison.Ordinal))
        {
            candidates.Add(path[..^4] + ".mts");
        }
        else if (path.EndsWith(".cjs", StringComparison.Ordinal))
        {
            candidates.Add(path[..^4] + ".cts");
        }

        if (!Extensions.Any(extension => path.EndsWith(extension, StringComparison.Ordinal)))
        {
            foreach (var extension in Extensions) { candidates.Add(path + extension); candidates.Add(Join(path, "index" + extension)); }
        }

        return candidates.FirstOrDefault(candidate => tracked.TryGetValue(candidate, out var entry)
            && TypeScriptFile.IsSource(candidate)
            && repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored));
    }

    private static JsonElement? GetObject(JsonElement? root, string name)
        => root is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child)
            && child.ValueKind == JsonValueKind.Object ? child : null;
    private static string? GetString(JsonElement? root, string name)
        => root is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child)
            && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
    private static bool Has(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _);
    private static string? PackageTarget(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var condition in PackageConditions)
        {
            if (value.TryGetProperty(condition, out var target) && PackageTarget(target) is { } path)
            {
                return path;
            }
        }

        return null;
    }
    private static string[] ReadStrings(JsonElement root, string name)
        => root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray() : [];
    private static string DirectoryOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static string Join(string root, string relative)
    {
        var parts = new List<string>();
        foreach (var part in (root.Length == 0 ? relative : root + "/" + relative).Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else
            {
                parts.Add(part);
            }
        }
        return string.Join('/', parts);
    }
    private sealed record Config(string Directory, string BaseUrl, bool HasBaseUrl, Dictionary<string, string[]> Paths, HashSet<string> Files, string[] Include);
    private static readonly HashSet<string> Builtins =
    [
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console", "constants", "crypto",
        "dgram", "diagnostics_channel", "dns", "domain", "events", "fs", "http", "http2", "https",
        "inspector", "module", "net", "os", "path", "perf_hooks", "process", "punycode", "querystring",
        "readline", "repl", "sqlite", "stream", "string_decoder", "sys", "test", "timers", "tls",
        "trace_events", "tty", "url", "util", "v8", "vm", "wasi", "worker_threads", "zlib",
    ];
}
