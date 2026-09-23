// <copyright file="AuthPathTemplateResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Paths;

namespace AIUsageTracker.Tests.Core.Paths;

public class AuthPathTemplateResolverTests
{
    private const string UserProfileRoot = @"C:\Users\tester";

    [Theory]
    [InlineData(@"%USERPROFILE%\.grok\auth.json", @"C:\Users\tester\.grok\auth.json")]
    [InlineData(@"%APPDATA%\codex\auth.json", @"C:\Users\tester\AppData\Roaming\codex\auth.json")]
    [InlineData(@"%LOCALAPPDATA%\opencode\auth.json", @"C:\Users\tester\AppData\Local\opencode\auth.json")]
    [InlineData(@"%userprofile%\.codex\auth.json", @"C:\Users\tester\.codex\auth.json")]
    public void Resolve_ProfileTokens_ExpandFromSuppliedRoot(string template, string expected)
    {
        var resolved = AuthPathTemplateResolver.Resolve(template, UserProfileRoot, _ => null);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_ProfileTokens_IgnoreTheEnvironmentLookup()
    {
        // The supplied profile root is what isolates tests and alternate profiles; an
        // environment value for the same name must never win over it.
        var resolved = AuthPathTemplateResolver.Resolve(
            @"%USERPROFILE%\.grok\auth.json",
            UserProfileRoot,
            _ => @"D:\somewhere-else");

        Assert.Equal(@"C:\Users\tester\.grok\auth.json", resolved);
    }

    [Fact]
    public void Resolve_EnvironmentToken_ExpandsFromLookup()
    {
        var resolved = AuthPathTemplateResolver.Resolve(
            @"%GROK_HOME%\auth.json",
            UserProfileRoot,
            name => string.Equals(name, "GROK_HOME", StringComparison.Ordinal) ? @"D:\state\grok" : null);

        Assert.Equal(@"D:\state\grok\auth.json", resolved);
    }

    [Theory]
    [InlineData(@"D:\state\grok\")]
    [InlineData("D:/state/grok/")]
    [InlineData(@"  D:\state\grok  ")]
    public void Resolve_EnvironmentValue_IsTrimmedBeforeJoining(string value)
    {
        var resolved = AuthPathTemplateResolver.Resolve(@"%GROK_HOME%\auth.json", UserProfileRoot, _ => value);

        Assert.EndsWith(@"grok\auth.json", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain(@"/\", resolved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UnsetEnvironmentToken_YieldsNoCandidate(string? value)
    {
        // An unset CLI home override must drop the candidate, not produce a literal
        // "%GROK_HOME%\auth.json" path or a path rooted at the drive.
        var resolved = AuthPathTemplateResolver.Resolve(@"%GROK_HOME%\auth.json", UserProfileRoot, _ => value);

        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void Resolve_SubstitutedValues_AreNotRescannedForTokens()
    {
        var resolved = AuthPathTemplateResolver.Resolve(
            @"%GROK_HOME%\auth.json",
            UserProfileRoot,
            name => string.Equals(name, "GROK_HOME", StringComparison.Ordinal) ? @"D:\100%odd%\grok" : "must-not-be-used");

        Assert.Equal(@"D:\100%odd%\grok\auth.json", resolved);
    }

    [Theory]
    [InlineData(@"C:\plain\auth.json")]
    [InlineData(@"C:\50%\auth.json")]
    [InlineData(@"C:\a%b\c%d\auth.json")]
    public void Resolve_TemplatesWithoutTokens_ArePreserved(string template)
    {
        var resolved = AuthPathTemplateResolver.Resolve(template, UserProfileRoot, _ => "must-not-be-used");

        Assert.Equal(template, resolved);
    }
}
