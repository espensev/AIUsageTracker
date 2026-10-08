// <copyright file="ClaudeCodeProviderCliHomeTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Infrastructure.Providers;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

// Mutates a process environment variable, so it shares the non-parallel collection.
[Collection("TokenDiscovery")]
public class ClaudeCodeProviderCliHomeTests : HttpProviderTestBase<ClaudeCodeProvider>, IDisposable
{
    private const string CliHomeVariable = "CLAUDE_CONFIG_DIR";

    private readonly string _testDirectory;
    private readonly string? _priorCliHome;

    public ClaudeCodeProviderCliHomeTests()
    {
        this._testDirectory = TestTempPaths.CreateDirectory("claude-code-cli-home");
        this._priorCliHome = Environment.GetEnvironmentVariable(CliHomeVariable);
        Environment.SetEnvironmentVariable(CliHomeVariable, this._testDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliHomeVariable, this._priorCliHome);
        TestTempPaths.CleanupPath(this._testDirectory);
    }

    [Fact]
    public async Task GetUsageAsync_RelocatedCliHome_ReadsTheSessionFromTheOverrideAsync()
    {
        var nativeToken = $"sk-ant-oat-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(
            Path.Combine(this._testDirectory, ".credentials.json"),
            JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = nativeToken } }));
        this.Config.ApiKey = "sk-ant-oat-stale-token";
        this.SetupHttpResponse(
            r => string.Equals(r.RequestUri?.ToString(), ClaudeCodeProvider.OAuthUsageEndpoint, StringComparison.Ordinal),
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Forbidden,
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });

        // No explicit credentials path: discovery must follow the CLI's own home override.
        var provider = new ClaudeCodeProvider(this.Logger.Object, this.HttpClient);
        await provider.GetUsageAsync(this.Config);

        this.MessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.RequestUri!.ToString() == ClaudeCodeProvider.OAuthUsageEndpoint &&
                request.Headers.Authorization!.Parameter == nativeToken),
            ItExpr.IsAny<CancellationToken>());
    }
}
