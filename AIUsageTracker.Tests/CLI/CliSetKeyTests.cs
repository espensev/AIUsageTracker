// <copyright file="CliSetKeyTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Moq;
using CliProgram = AIUsageTracker.CLI.Program;

namespace AIUsageTracker.Tests.CLI;

public sealed class CliSetKeyTests
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetKeyAsync_SuppressedProviderReAdd_SavesConfigAndReportsHonestResultAsync(bool saved)
    {
        var monitor = new Mock<IMonitorService>(MockBehavior.Strict);

        // A suppressed provider is filtered out of GetConfigsAsync, so a CLI
        // re-add takes the new-config path. It must still persist through the
        // Monitor's unsuppressing SaveConfigAsync and only report success when
        // the save succeeded.
        IReadOnlyList<ProviderConfig> visibleConfigs = [];
        monitor.Setup(service => service.GetConfigsAsync()).ReturnsAsync(visibleConfigs);
        monitor.Setup(service => service.SaveConfigAsync(It.IsAny<ProviderConfig>())).ReturnsAsync(saved);
        monitor.Setup(service => service.TriggerRefreshAsync()).ReturnsAsync(true);

        await CliProgram.SetKeyAsync(monitor.Object, "groq", TestApiKey);

        monitor.Verify(
            service => service.SaveConfigAsync(
                It.Is<ProviderConfig>(config => config.ProviderId == "groq" && config.ApiKey == TestApiKey)),
            Times.Once);
        monitor.Verify(service => service.TriggerRefreshAsync(), Times.Exactly(saved ? 1 : 0));
    }
}
