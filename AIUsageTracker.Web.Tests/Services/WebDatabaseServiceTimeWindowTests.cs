// <copyright file="WebDatabaseServiceTimeWindowTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Web.Tests.Services;

/// <summary>
/// Verifies the chart/analytics queries against both storage schemas the Web app can open:
/// the current schema (fetched_at INTEGER epoch seconds) and the legacy schema (ISO TEXT).
/// </summary>
[TestClass]
public class WebDatabaseServiceTimeWindowTests
{
    private string _tempDirectory = string.Empty;

    public enum TimestampStorage
    {
        EpochInteger,
        LegacyIsoText,
    }

    [TestInitialize]
    public void Initialize()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory(
            "WebDatabaseServiceTimeWindowTests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    [TestMethod]
    public async Task GetChartDataAsync_EpochSchema_ReturnsRecentRowsAndExcludesOlderOnesAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.EpochInteger);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 2, used: 10, percent: 10, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 62, used: 20, percent: 20, latencyMs: 110, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 3 * 24 * 60, used: 99, percent: 99, latencyMs: 120, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetChartDataAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Recent epoch rows in two distinct buckets should be returned; the 3-day-old row must be excluded.");
        Assert.AreEqual(20.0, result[0].UsedPercent, 0.01, "Points are ordered oldest first (Timestamp ASC).");
        Assert.AreEqual(10.0, result[1].UsedPercent, 0.01);
        Assert.IsTrue(result.All(point => point.Timestamp > DateTime.UtcNow.AddHours(-24)), "Bucket timestamps must fall inside the requested window.");
    }

    [TestMethod]
    public async Task GetChartDataAsync_LegacyTextSchema_ReturnsRecentRowsAndExcludesOlderOnesAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.LegacyIsoText);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 2, used: 10, percent: 10, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 62, used: 20, percent: 20, latencyMs: 110, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 3 * 24 * 60, used: 99, percent: 99, latencyMs: 120, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetChartDataAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Legacy ISO text rows must keep working after the epoch fix.");
        Assert.AreEqual(20.0, result[0].UsedPercent, 0.01, "Points are ordered oldest first (Timestamp ASC).");
        Assert.AreEqual(10.0, result[1].UsedPercent, 0.01);
    }

    [TestMethod]
    public async Task GetSpendingTrendAsync_EpochSchema_ReturnsOnePointPerDayAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.EpochInteger);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 60, used: 20, percent: 20, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 2 * 24 * 60, used: 40, percent: 40, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 10 * 24 * 60, used: 99, percent: 99, latencyMs: 100, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetSpendingTrendAsync(["openai"], days: 7);

        Assert.AreEqual(
            2,
            result.Count,
            $"Two days inside the 7-day window must yield two points; the 10-day-old row must be excluded. Actual: [{string.Join("; ", result.Select(p => $"{p.Date}={p.Amount}"))}]");
        CollectionAssert.AreEqual(
            new[] { 40.0, 20.0 },
            result.Select(point => point.Amount).ToArray(),
            "Points are ordered oldest day first (Day ASC).");
        Assert.IsTrue(result.All(point => string.Equals(point.ProviderId, "openai", StringComparison.Ordinal)), "Points must be attributed to the provider.");
    }

    [TestMethod]
    public async Task GetSpendingTrendAsync_LegacyTextSchema_ReturnsOnePointPerDayAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.LegacyIsoText);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 60, used: 20, percent: 20, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 2 * 24 * 60, used: 40, percent: 40, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 10 * 24 * 60, used: 99, percent: 99, latencyMs: 100, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetSpendingTrendAsync(["openai"], days: 7);

        Assert.AreEqual(
            2,
            result.Count,
            $"Two days inside the 7-day window must yield two points. Actual: [{string.Join("; ", result.Select(p => $"{p.Date}={p.Amount}"))}]");
        CollectionAssert.AreEqual(
            new[] { 40.0, 20.0 },
            result.Select(point => point.Amount).ToArray(),
            "Points are ordered oldest day first (Day ASC).");
    }

    [TestMethod]
    public async Task GetModelUsageBreakdownAsync_EpochSchema_ReturnsModelsInsideWindowAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.EpochInteger);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 60, used: 5, percent: 5, latencyMs: 100, httpStatus: 200, modelName: "gpt-4o");
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 8 * 24 * 60, used: 50, percent: 50, latencyMs: 100, httpStatus: 200, modelName: "old-model");

        var result = await this.CreateService(databasePath).GetModelUsageBreakdownAsync(hours: 168);

        var breakdown = AssertSingle(result, "gpt-4o");
        Assert.AreEqual(5.0, breakdown.TotalUsed, 0.01);
        Assert.AreEqual(1, breakdown.SampleCount);
    }

    [TestMethod]
    public async Task GetModelUsageBreakdownAsync_LegacyTextSchema_ReturnsModelsInsideWindowAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.LegacyIsoText);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 60, used: 5, percent: 5, latencyMs: 100, httpStatus: 200, modelName: "gpt-4o");
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 8 * 24 * 60, used: 50, percent: 50, latencyMs: 100, httpStatus: 200, modelName: "old-model");

        var result = await this.CreateService(databasePath).GetModelUsageBreakdownAsync(hours: 168);

        var breakdown = AssertSingle(result, "gpt-4o");
        Assert.AreEqual(5.0, breakdown.TotalUsed, 0.01);
    }

    [TestMethod]
    public async Task GetLatencyTrendAsync_EpochSchema_ReturnsBucketsAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.EpochInteger);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 2, used: 10, percent: 10, latencyMs: 150, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 62, used: 20, percent: 20, latencyMs: 250, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetLatencyTrendAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Two samples in distinct buckets must be returned.");
        Assert.AreEqual(250.0, result[0].AvgLatencyMs, 0.01, "Points are ordered oldest first (Timestamp ASC).");
        Assert.AreEqual(150.0, result[1].AvgLatencyMs, 0.01);
    }

    [TestMethod]
    public async Task GetLatencyTrendAsync_LegacyTextSchema_ReturnsBucketsAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.LegacyIsoText);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 2, used: 10, percent: 10, latencyMs: 150, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 62, used: 20, percent: 20, latencyMs: 250, httpStatus: 200);

        var result = await this.CreateService(databasePath).GetLatencyTrendAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Legacy ISO text rows must keep bucketing after the epoch fix.");
    }

    [TestMethod]
    public async Task GetHttpStatusHistoryAsync_EpochSchema_ReturnsCountsAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.EpochInteger);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 2, used: 10, percent: 10, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.EpochInteger, minutesAgo: 62, used: 20, percent: 20, latencyMs: 100, httpStatus: 503);

        var result = await this.CreateService(databasePath).GetHttpStatusHistoryAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Two samples in distinct buckets must be returned.");
        Assert.AreEqual(1, result.Sum(point => point.SuccessCount));
        Assert.AreEqual(1, result.Sum(point => point.ErrorCount));
    }

    [TestMethod]
    public async Task GetHttpStatusHistoryAsync_LegacyTextSchema_ReturnsCountsAsync()
    {
        var databasePath = this.CreateDatabase(TimestampStorage.LegacyIsoText);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 2, used: 10, percent: 10, latencyMs: 100, httpStatus: 200);
        this.SeedHistoryRow(databasePath, TimestampStorage.LegacyIsoText, minutesAgo: 62, used: 20, percent: 20, latencyMs: 100, httpStatus: 503);

        var result = await this.CreateService(databasePath).GetHttpStatusHistoryAsync(hours: 24);

        Assert.AreEqual(2, result.Count, "Legacy ISO text rows must keep bucketing after the epoch fix.");
        Assert.AreEqual(1, result.Sum(point => point.SuccessCount));
        Assert.AreEqual(1, result.Sum(point => point.ErrorCount));
    }

    private static AIUsageTracker.Core.Models.ModelUsageBreakdown AssertSingle(
        IReadOnlyList<AIUsageTracker.Core.Models.ModelUsageBreakdown> result,
        string expectedModelName)
    {
        Assert.AreEqual(1, result.Count, $"Only the in-window model '{expectedModelName}' should be returned.");
        Assert.AreEqual(expectedModelName, result[0].ModelName);
        return result[0];
    }

    private WebDatabaseService CreateService(string databasePath)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        return new WebDatabaseService(
            cache,
            NullLogger<WebDatabaseService>.Instance,
            new TestAppPathProvider(databasePath));
    }

    private string CreateDatabase(TimestampStorage timestampStorage)
    {
        var databasePath = Path.Combine(this._tempDirectory, "usage.db");
        var fetchedAtDeclaration = timestampStorage == TimestampStorage.EpochInteger
            ? "fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))"
            : "fetched_at TEXT NOT NULL";

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $@"
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
    {fetchedAtDeclaration},
    next_reset_time TEXT NULL,
    details_json TEXT NULL,
    http_status INTEGER NOT NULL DEFAULT 200,
    model_name TEXT NULL,
    name TEXT NULL,
    card_type TEXT NULL
);

INSERT INTO providers (provider_id, provider_name, is_active, auth_source, account_name)
VALUES ('openai', 'OpenAI', 1, 'api_key', 'acct-openai');";
        command.ExecuteNonQuery();
        return databasePath;
    }

    private void SeedHistoryRow(
        string databasePath,
        TimestampStorage timestampStorage,
        int minutesAgo,
        double used,
        double percent,
        double latencyMs,
        int httpStatus,
        string? modelName = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();

        var fetchedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO provider_history (
    provider_id, requests_used, requests_available, requests_percentage,
    is_available, status_message, response_latency_ms, fetched_at,
    next_reset_time, details_json, http_status, model_name, name, card_type)
VALUES (
    'openai', $used, 100, $percent,
    1, 'fixture', $latencyMs, $fetchedAt,
    NULL, '{}', $httpStatus, $modelName, $modelName, NULL);";
        command.Parameters.AddWithValue("$used", used);
        command.Parameters.AddWithValue("$percent", percent);
        command.Parameters.AddWithValue("$latencyMs", latencyMs);
        command.Parameters.AddWithValue("$httpStatus", httpStatus);
        command.Parameters.AddWithValue("$modelName", (object?)modelName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$fetchedAt",
            timestampStorage == TimestampStorage.EpochInteger
                ? new DateTimeOffset(fetchedAt).ToUnixTimeSeconds()
                : fetchedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _databasePath;

        public TestAppPathProvider(string databasePath)
        {
            this._databasePath = databasePath;
        }

        public string GetAppDataRoot() => Path.GetDirectoryName(this._databasePath) ?? string.Empty;

        public string GetDatabasePath() => this._databasePath;

        public string GetLogDirectory() => this.GetAppDataRoot();

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
