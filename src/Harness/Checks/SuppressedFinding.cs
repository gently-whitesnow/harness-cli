using Harness.Config;

namespace Harness.Checks;

internal sealed record SuppressedFinding(Finding Finding, Suppression Suppression);
