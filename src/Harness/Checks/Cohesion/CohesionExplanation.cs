namespace Harness.Checks.Cohesion;

/// <summary>Long-form content for `harness explain cohesion.csharp`.</summary>
internal static class CohesionExplanation
{
    public const string Text =
        """
        Rationale
          Cohesion is the other half of the same question coupling asks. A type is cohesive
          when its members are about one thing; when it holds two sets of state and
          behaviour that never meet, it is two types that happen to share a file and a name.
          That specific shape can be measured without understanding the code: it is a
          question about which members mention which.

        Discovery
          Every Git-tracked `.cs` file is read, with the same exclusions the other C# checks
          use. For each declared type, its methods, fields and properties are collected.

        Formula
          independent member groups   members of one type are placed in the same group when
                                      one names the other, directly or through a chain of
                                      members between them. The count is the number of
                                      groups that hold both state and behaviour.

          A field or a property that stores its value is state; a method, and a property
          with an expression body, are behaviour, because they compute a result rather than
          hold one. Constructors are left out: a constructor touches everything a type holds
          by definition, so counting it would join every group to every other one and hide
          the thing this measurement is looking for. A group of nothing but state says a
          field is mentioned by no method, and a group of nothing but behaviour says a helper
          touches nothing the type holds; both are different claims from this one, and the
          compiler and the analyzers already make them. Overloads count as one member,
          because what they share is the state they touch.

        Reading the number
          One group is the ordinary case. Two or more means the type carries independent
          concerns, and splitting it usually costs nothing but the rename. It is not a
          defect on its own: a type may hold a second group deliberately — a cache, a
          counter, a lazily built index — and a well-named type with two groups is better
          than two badly named types.

        Limits
          The reader is lexical and has no symbol table. A member is matched by name, so a
          local variable, a parameter or a member of another type that happens to share a
          name joins groups that are not really joined — the measurement understates the
          split rather than inventing one. Members reached only through an interface, a
          delegate, a container or reflection are not seen at all. Nested types are measured
          separately from the type that contains them.

        Possible damage
          Splitting a type to satisfy this count adds a file, a name and an indirection, and
          it can separate two things that are read together every time they are read at all.
          Nothing here is blocking, and a finding that does not survive a look at the type
          costs only the reading.

        Comparison points
          `settings.cohesion.csharp.minimumMembers` is how many members a type must declare
          before it is measured at all — below it the count says nothing. `groups` is the
          comparison point the number is reported against.

        Remediation
          Open the type and read the groups. If each has its own state and its own reason to
          change, give each its own name. If they share a lifetime, an invariant, or a
          caller that always needs both, leave them where they are.
        """;
}
