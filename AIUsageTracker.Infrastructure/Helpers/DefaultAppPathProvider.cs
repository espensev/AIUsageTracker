// <copyright file="DefaultAppPathProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Infrastructure.Helpers;

public class DefaultAppPathProvider : IAppPathProvider
{
    private const string AppDirectoryName = "AIUsageTracker";
    private readonly string _localAppDataRoot;

    public DefaultAppPathProvider()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public DefaultAppPathProvider(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);
        this._localAppDataRoot = localAppDataRoot;
    }

    public string GetAppDataRoot()
    {
        return GetCanonicalAppDataRoot(this._localAppDataRoot);
    }

    public string GetDatabasePath()
    {
        return GetCanonicalDatabasePath(this._localAppDataRoot);
    }

    public string GetLogDirectory()
    {
        return GetCanonicalLogDirectory(this._localAppDataRoot);
    }

    public string GetAuthFilePath()
    {
        var home = this.GetUserProfileRoot();
        return GetCanonicalAuthFilePath(home);
    }

    public string GetPreferencesFilePath()
    {
        return GetCanonicalPreferencesPath(this._localAppDataRoot);
    }

    public string GetProviderConfigFilePath()
    {
        return GetCanonicalProviderConfigPath(this._localAppDataRoot);
    }

    public string GetMonitorInfoFilePath()
    {
        return Path.Join(this.GetAppDataRoot(), "monitor.json");
    }

    public string GetUserProfileRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    private static string GetCanonicalAppDataRoot(string localAppDataRoot)
    {
        return Path.Join(localAppDataRoot, AppDirectoryName);
    }

    private static string GetCanonicalDatabasePath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "usage.db");
    }

    private static string GetCanonicalLogDirectory(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "logs");
    }

    private static string GetCanonicalPreferencesPath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "preferences.json");
    }

    private static string GetCanonicalProviderConfigPath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "providers.json");
    }

    private static string GetCanonicalAuthFilePath(string userProfileRoot)
    {
        return Path.Join(userProfileRoot, ".opencode", "auth.json");
    }
}
