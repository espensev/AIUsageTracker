// <copyright file="DashboardOutputCacheTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;

namespace AIUsageTracker.Web.Tests;

/// <summary>
/// The DashboardCache output-cache policy must vary by every dashboard toggle
/// (showUsed, showInactive, expAnomaly). Otherwise toggling anomaly detection
/// can serve the other variant's cached HTML for up to 15 seconds.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DashboardOutputCacheTests : WebTestBase
{
    [TestMethod]
    public async Task Dashboard_ExpAnomalyToggle_IsNotServedFromOtherVariantsCacheEntryAsync()
    {
        using var client = CreateClient();

        using var anomalyOffResponse = await client.GetAsync("/?expAnomaly=false");
        var anomalyOffBody = await ReadBodyAsync(anomalyOffResponse);
        Assert.AreEqual(HttpStatusCode.OK, anomalyOffResponse.StatusCode);

        using var anomalyOnResponse = await client.GetAsync("/?expAnomaly=true");
        var anomalyOnBody = await ReadBodyAsync(anomalyOnResponse);
        Assert.AreEqual(HttpStatusCode.OK, anomalyOnResponse.StatusCode);

        CollectionAssert.AreNotEqual(
            System.Text.Encoding.UTF8.GetBytes(anomalyOffBody),
            System.Text.Encoding.UTF8.GetBytes(anomalyOnBody),
            "The expAnomaly=true dashboard must render differently from expAnomaly=false; " +
            "identical bodies mean the toggle does not change the page and cannot validate cache variance.");
        Assert.AreEqual(
            "False",
            GetExpAnomalyCookie(anomalyOffResponse),
            "The expAnomaly=false response must carry its own cookie.");
        Assert.AreEqual(
            "True",
            GetExpAnomalyCookie(anomalyOnResponse),
            "The expAnomaly=true response must carry its own cookie, not a replayed cached variant cookie.");
    }

    private static string? GetExpAnomalyCookie(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';', StringSplitOptions.TrimEntries)[0])
            .FirstOrDefault(cookie => cookie.StartsWith("expAnomaly=", StringComparison.Ordinal))
            ?.Split('=', 2)[1];
    }
}
