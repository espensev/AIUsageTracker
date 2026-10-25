// <copyright file="MonitorClientTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.MonitorClient;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Core;

public sealed class MonitorClientTests : IDisposable
{
    private readonly string _tempDirectory;

    public MonitorClientTests()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory("monitor-client-tests");
    }

    [Fact]
    public async Task MonitorService_GetConfigsAsync_ReturnsEmptyList_WhenMonitorNotAvailableAsync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };

        var launcher = new MonitorLauncher(
            monitorInfoCandidatePathsOverride: () => new[] { Path.Combine(this._tempDirectory, "monitor.json") },
            healthCheckOverride: _ => Task.FromResult(false),
            processRunningOverride: _ => Task.FromResult(false));
        var service = new MonitorService(httpClient, NullLogger<MonitorService>.Instance, launcher);
        service.AgentUrl = "http://localhost:9999";

        // Act
        var configs = await service.GetConfigsAsync();

        // Assert
        Assert.NotNull(configs);
        Assert.Empty(configs);
    }

    [Fact]
    public async Task MonitorService_GetUsageAsync_ReturnsEmptyList_WhenMonitorNotAvailableAsync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };

        var launcher = new MonitorLauncher(
            monitorInfoCandidatePathsOverride: () => new[] { Path.Combine(this._tempDirectory, "monitor.json") },
            healthCheckOverride: _ => Task.FromResult(false),
            processRunningOverride: _ => Task.FromResult(false));
        var service = new MonitorService(httpClient, NullLogger<MonitorService>.Instance, launcher);
        service.AgentUrl = "http://localhost:9999";

        // Act
        var usages = await service.GetUsageAsync();

        // Assert
        Assert.NotNull(usages);
        Assert.Empty(usages);
    }

    [Fact]
    public void MonitorLauncher_HasRequiredMethods()
    {
        var type = typeof(MonitorLauncher);

        Assert.NotNull(type.GetMethod("StartAgentAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("StopAgentAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("IsAgentRunningAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("WaitForAgentAsync", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public async Task MonitorLauncher_GetAndValidateMonitorInfo_ReturnsNull_WhenHealthFailsAsync()
    {
        var infoPath = Path.Combine(this._tempDirectory, "monitor.json");
        var originalMetadata = JsonSerializer.SerializeToUtf8Bytes(new MonitorInfo
        {
            Port = 5123,
            ProcessId = 4242,
            AccessToken = "test-monitor-token",
        });
        await File.WriteAllBytesAsync(infoPath, originalMetadata);

        var launcher = new MonitorLauncher(
            monitorInfoCandidatePathsOverride: () => new[] { infoPath },
            healthCheckOverride: port =>
            {
                Assert.Equal(5123, port);
                return Task.FromResult(false);
            },
            processRunningOverride: processId => Task.FromResult(processId == 4242));

        var result = await launcher.GetAndValidateMonitorInfoAsync();

        Assert.Null(result);
        Assert.False(File.Exists(infoPath));
        var stalePath = Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*"));
        Assert.Equal(originalMetadata, await File.ReadAllBytesAsync(stalePath));
    }

    [Fact]
    public async Task MonitorLauncher_InvalidateMonitorInfo_DoesNotThrow_WhenFileMissingAsync()
    {
        var infoPath = Path.Combine(this._tempDirectory, "monitor.json");
        var launcher = new MonitorLauncher(
            monitorInfoCandidatePathsOverride: () => new[] { infoPath });

        var exception = await Record.ExceptionAsync(() => launcher.InvalidateMonitorInfoAsync());
        Assert.Null(exception);
        Assert.False(File.Exists(infoPath));
        Assert.Empty(Directory.GetFiles(this._tempDirectory));
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }
}
