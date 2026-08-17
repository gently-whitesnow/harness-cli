using Harness.Structure;

namespace Harness.Languages.CSharp;

internal readonly record struct NameOccurrence(string Name, int Line, EvidenceGrade Grade);
