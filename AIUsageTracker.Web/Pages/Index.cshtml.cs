// <copyright file="Index.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace AIUsageTracker.Web.Pages;

public sealed record ProviderUsageFamily(
    string ProviderId,
    ProviderUsage PrimaryCard,
    IReadOnlyList<ProviderUsage> SiblingCards);

[OutputCache(PolicyName = "DashboardCache")]
public class IndexModel : PageModel
{
    private readonly WebDatabaseService _dbService;
    private readonly UsageAnalyticsService _analyticsService;
    private readonly PreferencesStore _preferencesStore;

    public IndexModel(WebDatabaseService dbService, UsageAnalyticsService analyticsService, PreferencesStore preferencesStore)
    {
        this._dbService = dbService;
        this._analyticsService = analyticsService;
        this._preferencesStore = preferencesStore;
    }

    public IReadOnlyList<ProviderUsage>? LatestUsage { get; set; }

    public UsageSummary? Summary { get; set; }

    public IReadOnlyDictionary<string, BurnRateForecast> ForecastsByProvider { get; private set; }
        = new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ProviderReliabilitySnapshot> ReliabilityByProvider { get; private set; }
        = new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UsageAnomalySnapshot> AnomaliesByProvider { get; private set; }
        = new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BudgetStatus> BudgetStatuses { get; private set; } = [];

    public IReadOnlyList<UsageComparison> UsageComparisons { get; private set; } = [];

    public IReadOnlyDictionary<string, List<double>> SparklineData { get; private set; }
        = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public bool ShowUsedPercentage { get; set; }

    public bool ShowInactiveProviders { get; set; }

    public bool EnableExperimentalAnomalyDetection { get; set; }

    // Always on.
    public bool EnableExperimentalBudgetPolicies { get; set; } = true;

    // Always on.
    public bool EnableExperimentalComparison { get; set; } = true;

    public int ColorThresholdYellow { get; set; } = 60;

    public int ColorThresholdRed { get; set; } = 80;

    public static IReadOnlyList<ProviderUsageFamily> ProjectProviderFamilies(
        IEnumerable<ProviderUsage> usageRows)
    {
        ArgumentNullException.ThrowIfNull(usageRows);

        return usageRows
            .GroupBy(
                usage => ProviderMetadataCatalog.GetProviderOwnerId(usage.ProviderId),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var definition = ProviderMetadataCatalog.Find(group.Key);
                var declaredOrder = BuildDeclaredCardOrder(definition);
                var orderedCards = group
                    .GroupBy(GetUsageCardKey, StringComparer.OrdinalIgnoreCase)
                    .Select(cardGroup => cardGroup
                        .OrderByDescending(usage => usage.FetchedAt)
                        .First())
                    .OrderBy(usage => GetDeclaredCardIndex(usage, declaredOrder))
                    .ThenBy(usage => (usage as QuotaProviderUsage)?.CardId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ProviderUsageFamily(
                    group.Key,
                    orderedCards[0],
                    orderedCards.Skip(1).ToList());
            })
            .OrderBy(family => family.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task OnGetAsync([FromQuery] bool? showUsed)
    {
        this.ResolveShowUsedPreference(showUsed);
        this.ResolveShowInactivePreference();
        this.ResolveExperimentalAnomalyPreference();
        await this.LoadColorThresholdsAsync().ConfigureAwait(false);

        // Budget and comparison are always enabled (experimental)
        this.EnableExperimentalBudgetPolicies = true;
        this.EnableExperimentalComparison = true;

        await this.LoadDashboardDataAsync().ConfigureAwait(false);
    }

    private void ResolveShowUsedPreference(bool? showUsed)
    {
        if (showUsed.HasValue)
        {
            this.ShowUsedPercentage = showUsed.Value;
            this.SetBooleanCookie("showUsedPercentage", this.ShowUsedPercentage);
        }
        else if (this.Request.Cookies.TryGetValue("showUsedPercentage", out var cookieValue) && bool.TryParse(cookieValue, out var cookiePref))
        {
            this.ShowUsedPercentage = cookiePref;
        }
        else
        {
            this.ShowUsedPercentage = false; // Default to showing remaining percentage.
        }
    }

    private void ResolveShowInactivePreference()
    {
        if (this.Request.Query.TryGetValue("showInactive", out var showInactiveQuery) &&
            bool.TryParse(showInactiveQuery, out var showInactive))
        {
            this.ShowInactiveProviders = showInactive;
            this.SetBooleanCookie("showInactiveProviders", this.ShowInactiveProviders);
        }
        else if (this.Request.Cookies.TryGetValue("showInactiveProviders", out var inactiveCookieValue) &&
                 bool.TryParse(inactiveCookieValue, out var inactiveCookiePref))
        {
            this.ShowInactiveProviders = inactiveCookiePref;
        }
        else
        {
            this.ShowInactiveProviders = false; // Default to hiding inactive providers.
        }
    }

    private void ResolveExperimentalAnomalyPreference()
    {
        if (this.Request.Query.TryGetValue("expAnomaly", out var expAnomalyQuery) &&
            bool.TryParse(expAnomalyQuery, out var expAnomaly))
        {
            this.EnableExperimentalAnomalyDetection = expAnomaly;
            this.SetBooleanCookie("expAnomaly", this.EnableExperimentalAnomalyDetection);
        }
        else if (this.Request.Cookies.TryGetValue("expAnomaly", out var expAnomalyCookie) &&
                 bool.TryParse(expAnomalyCookie, out var expAnomalyCookiePref))
        {
            this.EnableExperimentalAnomalyDetection = expAnomalyCookiePref;
        }
        else
        {
            this.EnableExperimentalAnomalyDetection = false;
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        if (!this.IsDatabaseAvailable)
        {
            return;
        }

        var latestUsageTask = this._dbService.GetLatestUsageAsync(includeInactive: true);
        var summaryTask = this._dbService.GetUsageSummaryAsync();

        await Task.WhenAll(latestUsageTask, summaryTask).ConfigureAwait(false);

        this.LatestUsage = await latestUsageTask.ConfigureAwait(false);
        this.Summary = await summaryTask.ConfigureAwait(false);

        if (this.LatestUsage.Count == 0)
        {
            return;
        }

        var providerIds = this.LatestUsage
            .Select(usage => ProviderMetadataCatalog.GetProviderOwnerId(usage.ProviderId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await this.LoadAnalyticsAsync(providerIds).ConfigureAwait(false);
        await this.LoadSparklineDataAsync(providerIds).ConfigureAwait(false);
    }

    private async Task LoadAnalyticsAsync(IReadOnlyList<string> providerIds)
    {
        var forecastTask = this._analyticsService.GetBurnRateForecastsAsync(providerIds);
        var reliabilityTask = this._analyticsService.GetProviderReliabilityAsync(providerIds);
        Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>>? anomalyTask = null;
        if (this.EnableExperimentalAnomalyDetection)
        {
            anomalyTask = this._analyticsService.GetUsageAnomaliesAsync(providerIds);
            await Task.WhenAll(forecastTask, reliabilityTask, anomalyTask).ConfigureAwait(false);
        }
        else
        {
            await Task.WhenAll(forecastTask, reliabilityTask).ConfigureAwait(false);
        }

        this.ForecastsByProvider = await forecastTask.ConfigureAwait(false);
        this.ReliabilityByProvider = await reliabilityTask.ConfigureAwait(false);
        var anomalies = anomalyTask != null ? (await anomalyTask.ConfigureAwait(false)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) : null;
        this.AnomaliesByProvider = anomalies ?? new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);

        if (this.EnableExperimentalBudgetPolicies)
        {
            this.BudgetStatuses = (await this._analyticsService.GetBudgetStatusesAsync(providerIds).ConfigureAwait(false)).ToList();
        }

        if (this.EnableExperimentalComparison)
        {
            this.UsageComparisons = (await this._analyticsService.GetUsageComparisonsAsync(providerIds).ConfigureAwait(false)).ToList();
        }
    }

    private async Task LoadSparklineDataAsync(IReadOnlyList<string> providerIds)
    {
        var samples = await this._dbService.GetHistorySamplesAsync(providerIds, 24, 24).ConfigureAwait(false);

        this.SparklineData = samples
            .GroupBy(s => s.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.FetchedAt)
                      .Select(s => s is QuotaProviderUsage q && q.RequestsAvailable > 0
                          ? Math.Clamp((q.RequestsUsed / q.RequestsAvailable) * 100.0, 0, 100)
                          : 0)
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task LoadColorThresholdsAsync()
    {
        var prefs = await this._preferencesStore.LoadAsync().ConfigureAwait(false);
        this.ColorThresholdYellow = prefs.ColorThresholdYellow;
        this.ColorThresholdRed = prefs.ColorThresholdRed;
    }

    private void SetBooleanCookie(string name, bool value)
    {
        this.Response.Cookies.Append(name, value.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
        });
    }

    private static string GetUsageCardKey(ProviderUsage usage)
    {
        if (usage is QuotaProviderUsage quota && !string.IsNullOrWhiteSpace(quota.CardId))
        {
            return $"card:{quota.CardId}";
        }

        return $"provider:{usage.ProviderId}:{usage.GetType().Name}";
    }

    private static int GetDeclaredCardIndex(
        ProviderUsage usage,
        IReadOnlyDictionary<string, int> declaredOrder)
    {
        return usage is QuotaProviderUsage quota &&
               !string.IsNullOrWhiteSpace(quota.CardId) &&
               declaredOrder.TryGetValue(quota.CardId, out var index)
            ? index
            : int.MaxValue;
    }

    private static IReadOnlyDictionary<string, int> BuildDeclaredCardOrder(ProviderDefinition? definition)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (definition == null)
        {
            return order;
        }

        var prefix = definition.ProviderId + ".";
        for (var index = 0; index < definition.QuotaWindows.Count; index++)
        {
            var childProviderId = definition.QuotaWindows[index].ChildProviderId;
            if (!string.IsNullOrWhiteSpace(childProviderId) &&
                childProviderId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                order[childProviderId[prefix.Length..]] = index;
            }
        }

        return order;
    }
}
