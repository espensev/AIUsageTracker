// <copyright file="SeederTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Nodes;
using AIUsageTracker.Tests.Infrastructure;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Tests;

public sealed class SeederTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"providers\":null}")]
    [InlineData("{\"providers\":[null]}")]
    [InlineData("{\"providers\":[{\"provider_id\":\"duplicate\"},{\"provider_id\":\"duplicate\"}]}")]
    [InlineData("{\"providers\":[{\"provider_id\":\"child\"}],\"latest_history\":[{\"provider_id\":\"child\",\"parent_provider_id\":\"missing\"}]}")]
    [InlineData("{\"providers\":[{\"provider_id\":\"Parent\"},{\"provider_id\":\"child\"}],\"latest_history\":[{\"provider_id\":\"child\",\"parent_provider_id\":\"parent\"}]}")]
    public void SeedDatabase_InvalidInput_PreservesExistingDatabaseBytes(string? fixtureContents)
    {
        var directory = TestTempPaths.CreateDirectory("seeder-input-safety");
        try
        {
            var database = Path.Combine(directory, "existing.db");
            using (var connection = Open(database))
            {
                connection.Execute("CREATE TABLE preserved (value TEXT); INSERT INTO preserved VALUES ('retained');");
            }

            var before = File.ReadAllBytes(database);
            var fixture = Path.Combine(directory, "invalid.json");
            if (fixtureContents != null)
            {
                File.WriteAllText(fixture, fixtureContents);
            }

            var result = -1;
            var error = Record.Exception(() => result = Seeder.Program.SeedDatabase(fixture, database));

            Assert.True(File.Exists(database));
            Assert.Equal(before, File.ReadAllBytes(database));
            Assert.Null(error);
            Assert.Equal(1, result);
            var newParent = Path.Combine(directory, "untouched-parent");
            Assert.Equal(1, Seeder.Program.SeedDatabase(fixture, Path.Combine(newParent, "usage.db")));
            Assert.False(Directory.Exists(newParent));
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeedDatabase_ParentAppearsLaterInHistory_ResolvesExactCaseIdentityBeforeInsert(bool useSevenDayHistory)
    {
        var directory = TestTempPaths.CreateDirectory("seeder-parent-order");
        try
        {
            var fixture = Path.Combine(directory, "parent-fixture.json");
            var database = Path.Combine(directory, "usage.db");
            var history = new[]
            {
                new { provider_id = "child", parent_provider_id = "Parent" },
                new { provider_id = "Parent", parent_provider_id = (string?)null },
            };
            File.WriteAllText(fixture, JsonSerializer.Serialize(new
            {
                providers = new[] { new { provider_id = "parent", provider_name = "Metadata owner", is_active = 1 } },
                latest_history = useSevenDayHistory ? [] : history,
                history_7days = useSevenDayHistory ? history : [],
            }));

            Assert.Equal(0, Seeder.Program.SeedDatabase(fixture, database));

            using var connection = Open(database);
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM provider_history"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM providers WHERE provider_id IN ('Parent', 'parent')"));
            Assert.Equal("Parent", connection.ExecuteScalar<string>("SELECT parent_provider_id FROM provider_history WHERE provider_id = 'child'"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT is_active FROM providers WHERE provider_id = 'parent'"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT is_active FROM providers WHERE provider_id = 'Parent'"));
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }
    }

    [Fact]
    public void ExportData_EpochHistory_PreservesCardsAndFieldsThroughReseed()
    {
        var directory = TestTempPaths.CreateDirectory("seeder-roundtrip");
        try
        {
            var original = Path.Combine(directory, "original.db");
            var restored = Path.Combine(directory, "restored.db");
            var exported = Path.Combine(directory, "export.json");
            var fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "provider-data.json");
            Assert.Equal(0, Seeder.Program.SeedDatabase(fixture, original));
            using var connection = Open(original);
            var providerId = connection.ExecuteScalar<string>("SELECT provider_id FROM providers LIMIT 1")!;
            connection.Execute("DELETE FROM provider_history");
            var now = DateTimeOffset.UtcNow;
            const string insert = @"
INSERT INTO provider_history (provider_id, requests_used, requests_available, requests_percentage,
    is_available, status_message, next_reset_time, fetched_at, details_json, response_latency_ms,
    http_status, upstream_response_validity, upstream_response_note, parent_provider_id, card_id,
    group_id, window_kind, model_name, name, card_type, reset_credits_available, reset_credit_expirations_utc)
VALUES (@Provider, 12.5, 100, 12.5, 1, 'retained history', '2026-11-01T00:00:00Z', @Fetched,
    '[]', 42.5, 200, 1, 'valid fixture', @Provider, @Card, @Provider, 2, 'model-fixture', @Card,
    'windowed', 3, '[""2026-11-01T00:00:00Z""]');";
            connection.Execute(insert, new { Provider = providerId, Card = "weekly-credits", Fetched = now.AddDays(-1).ToUnixTimeSeconds() });
            connection.Execute(insert, new { Provider = providerId, Card = "on-demand-credits", Fetched = now.AddDays(-7).AddHours(1).ToUnixTimeSeconds() });
            connection.Execute(insert, new { Provider = providerId, Card = "on-demand-credits", Fetched = now.AddDays(-7).AddHours(-1).ToUnixTimeSeconds() });
            connection.Execute(insert, new { Provider = providerId, Card = "weekly-credits", Fetched = now.AddDays(-40).ToUnixTimeSeconds() });

            Assert.Equal(0, Seeder.Program.ExportData(exported, original));
            using var json = JsonDocument.Parse(File.ReadAllText(exported));
            Assert.Equal(2, json.RootElement.GetProperty("history_7days").GetArrayLength());
            Assert.Equal(2, json.RootElement.GetProperty("latest_history").GetArrayLength());
            Assert.Equal(0, Seeder.Program.SeedDatabase(exported, restored));
            using var restoredConnection = Open(restored);
            const string fields = "provider_id,requests_used,requests_available,requests_percentage,is_available,status_message,next_reset_time,fetched_at,details_json,response_latency_ms,http_status,upstream_response_validity,upstream_response_note,parent_provider_id,card_id,group_id,window_kind,model_name,name,card_type,reset_credits_available,reset_credit_expirations_utc";
            var expected = connection.Query($"SELECT {fields} FROM provider_history WHERE fetched_at >= @Cutoff ORDER BY card_id", new { Cutoff = now.AddDays(-7).ToUnixTimeSeconds() }).ToArray();
            var actual = restoredConnection.Query($"SELECT {fields} FROM provider_history ORDER BY card_id").ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var row = 0; row < expected.Length; row++)
            {
                var expectedFields = (IDictionary<string, object>)expected[row];
                var actualFields = (IDictionary<string, object>)actual[row];
                foreach (var field in expectedFields.Keys)
                {
                    Assert.Equal(expectedFields[field], actualFields[field]);
                }
            }

            Assert.Equal(0, restoredConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check"));
            using var statistics = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(0, Seeder.Program.PrintStats(original, statistics));
            var daySection = statistics.ToString().Split("Rows per day (last 30 days):", StringSplitOptions.None)[1]
                .Split("Rows per month (older than 30 days):", StringSplitOptions.None)[0];
            var monthSection = statistics.ToString().Split("Rows per month (older than 30 days):", StringSplitOptions.None)[1];
            Assert.Contains(now.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), daySection, StringComparison.Ordinal);
            Assert.DoesNotContain(now.AddDays(-40).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), daySection, StringComparison.Ordinal);
            Assert.Contains(now.AddDays(-40).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), monthSection, StringComparison.Ordinal);
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeedDatabase_CheckedInFixture_PreservesHistoryAndForeignKeys(bool useSevenDayHistory)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "provider-data.json");
        Assert.True(File.Exists(fixturePath), $"Checked-in fixture is copied to the test output: {fixturePath}");

        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var providers = fixture.RootElement.GetProperty("providers").EnumerateArray().ToArray();
        var history = fixture.RootElement.GetProperty(useSevenDayHistory ? "history_7days" : "latest_history").EnumerateArray().ToArray();
        var providerIds = providers.Select(provider => provider.GetProperty("provider_id").GetString()).ToHashSet(StringComparer.Ordinal);
        var missingIds = history.Select(row => row.GetProperty("provider_id").GetString())
            .Where(id => id is not null && !providerIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            missingIds.Length > 0,
            "Checked-in fixture still contains history rows without provider metadata (the CI failure mode).");

        var directory = TestTempPaths.CreateDirectory("seeder-tests");
        var database = Path.Combine(directory, "usage.db");
        try
        {
            if (useSevenDayHistory)
            {
                var fallbackFixture = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
                fallbackFixture["latest_history"] = new JsonArray();
                fixturePath = Path.Combine(directory, "fallback-fixture.json");
                File.WriteAllText(fixturePath, fallbackFixture.ToJsonString());
            }

            Assert.Equal(0, Seeder.Program.SeedDatabase(fixturePath, database));

            using var connection = Open(database);
            Assert.Equal(history.Length, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM provider_history"));
            Assert.Equal(providers.Length + missingIds.Length, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM providers"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check"));
            Assert.Equal(history.Length, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM provider_history WHERE typeof(fetched_at) = 'integer'"));

            foreach (var provider in providers)
            {
                Assert.Equal(
                    provider.GetProperty("is_active").GetInt32(),
                    connection.ExecuteScalar<int>(
                        "SELECT is_active FROM providers WHERE provider_id = @Id",
                        new { Id = provider.GetProperty("provider_id").GetString()! }));
            }

            foreach (var id in missingIds)
            {
                Assert.Equal(0, connection.ExecuteScalar<int>("SELECT is_active FROM providers WHERE provider_id = @Id", new { Id = id! }));
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM providers WHERE provider_id = @Id AND provider_name = @Id", new { Id = id! }));
            }
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }
    }

    private static SqliteConnection Open(string database)
    {
        var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
