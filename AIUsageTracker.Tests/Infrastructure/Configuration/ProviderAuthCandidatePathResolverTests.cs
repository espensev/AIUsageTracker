// <copyright file="ProviderAuthCandidatePathResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure.Configuration;

public class ProviderAuthCandidatePathResolverTests
{
    private const string UserProfileRoot = @"C:\Users\tester";

    [Theory]
    [InlineData("grok", "GROK_HOME", @"D:\state\grok", @"D:\state\grok\auth.json", @"C:\Users\tester\.grok\auth.json")]
    [InlineData("codex", "CODEX_HOME", @"D:\state\codex", @"D:\state\codex\auth.json", @"C:\Users\tester\.codex\auth.json")]
    [InlineData("codex-spark", "CODEX_HOME", @"D:\state\codex", @"D:\state\codex\auth.json", @"C:\Users\tester\.codex\auth.json")]
    [InlineData("claude-code", "CLAUDE_CONFIG_DIR", @"D:\state\claude", @"D:\state\claude\.credentials.json", @"C:\Users\tester\.claude\.credentials.json")]
    public void ResolvePaths_CliHomeOverrideSet_IsTriedBeforeTheProfileDefault(
        string definitionKey,
        string homeVariable,
        string homeValue,
        string expectedOverridePath,
        string expectedProfilePath)
    {
        var paths = ProviderAuthCandidatePathResolver.ResolvePaths(
            GetDefinition(definitionKey).CreateAuthDiscoverySpec(),
            UserProfileRoot,
            name => string.Equals(name, homeVariable, StringComparison.Ordinal) ? homeValue : null).ToList();

        // The CLI's own home override must win over the profile default, as it does in the CLI.
        Assert.Equal(expectedOverridePath, paths[0]);
        Assert.Contains(expectedProfilePath, paths);
    }

    [Theory]
    [InlineData("grok", "GROK_HOME", @"C:\Users\tester\.grok\auth.json")]
    [InlineData("codex", "CODEX_HOME", @"C:\Users\tester\.codex\auth.json")]
    [InlineData("codex-spark", "CODEX_HOME", @"C:\Users\tester\.codex\auth.json")]
    [InlineData("claude-code", "CLAUDE_CONFIG_DIR", @"C:\Users\tester\.claude\.credentials.json")]
    public void ResolvePaths_CliHomeOverrideUnset_FallsBackToTheProfileDefault(
        string definitionKey,
        string homeVariable,
        string expectedProfilePath)
    {
        var paths = ProviderAuthCandidatePathResolver.ResolvePaths(
            GetDefinition(definitionKey).CreateAuthDiscoverySpec(),
            UserProfileRoot,
            _ => null);

        Assert.Equal(expectedProfilePath, paths[0]);
        Assert.DoesNotContain(paths, path => path.Contains(homeVariable, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolvePaths_PathProvider_OwnsTheCliHomeOverrideLookup()
    {
        // The path provider is the discovery seam: a caller that redirects the profile root
        // must also control the CLI home overrides, or the host's own CODEX_HOME leaks in.
        var pathProvider = new Mock<IAppPathProvider>();
        pathProvider.Setup(provider => provider.GetUserProfileRoot()).Returns(UserProfileRoot);
        pathProvider.Setup(provider => provider.GetEnvironmentVariable("CODEX_HOME")).Returns(@"D:\state\codex");

        var paths = ProviderAuthCandidatePathResolver.ResolvePaths(
            CodexProvider.StaticDefinition.CreateAuthDiscoverySpec(),
            pathProvider.Object);

        Assert.Equal(@"D:\state\codex\auth.json", paths[0]);
        Assert.Contains(@"C:\Users\tester\.codex\auth.json", paths);
    }

    private static ProviderDefinition GetDefinition(string definitionKey)
    {
        return definitionKey switch
        {
            "grok" => GrokProvider.StaticDefinition,
            "codex" => CodexProvider.StaticDefinition,
            "codex-spark" => CodexProvider.SparkDefinition,
            "claude-code" => ClaudeCodeProvider.StaticDefinition,
            _ => throw new ArgumentOutOfRangeException(nameof(definitionKey), definitionKey, "Unknown definition key"),
        };
    }
}
