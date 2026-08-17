namespace Harness.Tests;

public sealed class CentralPackagesTests
{
    [Fact]
    public void Package_reference_without_central_file_fails()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Project("<PackageReference Include=\"Example\" Version=\"1.0.0\" />"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("no tracked Directory.Packages.props"), run.Output);
    }

    [Fact]
    public void Scoped_central_file_covers_versionless_reference()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/Directory.Packages.props", Packages("<PackageVersion Include=\"Example\" Version=\"1.0.0\" />"))
            .WriteFile("apps/api/App/App.csproj", Project("<PackageReference Include=\"Example\" />"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Local_version_and_missing_central_version_fail()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Packages.props", Packages(string.Empty))
            .WriteFile("src/App/App.csproj", Project("<PackageReference Include=\"Example\" Version=\"1.0.0\" />"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("keeps a local version"), run.Output);
        Assert.True(run.OutputContains("has no PackageVersion for 'Example'"), run.Output);
    }

    [Fact]
    public void Conflicting_central_versions_fail()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Packages.props", Packages(
                "<PackageVersion Include=\"Example\" Version=\"1.0.0\" />\n    <PackageVersion Update=\"Example\" Version=\"2.0.0\" />"))
            .WriteFile("src/App/App.csproj", Project("<PackageReference Include=\"Example\" />"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("conflicting central versions"), run.Output);
    }

    private static string Project(string reference)
        => $"""
           <Project Sdk="Microsoft.NET.Sdk">
             <ItemGroup>
               {reference}
             </ItemGroup>
           </Project>
           """;

    private static string Packages(string versions)
        => $"""
           <Project>
             <PropertyGroup>
               <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
             </PropertyGroup>
             <ItemGroup>
               {versions}
             </ItemGroup>
           </Project>
           """;
}
