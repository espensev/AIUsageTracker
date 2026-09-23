// <copyright file="IndexUsageProjectionTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Pages;

namespace AIUsageTracker.Web.Tests;

[TestClass]
public sealed class IndexUsageProjectionTests
{
    [TestMethod]
    public void ProjectProviderFamilies_UsesDefinitionOrderWithoutDuplicateTopLevelCards()
    {
        var rows = new ProviderUsage[]
        {
            CreateGrokCard("on-demand-credits", "On-Demand", 10),
            CreateGrokCard("weekly-credits", "Weekly", 30),
        };

        var families = IndexModel.ProjectProviderFamilies(rows);

        Assert.AreEqual(1, families.Count);
        var family = families[0];
        Assert.AreEqual("grok", family.ProviderId);
        Assert.AreEqual("weekly-credits", ((QuotaProviderUsage)family.PrimaryCard).CardId);
        Assert.AreEqual(1, family.SiblingCards.Count);
        var sibling = family.SiblingCards[0];
        Assert.AreEqual("on-demand-credits", ((QuotaProviderUsage)sibling).CardId);
    }

    [TestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public void ProjectProviderAttention_OnlyReportsFailuresAtTheLatestReading(int failureOffsetMinutes, bool expectedAttention)
    {
        var successfulReading = CreateGrokCard("weekly-credits", "Weekly", 30);
        successfulReading.FetchedAt = new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);
        var failedReading = CreateGrokCard(string.Empty, "Provider status", 0);
        failedReading.ProviderId = "GROK";
        failedReading.IsAvailable = false;
        failedReading.FetchedAt = successfulReading.FetchedAt.AddMinutes(failureOffsetMinutes);

        foreach (var rows in new[]
        {
            new ProviderUsage[] { failedReading, successfulReading },
            new ProviderUsage[] { successfulReading, failedReading },
        })
        {
            var attention = IndexModel.ProjectProviderAttention(rows);

            Assert.AreEqual(expectedAttention ? 1 : 0, attention.Count);
            if (expectedAttention)
            {
                Assert.AreSame(failedReading, attention[0]);
            }
        }
    }

    private static WindowedProviderUsage CreateGrokCard(string cardId, string name, double usedPercent)
    {
        return new WindowedProviderUsage
        {
            ProviderId = "grok",
            ProviderName = "Grok CLI",
            CardId = cardId,
            GroupId = "grok",
            Name = name,
            UsedPercent = usedPercent,
            IsAvailable = true,
            FetchedAt = DateTime.UtcNow,
        };
    }
}
