using System.Globalization;
using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// Per-account storage (Id + unique Name + balance + cushion). Ids are
// app-assigned like FinanceId (NextId = max + 1); the migration seeds the first
// account, "primary", as Id 1. Decimals are invariant-culture strings, matching
// the rest of this layer. Name uniqueness is enforced by the table's UNIQUE
// column (SQLite honours UNIQUE, unlike declared foreign keys). See
// planning/10-multiple-accounts.md item 6.
public sealed class AccountRepository(PatternDatabase database)
{
    public void Save(Account account)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Accounts (Id, Name, Balance, IdealSafetyCushion)
            VALUES ($Id, $Name, $Balance, $IdealSafetyCushion)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Balance = excluded.Balance,
                IdealSafetyCushion = excluded.IdealSafetyCushion;
            """;

        command.Parameters.AddWithValue("$Id", account.Id);
        command.Parameters.AddWithValue("$Name", account.Name);
        command.Parameters.AddWithValue("$Balance", account.Balance.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$IdealSafetyCushion", account.IdealSafetyCushion.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Account> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Balance, IdealSafetyCushion FROM Accounts ORDER BY Id;";

        using var reader = command.ExecuteReader();
        var accounts = new List<Account>();
        while (reader.Read())
        {
            accounts.Add(Read(reader));
        }

        return accounts;
    }

    public Account? GetById(int id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Balance, IdealSafetyCushion FROM Accounts WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public Account? GetByName(string name)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Balance, IdealSafetyCushion FROM Accounts WHERE Name = $Name;";
        command.Parameters.AddWithValue("$Name", name);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    // The next app-assigned Id (max + 1, or 1 for the very first account) —
    // mirrors how FinanceId is assigned, and lets the migration seed "primary"
    // as Id 1.
    public int NextId()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Accounts;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // Startup migration (item 6): every install must have at least one account.
    // On the first run after multi-account lands, the single legacy balance and
    // cushion become the "primary" account (Id 1 — which the later AccountId
    // backfill defaults to); a brand-new install just gets an empty one.
    // Idempotent: does nothing once any account exists.
    public Account EnsureDefaultAccount(decimal seedBalance, decimal seedCushion)
    {
        var existing = GetAll();
        if (existing.Count > 0)
        {
            return existing[0];
        }

        var primary = Account.Create(new AccountOptions
        {
            Id = 1,
            Name = "primary",
            Balance = seedBalance,
            IdealSafetyCushion = seedCushion,
        });

        Save(primary);
        return primary;
    }

    // No reference guard yet: nothing points at an account until patterns gain
    // AccountId (item 2) and transfers exist (item 3). The block-if-referenced
    // guard lands with those, alongside the App-level delete flow.
    public void Delete(int id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Accounts WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    private static Account Read(SqliteDataReader reader) =>
        Account.Create(new AccountOptions
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Balance = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            IdealSafetyCushion = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
        });
}
