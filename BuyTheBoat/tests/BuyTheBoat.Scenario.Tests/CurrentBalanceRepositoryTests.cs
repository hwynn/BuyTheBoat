using Microsoft.Data.Sqlite;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.Scenario.Tests;

public class CurrentBalanceRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
    private readonly CurrentBalanceRepository _repository;

    public CurrentBalanceRepositoryTests()
    {
        _repository = new CurrentBalanceRepository(new PatternDatabase(_databasePath));
    }

    [Fact]
    public void A_fresh_database_has_no_current_balance_yet()
    {
        _repository.GetCurrent().ShouldBeNull();
    }

    [Fact]
    public void Saved_balance_date_horizon_and_cushion_round_trip()
    {
        _repository.Save(1234.56m, new DateOnly(2025, 3, 14), new DateOnly(2030, 3, 14), 250m);

        var current = _repository.GetCurrent();
        current.ShouldNotBeNull();
        current.Balance.ShouldBe(1234.56m);
        current.AsOfDate.ShouldBe(new DateOnly(2025, 3, 14));
        current.HorizonEndDate.ShouldBe(new DateOnly(2030, 3, 14));
        current.IdealSafetyCushion.ShouldBe(250m);
    }

    [Fact]
    public void Saving_again_updates_the_same_row_instead_of_adding_another()
    {
        _repository.Save(100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), 0m);
        _repository.Save(200m, new DateOnly(2025, 2, 2), new DateOnly(2026, 2, 2), 300m);

        var current = _repository.GetCurrent();
        current.ShouldNotBeNull();
        current.Balance.ShouldBe(200m);
        current.AsOfDate.ShouldBe(new DateOnly(2025, 2, 2));
        current.HorizonEndDate.ShouldBe(new DateOnly(2026, 2, 2));
        current.IdealSafetyCushion.ShouldBe(300m);
    }

    [Fact]
    public void A_row_saved_before_the_horizon_column_existed_falls_back_to_a_3_month_default()
    {
        // Simulates a pre-migration row, written directly rather than through
        // Save (which always supplies a horizon) — HorizonEndDate left NULL.
        using var connection = new PatternDatabase(_databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CurrentBalance (Id, Balance, AsOfDate, HorizonEndDate)
            VALUES (1, '500', '2025-01-01', NULL);
            """;
        command.ExecuteNonQuery();

        var current = _repository.GetCurrent();
        current.ShouldNotBeNull();
        current.HorizonEndDate.ShouldBe(new DateOnly(2025, 4, 1));
    }

    [Fact]
    public void A_row_saved_before_the_cushion_column_existed_falls_back_to_zero()
    {
        // Pre-migration row (IdealSafetyCushion NULL) — cushion defaults to off.
        using var connection = new PatternDatabase(_databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CurrentBalance (Id, Balance, AsOfDate, HorizonEndDate, IdealSafetyCushion)
            VALUES (1, '500', '2025-01-01', '2025-04-01', NULL);
            """;
        command.ExecuteNonQuery();

        var current = _repository.GetCurrent();
        current.ShouldNotBeNull();
        current.IdealSafetyCushion.ShouldBe(0m);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
