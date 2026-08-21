using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure.Configuration;

public class ProviderSessionTokenResolverTests
{
    [Fact]
    public async Task TryResolveAsync_ReadsAccessTokenFromDottedSchemaRootAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("provider-session-token-resolver");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            var authContent = new
            {
                sessions = new
                {
                    github = new
                    {
                        user = "test-user",
                        oauth_token = "gho_test_token",
                    },
                },
            };

            await File.WriteAllTextAsync(authFilePath, JsonSerializer.Serialize(authContent));

            var pathProvider = new Mock<IAppPathProvider>();
            pathProvider.Setup(provider => provider.GetUserProfileRoot()).Returns(testRoot);

            var definition = new ProviderDefinition(
                "github-copilot",
                "GitHub Copilot",
                PlanType.Coding,
                isQuotaBased: true)
            {
                AuthIdentityCandidatePathTemplates = new[] { authFilePath },
                SessionAuthFileSchemas = new[]
                {
                    new ProviderAuthFileSchema("sessions.github", "oauth_token", "user"),
                },
            };

            var resolver = new ProviderSessionTokenResolver(
                definition.CreateAuthDiscoverySpec(),
                "GitHub auth session",
                "GitHub session",
                NullLogger<TokenDiscoveryService>.Instance,
                pathProvider.Object);

            var resolved = await resolver.TryResolveAsync();

            Assert.NotNull(resolved);
            Assert.Equal("github-copilot", resolved!.ProviderId);
            Assert.Equal("gho_test_token", resolved.ApiKey);
            Assert.Equal("GitHub auth session", resolved.Description);
            Assert.Contains(authFilePath, resolved.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task TryResolveAsync_ReadsAccessTokenFromWildcardSchemaRootAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("provider-session-token-resolver-wildcard");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");

            // Mirrors the Grok CLI auth store: the root property embeds a dynamic OIDC
            // client id and therefore cannot be matched exactly or dot-navigated.
            var authContent = new Dictionary<string, object>
            {
                ["https://auth.x.ai::b1a00492-073a-47ea-816f-4c329264a828"] = new Dictionary<string, object?>
                {
                    ["key"] = "grok-access-token",
                    ["user_id"] = "user-1",
                    ["auth_mode"] = "oidc",
                },
            };

            await File.WriteAllTextAsync(authFilePath, JsonSerializer.Serialize(authContent));

            var pathProvider = new Mock<IAppPathProvider>();
            pathProvider.Setup(provider => provider.GetUserProfileRoot()).Returns(testRoot);

            var definition = new ProviderDefinition(
                "grok",
                "Grok CLI",
                PlanType.Coding,
                isQuotaBased: true)
            {
                AuthIdentityCandidatePathTemplates = new[] { authFilePath },
                SessionAuthFileSchemas = new[]
                {
                    new ProviderAuthFileSchema("https://auth.x.ai::*", "key", "user_id"),
                },
            };

            var resolver = new ProviderSessionTokenResolver(
                definition.CreateAuthDiscoverySpec(),
                "Grok CLI auth session",
                "Grok session",
                NullLogger<TokenDiscoveryService>.Instance,
                pathProvider.Object);

            var resolved = await resolver.TryResolveAsync();

            Assert.NotNull(resolved);
            Assert.Equal("grok", resolved!.ProviderId);
            Assert.Equal("grok-access-token", resolved.ApiKey);
            Assert.Equal("Grok CLI auth session", resolved.Description);
            Assert.Contains(authFilePath, resolved.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }
}
