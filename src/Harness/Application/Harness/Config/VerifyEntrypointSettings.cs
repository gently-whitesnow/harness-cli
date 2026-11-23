namespace Harness.Config;

internal sealed record VerifyEntrypointSettings(string Path)
{
    public static VerifyEntrypointSettings Default { get; } = new("verify.sh");
}
