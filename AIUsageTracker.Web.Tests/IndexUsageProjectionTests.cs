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
