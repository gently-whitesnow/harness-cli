namespace Harness.Config;

internal sealed record FunctionSettings(int OwnTokens)
{
    public static FunctionSettings Default { get; } = new(700);
}
