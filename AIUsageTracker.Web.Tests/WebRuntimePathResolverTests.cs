// <copyright file="WebRuntimePathResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Web.Tests;

/// <summary>
/// The Web UI must read the canonical Monitor database directly. Copying it to a
/// per-process snapshot at startup freezes the data (the 60s auto-refresh never
/// shows new rows) and accumulates database copies containing account data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WebRuntimePathResolverTests
{
    private string _tempDirectory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory(
            "WebRuntimePathResolverTests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    [TestMethod]
    public void Resolve_DatabasePath_IsCanonicalMonitorDatabase()
    {
        var canonicalDatabasePath = WebTestDatabaseFixture.CreatePopulated(this._tempDirectory);

        var runtimePaths = WebRuntimePathResolver.Resolve(this._tempDirectory);

        Assert.AreEqual(
            canonicalDatabasePath,
            runtimePaths.DatabasePath,
            "The Web UI must open the canonical Monitor database, not a startup snapshot copy.");
        Assert.IsFalse(
            runtimePaths.DatabasePath.Contains(".runtime", StringComparison.Ordinal),
            "No per-process database copies may live under the app base directory.");
        Assert.IsFalse(
            runtimePaths.DatabasePath.Contains("db-snapshot", StringComparison.Ordinal),
            "No db-snapshot directory may be used.");
    }

    [TestMethod]
    public async Task WebUi_ServesRowsInsertedIntoCanonicalDatabaseAfterStartupAsync()
    {
        var canonicalDatabasePath = WebTestDatabaseFixture.CreatePopulated(this._tempDirectory);
        using var factory = new KestrelWebApplicationFactory<Program>(this._tempDirectory);
        using var client = new HttpClient { BaseAddress = new Uri(factory.ServerAddress) };

        using (var baselineResponse = await client.GetAsync("/charts?handler=ChartPayload&hours=24"))
        {
            var baselineBody = await baselineResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, baselineResponse.StatusCode);
            Assert.IsFalse(
                baselineBody.Contains("freshness-probe", StringComparison.Ordinal),
                "Sanity check: the probe provider must not exist before it is inserted.");
        }

        this.InsertFreshnessProbeRow(canonicalDatabasePath);

        // Distinct 'hours' value keeps this request away from the 20s output-cache entry
        // captured by the baseline request.
        using var response = await client.GetAsync("/charts?handler=ChartPayload&hours=23");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(
            body.Contains("freshness-probe", StringComparison.Ordinal),
            "Rows written to the canonical Monitor database after startup must be served " +
            "without restarting the Web UI (no frozen snapshot).");
    }

    private void InsertFreshnessProbeRow(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO providers (provider_id, provider_name, is_active, auth_source, account_name)
VALUES ('freshness-probe', 'Freshness Probe', 1, 'api_key', 'acct-freshness');

INSERT INTO provider_history (
    provider_id, requests_used, requests_available, requests_percentage,
    is_available, status_message, response_latency_ms, fetched_at,
    next_reset_time, details_json, http_status, card_type)
VALUES (
    'freshness-probe', 10, 100, 10,
    1, 'inserted after startup', 100, $fetchedAt,
    NULL, '{}', 200, NULL);";
        command.Parameters.AddWithValue(
            "$fetchedAt",
            DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }
}
