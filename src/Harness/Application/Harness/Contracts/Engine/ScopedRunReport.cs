namespace Harness.Contracts.Engine;

internal sealed record ScopedRunReport(string Scope, string ConfigPath, RunReport Report);
