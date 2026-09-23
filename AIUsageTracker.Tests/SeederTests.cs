// <copyright file="SeederTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Tests;

public sealed class SeederTests
{
    [Fact]
    public void SeedDatabase_CheckedInFixture_PreservesHistoryAndForeignKeys()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "provider-data.json");
        Assert.True(File.Exists(fixturePath), $"Checked-in fixture is copied to the test output: {fixturePath}");

        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var providers = fixture.RootElement.GetProperty("providers").EnumerateArray().ToArray();
        var history = fixture.RootElement.GetProperty("latest_history").EnumerateArray().ToArray();
        var providerIds = providers.Select(provider => provider.GetProperty("provider_id").GetString()).ToHashSet(StringComparer.Ordinal);
        var missingIds = history.Select(row => row.GetProperty("provider_id").GetString())
            .Where(id => id is not null && !providerIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            missingIds.Length > 0,
            "Checked-in fixture still contains history rows without provider metadata (the CI failure mode).");

        var liveStamp = CaptureLiveDatabaseStamp();
        var directory = TestTempPaths.CreateDirectory("seeder-tests");
        var database = Path.Combine(directory, "usage.db");
        try
        {
            Assert.Equal(0, Seeder.Program.SeedDatabase(fixturePath, database));

            using var connection = Open(database);
            Assert.Equal(history.Length, Scalar(connection, "SELECT COUNT(*) FROM provider_history"));
            Assert.Equal(providers.Length + missingIds.Length, Scalar(connection, "SELECT COUNT(*) FROM providers"));
            Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal("ok", Text(connection, "PRAGMA integrity_check"));
            Assert.Equal(history.Length, Scalar(connection, "SELECT COUNT(*) FROM provider_history WHERE typeof(fetched_at) = 'integer'"));

            foreach (var provider in providers)
            {
                Assert.Equal(
                    provider.GetProperty("is_active").GetInt32(),
                    Scalar(
                        connection,
                        "SELECT is_active FROM providers WHERE provider_id = $id",
                        ("$id", provider.GetProperty("provider_id").GetString()!)));
            }

            foreach (var id in missingIds)
            {
                Assert.Equal(0, Scalar(connection, "SELECT is_active FROM providers WHERE provider_id = $id", ("$id", id!)));
            }
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }

        AssertLiveDatabaseUntouched(liveStamp);
    }

    [Fact]
    public void SeedDatabase_HistoryWithoutProviderMetadata_InsertsInactiveStub()
    {
        var liveStamp = CaptureLiveDatabaseStamp();
        var directory = TestTempPaths.CreateDirectory("seeder-tests");
        var database = Path.Combine(directory, "usage.db");
        var fixturePath = Path.Combine(directory, "fixture.json");
        try
        {
            File.WriteAllText(
                fixturePath,
                """
                {
                  "exported_at": "2026-09-23T00:00:00Z",
                  "providers": [
                    { "provider_id": "known", "provider_name": "Known", "is_active": 1 }
                  ],
                  "latest_history": [
                    {
                      "provider_id": "known",
                      "requests_used": 1,
                      "requests_available": 10,
                      "requests_percentage": 10,
                      "is_available": 1,
                      "status_message": "ok",
                      "fetched_at": "2026-09-23T00:00:00Z"
                    },
                    {
                      "provider_id": "orphan",
                      "requests_used": 2,
                      "requests_available": 10,
                      "requests_percentage": 20,
                      "is_available": 0,
                      "status_message": "gone",
                      "fetched_at": "2026-09-23T00:00:01Z"
                    }
                  ]
                }
                """);

            Assert.Equal(0, Seeder.Program.SeedDatabase(fixturePath, database));

            using var connection = Open(database);
            Assert.Equal(2, Scalar(connection, "SELECT COUNT(*) FROM providers"));
            Assert.Equal(1, Scalar(connection, "SELECT is_active FROM providers WHERE provider_id = 'known'"));
            Assert.Equal(0, Scalar(connection, "SELECT is_active FROM providers WHERE provider_id = 'orphan'"));
            Assert.Equal("orphan", Text(connection, "SELECT provider_name FROM providers WHERE provider_id = 'orphan'"));
            Assert.Equal(2, Scalar(connection, "SELECT COUNT(*) FROM provider_history"));
            Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        }
        finally
        {
            TestTempPaths.CleanupPath(directory);
        }

        AssertLiveDatabaseUntouched(liveStamp);
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

    private static int Scalar(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static (bool Exists, DateTime LastWriteUtc, long Length) CaptureLiveDatabaseStamp()
    {
        var livePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsageTracker",
            "usage.db");
        if (!File.Exists(livePath))
        {
            return (false, DateTime.MinValue, 0);
        }

        var info = new FileInfo(livePath);
        return (true, info.LastWriteTimeUtc, info.Length);
    }

    private static void AssertLiveDatabaseUntouched((bool Exists, DateTime LastWriteUtc, long Length) before)
    {
        var after = CaptureLiveDatabaseStamp();
        Assert.Equal(before.Exists, after.Exists);
        Assert.Equal(before.LastWriteUtc, after.LastWriteUtc);
        Assert.Equal(before.Length, after.Length);
    }
}
