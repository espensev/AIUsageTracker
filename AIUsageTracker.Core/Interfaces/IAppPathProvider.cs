// <copyright file="IAppPathProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public interface IAppPathProvider
{
    string GetAppDataRoot();

    string GetDatabasePath();

    string GetLogDirectory();

    string GetAuthFilePath();

    string GetPreferencesFilePath();

    string GetProviderConfigFilePath();

    string GetMonitorInfoFilePath();

    // Discovery root for external tools (e.g., .claude, .codex)
    string GetUserProfileRoot();

    // Relocation overrides for those tools (e.g., CODEX_HOME, GROK_HOME, CLAUDE_CONFIG_DIR).
    // Part of the same discovery seam: redirecting the profile root must not leave the
    // host's own overrides in effect.
    string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
