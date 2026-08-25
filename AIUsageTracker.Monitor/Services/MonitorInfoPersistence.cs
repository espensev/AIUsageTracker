// <copyright file="MonitorInfoPersistence.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

internal static class MonitorInfoPersistence
{
    private const int AccessTokenByteLength = 32;

    public static string GetOrCreateAccessToken(IAppPathProvider pathProvider, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        var infoPath = pathProvider.GetMonitorInfoFilePath();
        try
        {
            if (File.Exists(infoPath))
            {
                var existingJson = File.ReadAllText(infoPath);
                var existingInfo = JsonSerializer.Deserialize<MonitorInfo>(
                    existingJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (IsValidAccessToken(existingInfo?.AccessToken))
                {
                    return existingInfo!.AccessToken!;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(ex, "Failed to read the existing Monitor access token; generating a replacement.");
        }

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(AccessTokenByteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static void SaveMonitorInfo(
        int port,
        bool debug,
        ILogger logger,
        IAppPathProvider pathProvider,
        string? startupStatus = null,
        string? accessToken = null)
    {
        accessToken = IsValidAccessToken(accessToken)
            ? accessToken
            : GetOrCreateAccessToken(pathProvider, logger);

        var info = new MonitorInfo
        {
            Port = port,
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ProcessId = Environment.ProcessId,
            DebugMode = debug,
            Errors = new List<string>(),
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            AccessToken = accessToken,
        };

        if (!string.IsNullOrEmpty(startupStatus))
        {
            var errors = info.Errors?.ToList() ?? new List<string>();
            errors.Add($"Startup status: {startupStatus}");
            info.Errors = errors;
        }

        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        var infoPath = pathProvider.GetMonitorInfoFilePath();

        try
        {
            WriteProtectedMonitorInfo(infoPath, json, logger);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidOperationException or
            System.Security.SecurityException)
        {
            logger.LogError(ex, "Failed to write monitor info to {MonitorInfoPath}", infoPath);
            throw;
        }
    }

    public static void ReportError(string message, IAppPathProvider pathProvider, ILogger? logger = null)
    {
        var jsonFile = pathProvider.GetMonitorInfoFilePath();
        if (!File.Exists(jsonFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonFile);
            var info = JsonSerializer.Deserialize<MonitorInfo>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info == null)
            {
                return;
            }

            var errors = info.Errors?.ToList() ?? new List<string>();
            errors.Add(message);
            info.Errors = errors;
            var updatedJson = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            WriteProtectedMonitorInfo(jsonFile, updatedJson, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(ex, "Failed to report error to monitor info");
        }
    }

    private static bool IsValidAccessToken(string? accessToken)
    {
        return !string.IsNullOrWhiteSpace(accessToken) && accessToken.Length >= 43;
    }

    private static void WriteProtectedMonitorInfo(string infoPath, string json, ILogger? logger)
    {
        var fullInfoPath = Path.GetFullPath(infoPath);
        var directory = Path.GetDirectoryName(fullInfoPath)
            ?? throw new InvalidOperationException($"Monitor metadata path has no parent directory: {infoPath}");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".monitor.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            RestrictMetadataAccess(temporaryPath);
            File.Move(temporaryPath, fullInfoPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(ex, "Failed to remove temporary Monitor metadata {MonitorInfoPath}", temporaryPath);
                }
            }
        }
    }

    private static void RestrictMetadataAccess(string infoPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(infoPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetOwner(userSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new FileInfo(infoPath), security);
    }
}
