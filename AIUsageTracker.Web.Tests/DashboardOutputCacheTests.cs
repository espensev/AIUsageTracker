// <copyright file="DashboardOutputCacheTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Web.Tests;

/// <summary>
/// The DashboardCache output-cache policy must vary by every dashboard toggle
/// (showUsed, showInactive, expAnomaly). A missing vary key lets one toggle
/// state be served from another state's cache entry for the cache lifetime.
/// </summary>
/// <remarks>
/// Exercised through a minimal host that registers the real infrastructure
/// (<see cref="WebStartupServiceExtensions.AddWebUiInfrastructure"/>) and a
/// probe endpoint using the DashboardCache policy, so the registered vary keys
/// are proven by behavior rather than framework internals. (The dashboard's own
/// toggle requests set cookies and are never output-cached, so they cannot
/// demonstrate this variance.)
/// </remarks>
[TestClass]
[DoNotParallelize]
public class DashboardOutputCacheTests
{
    [TestMethod]
    public async Task DashboardCachePolicy_VariesByAllDashboardTogglesAsync()
    {
        var port = GetAvailablePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddWebUiInfrastructure();
        await using var app = builder.Build();

        app.UseOutputCache();
        app.MapGet(
            "/dashboard-cache-probe",
            (bool? showUsed, bool? showInactive, bool? expAnomaly) =>
                $"showUsed={showUsed};showInactive={showInactive};expAnomaly={expAnomaly}")
            .WithMetadata(new OutputCacheAttribute { PolicyName = "DashboardCache" });
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        using var variantOff = await client.GetAsync(
            "/dashboard-cache-probe?showUsed=false&showInactive=false&expAnomaly=false");
        var offBody = await variantOff.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, variantOff.StatusCode);
        Assert.AreEqual(
            "showUsed=False;showInactive=False;expAnomaly=False",
            offBody,
            "Sanity check: the anomaly-off variant must render its own value.");

        // Same vary keys the policy knows, different expAnomaly state. Within the
        // 15s cache window this hits variantOff's cache entry unless the policy
        // also varies by expAnomaly.
        using var variantOn = await client.GetAsync(
            "/dashboard-cache-probe?showUsed=false&showInactive=false&expAnomaly=true");
        var onBody = await variantOn.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, variantOn.StatusCode);
        Assert.AreEqual(
            "showUsed=False;showInactive=False;expAnomaly=True",
            onBody,
            "The anomaly-on request must render its own variant, not the anomaly-off " +
            "variant replayed from the DashboardCache entry.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
