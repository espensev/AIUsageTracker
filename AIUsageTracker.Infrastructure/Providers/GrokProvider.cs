// <copyright file="GrokProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

/// <summary>
/// Usage provider for the xAI Grok CLI (SuperGrok subscription). Reads the weekly credit
/// usage from the CLI's billing endpoint using the OIDC session stored in the CLI's own
/// auth file (<c>~/.grok/auth.json</c>).
/// </summary>
public class GrokProvider : ProviderBase
{
    private const string GrokProviderId = "grok";
    private const string WeeklyCardId = "weekly-credits";
    private const string OnDemandCardId = "on-demand-credits";
    private const string BillingEndpoint = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GrokProvider> _logger;
    private readonly string? _authFilePath;

    public GrokProvider(HttpClient httpClient, ILogger<GrokProvider> logger, string? authFilePath = null)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._authFilePath = authFilePath;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        GrokProviderId,
        "Grok CLI",
        PlanType.Coding,
        isQuotaBased: true)
    {
        FamilyMode = ProviderFamilyMode.FlatWindowCards,
        FlatCardShowProviderPrefix = true,
        AdditionalHandledProviderIds = new[] { "grok-cli" },
        SettingsMode = ProviderSettingsMode.SessionAuthStatus,
        SessionStatusLabel = "Grok CLI",
        IconAssetName = "grok",
        BadgeColorHex = "#1A1A1A",
        BadgeInitial = "G",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.grok\\auth.json",
        },
        SessionAuthFileSchemas = new[]
        {
            // The Grok CLI stores sessions under an issuer-scoped root property whose name
            // embeds the OIDC client id ("https://auth.x.ai::<client-id>"), so the schema
            // uses a wildcard root that matches any client id.
            // create_time/expires_at let the reader pick the newest unexpired session when a
            // CLI upgrade leaves more than one client-id root in the auth file.
            new ProviderAuthFileSchema(
                "https://auth.x.ai::*",
                "key",
                "user_id",
                CreatedAtProperty: "create_time",
                ExpiresAtProperty: "expires_at"),
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(
                WindowKind.Rolling,
                "Weekly",
                ChildProviderId: GrokProviderId + "." + WeeklyCardId,
                SettingsLabel: "Weekly credits",
                PeriodDuration: TimeSpan.FromDays(7)),
            new(
                WindowKind.None,
                "On-Demand",
                ChildProviderId: GrokProviderId + "." + OnDemandCardId,
                SettingsLabel: "On-Demand credits",
                DisplayAsFraction: true),
        },
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // The Grok CLI rotates its OIDC access token every few hours and rewrites the auth
        // file on each run, so prefer a live read over the token captured at discovery time.
        var nativeToken = await this.LoadNativeTokenAsync(cancellationToken).ConfigureAwait(false);
        var usedNativeToken = !string.IsNullOrWhiteSpace(nativeToken);
        var token = usedNativeToken ? nativeToken : config.ApiKey;

        if (string.IsNullOrWhiteSpace(token))
        {
            return new[]
            {
                this.CreateUnavailableUsage(
                    "Grok CLI session missing - run grok login",
                    authSource: config.AuthSource,
                    state: ProviderUsageState.Missing),
            };
        }

        // Auth-source attribution is decided by whether the native session file supplied the
        // token, not by comparing token strings: a native token that happens to equal the
        // configured key must still be attributed to the CLI session.
        var authSource = usedNativeToken && string.IsNullOrWhiteSpace(config.AuthSource)
            ? "Grok CLI session auth"
            : config.AuthSource;

        var effectiveConfig = string.Equals(token, config.ApiKey, StringComparison.Ordinal)
            && string.Equals(authSource, config.AuthSource, StringComparison.Ordinal)
            ? config
            : new ProviderConfig
            {
                ProviderId = config.ProviderId,
                ApiKey = token!,
                Limit = config.Limit,
                BaseUrl = config.BaseUrl,
                ShowInTray = config.ShowInTray,
                EnableNotifications = config.EnableNotifications,
                EnabledSubTrays = config.EnabledSubTrays,
                AuthSource = authSource,
                Description = config.Description,
                Models = config.Models,
                ShowCachedModelsWhenOffline = config.ShowCachedModelsWhenOffline,
            };

        var fetchResult = await this.FetchJsonAsync<GrokBillingResponse>(
            BillingEndpoint,
            effectiveConfig,
            this._httpClient,
            this._logger,
            cancellationToken)
            .ConfigureAwait(false);

        if (!fetchResult.IsSuccess)
        {
            return new[] { fetchResult.FailureUsage! };
        }

        var data = fetchResult.Data!;
        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (data.Config == null)
        {
            return new[] { this.CreateUnavailableUsage("No billing data available", authSource: effectiveConfig.AuthSource) };
        }

        return this.BuildUsageCards(data.Config, fetchResult.RawContent, fetchResult.HttpStatus, effectiveConfig.AuthSource, providerLabel);
    }

    private IEnumerable<ProviderUsage> BuildUsageCards(
        GrokBillingConfig billing,
        string rawJson,
        int httpStatus,
        string? authSource,
        string providerLabel)
    {
        var cards = new List<ProviderUsage>();

        var resetTime = TryReadIsoTimestamp(billing.CurrentPeriod?.End ?? billing.BillingPeriodEnd, out var parsedReset)
            ? (DateTime?)parsedReset
            : null;

        if (billing.CreditUsagePercent.HasValue)
        {
            var usedPercent = UsageMath.ClampPercent(billing.CreditUsagePercent.Value);
            var remainingPercent = Math.Max(0.0, 100.0 - usedPercent);
            var productSummary = FormatProductSummary(billing.ProductUsage);

            var description = $"{remainingPercent.ToString("F0", CultureInfo.InvariantCulture)}% weekly credits remaining";
            if (!string.IsNullOrEmpty(productSummary))
            {
                description += $" | {productSummary}";
            }

            if (resetTime.HasValue)
            {
                description += $" | resets {resetTime.Value.ToLocalTime().ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)}";
            }

            cards.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                Name = "Weekly",
                CardId = WeeklyCardId,
                GroupId = this.ProviderId,
                IsAvailable = true,
                UsedPercent = usedPercent,
                WindowKind = WindowKind.Rolling,
                PeriodDuration = TimeSpan.FromDays(7),
                NextResetTime = resetTime,
                PlanType = this.Definition.PlanType,
                IsQuotaBased = this.Definition.IsQuotaBased,
                Description = description,
                AuthSource = authSource ?? string.Empty,
                RawJson = rawJson,
                HttpStatus = httpStatus,
            });
        }

        if (billing.OnDemandCap is { Val: > 0 })
        {
            var cap = billing.OnDemandCap.Val;
            var used = billing.OnDemandUsed?.Val ?? 0;
            var onDemandPercent = UsageMath.ClampPercent(cap > 0 ? used * 100.0 / cap : 0);
            var remaining = Math.Max(0, cap - used);

            cards.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                Name = "On-Demand",
                CardId = OnDemandCardId,
                GroupId = this.ProviderId,
                IsAvailable = true,
                UsedPercent = onDemandPercent,
                RequestsUsed = used,
                RequestsAvailable = cap,
                DisplayAsFraction = true,
                PlanType = this.Definition.PlanType,
                IsQuotaBased = this.Definition.IsQuotaBased,
                Description = $"{remaining.ToString(CultureInfo.InvariantCulture)} / {cap.ToString(CultureInfo.InvariantCulture)} on-demand credits remaining",
                AuthSource = authSource ?? string.Empty,
                RawJson = rawJson,
                HttpStatus = httpStatus,
            });
        }

        if (cards.Count == 0)
        {
            cards.Add(new StatusProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = true,
                Description = "Connected (no billing data reported)",
                AuthSource = authSource ?? string.Empty,
                RawJson = rawJson,
                HttpStatus = httpStatus,
            });
        }

        return cards;
    }

    private static string? FormatProductSummary(List<GrokProductUsage>? productUsage)
    {
        if (productUsage is not { Count: > 0 })
        {
            return null;
        }

        var parts = productUsage
            .Where(product => !string.IsNullOrWhiteSpace(product.Product))
            .Select(product => $"{product.Product} {product.UsagePercent?.ToString("F0", CultureInfo.InvariantCulture) ?? "?"}%");
        var summary = string.Join(", ", parts);
        return string.IsNullOrEmpty(summary) ? null : summary;
    }

    private async Task<string?> LoadNativeTokenAsync(CancellationToken cancellationToken)
    {
        foreach (var path in this.GetAuthFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var authData = ProviderAuthFileSchemaReader.Read(doc.RootElement, StaticDefinition.SessionAuthFileSchemas);
                if (authData != null)
                {
                    return authData.AccessToken;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                this._logger.LogDebug(ex, "Failed to read Grok auth file at {Path}", path);
            }
        }

        return null;
    }

    private IEnumerable<string> GetAuthFileCandidates()
    {
        if (!string.IsNullOrWhiteSpace(this._authFilePath))
        {
            yield return this._authFilePath;
            yield break;
        }

        var discoverySpec = StaticDefinition.CreateAuthDiscoverySpec();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in ProviderAuthCandidatePathResolver.ResolvePaths(discoverySpec, userProfile))
        {
            yield return path;
        }
    }

    private static bool TryReadIsoTimestamp(string? raw, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            timestamp = parsed;
            return true;
        }

        return false;
    }

    private sealed class GrokBillingResponse
    {
        [JsonPropertyName("config")]
        public GrokBillingConfig? Config { get; set; }
    }

    private sealed class GrokBillingConfig
    {
        [JsonPropertyName("currentPeriod")]
        public GrokUsagePeriod? CurrentPeriod { get; set; }

        [JsonPropertyName("creditUsagePercent")]
        public double? CreditUsagePercent { get; set; }

        [JsonPropertyName("onDemandCap")]
        public GrokCreditValue? OnDemandCap { get; set; }

        [JsonPropertyName("onDemandUsed")]
        public GrokCreditValue? OnDemandUsed { get; set; }

        [JsonPropertyName("productUsage")]
        public List<GrokProductUsage>? ProductUsage { get; set; }

        [JsonPropertyName("billingPeriodEnd")]
        public string? BillingPeriodEnd { get; set; }
    }

    private sealed class GrokUsagePeriod
    {
        [JsonPropertyName("end")]
        public string? End { get; set; }
    }

    // The billing API returns credit values as {"val": <number>}; string tolerance mirrors
    // the defensive parsing used for the Kimi API.
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    private sealed class GrokCreditValue
    {
        [JsonPropertyName("val")]
        public double Val { get; set; }
    }

    private sealed class GrokProductUsage
    {
        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("usagePercent")]
        public double? UsagePercent { get; set; }
    }
}
