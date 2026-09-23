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
