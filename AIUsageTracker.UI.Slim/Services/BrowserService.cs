// <copyright file="BrowserService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for browser-related operations.
/// </summary>
public class BrowserService : IBrowserService
{
#pragma warning disable S1075 // Localhost URL is a default constant
    private const string WebUiUrl = "http://localhost:5100";
#pragma warning restore S1075
    private const string WebProjectName = "AIUsageTracker.Web";
    private readonly ILogger<BrowserService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public BrowserService(ILogger<BrowserService> logger, IHttpClientFactory httpClientFactory)
    {
        this._logger = logger;
        this._httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger.LogError(ex, "Failed to open URL: {Url}", url);
        }
    }

    /// <inheritdoc/>
    public async Task OpenWebUIAsync()
    {
        try
        {
            var isServiceRunning = false;

            // Check if web service is already running.
            using var client = this._httpClientFactory.CreateClient("LocalhostProbe");
            try
            {
                var response = await client.GetAsync(WebUiUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    isServiceRunning = true;
                }
                else
                {
                    this._logger.LogDebug("Web service responded with status: {Status}", response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                this._logger.LogDebug(ex, "Web service not reachable, attempting to start it");
            }
            catch (TaskCanceledException ex)
            {
                this._logger.LogDebug(ex, "Web service probe timed out, attempting to start it");
            }

            if (!isServiceRunning)
            {
                this.StartWebService();
            }

            // Open browser to the Web UI.
            this.OpenUrl(WebUiUrl);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open Web UI");
            throw new InvalidOperationException("Failed to open Web UI.", ex);
        }
    }

    /// <inheritdoc/>
    public void OpenReleasesPage()
    {
        this.OpenUrl(GitHubUpdateChecker.GetReleasesPageUrl());
    }

    private void StartWebService()
    {
        var startInfo = ResolveWebStartInfo(AppContext.BaseDirectory);
        if (startInfo == null)
        {
            this._logger.LogWarning("Web executable and project directory not found; cannot auto-start Web service.");
            return;
        }

        Process.Start(startInfo);
        this._logger.LogInformation("Started Web service via {LaunchTarget} from {WorkingDirectory}", startInfo.FileName, startInfo.WorkingDirectory);
    }

    internal static ProcessStartInfo? ResolveWebStartInfo(string baseDirectory)
    {
        var outputDirectory = new DirectoryInfo(baseDirectory);
        var binaryDirectory = outputDirectory.Parent?.Parent;
        var executableName = $"{WebProjectName}.exe";
        var usesArtifactsOutput = binaryDirectory != null
            && string.Equals(binaryDirectory.Name, "bin", StringComparison.OrdinalIgnoreCase)
            && string.Equals(binaryDirectory.Parent?.Name, "artifacts", StringComparison.OrdinalIgnoreCase);
        var possiblePaths = usesArtifactsOutput
            ? new[]
            {
                // Use the same configuration/runtime as Slim, without invoking the SDK.
                Path.Combine(binaryDirectory!.FullName, WebProjectName, outputDirectory.Name, executableName),
                Path.Combine(baseDirectory, executableName),
            }
            : new[]
            {
                Path.Combine(baseDirectory, executableName),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Web", executableName)),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", WebProjectName, "bin", "Debug", "net10.0", executableName)),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", WebProjectName, "bin", "Release", "net10.0", executableName)),
            };

        var webExecutablePath = possiblePaths.FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(webExecutablePath))
        {
            var webProjectDirectory = FindProjectDirectory(baseDirectory, WebProjectName);
            if (!string.IsNullOrWhiteSpace(webProjectDirectory))
            {
                return new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{webProjectDirectory}\" --urls \"{WebUiUrl}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = webProjectDirectory,
                };
            }

            return null;
        }

        return new ProcessStartInfo
        {
            FileName = webExecutablePath,
            Arguments = $"--urls \"{WebUiUrl}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(webExecutablePath),
        };
    }

    private static string? FindProjectDirectory(string baseDirectory, string projectName)
    {
        var searchDirectory = new DirectoryInfo(baseDirectory);

        while (searchDirectory != null)
        {
            var projectPath = Path.Combine(searchDirectory.FullName, projectName, $"{projectName}.csproj");
            if (File.Exists(projectPath))
            {
                return Path.GetDirectoryName(projectPath);
            }

            searchDirectory = searchDirectory.Parent;
        }

        return null;
    }
}
