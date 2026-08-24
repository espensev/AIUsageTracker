// <copyright file="ProviderConfigResponse.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class ProviderConfigResponse
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("has_key")]
    public bool HasKey { get; init; }

    [JsonPropertyName("is_session_token")]
    public bool IsSessionToken { get; init; }

    [JsonPropertyName("limit")]
    public double? Limit { get; init; }

    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; init; }

    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; init; }

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; init; }

    [JsonPropertyName("enabled_sub_trays")]
    public IReadOnlyList<string> EnabledSubTrays { get; init; } = [];

    [JsonPropertyName("auth_source")]
    public string AuthSource { get; init; } = string.Empty;

    [JsonPropertyName("models")]
    public IReadOnlyList<AIModelConfig> Models { get; init; } = [];

    [JsonPropertyName("show_cached_models_when_offline")]
    public bool ShowCachedModelsWhenOffline { get; init; }

    public static ProviderConfigResponse FromProviderConfig(ProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var hasKey = !string.IsNullOrWhiteSpace(config.ApiKey);
        return new ProviderConfigResponse
        {
            ProviderId = config.ProviderId,
            HasKey = hasKey,
            IsSessionToken = hasKey && IsSessionCredential(config.ApiKey),
            Limit = config.Limit,
            BaseUrl = config.BaseUrl,
            ShowInTray = config.ShowInTray,
            EnableNotifications = config.EnableNotifications,
            EnabledSubTrays = config.EnabledSubTrays,
            AuthSource = config.AuthSource,
            Models = config.Models,
            ShowCachedModelsWhenOffline = config.ShowCachedModelsWhenOffline,
        };
    }

    public ProviderConfig ToProviderConfig()
    {
        return new ProviderConfig
        {
            ProviderId = this.ProviderId,
            ApiKey = string.Empty,
            HasStoredApiKey = this.HasKey,
            HasStoredSessionToken = this.HasKey && this.IsSessionToken,
            Limit = this.Limit,
            BaseUrl = this.BaseUrl,
            ShowInTray = this.ShowInTray,
            EnableNotifications = this.EnableNotifications,
            EnabledSubTrays = this.EnabledSubTrays,
            AuthSource = this.AuthSource,
            Models = this.Models,
            ShowCachedModelsWhenOffline = this.ShowCachedModelsWhenOffline,
        };
    }

    private static bool IsSessionCredential(string apiKey)
    {
        return !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }
}
