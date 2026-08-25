namespace Harness.Checks;

internal sealed record Finding(FindingSeverity Severity, string Location, string Message);
