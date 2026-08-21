// <copyright file="GrokProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GrokProviderTests : HttpProviderTestBase<GrokProvider>
{
    private const string BillingEndpoint = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";

    private static readonly string TestToken = Guid.NewGuid().ToString();

    private readonly GrokProvider _provider;
    private readonly string _authFilePath;

    public GrokProviderTests()
    {
        // Point the provider at an auth file that does not exist so tests exercise the
        // configured token; individual tests may write the file to verify native reads.
        this._authFilePath = Path.Combine(Path.GetTempPath(), "grok-provider-tests", "auth.json");
        this._provider = new GrokProvider(this.HttpClient, this.Logger.Object, this._authFilePath);
        this.Config.ApiKey = TestToken;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_MapsWeeklyCreditsCardAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            config = new
            {
                currentPeriod = new
                {
                    type = "USAGE_PERIOD_TYPE_WEEKLY",
                    start = "2026-08-18T20:39:07.120945+00:00",
                    end = "2026-08-25T20:39:07.120945+00:00",
                },
                creditUsagePercent = 21.0,
                onDemandCap = new { val = 0 },
                onDemandUsed = new { val = 0 },
                productUsage = new[] { new { product = "GrokBuild", usagePercent = 21.0 } },
                prepaidBalance = new { val = 0 },
            },
        });

        this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.OfType<QuotaProviderUsage>().Single();
        Assert.Equal("Grok CLI", usage.ProviderName);
        Assert.Equal("Weekly", usage.Name);
        Assert.Equal("weekly-credits", usage.CardId);
        Assert.Equal(21.0, usage.UsedPercent, 1);
        Assert.Equal(WindowKind.Rolling, usage.WindowKind);
        Assert.Equal(TimeSpan.FromDays(7), usage.PeriodDuration);
        Assert.NotNull(usage.NextResetTime);
        Assert.True(usage.IsAvailable);
        Assert.Contains("79", usage.Description, StringComparison.Ordinal);
        Assert.Contains("GrokBuild", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_OnDemandCap_AddsOnDemandCardAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            config = new
            {
                currentPeriod = new
                {
                    type = "USAGE_PERIOD_TYPE_WEEKLY",
                    start = "2026-08-18T20:39:07.120945+00:00",
                    end = "2026-08-25T20:39:07.120945+00:00",
                },
                creditUsagePercent = 10.0,
                onDemandCap = new { val = 100 },
                onDemandUsed = new { val = 25 },
            },
        });

        this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var cards = result.OfType<QuotaProviderUsage>().ToList();
        Assert.Equal(2, cards.Count);

        var onDemand = cards.Single(card => string.Equals(card.CardId, "on-demand-credits", StringComparison.Ordinal));
        Assert.Equal(25.0, onDemand.UsedPercent, 1);
        Assert.Equal(25, onDemand.RequestsUsed);
        Assert.Equal(100, onDemand.RequestsAvailable);
        Assert.True(onDemand.DisplayAsFraction);
    }

    [Fact]
    public async Task GetUsageAsync_OnDemandUsageExceedsCap_ClampsRemainingCreditsAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            config = new
            {
                creditUsagePercent = 10.0,
                onDemandCap = new { val = 100 },
                onDemandUsed = new { val = 125 },
            },
        });

        this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var onDemand = result
            .OfType<QuotaProviderUsage>()
            .Single(card => string.Equals(card.CardId, "on-demand-credits", StringComparison.Ordinal));
        Assert.Equal(100.0, onDemand.UsedPercent, 1);
        Assert.Equal("0 / 100 on-demand credits remaining", onDemand.Description);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task GetUsageAsync_NonObjectAuthRoot_FallsBackToConfiguredTokenAsync(string authContent)
    {
        // Arrange
        var testRoot = TestTempPaths.CreateDirectory("grok-provider-non-object-auth");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            await File.WriteAllTextAsync(authFilePath, authContent);

            var provider = new GrokProvider(this.HttpClient, this.Logger.Object, authFilePath);
            this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    config = new { creditUsagePercent = 10.0 },
                })),
            });

            // Act
            var result = await provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.OfType<QuotaProviderUsage>().Single();
            Assert.True(usage.IsAvailable);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task GetUsageAsync_MissingConfig_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new { config = (object?)null })),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.OfType<StatusProviderUsage>().Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("No billing data", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupHttpResponse(BillingEndpoint, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("{\"error\":\"unauthorized\"}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.OfType<StatusProviderUsage>().Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
    }
}
