// <copyright file="AuthPathTemplateResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;

namespace AIUsageTracker.Core.Paths;

public static class AuthPathTemplateResolver
{
    public static string Resolve(string pathTemplate, string userProfileRoot)
    {
        return Resolve(pathTemplate, userProfileRoot, Environment.GetEnvironmentVariable);
    }

    /// <summary>
    /// Expands the <c>%NAME%</c> tokens of an auth-file path template.
    /// </summary>
    /// <remarks>
    /// The profile tokens (<c>%USERPROFILE%</c>, <c>%APPDATA%</c>, <c>%LOCALAPPDATA%</c>) always
    /// derive from <paramref name="userProfileRoot"/>. Any other token is a CLI home override
    /// such as <c>%GROK_HOME%</c> or <c>%CODEX_HOME%</c> and is read through
    /// <paramref name="environmentLookup"/>; when that variable is unset the template yields an
    /// empty string so the caller drops the candidate instead of probing a literal path.
    /// </remarks>
    public static string Resolve(
        string pathTemplate,
        string userProfileRoot,
        Func<string, string?> environmentLookup)
    {
        ArgumentNullException.ThrowIfNull(pathTemplate);
        ArgumentNullException.ThrowIfNull(userProfileRoot);
        ArgumentNullException.ThrowIfNull(environmentLookup);

        var resolved = new StringBuilder(pathTemplate.Length + userProfileRoot.Length);
        var position = 0;

        while (position < pathTemplate.Length)
        {
            var open = pathTemplate.IndexOf('%', position);
            if (open < 0)
            {
                break;
            }

            var close = pathTemplate.IndexOf('%', open + 1);
            var name = close < 0 ? string.Empty : pathTemplate[(open + 1)..close];
            if (!IsTokenName(name))
            {
                // A lone or path-spanning '%' is part of the path, not a token.
                resolved.Append(pathTemplate, position, open + 1 - position);
                position = open + 1;
                continue;
            }

            var value = ResolveToken(name, userProfileRoot, environmentLookup);
            if (value == null)
            {
                return string.Empty;
            }

            // Substituted values are appended verbatim and never rescanned for tokens.
            resolved.Append(pathTemplate, position, open - position).Append(value);
            position = close + 1;
        }

        return resolved.Append(pathTemplate, position, pathTemplate.Length - position).ToString();
    }

    private static string? ResolveToken(
        string name,
        string userProfileRoot,
        Func<string, string?> environmentLookup)
    {
        if (string.Equals(name, "USERPROFILE", StringComparison.OrdinalIgnoreCase))
        {
            return userProfileRoot;
        }

        if (string.Equals(name, "APPDATA", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(userProfileRoot, "AppData", "Roaming");
        }

        if (string.Equals(name, "LOCALAPPDATA", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(userProfileRoot, "AppData", "Local");
        }

        var value = environmentLookup(name)?.Trim().TrimEnd('\\', '/');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool IsTokenName(string name)
    {
        return name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
