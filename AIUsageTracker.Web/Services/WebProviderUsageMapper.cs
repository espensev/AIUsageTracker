// <copyright file="WebProviderUsageMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Web.Services;

internal static class WebProviderUsageMapper
{
    public static ProviderUsage Map(object row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var providerId = GetString(row, "provider_id");
        var definition = ProviderMetadataCatalog.Find(providerId);
        var providerName = GetString(row, "ProviderName");
        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
        }

        var isAvailable = GetBoolean(row, "is_available", defaultValue: true);
        var cardType = GetString(row, "card_type");
        ProviderUsage usage = cardType.ToLowerInvariant() switch
        {
            "status" => new StatusProviderUsage(),
            "model" => new ModelScopedProviderUsage
            {
                ModelName = GetNullableString(row, "model_name"),
            },
            "windowed" => new WindowedProviderUsage
            {
                ParentProviderId = GetNullableString(row, "parent_provider_id"),
            },
            _ => new QuotaProviderUsage(),
        };

        usage.ProviderId = providerId;
        usage.ProviderName = providerName;
        usage.IsAvailable = isAvailable;
        usage.State = isAvailable ? ProviderUsageState.Available : ProviderUsageState.Unavailable;
        usage.Description = GetString(row, "status_message");
        usage.AuthSource = GetString(row, "AuthSource");
        usage.AccountName = GetString(row, "AccountName");
        usage.FetchedAt = ParseDateTimeUtc(GetValue(row, "fetched_at"));
        usage.ResponseLatencyMs = GetDouble(row, "response_latency_ms");
        usage.HttpStatus = GetInt32(row, "http_status", 200);
        usage.UpstreamResponseValidity = GetEnum(
            row,
            "upstream_response_validity",
            UpstreamResponseValidity.Unknown);
        usage.UpstreamResponseNote = GetString(row, "upstream_response_note");
        usage.IsTooltipOnly = definition?.IsTooltipOnly ?? false;

        if (usage is not QuotaProviderUsage quota)
        {
            return usage;
        }

        quota.RequestsUsed = GetDouble(row, "requests_used");
        quota.RequestsAvailable = GetDouble(row, "requests_available");
        quota.UsedPercent = GetDouble(row, "requests_percentage");
        quota.NextResetTime = ParseNullableDateTimeUtc(GetValue(row, "next_reset_time"));
        quota.WindowKind = GetEnum(row, "window_kind", WindowKind.None);
        quota.Name = GetNullableString(row, "name");
        quota.CardId = GetNullableString(row, "card_id");
        quota.GroupId = GetNullableString(row, "group_id");
        quota.ResetCreditsAvailable = GetNullableInt32(row, "reset_credits_available");
        quota.ResetCreditExpirationsUtc = ParseResetCreditExpirations(
            GetNullableString(row, "reset_credit_expirations_utc"));

        if (definition == null)
        {
            return quota;
        }

        quota.PlanType = definition.PlanType;
        quota.IsQuotaBased = definition.IsQuotaBased;
        quota.IsCurrencyUsage = definition.IsCurrencyUsage;
        quota.IsStatusOnly = definition.IsStatusOnly;
        quota.DisplayAsFraction = definition.DisplayAsFraction;

        var windowDefinition = FindWindowDefinition(definition, quota.CardId);
        if (windowDefinition != null)
        {
            quota.WindowKind = windowDefinition.Kind;
            quota.Name = windowDefinition.DualBarLabel;
            quota.PeriodDuration = windowDefinition.PeriodDuration;
            quota.DisplayAsFraction = windowDefinition.DisplayAsFraction;
        }

        return quota;
    }

    private static QuotaWindowDefinition? FindWindowDefinition(
        ProviderDefinition definition,
        string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        var childProviderId = $"{definition.ProviderId}.{cardId}";
        return definition.QuotaWindows.FirstOrDefault(window =>
            string.Equals(window.ChildProviderId, childProviderId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DateTime>? ParseResetCreditExpirations(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<long[]>(raw)?
                .Select(ticks => new DateTime(ticks, DateTimeKind.Utc))
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static object? GetValue(object row, string name)
    {
        if (row is IDictionary<string, object> values && values.TryGetValue(name, out var value))
        {
            return value;
        }

        var property = row.GetType().GetProperties()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return property?.GetValue(row);
    }

    private static string GetString(object row, string name)
    {
        return GetNullableString(row, name) ?? string.Empty;
    }

    private static string? GetNullableString(object row, string name)
    {
        var value = GetValue(row, name);
        return value == null || value is DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool GetBoolean(object row, string name, bool defaultValue)
    {
        var value = GetValue(row, name);
        if (value == null || value is DBNull)
        {
            return defaultValue;
        }

        return value is bool boolValue
            ? boolValue
            : Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
    }

    private static double GetDouble(object row, string name)
    {
        var value = GetValue(row, name);
        return value == null || value is DBNull
            ? 0
            : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static int GetInt32(object row, string name, int defaultValue)
    {
        var value = GetValue(row, name);
        return value == null || value is DBNull
            ? defaultValue
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt32(object row, string name)
    {
        var value = GetValue(row, name);
        return value == null || value is DBNull
            ? null
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static TEnum GetEnum<TEnum>(object row, string name, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        var value = GetValue(row, name);
        if (value == null || value is DBNull)
        {
            return defaultValue;
        }

        var numericValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return Enum.IsDefined(typeof(TEnum), numericValue)
            ? (TEnum)Enum.ToObject(typeof(TEnum), numericValue)
            : defaultValue;
    }

    private static DateTime? ParseNullableDateTimeUtc(object? value)
    {
        if (value == null || value is DBNull)
        {
            return null;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ParseDateTimeUtc(value);
    }

    private static DateTime ParseDateTimeUtc(object? value)
    {
        if (value == null || value is DBNull)
        {
            return DateTime.UtcNow;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.UtcDateTime;
        }

        if (TryParseEpochSeconds(value, out var epochSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        }

        if (DateTime.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static bool TryParseEpochSeconds(object value, out long epochSeconds)
    {
        switch (value)
        {
            case long longValue:
                epochSeconds = longValue;
                return true;
            case int intValue:
                epochSeconds = intValue;
                return true;
            case short shortValue:
                epochSeconds = shortValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                epochSeconds = Convert.ToInt64(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case decimal decimalValue:
                epochSeconds = Convert.ToInt64(decimalValue, CultureInfo.InvariantCulture);
                return true;
            case string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                epochSeconds = parsed;
                return true;
            default:
                epochSeconds = 0;
                return false;
        }
    }
}
