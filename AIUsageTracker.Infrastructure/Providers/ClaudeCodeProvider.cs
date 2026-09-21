// <copyright file="ClaudeCodeProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class ClaudeCodeProvider : ProviderBase
{
    /// <summary>
    /// The OAuth usage endpoint for Claude subscriptions.
    /// </summary>
    internal const string OAuthUsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    /// <summary>
    /// The beta header required for OAuth usage endpoint.
    /// </summary>
    internal const string OAuthBetaHeader = "oauth-2025-04-20";

    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";

    private readonly ILogger<ClaudeCodeProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _credentialsFilePath;

    public ClaudeCodeProvider(ILogger<ClaudeCodeProvider> logger, HttpClient httpClient, string? credentialsFilePath = null)
    {
        this._logger = logger;
        this._httpClient = httpClient;
        this._credentialsFilePath = credentialsFilePath;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "claude-code",
        "Claude Code",
        PlanType.Usage,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "ANTHROPIC_API_KEY", "CLAUDE_API_KEY" },
        IconAssetName = "anthropic",
        BadgeColorHex = "#FFA500",
        BadgeInitial = "C",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.claude\\.credentials.json",
        },
        SessionAuthFileSchemas = new[]
        {
            new ProviderAuthFileSchema("claudeAiOauth", "accessToken"),
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.None, "Current Session", ChildProviderId: "claude-code.current-session", SettingsLabel: "Current Session (5-hour quota)", DetailName: "Current Session", PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.None, "Sonnet",          ChildProviderId: "claude-code.sonnet",          SettingsLabel: "Sonnet (7-day model quota)",    DetailName: "Sonnet",          PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.None, "Opus",            ChildProviderId: "claude-code.opus",            SettingsLabel: "Opus (7-day model quota)",      DetailName: "Opus",            PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.None, "All Models",      ChildProviderId: "claude-code.all-models",      SettingsLabel: "All Models (7-day combined)",   DetailName: "All Models",      PeriodDuration: TimeSpan.FromDays(7)),
        },
        FamilyMode = ProviderFamilyMode.FlatWindowCards,
        FlatCardShowProviderPrefix = true,
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        // Claude Code quotas belong to the current CLI session. A legacy key in
        // shared provider configuration must not hide a valid native OAuth token.
        var nativeToken = this.ReadFreshOAuthToken();
        var effectiveApiKey = nativeToken ?? config.ApiKey;
        if (string.IsNullOrEmpty(effectiveApiKey))
        {
            return new[]
            {
                new StatusProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = false,
                Description = "No API key configured",
                State = ProviderUsageState.Missing,
                RawJson = "{\"source\":\"claude-code\",\"status\":\"api_key_missing\"}",
                HttpStatus = 401,
            },
            };
        }

        var isOAuthToken = effectiveApiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);

        // Try OAuth usage endpoint first (for subscription users)
        var failureStatus = 0;
        try
        {
            var (oauthUsages, oauthFailureStatus) = await this.TryGetUsageFromOAuthAsync(effectiveApiKey, providerLabel).ConfigureAwait(false);
            if (oauthUsages != null)
            {
                if (nativeToken != null)
                {
                    foreach (var usage in oauthUsages)
                    {
                        usage.AuthSource = "Claude Code CLI session auth";
                    }
                }

                return oauthUsages;
            }

            failureStatus = oauthFailureStatus;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogDebug(ex, "OAuth usage endpoint not available, trying rate limit headers");
        }

        // Skip the API rate-limit probe when the token is an OAuth token — it will
        // always return 401 because OAuth tokens are not API keys.
        if (!isOAuthToken)
        {
            try
            {
                var (apiUsage, apiFailureStatus) = await this.GetUsageFromApiAsync(effectiveApiKey, providerLabel).ConfigureAwait(false);
                if (apiUsage != null)
                {
                    return new[] { apiUsage };
                }

                if (apiFailureStatus != 0)
                {
                    failureStatus = apiFailureStatus;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                this._logger.LogWarning(ex, "Failed to get Claude usage from API");
            }
        }

        // Neither source answered. Report that rather than shelling out to the claude CLI:
        // it has no usage subcommand, so "claude usage" starts a full agent session.
        var unavailable = this.CreateUsageUnavailable(failureStatus, providerLabel);
        unavailable.AuthSource = nativeToken != null ? "Claude Code CLI session auth" : config.AuthSource;
        return new[] { unavailable };
    }

    /// <summary>
    /// Gets usage information from the OAuth usage endpoint for subscription users.
    /// </summary>
    /// <param name="accessToken">The OAuth access token from credentials file.</param>
    /// <returns>Provider usages if successful, null otherwise.</returns>
    internal async Task<IEnumerable<ProviderUsage>?> GetUsageFromOAuthAsync(string accessToken, string providerLabel)
    {
        var (usages, _) = await this.TryGetUsageFromOAuthAsync(accessToken, providerLabel).ConfigureAwait(false);
        return usages;
    }

    /// <summary>
    /// Gets usage from the OAuth usage endpoint, keeping the HTTP status of a refusal.
    /// </summary>
    /// <returns>The usages, or null with the refusing HTTP status (0 when there was none).</returns>
    private async Task<(IEnumerable<ProviderUsage>? Usages, int FailureStatus)> TryGetUsageFromOAuthAsync(string accessToken, string providerLabel)
    {
        try
        {
            var (statusCode, responseBody) = await this.SendOAuthRequestAsync(accessToken).ConfigureAwait(false);

            if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                this._logger.LogDebug("OAuth usage endpoint returned 429, retrying once after 2s");
                await Task.Delay(2000).ConfigureAwait(false);
                (statusCode, responseBody) = await this.SendOAuthRequestAsync(accessToken).ConfigureAwait(false);
            }

            if ((int)statusCode < 200 || (int)statusCode >= 300)
            {
                this._logger.LogDebug("OAuth usage endpoint returned {StatusCode}: {Body}", statusCode, responseBody);
                return (null, (int)statusCode);
            }

            var usageResponse = JsonSerializer.Deserialize<OAuthUsageResponse>(responseBody);
            if (usageResponse == null)
            {
                this._logger.LogWarning("Failed to deserialize OAuth usage response");
                return (null, 0);
            }

            return (this.ParseOAuthUsageResponse(usageResponse, responseBody, (int)statusCode, providerLabel), 0);
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogDebug(ex, "OAuth usage endpoint request failed");
            return (null, 0);
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse OAuth usage response");
            return (null, 0);
        }
    }

    private StatusProviderUsage CreateUsageUnavailable(int failureStatus, string providerLabel)
    {
        StatusProviderUsage usage;
        if (failureStatus == 0)
        {
            usage = this.CreateUnavailableUsage("Usage data unavailable", state: ProviderUsageState.Unavailable);
        }
        else
        {
            var description = DescribeUnavailableStatus((System.Net.HttpStatusCode)failureStatus);
            usage = this.CreateUnavailableUsage(description, failureStatus, failureContext: HttpFailureContext.FromHttpStatus(failureStatus, description));
        }

        usage.ProviderName = providerLabel;
        return usage;
    }

    private async Task<(System.Net.HttpStatusCode StatusCode, string Body)> SendOAuthRequestAsync(string accessToken)
    {
        var request = CreateBearerRequest(HttpMethod.Get, OAuthUsageEndpoint, accessToken);
        request.Headers.Add("anthropic-beta", OAuthBetaHeader);

        using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    /// <summary>
    /// Re-reads the OAuth access token from ~/.claude/.credentials.json.
    /// The Claude Code CLI refreshes this file when the token expires.
    /// </summary>
    private string? ReadFreshOAuthToken()
    {
        try
        {
            var credentialsPath = this._credentialsFilePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                ".credentials.json");

            if (!File.Exists(credentialsPath))
            {
                return null;
            }

            var json = File.ReadAllText(credentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            this._logger.LogDebug("Re-read fresh OAuth token from credentials file ({Length} chars)", token.Length);
            return token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this._logger.LogDebug(ex, "Failed to re-read OAuth token from credentials file");
            return null;
        }
    }

    private List<ProviderUsage> ParseOAuthUsageResponse(OAuthUsageResponse response, string rawJson, int httpStatus, string providerLabel)
    {
        var results = new List<ProviderUsage>();

        if (response.FiveHour != null)
        {
            results.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "current-session",
                GroupId = this.ProviderId,
                Name = "Current Session",
                UsedPercent = UsageMath.ClampPercent(response.FiveHour.Utilization),
                NextResetTime = response.FiveHour.ResetsAt,
                PeriodDuration = TimeSpan.FromHours(5),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.FiveHour.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% used",
            });
        }

        if (response.SevenDaySonnet != null)
        {
            results.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "sonnet",
                GroupId = this.ProviderId,
                Name = "Sonnet",
                UsedPercent = UsageMath.ClampPercent(response.SevenDaySonnet.Utilization),
                NextResetTime = response.SevenDay?.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.SevenDaySonnet.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% used",
            });
        }

        if (response.SevenDayOpus != null)
        {
            results.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "opus",
                GroupId = this.ProviderId,
                Name = "Opus",
                UsedPercent = UsageMath.ClampPercent(response.SevenDayOpus.Utilization),
                NextResetTime = response.SevenDay?.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.SevenDayOpus.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% used",
            });
        }

        // All-models 7-day rolling quota
        if (response.SevenDay != null)
        {
            var desc = response.FiveHour != null
                ? $"5h: {response.FiveHour.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% | 7d: {response.SevenDay.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% used"
                : $"7d: {response.SevenDay.Utilization.ToString("F0", CultureInfo.InvariantCulture)}% used";
            if (response.ExtraUsage?.IsEnabled == true)
            {
                desc += " | Extra usage enabled";
            }

            results.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "all-models",
                GroupId = this.ProviderId,
                Name = "All Models",
                UsedPercent = UsageMath.ClampPercent(response.SevenDay.Utilization),
                NextResetTime = response.SevenDay.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = desc,
            });
        }

        if (results.Count == 0)
        {
            results.Add(new StatusProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = "Usage data unavailable",
            });
        }

        return results;
    }

    private async Task<(ProviderUsage? Usage, int FailureStatus)> GetUsageFromApiAsync(string apiKey, string providerLabel)
    {
        try
        {
            // Make a test request to get rate limit headers
            // Note: Anthropic API doesn't have a usage endpoint, so we use rate limits from headers
            using var testRequest = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint);
            testRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            testRequest.Headers.Add("anthropic-version", "2023-06-01");
            testRequest.Content = new StringContent("{\"model\":\"claude-sonnet-4-20250514\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");

            using var testResponse = await this._httpClient.SendAsync(testRequest).ConfigureAwait(false);
            var responseBody = await testResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Extract rate limit information from headers
            var rateLimitHeaders = ExtractRateLimitInfo(testResponse.Headers);

            // Log response for debugging
            this._logger.LogDebug("Claude API test call: Status={StatusCode}, RPM={RequestsRemaining}/{RequestsLimit}", testResponse.StatusCode, rateLimitHeaders.RequestsRemaining, rateLimitHeaders.RequestsLimit);

            // Even if the request fails (e.g., 429 rate limited), we can still get rate limit headers
            if (rateLimitHeaders.RequestsLimit > 0)
            {
                // Calculate usage percentage based on rate limits
                double usagePercentage = 0;
                string? warningMessage = null;

                // Calculate percentage: (limit - remaining) / limit * 100
                var used = rateLimitHeaders.RequestsLimit - rateLimitHeaders.RequestsRemaining;
                usagePercentage = (used / (double)rateLimitHeaders.RequestsLimit) * 100.0;

                // Determine warning level
                if (usagePercentage >= 90)
                {
                    warningMessage = "⚠️ CRITICAL: Approaching rate limit!";
                }
                else if (usagePercentage >= 70)
                {
                    warningMessage = "⚠️ WARNING: High usage";
                }

                // Build description with rate limit info
                var description = $"Tier: {rateLimitHeaders.GetTierName()} | RPM: {rateLimitHeaders.RequestsRemaining.ToString(CultureInfo.InvariantCulture)}/{rateLimitHeaders.RequestsLimit.ToString(CultureInfo.InvariantCulture)} | Tokens/min: {rateLimitHeaders.InputTokensRemaining.ToString(CultureInfo.InvariantCulture)}/{rateLimitHeaders.InputTokensLimit.ToString(CultureInfo.InvariantCulture)}";

                var usage = new QuotaProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    UsedPercent = usagePercentage,
                    RequestsUsed = 0, // Anthropic doesn't provide cost via API
                    RequestsAvailable = 0,
                    IsQuotaBased = false,
                    PlanType = this.Definition.PlanType,
                    IsAvailable = true,
                    Description = description,
                    AccountName = warningMessage ?? string.Empty, // Using AccountName to carry warning state
                    RawJson = responseBody,
                    HttpStatus = (int)testResponse.StatusCode,
                };
                return (usage, 0);
            }

            // No rate limit headers found
            return (null, testResponse.IsSuccessStatusCode ? 0 : (int)testResponse.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "Error calling Anthropic API");
            return (null, 0);
        }
    }

    private static RateLimitInfo ExtractRateLimitInfo(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        return new RateLimitInfo
        {
            RequestsLimit = (int)(TryGetHeaderDouble(headers, "anthropic-ratelimit-requests-limit") ?? 0),
            RequestsRemaining = (int)(TryGetHeaderDouble(headers, "anthropic-ratelimit-requests-remaining") ?? 0),
            InputTokensLimit = (int)(TryGetHeaderDouble(headers, "anthropic-ratelimit-input-tokens-limit") ?? 0),
            InputTokensRemaining = (int)(TryGetHeaderDouble(headers, "anthropic-ratelimit-input-tokens-remaining") ?? 0),
        };
    }

    /// <summary>
    /// Response model for the OAuth usage endpoint.
    /// </summary>
    internal sealed class OAuthUsageResponse
    {
        [JsonPropertyName("five_hour")]
        public OAuthQuotaBucket? FiveHour { get; set; }

        [JsonPropertyName("seven_day")]
        public OAuthQuotaBucket? SevenDay { get; set; }

        [JsonPropertyName("seven_day_sonnet")]
        public OAuthModelQuota? SevenDaySonnet { get; set; }

        [JsonPropertyName("seven_day_opus")]
        public OAuthModelQuota? SevenDayOpus { get; set; }

        [JsonPropertyName("extra_usage")]
        public OAuthExtraUsage? ExtraUsage { get; set; }
    }

    /// <summary>
    /// Quota bucket with utilization percentage and reset time.
    /// </summary>
    internal sealed class OAuthQuotaBucket
    {
        [JsonPropertyName("utilization")]
        public double Utilization { get; set; }

        [JsonPropertyName("resets_at")]
        public DateTime? ResetsAt { get; set; }
    }

    /// <summary>
    /// Model-specific quota information.
    /// </summary>
    internal sealed class OAuthModelQuota
    {
        [JsonPropertyName("utilization")]
        public double Utilization { get; set; }
    }

    /// <summary>
    /// Extra usage (overage) information.
    /// </summary>
    internal sealed class OAuthExtraUsage
    {
        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }
    }

    private sealed class RateLimitInfo
    {
        public int RequestsLimit { get; set; }

        public int RequestsRemaining { get; set; }

        public int InputTokensLimit { get; set; }

        public int InputTokensRemaining { get; set; }

        public string GetTierName()
        {
            // Determine tier based on request limits
            // Tier 1: 50 RPM
            // Tier 2: 1,000 RPM
            // Tier 3: 2,000 RPM
            // Tier 4: 4,000 RPM
            return this.RequestsLimit switch
            {
                <= 50 => "Tier 1",
                <= 1000 => "Tier 2",
                <= 2000 => "Tier 3",
                <= 4000 => "Tier 4",
                _ => "Custom",
            };
        }
    }
}
