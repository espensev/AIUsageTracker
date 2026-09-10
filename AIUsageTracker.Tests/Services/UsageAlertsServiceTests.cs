// <copyright file="UsageAlertsServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Services;

public class UsageAlertsServiceTests
{
    [Fact]
    public async Task DetectResetEventsAsync_ZaiWeeklyReset_DoesNotMixFiveHourHistoryAsync()
    {
        var database = new Mock<IUsageDatabase>();
        var notifications = new Mock<INotificationService>();
        var config = new Mock<IConfigService>();
        var weeklyReset = DateTime.UtcNow.AddDays(7);
        var history = new ProviderUsage[]
        {
            Usage("5h", 10, DateTime.UtcNow, DateTime.UtcNow.AddHours(4)),
            Usage("5h", 5, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(4)),
            Usage("weekly", 0, DateTime.UtcNow, weeklyReset),
            Usage("weekly", 100, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(-1)),
        };
        database.Setup(service => service.GetRecentHistoryAsync(2)).ReturnsAsync(history);
        config.Setup(service => service.GetPreferencesAsync()).ReturnsAsync(new AppPreferences
        {
            EnableNotifications = false,
        });

        var service = new UsageAlertsService(
            NullLogger<UsageAlertsService>.Instance,
            database.Object,
            notifications.Object,
            config.Object);

        await service.DetectResetEventsAsync([
            Usage("5h", 10, DateTime.UtcNow, DateTime.UtcNow.AddHours(4)),
            Usage("weekly", 0, DateTime.UtcNow, weeklyReset),
        ]);

        database.Verify(
            service => service.StoreResetEventAsync(
                "zai-coding-plan",
                "Z.ai Coding Plan",
                100,
                0,
                "quota"),
            Times.Once);
    }

    private static WindowedProviderUsage Usage(string cardId, double usedPercent, DateTime fetchedAt, DateTime nextResetTime) => new()
    {
        ProviderId = "zai-coding-plan",
        ProviderName = "Z.ai Coding Plan",
        CardId = cardId,
        Name = cardId,
        UsedPercent = usedPercent,
        RequestsUsed = usedPercent,
        RequestsAvailable = 100 - usedPercent,
        IsQuotaBased = true,
        IsAvailable = true,
        FetchedAt = fetchedAt,
        NextResetTime = nextResetTime,
    };
}
