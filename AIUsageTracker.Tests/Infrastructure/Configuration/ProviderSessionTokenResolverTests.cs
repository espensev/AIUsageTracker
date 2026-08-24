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
            var authContent = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["https://auth.x.ai::b1a00492-073a-47ea-816f-4c329264a828"] = new Dictionary<string, object?>(StringComparer.Ordinal)
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

    [Fact]
    public async Task TryResolveAsync_WildcardSchemaRoot_SkipsMatchingEntryWithoutTokenAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("provider-session-token-resolver-wildcard-multiple");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            var authContent = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["https://auth.x.ai::stale-client"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["key"] = string.Empty,
                    ["user_id"] = "stale-user",
                },
                ["https://auth.x.ai::active-client"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["key"] = "active-grok-token",
                    ["user_id"] = "active-user",
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
            Assert.Equal("active-grok-token", resolved!.ApiKey);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task TryResolveAsync_NonObjectAuthRoot_ReturnsNullAsync(string authContent)
    {
        var testRoot = TestTempPaths.CreateDirectory("provider-session-token-resolver-non-object");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            await File.WriteAllTextAsync(authFilePath, authContent);

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

            Assert.Null(resolved);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task TryResolveAsync_WildcardSchemaRoots_PrefersUnexpiredEntryAsync()
    {
        // An expired root that appears first in file order must not mask the live session.
        var resolved = await ResolveWildcardWithTimestampsAsync(
            "provider-session-token-resolver-wildcard-expired",
            firstRoot: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = "expired-token",
                ["user_id"] = "old-user",
                ["create_time"] = "2026-06-01T00:00:00Z",
                ["expires_at"] = "2026-06-01T06:00:00Z",
            },
            secondRoot: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = "live-token",
                ["user_id"] = "live-user",
                ["create_time"] = "2026-01-01T00:00:00Z",
                ["expires_at"] = "2099-01-01T00:00:00Z",
            });

        Assert.NotNull(resolved);
        Assert.Equal("live-token", resolved!.ApiKey);
    }

    [Fact]
    public async Task TryResolveAsync_WildcardSchemaRoots_PrefersNewestCreatedEntryAsync()
    {
        // Both sessions are unexpired; the newer create_time wins over file order.
        var resolved = await ResolveWildcardWithTimestampsAsync(
            "provider-session-token-resolver-wildcard-newest",
            firstRoot: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = "older-token",
                ["user_id"] = "older-user",
                ["create_time"] = "2026-01-01T00:00:00Z",
                ["expires_at"] = "2099-01-01T00:00:00Z",
            },
            secondRoot: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = "newer-token",
                ["user_id"] = "newer-user",
                ["create_time"] = "2026-06-01T00:00:00Z",
                ["expires_at"] = "2099-01-01T00:00:00Z",
            });

        Assert.NotNull(resolved);
        Assert.Equal("newer-token", resolved!.ApiKey);
    }

    private static async Task<DiscoveredSessionToken?> ResolveWildcardWithTimestampsAsync(
        string testDirectoryName,
        Dictionary<string, object?> firstRoot,
        Dictionary<string, object?> secondRoot)
    {
        var testRoot = TestTempPaths.CreateDirectory(testDirectoryName);

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            var authContent = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["https://auth.x.ai::first-client"] = firstRoot,
                ["https://auth.x.ai::second-client"] = secondRoot,
            };

            await File.WriteAllTextAsync(authFilePath, JsonSerializer.Serialize(authContent)).ConfigureAwait(false);

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
                    new ProviderAuthFileSchema(
                        "https://auth.x.ai::*",
                        "key",
                        "user_id",
                        CreatedAtProperty: "create_time",
                        ExpiresAtProperty: "expires_at"),
                },
            };

            var resolver = new ProviderSessionTokenResolver(
                definition.CreateAuthDiscoverySpec(),
                "Grok CLI auth session",
                "Grok session",
                NullLogger<TokenDiscoveryService>.Instance,
                pathProvider.Object);

            return await resolver.TryResolveAsync().ConfigureAwait(false);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }
}
