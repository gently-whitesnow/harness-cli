using System.Text.RegularExpressions;

namespace Harness.Tests;

/// <summary>
/// The public site under site/ mirrors the contract the binary runs (ADR-0047): the check
/// registry it lists and the release it names cannot drift from the shipped binary.
/// </summary>
public sealed class SiteContractTests
{
    private static string SitePath(string name) => Path.Combine(Release.RepositoryRoot(), "site", name);

    [Fact]
    public void Site_lists_exactly_the_shipped_checks()
    {
        using var repository = Fixtures.Compliant();

        var help = HarnessCli.Run(repository.Path, "help");
        var shipped = Regex.Matches(help.Output, @"^\s{2}(\S+)\s+group\s", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();
        var listed = Regex.Matches(File.ReadAllText(SitePath("app.js")), @"\{ id: '([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(shipped);
        Assert.Equal(shipped.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("app.js")]
    public void Site_names_the_current_release(string file)
    {
        var text = File.ReadAllText(SitePath(file));

        Assert.Contains(Release.Current, text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"harness (?!" + Regex.Escape(Release.Current) + @")\d+\.\d+\.\d+", text);
    }
}
