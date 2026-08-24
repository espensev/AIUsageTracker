// <copyright file="ProviderAuthFileSchemaReader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class ProviderAuthFileSchemaReader
{
    public static ProviderAuthData? Read(
        JsonElement root,
        IEnumerable<ProviderAuthFileSchema> schemas)
        => Read(root, schemas, DateTimeOffset.UtcNow);

    public static ProviderAuthData? Read(
        JsonElement root,
        IEnumerable<ProviderAuthFileSchema> schemas,
        DateTimeOffset utcNow)
    {
        foreach (var schema in schemas)
        {
            // A wildcard root can match multiple entries (e.g. two client-id roots left behind
            // by a CLI upgrade). Selection rule: an unexpired entry beats an expired one, then
            // the newest created-at wins, then the first entry in file order. Schemas without
            // timestamp properties keep the original first-non-empty-token behavior.
            ProviderAuthData? best = null;
            var bestRank = (NotExpired: false, CreatedAt: DateTimeOffset.MinValue);

            foreach (var sessionRoot in ResolveSchemaRoots(root, schema.RootProperty))
            {
                if (sessionRoot.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var accessToken = sessionRoot.ReadString(schema.AccessTokenProperty);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    continue;
                }

                var expiresAt = ReadTimestamp(sessionRoot, schema.ExpiresAtProperty);
                var rank = (
                    NotExpired: expiresAt == null || expiresAt > utcNow,
                    CreatedAt: ReadTimestamp(sessionRoot, schema.CreatedAtProperty) ?? DateTimeOffset.MinValue);
                if (best != null && rank.CompareTo(bestRank) <= 0)
                {
                    continue;
                }

                var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty)
                    ? sessionRoot.ReadString(schema.AccountIdProperty)
                    : null;

                var identityToken = !string.IsNullOrWhiteSpace(schema.IdentityTokenProperty)
                    ? sessionRoot.ReadString(schema.IdentityTokenProperty)
                    : null;

                best = new ProviderAuthData(accessToken, accountId, identityToken);
                bestRank = rank;
            }

            if (best != null)
            {
                return best;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement sessionRoot, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var raw = sessionRoot.ReadString(propertyName);
        return !string.IsNullOrWhiteSpace(raw) &&
               DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static IEnumerable<JsonElement> ResolveSchemaRoots(
        JsonElement root,
        string rootProperty)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        // Wildcard schemas ("<prefix>*") match any root property by prefix. This supports
        // issuer-scoped auth stores (e.g. "https://auth.x.ai::*" for the Grok CLI) whose
        // property names embed a dynamic segment (the OIDC client id). Such names contain
        // dots and must be matched whole instead of being dot-navigated.
        if (rootProperty.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = rootProperty[..^1];
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    yield return property.Value;
                }
            }

            yield break;
        }

        var parts = rootProperty.Split('.');
        var lastPart = parts[^1];

        var navigated = parts.Aggregate<string, JsonElement?>(
            root,
            (current, part) =>
            {
                if (!current.HasValue || !current.Value.TryGetProperty(part, out var next))
                {
                    return null;
                }

                return next.ValueKind != JsonValueKind.Object && !string.Equals(part, lastPart, StringComparison.Ordinal)
                    ? null
                    : next;
            });

        if (navigated.HasValue)
        {
            yield return navigated.Value;
        }
    }
}
