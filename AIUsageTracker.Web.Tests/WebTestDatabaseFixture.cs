// <copyright file="WebTestDatabaseFixture.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Web.Tests;

internal static class WebTestDatabaseFixture
{
    public static string CreateEmpty(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);

        var appDataDirectory = Path.Combine(localAppDataRoot, "AIUsageTracker");
        Directory.CreateDirectory(appDataDirectory);
        var databasePath = Path.Combine(appDataDirectory, "usage.db");

        using var connection = OpenConnection(databasePath, SqliteOpenMode.ReadWriteCreate);
        using var command = connection.CreateCommand();
        command.CommandText = @"
PRAGMA journal_mode=WAL;

CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT NOT NULL,
    auth_source TEXT NULL,
    account_name TEXT NULL,
    created_at TEXT NULL,
    updated_at TEXT NULL,
    is_active INTEGER NOT NULL,
    config_json TEXT NULL,
    plan_type TEXT NULL
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
    upstream_response_validity INTEGER NOT NULL DEFAULT 0,
    upstream_response_note TEXT NOT NULL DEFAULT '',
    parent_provider_id TEXT NULL,
    card_id TEXT NULL,
    group_id TEXT NULL,
    window_kind INTEGER NOT NULL DEFAULT 0,
    model_name TEXT NULL,
    name TEXT NULL,
    card_type TEXT NULL,
    reset_credits_available INTEGER NULL,
    reset_credit_expirations_utc TEXT NULL
);

CREATE TABLE reset_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    previous_usage REAL NULL,
    new_usage REAL NULL,
    reset_type TEXT NOT NULL,
    timestamp TEXT NOT NULL
);

CREATE TABLE raw_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
    raw_json TEXT NULL
);";
        command.ExecuteNonQuery();
        return databasePath;
    }

    public static string CreatePopulated(string localAppDataRoot)
    {
        var databasePath = CreateEmpty(localAppDataRoot);
        using var connection = OpenConnection(databasePath, SqliteOpenMode.ReadWrite);
        SeedProviders(connection);
        SeedHistory(connection);
        SeedResetEvents(connection);
        return databasePath;
    }

    private static SqliteConnection OpenConnection(string databasePath, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void SeedProviders(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO providers (
    provider_id, provider_name, auth_source, account_name,
    created_at, updated_at, is_active, config_json, plan_type)
VALUES
    ('openai', 'OpenAI', 'api_key', 'fixture-openai', $now, $now, 1, '{}', 'Usage'),
    ('claude', 'Claude', 'api_key', 'fixture-claude', $now, $now, 1, '{}', 'Usage'),
    ('grok', 'Grok CLI', 'session', 'fixture-grok', $now, $now, 1, '{}', 'Subscription');";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void SeedHistory(SqliteConnection connection)
    {
        var now = DateTime.UtcNow;
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO provider_history (
    provider_id, requests_used, requests_available, requests_percentage,
    is_available, status_message, response_latency_ms, fetched_at,
    next_reset_time, details_json, http_status, upstream_response_validity,
    upstream_response_note, parent_provider_id, card_id, group_id,
    window_kind, model_name, name, card_type)
VALUES
    ('openai', 10, 100, 10, 1, 'fixture openai', 120, $openaiAt,
     $openaiReset, '{}', 200, 2, 'HTTP 200', NULL, NULL, NULL, 0, NULL, NULL, 'quota'),
    ('claude', 60, 100, 60, 0, 'fixture unavailable', 200, $claudeAt,
     NULL, '{}', 503, 3, 'HTTP 503', NULL, NULL, NULL, 0, NULL, NULL, 'quota'),
    ('grok', 10, 100, 10, 1, 'old weekly fixture', 90, $oldWeeklyAt,
     $weeklyReset, '{}', 200, 2, 'HTTP 200', 'grok', 'weekly-credits', 'grok', 2, NULL, 'stale label', 'windowed'),
    ('grok', 30, 100, 30, 1, 'latest weekly fixture', 95, $weeklyAt,
     $weeklyReset, '{}', 200, 2, 'HTTP 200', 'grok', 'weekly-credits', 'grok', 2, NULL, 'stale label', 'windowed'),
    ('grok', 5, 50, 10, 1, 'on-demand fixture', 92, $onDemandAt,
     NULL, '{}', 200, 2, 'HTTP 200', 'grok', 'on-demand-credits', 'grok', 0, NULL, 'stale label', 'windowed');";
        command.Parameters.AddWithValue("$openaiAt", ToEpochSeconds(now.AddMinutes(-4)));
        command.Parameters.AddWithValue("$openaiReset", FormatTimestamp(now.AddHours(5)));
        command.Parameters.AddWithValue("$claudeAt", ToEpochSeconds(now.AddMinutes(-3)));
        command.Parameters.AddWithValue("$oldWeeklyAt", ToEpochSeconds(now.AddMinutes(-10)));
        command.Parameters.AddWithValue("$weeklyAt", ToEpochSeconds(now.AddMinutes(-2)));
        command.Parameters.AddWithValue("$weeklyReset", FormatTimestamp(now.AddDays(6)));
        command.Parameters.AddWithValue("$onDemandAt", ToEpochSeconds(now.AddMinutes(-1)));
        command.ExecuteNonQuery();
    }

    private static void SeedResetEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO reset_events (
    provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp)
VALUES ('openai', 'OpenAI', 99, 10, 'quota_reset', $timestamp);";
        command.Parameters.AddWithValue("$timestamp", FormatTimestamp(DateTime.UtcNow.AddMinutes(-4)));
        command.ExecuteNonQuery();
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static long ToEpochSeconds(DateTime value)
    {
        return new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();
    }
}
