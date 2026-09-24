namespace Harness.Config;

internal sealed record FunctionSettings(int OwnLines)
{
    public static FunctionSettings Default { get; } = new(80);
}
