// <copyright file="BrowserServiceLaunchTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim.Services;

namespace AIUsageTracker.Tests.UI;

public sealed class BrowserServiceLaunchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tracker-web-launch-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("debug")]
    [InlineData("release")]
    [InlineData("release_win-x64")]
    public void ResolveWebStartInfo_ArtifactsLayout_StartsMatchingBuildWithoutSdk(string pivot)
    {
        var output = Path.Combine(this._root, "artifacts", "bin", "AIUsageTracker.UI.Slim", pivot);
        var executable = this.CreateFile("artifacts", "bin", "AIUsageTracker.Web", pivot, "AIUsageTracker.Web.exe");
        this.CreateFile("AIUsageTracker.Web", "AIUsageTracker.Web.csproj");

        var startInfo = BrowserService.ResolveWebStartInfo(output);

        Assert.NotNull(startInfo);
        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(executable), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void ResolveWebStartInfo_OnlyDifferentPivotBuilt_FallsBackToProject()
    {
        var output = Path.Combine(this._root, "artifacts", "bin", "AIUsageTracker.UI.Slim", "debug");
        this.CreateFile("artifacts", "bin", "AIUsageTracker.Web", "release", "AIUsageTracker.Web.exe");
        var project = this.CreateFile("AIUsageTracker.Web", "AIUsageTracker.Web.csproj");

        var startInfo = BrowserService.ResolveWebStartInfo(output);

        Assert.NotNull(startInfo);
        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(project), startInfo.WorkingDirectory);
    }

    [Fact]
    public void ResolveWebStartInfo_PortableLayout_StartsBundledSibling()
    {
        var executable = this.CreateFile("Web", "AIUsageTracker.Web.exe");

        var startInfo = BrowserService.ResolveWebStartInfo(Path.Combine(this._root, "Tracker"));

        Assert.NotNull(startInfo);
        Assert.Equal(executable, startInfo.FileName);
    }

    [Fact]
    public void ResolveWebStartInfo_InstalledLayout_PrefersColocatedExecutable()
    {
        var executable = this.CreateFile("Tracker", "AIUsageTracker.Web.exe");
        this.CreateFile("Web", "AIUsageTracker.Web.exe");

        var startInfo = BrowserService.ResolveWebStartInfo(Path.Combine(this._root, "Tracker"));

        Assert.NotNull(startInfo);
        Assert.Equal(executable, startInfo.FileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private string CreateFile(params string[] parts)
    {
        var path = Path.Combine(new[] { this._root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
