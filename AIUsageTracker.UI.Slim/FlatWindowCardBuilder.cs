// <copyright file="FlatWindowCardBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class FlatWindowCardBuilder
{
    internal static IReadOnlyList<QuotaProviderUsage> BuildFlatWindowCards(AgentGroupedProviderUsage provider)
    {
        var definition = ProviderMetadataCatalog.Find(provider.ProviderId)
            ?? throw new InvalidOperationException($"Provider definition not found for '{provider.ProviderId}'.");
        var showPrefix = definition.FlatCardShowProviderPrefix;
        var parentDisplayName = showPrefix ? ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId) : null;

        var cards = new List<QuotaProviderUsage>(provider.Models.Count);
        foreach (var model in provider.Models)
        {
            var windowDefinition = ResolveCardWindow(definition, model.ModelId);
            var periodDuration = windowDefinition == null
                ? ResolvePeriodDuration(provider.ProviderId)
                : windowDefinition.PeriodDuration;
            var displayAsFraction = windowDefinition?.DisplayAsFraction ?? definition.DisplayAsFraction;
            var modelState = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, provider.IsQuotaBased);
            var cardName = showPrefix ? $"{parentDisplayName} ({model.ModelName})" : model.ModelName;
            var description = ResolveCardDescription(provider, modelState.Description);

            cards.Add(new ModelScopedProviderUsage
            {
                ProviderId = provider.ProviderId,
                CardId = model.ModelId,
                GroupId = provider.ProviderId,
                Name = model.ModelName,
                ModelName = model.ModelName,
                ProviderName = cardName,
                AccountName = provider.AccountName,
                IsAvailable = provider.IsAvailable,
                State = provider.State,
                PlanType = definition.PlanType,
                IsQuotaBased = definition.IsQuotaBased,
                IsCurrencyUsage = definition.IsCurrencyUsage,
                DisplayAsFraction = displayAsFraction,
                RequestsUsed = displayAsFraction ? model.RequestsUsed : modelState.UsedPercentage,
                RequestsAvailable = model.RequestsAvailable,
                UsedPercent = modelState.UsedPercentage,
                WindowKind = windowDefinition?.Kind ?? WindowKind.None,
                Description = description,
                FetchedAt = provider.FetchedAt,
                NextResetTime = modelState.NextResetTime,
                PeriodDuration = periodDuration,
                ResetCreditsAvailable = model.ResetCreditsAvailable,
                ResetCreditExpirationsUtc = model.ResetCreditExpirationsUtc,
            });
        }

        return cards;
    }

    private static QuotaWindowDefinition? ResolveCardWindow(ProviderDefinition definition, string cardId)
    {
        var childProviderId = $"{definition.ProviderId}.{cardId}";
        return definition.QuotaWindows.FirstOrDefault(window =>
            !string.IsNullOrWhiteSpace(window.ChildProviderId) &&
            string.Equals(window.ChildProviderId, childProviderId, StringComparison.OrdinalIgnoreCase));
    }

    internal static TimeSpan? ResolvePeriodDuration(string providerId)
    {
        if (!ProviderMetadataCatalog.TryGet(providerId, out var definition))
        {
            return null;
        }

        if (string.Equals(providerId, definition.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            // Prefer a Rolling window; fall back to the longest available window.
            return (definition.QuotaWindows
                        .FirstOrDefault(window => window.Kind == WindowKind.Rolling && window.PeriodDuration.HasValue)
                    ?? definition.QuotaWindows
                        .Where(window => window.PeriodDuration.HasValue)
                        .OrderByDescending(window => window.PeriodDuration)
                        .FirstOrDefault())
                ?.PeriodDuration;
        }

        // Derived child provider (e.g. "claude-code.sonnet"): try explicit ChildProviderId match first,
        // then fall back to the parent's Rolling window duration so pace/headroom is computed correctly.
        var fromChildProviderId = definition.QuotaWindows
            .FirstOrDefault(window =>
                window.PeriodDuration.HasValue &&
                !string.IsNullOrWhiteSpace(window.ChildProviderId) &&
                string.Equals(window.ChildProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            ?.PeriodDuration;

        return fromChildProviderId
            ?? definition.QuotaWindows
                .FirstOrDefault(window => window.Kind == WindowKind.Rolling && window.PeriodDuration.HasValue)
                ?.PeriodDuration;
    }

    private static string ResolveCardDescription(AgentGroupedProviderUsage provider, string modelDescription)
    {
        if (provider.IsAvailable && provider.State == ProviderUsageState.Available)
        {
            return modelDescription;
        }

        return provider.Description;
    }
}
