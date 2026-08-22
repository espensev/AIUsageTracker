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
/// runtime snapshot at startup freezes the data (the 60s auto-refresh never shows
/// new rows) and accumulates database copies containing account data under the
/// app base directory.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WebRuntimePathResolverTests
{
    private const string LocalAppDataRootEnvironmentVariable = "AIUSAGETRACKER_LOCAL_APP_DATA_ROOT";

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
        var canonicalDatabasePath = this.CreateCanonicalDatabase();

        var runtimePaths = WebRuntimePathResolver.Resolve(this._tempDirectory);

        Assert.AreEqual(
            canonicalDatabasePath,
            runtimePaths.DatabasePath,
            "The Web UI must open the canonical Monitor database, not a startup snapshot copy.");
        Assert.IsFalse(
            runtimePaths.DatabasePath.Contains("db-snapshot", StringComparison.Ordinal),
            "No db-snapshot directory may be used.");
    }

    [TestMethod]
    public async Task WebUi_ServesRowsInsertedIntoCanonicalDatabaseAfterStartupAsync()
    {
        var canonicalDatabasePath = this.CreateCanonicalDatabase();
        using var factory = new KestrelWebApplicationFactory<Program>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LocalAppDataRootEnvironmentVariable] = this._tempDirectory,
            });
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

    /// <summary>Creates the canonical %root%/AIUsageTracker/usage.db with the migrated
    /// (epoch INTEGER, WAL) schema the Monitor's migration produces.</summary>
    private string CreateCanonicalDatabase()
    {
        var appDataDirectory = Path.Combine(this._tempDirectory, "AIUsageTracker");
        Directory.CreateDirectory(appDataDirectory);
        var databasePath = Path.Combine(appDataDirectory, "usage.db");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
PRAGMA journal_mode=WAL;

CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT NOT NULL,
    is_active INTEGER NOT NULL,
    auth_source TEXT NULL,
    account_name TEXT NULL
);

CREATE TABLE provider_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    requests_used REAL NOT NULL,
    requests_available REAL NOT NULL,
    requests_percentage REAL NOT NULL,
    is_available INTEGER NOT NULL,
    status_message TEXT NULL,
    response_latency_ms REAL NOT NULL,
    fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
    next_reset_time TEXT NULL,
    details_json TEXT NULL,
    http_status INTEGER NOT NULL DEFAULT 200,
    model_name TEXT NULL,
    name TEXT NULL,
    card_type TEXT NULL
);

INSERT INTO providers (provider_id, provider_name, is_active, auth_source, account_name)
VALUES ('openai', 'OpenAI', 1, 'api_key', 'acct-openai');

CREATE TABLE reset_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    previous_usage REAL NULL,
    new_usage REAL NULL,
    reset_type TEXT NOT NULL,
    timestamp TEXT NOT NULL
);

INSERT INTO provider_history (
    provider_id, requests_used, requests_available, requests_percentage,
    is_available, status_message, response_latency_ms, fetched_at,
    next_reset_time, details_json, http_status, card_type)
VALUES (
    'openai', 10, 100, 10,
    1, 'fixture', 100, $fetchedAt,
    NULL, '{}', 200, NULL);";
        command.Parameters.AddWithValue(
            "$fetchedAt",
            DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds());
        command.ExecuteNonQuery();
        return databasePath;
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
