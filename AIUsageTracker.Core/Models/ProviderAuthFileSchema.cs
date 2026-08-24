// <copyright file="ProviderAuthFileSchema.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <param name="RootProperty">Root property name; a trailing <c>*</c> matches any property by prefix (wildcard roots).</param>
/// <param name="AccessTokenProperty">Property holding the access token.</param>
/// <param name="AccountIdProperty">Optional property holding the account identifier.</param>
/// <param name="IdentityTokenProperty">Optional property holding an identity token.</param>
/// <param name="CreatedAtProperty">Optional timestamp property used to prefer the newest session when a wildcard root matches multiple entries.</param>
/// <param name="ExpiresAtProperty">Optional expiry timestamp property; expired entries are deprioritized when a wildcard root matches multiple entries.</param>
public sealed record ProviderAuthFileSchema(
    string RootProperty,
    string AccessTokenProperty,
    string? AccountIdProperty = null,
    string? IdentityTokenProperty = null,
    string? CreatedAtProperty = null,
    string? ExpiresAtProperty = null);
