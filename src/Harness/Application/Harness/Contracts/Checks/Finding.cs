namespace Harness.Contracts.Checks;

internal sealed record Finding(FindingSeverity Severity, string Location, string Message);
