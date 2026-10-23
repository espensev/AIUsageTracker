// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Seeder;

public class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && string.Equals(args[0], "export", StringComparison.Ordinal))
        {
            var exportPath = ValidateFixturePath(args.Length > 1 ? args[1] : "test-fixtures/provider-data.json");
            if (exportPath == null)
            {
                return 1;
            }

            return ExportData(exportPath);
        }

        if (args.Length > 0 && string.Equals(args[0], "stats", StringComparison.Ordinal))
        {
            return PrintStats();
        }

        var seedPath = ValidateFixturePath(args.Length > 0 ? args[0] : "test-fixtures/provider-data.json");
        if (seedPath == null)
        {
            return 1;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return SeedDatabase(seedPath, Path.Combine(appData, "AIUsageTracker", "usage.db"));
    }

    private static string? ValidateFixturePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(relativePath);
        var workingDir = Path.GetFullPath(Environment.CurrentDirectory);

        if (!fullPath.StartsWith(workingDir, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Path traversal rejected: '{relativePath}' resolves outside the working directory.");
            return null;
        }

        return fullPath;
    }

    public static int PrintStats(string? databasePath = null, TextWriter? output = null)
    {
        output ??= Console.Out;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = databasePath ?? Path.Combine(appData, "AIUsageTracker", "usage.db");

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        var fileSize = new FileInfo(dbPath).Length;
        output.WriteLine($"DB file: {dbPath}");
        output.WriteLine($"File size: {fileSize / 1024.0 / 1024.0:F1} MB");
        output.WriteLine();

        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Total rows and date range
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) as total_rows,
                    MIN(datetime(fetched_at, 'unixepoch')) as oldest,
                    MAX(datetime(fetched_at, 'unixepoch')) as newest,
                    AVG(CASE WHEN details_json IS NOT NULL THEN LENGTH(details_json) ELSE 0 END) as avg_details_bytes,
                    SUM(CASE WHEN details_json IS NOT NULL THEN LENGTH(details_json) ELSE 0 END) as total_details_bytes,
                    COUNT(DISTINCT provider_id) as provider_count
                FROM provider_history";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                output.WriteLine($"Total rows:      {reader.GetInt64(0):N0}");
                output.WriteLine($"Oldest entry:    {reader.GetString(1)}");
                output.WriteLine($"Newest entry:    {reader.GetString(2)}");
                output.WriteLine($"Providers:       {reader.GetInt64(5)}");
                output.WriteLine($"Avg details_json:{reader.GetDouble(3):F0} bytes");
                output.WriteLine($"Total details:   {reader.GetInt64(4) / 1024.0 / 1024.0:F1} MB");
            }
        }

        output.WriteLine();
        output.WriteLine("Rows per provider:");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT provider_id, COUNT(*) as cnt,
                       MIN(datetime(fetched_at, 'unixepoch')) as oldest,
                       MAX(datetime(fetched_at, 'unixepoch')) as newest
                FROM provider_history
                GROUP BY provider_id
                ORDER BY cnt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                output.WriteLine($"  {reader.GetString(0),-30} {reader.GetInt64(1),8:N0}  ({reader.GetString(2)[..10]} → {reader.GetString(3)[..10]})");
            }
        }

        output.WriteLine();
        output.WriteLine("Rows per day (last 30 days):");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DATE(fetched_at, 'unixepoch') as day, COUNT(*) as cnt
                FROM provider_history
                WHERE fetched_at >= CAST(strftime('%s', 'now', '-30 days') AS INTEGER)
                GROUP BY day
                ORDER BY day DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                output.WriteLine($"  {reader.GetString(0)}  {reader.GetInt64(1),6:N0}");
            }
        }

        output.WriteLine();
        output.WriteLine("Rows per month (older than 30 days):");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT strftime('%Y-%m', fetched_at, 'unixepoch') as month, COUNT(*) as cnt
                FROM provider_history
                WHERE fetched_at < CAST(strftime('%s', 'now', '-30 days') AS INTEGER)
                GROUP BY month
                ORDER BY month DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                output.WriteLine($"  {reader.GetString(0)}  {reader.GetInt64(1),8:N0}");
            }
        }

        return 0;
    }

    public static int ExportData(string outputPath, string? databasePath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = databasePath ?? Path.Combine(appData, "AIUsageTracker", "usage.db");

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        Console.WriteLine($"Reading from: {dbPath}");

        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var providers = LoadProvidersFromDatabase(connection);
        var latestHistory = LoadLatestHistoryFromDatabase(connection);
        var history7Days = LoadHistory7DaysFromDatabase(connection);

        var fixture = new TestDataFixture
        {
            ExportedAt = DateTime.UtcNow.ToString("o"),
            Providers = providers,
            LatestHistory = latestHistory,
            History7Days = history7Days,
        };

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        WriteTestFixtureToFile(fixture, outputPath);

        Console.WriteLine($"Exported to: {outputPath}");
        Console.WriteLine($"  Providers: {providers.Count}");
        Console.WriteLine($"  Latest history: {latestHistory.Count}");
        Console.WriteLine($"  7-day history: {history7Days.Count}");

        return 0;
    }

    private static List<ProviderFixture> LoadProvidersFromDatabase(SqliteConnection connection)
    {
        var providers = new List<ProviderFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT provider_id, provider_name, account_name, is_active, config_json FROM providers";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                providers.Add(new ProviderFixture
                {
                    ProviderId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ProviderName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    AccountName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsActive = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    ConfigJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                });
            }
        }

        return providers;
    }

    private static List<HistoryFixture> LoadLatestHistoryFromDatabase(SqliteConnection connection)
    {
        var latestHistory = new List<HistoryFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT provider_id, requests_used, requests_available, requests_percentage,
                       is_available, status_message, next_reset_time, fetched_at, details_json, response_latency_ms,
                       http_status, upstream_response_validity, upstream_response_note, parent_provider_id, card_id,
                       group_id, window_kind, model_name, name, card_type, reset_credits_available, reset_credit_expirations_utc
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY provider_id, COALESCE(card_id, '')
                        ORDER BY fetched_at DESC, id DESC) AS row_rank
                    FROM provider_history
                ) latest
                WHERE row_rank = 1
                ORDER BY provider_id, card_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                latestHistory.Add(ReadHistoryFixture(reader));
            }
        }

        return latestHistory;
    }

    private static List<HistoryFixture> LoadHistory7DaysFromDatabase(SqliteConnection connection)
    {
        var history7Days = new List<HistoryFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT provider_id, requests_used, requests_available, requests_percentage,
                       is_available, status_message, next_reset_time, fetched_at, details_json, response_latency_ms,
                       http_status, upstream_response_validity, upstream_response_note, parent_provider_id, card_id,
                       group_id, window_kind, model_name, name, card_type, reset_credits_available, reset_credit_expirations_utc
                FROM provider_history
                WHERE fetched_at >= CAST(strftime('%s', 'now', '-7 days') AS INTEGER)
                ORDER BY provider_id, fetched_at DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                history7Days.Add(ReadHistoryFixture(reader));
            }
        }

        return history7Days;
    }

    private static HistoryFixture ReadHistoryFixture(SqliteDataReader reader)
    {
        return new HistoryFixture
        {
            ProviderId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            RequestsUsed = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
            RequestsAvailable = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
            RequestsPercentage = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
            IsAvailable = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            StatusMessage = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            NextResetTime = reader.IsDBNull(6) ? null : reader.GetString(6),
            FetchedAt = reader.IsDBNull(7) ? string.Empty : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(7)).ToString("o", CultureInfo.InvariantCulture),
            DetailsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
            ResponseLatencyMs = reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
            HttpStatus = reader.GetInt32(10),
            UpstreamResponseValidity = reader.GetInt32(11),
            UpstreamResponseNote = reader.GetString(12),
            ParentProviderId = reader.IsDBNull(13) ? null : reader.GetString(13),
            CardId = reader.IsDBNull(14) ? null : reader.GetString(14),
            GroupId = reader.IsDBNull(15) ? null : reader.GetString(15),
            WindowKind = reader.GetInt32(16),
            ModelName = reader.IsDBNull(17) ? null : reader.GetString(17),
            Name = reader.IsDBNull(18) ? null : reader.GetString(18),
            CardType = reader.IsDBNull(19) ? null : reader.GetString(19),
            ResetCreditsAvailable = reader.IsDBNull(20) ? null : reader.GetInt32(20),
            ResetCreditExpirationsUtc = reader.IsDBNull(21) ? null : reader.GetString(21),
        };
    }

    private static void WriteTestFixtureToFile(TestDataFixture fixture, string outputPath)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Exported to: {outputPath}");
        Console.WriteLine($"  Providers: {fixture.Providers.Count}");
        Console.WriteLine($"  Latest history: {fixture.LatestHistory.Count}");
        Console.WriteLine($"  7-day history: {fixture.History7Days.Count}");
    }

    public static int SeedDatabase(string fixturePath, string dbPath)
    {
        if (!File.Exists(fixturePath))
        {
            Console.Error.WriteLine($"Fixture file not found: {fixturePath}");
            Console.Error.WriteLine("Run scripts/export-provider-data.ps1 on a machine with real provider data first.");
            return 1;
        }

        Console.WriteLine($"Loading fixture from: {fixturePath}");
        TestDataFixture? fixture;
        try
        {
            fixture = JsonSerializer.Deserialize<TestDataFixture>(File.ReadAllText(fixturePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Fixture could not be read or parsed ({ex.GetType().Name}).");
            return 1;
        }

        if (fixture == null || fixture.Providers == null || fixture.Providers.Count == 0 ||
            fixture.LatestHistory == null || fixture.History7Days == null ||
            fixture.Providers.Any(provider => provider == null || string.IsNullOrWhiteSpace(provider.ProviderId)) ||
            fixture.Providers.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != fixture.Providers.Count ||
            fixture.LatestHistory.Concat(fixture.History7Days).Any(history => history == null ||
                string.IsNullOrWhiteSpace(history.ProviderId) || history.StatusMessage == null || history.UpstreamResponseNote == null))
        {
            Console.Error.WriteLine("Fixture is empty or invalid.");
            return 1;
        }

        var historyToInsert = fixture.LatestHistory.Count > 0 ? fixture.LatestHistory : fixture.History7Days;
        var providerIds = fixture.Providers.Select(provider => provider.ProviderId)
            .Concat(historyToInsert.Select(history => history.ProviderId))
            .ToHashSet(StringComparer.Ordinal);
        if (historyToInsert.Any(history => history.ParentProviderId != null && !providerIds.Contains(history.ParentProviderId)))
        {
            Console.Error.WriteLine("Fixture references a parent provider absent from provider metadata and selected history.");
            return 1;
        }

        Console.WriteLine($"Fixture contains {fixture.Providers.Count} providers");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        Console.WriteLine($"Creating database at: {dbPath}");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        connection.Execute(@"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                account_name TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER NOT NULL DEFAULT 1,
                config_json TEXT,
                auth_source TEXT DEFAULT 'manual',
                plan_type TEXT DEFAULT 'usage'
            );

            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                next_reset_time TEXT,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                response_latency_ms REAL NOT NULL DEFAULT 0,
                http_status INTEGER NOT NULL DEFAULT 0,
                upstream_response_validity INTEGER NOT NULL DEFAULT 0,
                upstream_response_note TEXT NOT NULL DEFAULT '',
                fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
                details_json TEXT,
                parent_provider_id TEXT REFERENCES providers(provider_id) ON DELETE SET NULL,
                card_id TEXT,
                group_id TEXT,
                window_kind INTEGER NOT NULL DEFAULT 0,
                model_name TEXT,
                name TEXT,
                card_type TEXT,
                reset_credits_available INTEGER,
                reset_credit_expirations_utc TEXT,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );

            CREATE TABLE reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );

            CREATE TABLE raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL REFERENCES providers(provider_id) ON DELETE CASCADE,
                raw_json TEXT NOT NULL,
                http_status INTEGER NOT NULL DEFAULT 200,
                fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );

            CREATE INDEX IF NOT EXISTS idx_history_provider_time ON provider_history(provider_id, fetched_at);
            CREATE INDEX IF NOT EXISTS idx_raw_fetched ON raw_snapshots(fetched_at);
            CREATE INDEX IF NOT EXISTS idx_reset_provider_time ON reset_events(provider_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_history_fetched_time ON provider_history(fetched_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_provider_id_desc ON provider_history(provider_id, id DESC);
            CREATE INDEX IF NOT EXISTS idx_history_is_available ON provider_history(is_available);
            CREATE INDEX IF NOT EXISTS idx_history_provider_fetched_desc ON provider_history(provider_id, fetched_at DESC);
        ");

        foreach (var provider in fixture.Providers)
        {
            connection.Execute(
                @"
                INSERT INTO providers (provider_id, provider_name, account_name, is_active, updated_at, config_json)
                VALUES (@Id, @Name, @Account, @IsActive, CURRENT_TIMESTAMP, @Config)",
                new
                {
                    Id = provider.ProviderId,
                    Name = provider.ProviderName,
                    Account = provider.AccountName,
                    IsActive = provider.IsActive,
                    Config = provider.ConfigJson,
                });
        }

        foreach (var providerId in historyToInsert.Select(history => history.ProviderId).Distinct(StringComparer.Ordinal))
        {
            // Exported history can outlive its provider metadata. Preserve those rows
            // without presenting a history-only provider as actively configured. Resolve
            // every owner before inserting history so parent references do not depend on order.
            connection.Execute(
                @"
                INSERT INTO providers (provider_id, provider_name, is_active)
                VALUES (@Id, @Id, 0)
                ON CONFLICT(provider_id) DO NOTHING",
                new { Id = providerId });
        }

        foreach (var history in historyToInsert)
        {
            connection.Execute(
                @"
                INSERT INTO provider_history (provider_id, requests_used, requests_available, requests_percentage,
                    is_available, status_message, next_reset_time, fetched_at, details_json, response_latency_ms,
                    http_status, upstream_response_validity, upstream_response_note, parent_provider_id, card_id,
                    group_id, window_kind, model_name, name, card_type, reset_credits_available, reset_credit_expirations_utc)
                VALUES (@Id, @Used, @Avail, @Perc, @IsAvail, @Msg, @Next, @Fetched, @Details, @Latency,
                    @HttpStatus, @UpstreamResponseValidity, @UpstreamResponseNote, @ParentProviderId, @CardId,
                    @GroupId, @WindowKind, @ModelName, @Name, @CardType, @ResetCreditsAvailable, @ResetCreditExpirationsUtc)",
                new
                {
                    Id = history.ProviderId,
                    Used = history.RequestsUsed,
                    Avail = history.RequestsAvailable,
#pragma warning disable CS0618 // RequestsPercentage: database seeder pass-through
                    Perc = history.RequestsPercentage,
#pragma warning restore CS0618
                    IsAvail = history.IsAvailable,
                    Msg = history.StatusMessage,
                    Next = history.NextResetTime,
                    Fetched = ToFetchedAtEpochSeconds(history.FetchedAt),
                    Details = history.DetailsJson,
                    Latency = history.ResponseLatencyMs,
                    history.HttpStatus,
                    history.UpstreamResponseValidity,
                    history.UpstreamResponseNote,
                    history.ParentProviderId,
                    history.CardId,
                    history.GroupId,
                    history.WindowKind,
                    history.ModelName,
                    history.Name,
                    history.CardType,
                    history.ResetCreditsAvailable,
                    history.ResetCreditExpirationsUtc,
                });
        }

        Console.WriteLine($"Seeded {fixture.Providers.Count} providers with {historyToInsert.Count} history records.");
        return 0;
    }

    private static long ToFetchedAtEpochSeconds(string fetchedAt)
    {
        if (long.TryParse(fetchedAt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            return epoch;
        }

        if (DateTimeOffset.TryParse(
                fetchedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToUnixTimeSeconds();
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private sealed class ProviderFixture
    {
        [JsonPropertyName("provider_id")]
        public string ProviderId { get; set; } = string.Empty;

        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; } = string.Empty;

        [JsonPropertyName("account_name")]
        public string? AccountName { get; set; }

        [JsonPropertyName("is_active")]
        public int IsActive { get; set; }

        [JsonPropertyName("config_json")]
        public string? ConfigJson { get; set; }
    }

    private sealed class HistoryFixture
    {
        [JsonPropertyName("provider_id")]
        public string ProviderId { get; set; } = string.Empty;

        [JsonPropertyName("requests_used")]
        public double RequestsUsed { get; set; }

        [JsonPropertyName("requests_available")]
        public double RequestsAvailable { get; set; }

        [JsonPropertyName("requests_percentage")]
        public double RequestsPercentage { get; set; }

        [JsonPropertyName("is_available")]
        public int IsAvailable { get; set; }

        [JsonPropertyName("status_message")]
        public string StatusMessage { get; set; } = string.Empty;

        [JsonPropertyName("next_reset_time")]
        public string? NextResetTime { get; set; }

        [JsonPropertyName("fetched_at")]
        public string FetchedAt { get; set; } = string.Empty;

        [JsonPropertyName("details_json")]
        public string? DetailsJson { get; set; }

        [JsonPropertyName("response_latency_ms")]
        public double ResponseLatencyMs { get; set; }

        [JsonPropertyName("http_status")]
        public int HttpStatus { get; set; }

        [JsonPropertyName("upstream_response_validity")]
        public int UpstreamResponseValidity { get; set; }

        [JsonPropertyName("upstream_response_note")]
        public string UpstreamResponseNote { get; set; } = string.Empty;

        [JsonPropertyName("parent_provider_id")]
        public string? ParentProviderId { get; set; }

        [JsonPropertyName("card_id")]
        public string? CardId { get; set; }

        [JsonPropertyName("group_id")]
        public string? GroupId { get; set; }

        [JsonPropertyName("window_kind")]
        public int WindowKind { get; set; }

        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("card_type")]
        public string? CardType { get; set; }

        [JsonPropertyName("reset_credits_available")]
        public int? ResetCreditsAvailable { get; set; }

        [JsonPropertyName("reset_credit_expirations_utc")]
        public string? ResetCreditExpirationsUtc { get; set; }
    }

    private sealed class TestDataFixture
    {
        [JsonPropertyName("exported_at")]
        public string ExportedAt { get; set; } = string.Empty;

        [JsonPropertyName("providers")]
        public List<ProviderFixture> Providers { get; set; } = [];

        [JsonPropertyName("latest_history")]
        public List<HistoryFixture> LatestHistory { get; set; } = [];

        [JsonPropertyName("history_7days")]
        public List<HistoryFixture> History7Days { get; set; } = [];
    }
}
