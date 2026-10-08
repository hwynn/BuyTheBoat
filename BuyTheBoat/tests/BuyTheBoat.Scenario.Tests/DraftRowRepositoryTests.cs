using Microsoft.Data.Sqlite;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.Scenario.Tests;

public class DraftRowRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
    private readonly DraftRowRepository _repository;

    public DraftRowRepositoryTests()
    {
        _repository = new DraftRowRepository(new PatternDatabase(_databasePath));
    }

    [Fact]
    public void A_fresh_database_has_no_drafts()
    {
        _repository.GetAll().ShouldBeEmpty();
        _repository.NextId().ShouldBe(1);
    }

    [Fact]
    public void A_draft_round_trips_with_its_text_exactly_as_typed_including_invalid_text()
    {
        var draft = new DraftRow
        {
            Id = 1,
            Source = "Car insurance",
            Amount = "about 140",
            Frequency = "every 6 mo",
            Notes = "check policy PDF, \"exact\" date",
        };

        _repository.Save(draft);

        _repository.GetAll().ShouldBe([draft]);
    }

    [Fact]
    public void Saving_a_draft_again_replaces_it_and_drafts_come_back_oldest_first()
    {
        _repository.Save(new DraftRow { Id = 2, Source = "Gym" });
        _repository.Save(new DraftRow { Id = 1, Source = "Netflix" });
        _repository.Save(new DraftRow { Id = 1, Source = "Netflix", Amount = "-15.49" });

        var drafts = _repository.GetAll();
        drafts.Select(draft => draft.Id).ShouldBe([1, 2]);
        drafts[0].Amount.ShouldBe("-15.49");
        _repository.NextId().ShouldBe(3);
    }

    [Fact]
    public void A_deleted_draft_is_gone_and_deleting_it_twice_does_nothing()
    {
        _repository.Save(new DraftRow { Id = 1, Source = "Netflix" });

        _repository.Delete(1);
        _repository.Delete(1);

        _repository.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void A_database_made_before_drafts_existed_gains_the_table_on_open()
    {
        // Drop the table to stand in for an older export, then reopen as startup would.
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE DraftPatterns;";
            command.ExecuteNonQuery();
        }

        var reopened = new DraftRowRepository(new PatternDatabase(_databasePath));
        reopened.GetAll().ShouldBeEmpty();
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
