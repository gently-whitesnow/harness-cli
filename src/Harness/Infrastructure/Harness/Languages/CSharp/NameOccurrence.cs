using Harness.Structure;

namespace Harness.Infrastructure.Languages.CSharp;

internal readonly record struct NameOccurrence(string Name, int Line, EvidenceGrade Grade);
