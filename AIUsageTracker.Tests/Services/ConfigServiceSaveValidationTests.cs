// <copyright file="ConfigServiceSaveValidationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Services;

public sealed class ConfigServiceSaveValidationTests : IntegrationTestBase
{
    private ConfigService CreateConfigService()
    {
        var authPath = this.CreateFile("config/auth.json", "{}");
        var providersPath = this.CreateFile("config/providers.json", "{}");
        var prefsPath = this.CreateFile("preferences.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(prefsPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        // Exercise real temporary file persistence without invoking ambient token discovery.
        var files = new JsonConfigLoader(pathProvider: mockPathProvider.Object);
        IReadOnlyList<ProviderConfig> storedConfigs = Array.Empty<ProviderConfig>();
        var loader = new Mock<IConfigLoader>(MockBehavior.Strict);
        loader.Setup(value => value.LoadConfigAsync()).Returns(() => Task.FromResult(storedConfigs));
        loader.Setup(value => value.SaveConfigAsync(It.IsAny<IEnumerable<ProviderConfig>>()))
            .Returns(async (IEnumerable<ProviderConfig> configs) =>
            {
                var saved = configs.ToArray();
                await files.SaveConfigAsync(saved);
                storedConfigs = saved;
            });
        loader.Setup(value => value.LoadPreferencesAsync()).Returns(files.LoadPreferencesAsync);
        loader.Setup(value => value.SavePreferencesAsync(It.IsAny<AppPreferences>()))
            .Returns((AppPreferences preferences) => files.SavePreferencesAsync(preferences));

        return new ConfigService(
            NullLogger<ConfigService>.Instance,
            NullLoggerFactory.Instance,
            mockPathProvider.Object,
            loader.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveConfigAsync_PreferencesUnreadable_FailsBeforeWritingCredentialsAsync(bool locked)
    {
        var service = this.CreateConfigService();
        var preferencesPath = Path.Combine(this.TestRootPath, "preferences.json");
        await File.WriteAllTextAsync(preferencesPath, locked ? "{\"SuppressedProviderIds\":[\"groq\"]}" : "invalid-json");
        var preferencesBefore = await File.ReadAllBytesAsync(preferencesPath);
        var authPath = Path.Combine(this.TestRootPath, "config", "auth.json");
        var credentialsBefore = await File.ReadAllBytesAsync(authPath);
        using (var heldFile = locked ? File.Open(preferencesPath, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            // Rendering retains its existing availability fallback; mutation must not.
            Assert.NotNull(await service.GetPreferencesAsync());
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveConfigAsync(
                new ProviderConfig { ProviderId = "groq", ApiKey = Guid.NewGuid().ToString() }));
            Assert.True(failure.InnerException is IOException or JsonException);
        }

        Assert.Equal(credentialsBefore, await File.ReadAllBytesAsync(authPath));
        Assert.Equal(preferencesBefore, await File.ReadAllBytesAsync(preferencesPath));
    }

    [Fact]
    public async Task SaveConfigAsync_UnknownProviderId_ThrowsArgumentExceptionAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = "totally-unknown-provider-xyz-99999" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConfigAsync(config));
    }

    [Fact]
    public async Task SaveConfigAsync_KnownProviderId_DoesNotThrowAsync()
    {
        var service = this.CreateConfigService();

        // "claude-code" is a well-known provider in ProviderMetadataCatalog
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "sk-test" };

        var exception = await Record.ExceptionAsync(() => service.SaveConfigAsync(config));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SaveConfigAsync_ReaddedProvider_ClearsSuppressionAfterPersistenceAsync()
    {
        var service = this.CreateConfigService();
        await File.WriteAllTextAsync(
            Path.Combine(this.TestRootPath, "preferences.json"),
            JsonSerializer.Serialize(new AppPreferences { SuppressedProviderIds = ["CLAUDE-CODE", "groq"], FontSize = 23 }));

        await service.SaveConfigAsync(new ProviderConfig { ProviderId = "claude-code", ApiKey = Guid.NewGuid().ToString() });

        var preferences = await service.GetPreferencesAsync();
        Assert.Equal(new[] { "groq" }, preferences.SuppressedProviderIds);
        Assert.Equal(23, preferences.FontSize);
        Assert.Contains(await service.GetConfigsAsync(), config => string.Equals(config.ProviderId, "claude-code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveConfigAsync_PersistenceFails_RetainsSuppressionAsync()
    {
        var service = this.CreateConfigService();
        await File.WriteAllTextAsync(
            Path.Combine(this.TestRootPath, "preferences.json"),
            JsonSerializer.Serialize(new AppPreferences { SuppressedProviderIds = ["claude-code"] }));
        var providerPath = Path.Combine(this.TestRootPath, "config", "providers.json");
        File.Delete(providerPath);
        Directory.CreateDirectory(providerPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveConfigAsync(
            new ProviderConfig { ProviderId = "claude-code", ApiKey = Guid.NewGuid().ToString() }));

        Assert.Equal(new[] { "claude-code" }, (await service.GetPreferencesAsync()).SuppressedProviderIds);
    }

    [Fact]
    public async Task SaveConfigAsync_CoReportedProvider_ReenablesItsSuppressedOwnerAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = Guid.NewGuid().ToString() };
        await service.SaveConfigAsync(config);
        await service.SavePreferencesAsync(new AppPreferences { SuppressedProviderIds = ["codex", "groq"] });
        Assert.DoesNotContain(await service.GetConfigsAsync(), item => string.Equals(item.ProviderId, "codex.spark", StringComparison.Ordinal));

        await service.SaveConfigAsync(config);

        Assert.Equal(new[] { "groq" }, (await service.GetPreferencesAsync()).SuppressedProviderIds);
        Assert.Contains(await service.GetConfigsAsync(), item => string.Equals(item.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveConfigAsync_NullConfig_ThrowsArgumentNullExceptionAsync()
    {
        var service = this.CreateConfigService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveConfigAsync(null!));
    }

    [Fact]
    public async Task SaveConfigAsync_EmptyProviderId_ThrowsArgumentExceptionAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConfigAsync(config));
    }
}
