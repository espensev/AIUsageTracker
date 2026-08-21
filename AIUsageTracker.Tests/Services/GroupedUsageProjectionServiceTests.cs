// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.Services;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_AntigravityWithFlatCardUsage_ProjectsCardIdAsModel()
    {
        // Antigravity emits flat cards with CardId set; BuildModelsFromFlatCards picks them up.
        var usages = new[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "antigravity",
                CardId = "gemini-3-flash",
                Name = "Gemini 3 Flash",
                ProviderName = "Gemini 3 Flash",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 135,
                DisplayAsFraction = true,
                Description = "100% Remaining",
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("Google Antigravity", provider.ProviderName);
        var model = Assert.Single(provider.Models);
        Assert.Equal("gemini-3-flash", model.ModelId);
        Assert.Equal("Gemini 3 Flash", model.ModelName);
        Assert.Equal(100, model.RemainingPercentage);
        Assert.Equal(0, model.UsedPercentage);
        Assert.Equal("100% Remaining", model.Description);
    }

    [Fact]
    public void Build_WhenMostRecentEntryIsError_PrimaryPrefersHealthyEntry()
    {
        // Older successful entry + newer error entry — primary must prefer the
        // healthy entry so summary data reflects real usage, not zeroed error data.
        var old = DateTime.UtcNow.AddHours(-19);
        var now = DateTime.UtcNow;

        var usages = new[]
        {
            new QuotaProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = true,
                UsedPercent = 42,
                Description = "58% remaining",
                FetchedAt = old,
            },
            new QuotaProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = false,
                UsedPercent = 0,
                Description = "HTTP 401: Unauthorized",
                FetchedAt = now,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);

        // Primary should be the healthy entry even though it's older — error data
        // must not overwrite real summary fields.
        Assert.Equal("58% remaining", provider.Description);
        Assert.Equal(42, provider.UsedPercent);
        Assert.True(provider.IsAvailable); // group has at least one healthy entry
    }

    [Fact]
    public void Build_KimiWithWindowKindCards_ProjectsNoModels()
    {
        // Kimi emits WindowKind cards only. In the strict models-only grouped contract,
        // WindowKind != None rows are not projected as grouped models.
        var usages = new[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "weekly",
                Name = "Weekly Limit",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 25,
                RequestsUsed = 25,
                RequestsAvailable = 100,
                NextResetTime = DateTime.UtcNow.AddDays(5),
                PeriodDuration = TimeSpan.FromDays(7),
            },
            new WindowedProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "5-hour-limit",
                Name = "5 Hour Limit",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 50,
                NextResetTime = DateTime.UtcNow.AddHours(3),
                PeriodDuration = TimeSpan.FromHours(5),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void Build_ClaudeCodeCards_ProjectsAllAsModels_WhenAllWindowKindNone()
    {
        // Claude Code cards all have WindowKind.None — each gets its own flat card in the UI.
        var usages = new[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "current-session",
                Name = "Current Session",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 14,
            },
            new WindowedProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "sonnet",
                Name = "Sonnet",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 73,
            },
            new WindowedProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "all-models",
                Name = "All Models",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 73,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(3, provider.Models.Count); // all three become flat model cards
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "current-session", StringComparison.Ordinal));
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "sonnet", StringComparison.Ordinal));
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "all-models", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CodexAndSpark_WindowKindRows_ProjectAsTwoSeparateGroupsWithoutModels()
    {
        // codex and codex.spark are standalone owner providers (FamilyMode = Standalone).
        // Each emits a Burst + Rolling pair, resulting in two separate groups with no grouped models.
        var usages = new[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "codex",
                CardId = "burst",
                GroupId = "codex",
                Name = "5h",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 40,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new WindowedProviderUsage
            {
                ProviderId = "codex",
                CardId = "weekly",
                GroupId = "codex",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 72,
                PeriodDuration = TimeSpan.FromDays(7),
            },
            new WindowedProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.burst",
                GroupId = "codex.spark",
                Name = "5h",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 12,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new WindowedProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.weekly",
                GroupId = "codex.spark",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 8,
                PeriodDuration = TimeSpan.FromDays(7),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        // Two separate groups — each with no grouped models.
        Assert.Equal(2, snapshot.Providers.Count);

        var codex = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Empty(codex.Models);

        var spark = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Empty(spark.Models);
    }

    [Fact]
    public void Build_MinimaxIoAndCodingPlan_ProjectAsSeparateProviders()
    {
        var usages = new[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "minimax-io",
                ProviderName = "MiniMax.io",
                IsAvailable = true,
                UsedPercent = 25,
                RequestsUsed = 250,
                RequestsAvailable = 1000,
            },
            new WindowedProviderUsage
            {
                ProviderId = "minimax-coding-plan",
                ProviderName = "Minimax.io Coding Plan",
                CardId = "burst",
                Name = "5h",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 40,
                RequestsUsed = 40,
                RequestsAvailable = 100,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new WindowedProviderUsage
            {
                ProviderId = "minimax-coding-plan",
                ProviderName = "Minimax.io Coding Plan",
                CardId = "weekly",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 30,
                RequestsUsed = 150,
                RequestsAvailable = 500,
                PeriodDuration = TimeSpan.FromDays(7),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var minimaxIo = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "minimax-io", StringComparison.Ordinal));
        var minimaxCoding = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "minimax-coding-plan", StringComparison.Ordinal));

        Assert.Equal("MiniMax.io", minimaxIo.ProviderName);
        Assert.Equal("Minimax.io Coding Plan", minimaxCoding.ProviderName);
        Assert.Empty(minimaxIo.Models);
        Assert.Empty(minimaxCoding.Models);
    }

    [Fact]
    public void Build_GrokFlatWindowCards_PreservesBothCardsThroughSlimAdapter()
    {
        var resetTime = new DateTime(2026, 8, 25, 20, 39, 7, DateTimeKind.Utc);
        var usages = new ProviderUsage[]
        {
            new WindowedProviderUsage
            {
                ProviderId = "grok",
                ProviderName = "Grok CLI",
                CardId = "weekly-credits",
                GroupId = "grok",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 21,
                NextResetTime = resetTime,
                PeriodDuration = TimeSpan.FromDays(7),
                Description = "79% weekly credits remaining",
            },
            new WindowedProviderUsage
            {
                ProviderId = "grok",
                ProviderName = "Grok CLI",
                CardId = "on-demand-credits",
                GroupId = "grok",
                Name = "On-Demand",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 25,
                RequestsUsed = 25,
                RequestsAvailable = 100,
                DisplayAsFraction = true,
                Description = "75 / 100 on-demand credits remaining",
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(2, provider.Models.Count);

        var cards = GroupedUsageDisplayAdapter.Expand(snapshot);
        Assert.Equal(2, cards.Count);

        var weekly = Assert.Single(cards, card => string.Equals(card.CardId, "weekly-credits", StringComparison.Ordinal));
        Assert.Equal("Grok CLI (Weekly)", weekly.ProviderName);
        Assert.Equal(21, weekly.UsedPercent);
        Assert.Equal(resetTime, weekly.NextResetTime);
        Assert.Equal(TimeSpan.FromDays(7), weekly.PeriodDuration);
        Assert.False(weekly.DisplayAsFraction);

        var onDemand = Assert.Single(cards, card => string.Equals(card.CardId, "on-demand-credits", StringComparison.Ordinal));
        Assert.Equal("Grok CLI (On-Demand)", onDemand.ProviderName);
        Assert.Equal(25, onDemand.UsedPercent);
        Assert.Equal(25, onDemand.RequestsUsed);
        Assert.Equal(100, onDemand.RequestsAvailable);
        Assert.True(onDemand.DisplayAsFraction);
        Assert.Null(onDemand.PeriodDuration);
    }
}
