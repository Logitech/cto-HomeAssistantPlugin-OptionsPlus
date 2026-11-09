using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

namespace Loupedeck.HomeAssistantPlugin.Tests.Build;

public sealed class BuildIntegrationTests : IDisposable
{
    private readonly String _temporaryDirectory = Path.Combine(Path.GetTempPath(), "HomeAssistant build tests", Guid.NewGuid().ToString("N"));
    private readonly String _repositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task MissingApi_ReportsAnActionableError()
    {
        var result = await this.RunMsBuild("-t:ValidatePluginApi", $"-p:PluginApiDir={this._temporaryDirectory}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PluginApi.dll was not found", result.Output);
        Assert.Contains("UseToolPluginApi=true", result.Output);
    }

    [Fact]
    public async Task ApiOverride_WithSpacesAndNoTrailingSeparator_IsResolved()
    {
        Directory.CreateDirectory(this._temporaryDirectory);
        File.Copy(typeof(Plugin).Assembly.Location, Path.Combine(this._temporaryDirectory, "PluginApi.dll"));

        var result = await this.RunMsBuild("-t:ValidatePluginApi", $"-p:PluginApiDir={this._temporaryDirectory}");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task DeploymentAndClean_RespectOptOutAndLinkOwnership()
    {
        var pluginDirectory = Path.Combine(this._temporaryDirectory, "Application Support", "Plugins");
        var link = Path.Combine(pluginDirectory, "HomeAssistantPlugin.link");
        var arguments = new[] { $"-p:PluginDir={pluginDirectory}", "-p:ReloadPlugin=false" };

        var disabled = await this.RunMsBuild(["-t:WritePluginLink", "-p:DeployPlugin=false", .. arguments]);
        Assert.Equal(0, disabled.ExitCode);
        Assert.False(File.Exists(link));

        var deployed = await this.RunMsBuild(["-t:WritePluginLink", "-p:DeployPlugin=true", .. arguments]);
        Assert.Equal(0, deployed.ExitCode);
        var expectedRoot = Path.Combine(this._repositoryRoot, "bin", "Debug") + Path.DirectorySeparatorChar;
        Assert.Equal(expectedRoot, File.ReadAllText(link).Trim());

        await this.RunMsBuild(["-t:PluginClean", "-p:DeployPlugin=false", .. arguments]);
        Assert.True(File.Exists(link));

        File.WriteAllText(link, "another checkout");
        await this.RunMsBuild(["-t:PluginClean", "-p:DeployPlugin=true", .. arguments]);
        Assert.Equal("another checkout", File.ReadAllText(link));

        File.WriteAllText(link, expectedRoot);
        var cleaned = await this.RunMsBuild(["-t:PluginClean", "-p:DeployPlugin=true", .. arguments]);
        Assert.Equal(0, cleaned.ExitCode);
        Assert.False(File.Exists(link));
    }

    [Fact]
    public async Task OutputPath_DoesNotDependOnSolutionDir()
    {
        var result = await this.RunMsBuild("-getProperty:PluginPackageRoot", "-p:SolutionDir=unused");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Path.Combine(this._repositoryRoot, "bin", "Debug") + Path.DirectorySeparatorChar, result.Output.Trim());
    }

    [Fact]
    public async Task ToolVersion_MatchesTheRestoredReferencePackage()
    {
        var result = await this.RunMsBuild("-getProperty:LogiPluginToolVersion");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(this._repositoryRoot, ".config", "dotnet-tools.json")));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(manifest.RootElement.GetProperty("tools").GetProperty("logiplugintool").GetProperty("version").GetString(), result.Output.Trim());
    }

    [Fact]
    public async Task Packaging_WithDeploymentEnabled_FailsBeforeWritingLink()
    {
        var result = await this.RunMsBuild("-t:StagePlugin", "-p:DeployPlugin=true", $"-p:PluginDir={this._temporaryDirectory}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Packaging requires -p:DeployPlugin=false", result.Output);
        Assert.False(Directory.Exists(this._temporaryDirectory));
    }

    [Fact]
    public async Task Staging_RemovesStaleFilesAndExcludesHostBinaries()
    {
        Directory.CreateDirectory(this._temporaryDirectory);
        File.WriteAllText(Path.Combine(this._temporaryDirectory, "stale.dll"), "stale");
        var result = await this.RunMsBuild("-t:StagePlugin", "-p:DeployPlugin=false",
            $"-p:PluginApiDir={Path.GetDirectoryName(typeof(Plugin).Assembly.Location)}",
            $"-p:PluginStagingDir={this._temporaryDirectory}{Path.DirectorySeparatorChar}");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(File.Exists(Path.Combine(this._temporaryDirectory, "stale.dll")));
        Assert.Equal("HomeAssistantPlugin.dll", Path.GetFileName(Assert.Single(Directory.GetFiles(Path.Combine(this._temporaryDirectory, "bin")))));
        var manifest = File.ReadAllText(Path.Combine(this._temporaryDirectory, "metadata", "LoupedeckPackage.yaml"));
        Assert.Contains("pluginFolderWin: bin", manifest);
        Assert.Contains("pluginFolderMac: bin", manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._temporaryDirectory))
        {
            Directory.Delete(this._temporaryDirectory, recursive: true);
        }
    }

    private async Task<(Int32 ExitCode, String Output)> RunMsBuild(params String[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = this._repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "msbuild", "src/HomeAssistantPlugin.csproj", "-nologo" })
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output + await error);
    }

    private static String FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "HomeAssistantPlugin.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate the repository.");
    }
}
