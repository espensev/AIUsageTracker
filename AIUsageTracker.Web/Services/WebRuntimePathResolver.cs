// <copyright file="WebRuntimePathResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services;

internal static class WebRuntimePathResolver
{
    private const string AppDirectoryName = "AIUsageTracker";
    private const string LocalAppDataRootEnvironmentVariable = "AIUSAGETRACKER_LOCAL_APP_DATA_ROOT";

    public static string ResolveLocalAppDataRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(LocalAppDataRootEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.GetFullPath(configuredRoot);
    }

    public static WebRuntimePaths Resolve(string localAppDataRoot)
    {
        var appRoot = GetCanonicalAppDataRoot(localAppDataRoot);
        var logDirectory = GetCanonicalLogDirectory(localAppDataRoot);
        var runtimeFallbackRoot = Path.Combine(
            AppContext.BaseDirectory,
            ".runtime",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var writableAppRoot = EnsureWritableDirectory(appRoot, Path.Combine(runtimeFallbackRoot, "app-data"));
        var writableLogDirectory = EnsureWritableDirectory(logDirectory, Path.Combine(runtimeFallbackRoot, "logs"));
        var dataProtectionKeyDirectory = EnsureWritableDirectory(
            Path.Combine(writableAppRoot, "web-data-protection"),
            Path.Combine(runtimeFallbackRoot, "web-data-protection"));
        var databasePath = GetCanonicalDatabasePath(localAppDataRoot);

        return new WebRuntimePaths(
            writableAppRoot,
            writableLogDirectory,
            dataProtectionKeyDirectory,
            databasePath);
    }

    public static string EnsureWritableDirectory(string preferredPath, string fallbackPath)
    {
        if (TryEnsureDirectory(preferredPath))
        {
            return preferredPath;
        }

        Directory.CreateDirectory(fallbackPath);
        return fallbackPath;
    }

    public static string ResolveDatabasePath(string localAppDataRoot)
    {
        return GetCanonicalDatabasePath(localAppDataRoot);
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetCanonicalAppDataRoot(string localAppDataRoot)
    {
        return Path.Combine(localAppDataRoot, AppDirectoryName);
    }

    private static string GetCanonicalLogDirectory(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "logs");
    }

    private static string GetCanonicalDatabasePath(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "usage.db");
    }

    internal readonly record struct WebRuntimePaths(
        string AppRoot,
        string LogDirectory,
        string DataProtectionKeyDirectory,
        string DatabasePath);
}
