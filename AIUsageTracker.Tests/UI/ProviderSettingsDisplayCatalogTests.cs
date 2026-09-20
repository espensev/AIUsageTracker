// <copyright file="ProviderSettingsDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsDisplayCatalogTests
{
    [Fact]
    public void GetProviderIdsRequiringDisambiguation_DuplicateDisplayNames_LabelsOnlyCollisions()
    {
        var duplicateIds = SettingsWindow.GetProviderIdsRequiringDisambiguation(
            new[] { "minimax", "minimax-io", "codex" });

        Assert.Contains("minimax", duplicateIds);
        Assert.Contains("minimax-io", duplicateIds);
        Assert.DoesNotContain("codex", duplicateIds);
        Assert.Equal("MiniMax.io (minimax)", SettingsWindow.GetProviderSettingsDisplayLabel("minimax", duplicateIds.Contains("minimax")));
        Assert.Equal("OpenAI (Codex)", SettingsWindow.GetProviderSettingsDisplayLabel("codex", duplicateIds.Contains("codex")));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData(" CoDeX ", true)]
    [InlineData("openai", true)]
    [InlineData("minimax", false)]
    public void MatchesProviderSearch_NameOrId_IgnoresCaseAndOuterWhitespace(string? searchText, bool expected)
    {
        Assert.Equal(expected, SettingsWindow.MatchesProviderSearch("codex", searchText));
    }

    [Fact]
    public void CreateDisplayItems_ConfiguredProviderUnavailable_RemainsSearchable()
    {
        var fixture = SettingsWindowDeterministicFixture.Create();
        var usage = Assert.Single(fixture.Usages, item => string.Equals(item.ProviderId, "codex", StringComparison.Ordinal));
        usage.IsAvailable = false;

        var items = SettingsWindow.CreateProviderDisplayItems(fixture.Configs, fixture.Usages);

        var codex = Assert.Single(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
        Assert.True(SettingsWindow.MatchesProviderSearch(codex.Config.ProviderId, "codex"));
    }

    [Fact]
    public void CreateProviderDashboardGroups_UnavailableCachedCards_RemainEditableUnderTheirOwner()
    {
        var fixture = SettingsWindowDeterministicFixture.Create();
        var usage = Assert.Single(fixture.Usages, item => string.Equals(item.ProviderId, "codex", StringComparison.Ordinal));
        usage.IsAvailable = false;

        var groups = SettingsWindow.CreateProviderDashboardGroups(fixture.Usages);

        Assert.Contains(usage, groups["CODEX"]);
        Assert.Equal(
            MainWindowRuntimeLogic.BuildMainWindowUsageList(fixture.Usages).Select(item => item.ProviderId).Order(StringComparer.Ordinal),
            groups.SelectMany(group => group).Select(item => item.ProviderId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CreateProviderDashboardGroups_HiddenCards_AreStillAvailableToShowAgain()
    {
        var fixture = SettingsWindowDeterministicFixture.Create();
        var hiddenIds = new[] { "codex" };

        var visible = MainWindowRuntimeLogic.BuildMainWindowUsageList(fixture.Usages, hiddenIds);
        var groups = SettingsWindow.CreateProviderDashboardGroups(fixture.Usages);

        Assert.DoesNotContain(visible, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(groups["codex"], usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_IncludesCatalogProviders_NotAlreadyConfigured()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
        };

        var usages = new List<ProviderUsage>
        {
            new QuotaProviderUsage { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, usages);

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_DoesNotDuplicateAlreadyConfiguredDerivedProvider()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex.spark" },
        };

        var usages = new List<ProviderUsage>
        {
            new QuotaProviderUsage { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, usages);

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_SortsByDisplayNameThenProviderId()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "minimax" },
            new() { ProviderId = "codex" },
            new() { ProviderId = "opencode-zen" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.Equal(
            new[] { "minimax", "codex", "opencode-zen" },
            items.Where(item => new[] { "codex", "minimax", "opencode-zen" }.Contains(item.Config.ProviderId, StringComparer.Ordinal))
                .Select(item => item.Config.ProviderId)
                .ToArray());
    }

    [Fact]
    public void CreateDisplayItems_IncludesSupportedProviders_WhenNoConfigsExist()
    {
        var items = SettingsWindow.CreateProviderDisplayItems(Array.Empty<ProviderConfig>(), Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal) && !item.IsDerived);
        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "opencode-zen", StringComparison.Ordinal) && !item.IsDerived);
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "minimax", StringComparison.Ordinal) && !item.IsDerived);
    }

    [Fact]
    public void CreateDisplayItems_HidesLegacyOpenAiConfigFromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_HidesLegacyAnthropicConfigFromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "anthropic" },
            new() { ProviderId = "claude-code" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "anthropic", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "claude-code", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_HidesUnknownConfiguredProviders_FromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "unknown-provider" },
            new() { ProviderId = "codex" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "unknown-provider", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_SortsAlphabeticallyByDisplayName()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
            new() { ProviderId = "deepseek" },
        };

        var usages = new List<ProviderUsage>
        {
            new QuotaProviderUsage { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, usages);
        var orderedIds = items
            .Where(item =>
                string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal) ||
                string.Equals(item.Config.ProviderId, "deepseek", StringComparison.Ordinal))
            .Select(item => item.Config.ProviderId)
            .ToArray();

        Assert.Equal(new[] { "codex" }, orderedIds);
    }

    [Fact]
    public void CreateDisplayItems_UsesProviderMetadata_ForSettingsVisibility()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
            new() { ProviderId = "codex.spark" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
    }
}
