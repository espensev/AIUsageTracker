// <copyright file="ProviderConfigUpdateRequest.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class ProviderConfigUpdateRequest
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; init; } = string.Empty;

    [JsonPropertyName("preserve_api_key")]
    public bool PreserveApiKey { get; init; }

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

    public static ProviderConfigUpdateRequest FromProviderConfig(ProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new ProviderConfigUpdateRequest
        {
            ProviderId = config.ProviderId,
            ApiKey = config.ApiKey,
            PreserveApiKey = config.HasStoredApiKey && string.IsNullOrWhiteSpace(config.ApiKey),
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

    public ProviderConfig ToProviderConfig(ProviderConfig? existing)
    {
        return new ProviderConfig
        {
            ProviderId = this.ProviderId,
            ApiKey = this.PreserveApiKey ? existing?.ApiKey ?? string.Empty : this.ApiKey,
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
}
