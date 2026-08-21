// <copyright file="ProviderAuthFileSchemaReader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class ProviderAuthFileSchemaReader
{
    public static ProviderAuthData? Read(
        JsonElement root,
        IEnumerable<ProviderAuthFileSchema> schemas)
    {
        foreach (var schema in schemas)
        {
            if (!TryResolveSchemaRoot(root, schema.RootProperty, out var sessionRoot) ||
                sessionRoot.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var accessToken = sessionRoot.ReadString(schema.AccessTokenProperty);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                continue;
            }

            var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty)
                ? sessionRoot.ReadString(schema.AccountIdProperty)
                : null;

            var identityToken = !string.IsNullOrWhiteSpace(schema.IdentityTokenProperty)
                ? sessionRoot.ReadString(schema.IdentityTokenProperty)
                : null;

            return new ProviderAuthData(accessToken, accountId, identityToken);
        }

        return null;
    }

    private static bool TryResolveSchemaRoot(
        JsonElement root,
        string rootProperty,
        out JsonElement sessionRoot)
    {
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
                    sessionRoot = property.Value;
                    return true;
                }
            }

            sessionRoot = default;
            return false;
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

        sessionRoot = navigated ?? default;
        return navigated.HasValue;
    }
}
