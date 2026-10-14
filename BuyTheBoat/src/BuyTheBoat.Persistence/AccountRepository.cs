using System.Globalization;
using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;

namespace BuyTheBoat.Persistence;

/// <summary>
/// Stores accounts: Id, unique Name, balance, and safety cushion.
/// Ids are assigned by the app like FinanceId (NextId = max + 1); EnsureDefaultAccount creates the first account, "primary", as Id 1.
/// Decimals are stored as invariant-culture strings like the rest of this layer, and the table's UNIQUE column keeps names unique (SQLite enforces UNIQUE, unlike declared foreign keys).
/// </summary>
public sealed class AccountRepository(PatternDatabase database)
{
    /// <summary>[WRITES FILE] Creates a new account, or updates the existing one with the same Id.</summary>
    /// <param name="account">The account to save.</param>
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

    /// <summary>[READS FILE] Returns every account, in Id order.</summary>
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

    /// <summary>[READS FILE] Returns one account by its Id, or null if none exists.</summary>
    /// <param name="id">The account to look up.</param>
    public Account? GetById(int id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Balance, IdealSafetyCushion FROM Accounts WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>[READS FILE] Returns one account by its name, or null if none exists.</summary>
    /// <param name="name">The account name to look up.</param>
    public Account? GetByName(string name)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Balance, IdealSafetyCushion FROM Accounts WHERE Name = $Name;";
        command.Parameters.AddWithValue("$Name", name);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>[READS FILE] Returns the next Id to assign a new account: max existing + 1, or 1 if there are none yet.</summary>
    public int NextId()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Accounts;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// [WRITES FILE] Makes sure at least one account exists, creating a "primary" one from the older single balance and cushion when there are none.
    /// Does nothing once any account exists.
    /// </summary>
    /// <param name="seedBalance">The balance the primary account starts with when it's created.</param>
    /// <param name="seedCushion">The safety cushion the primary account starts with when it's created.</param>
    public Account EnsureDefaultAccount(decimal seedBalance, decimal seedCushion)
    {
        var existing = GetAll();
        if (existing.Count > 0)
        {
            return existing[0];
        }

        var primary = Account.Create(new AccountOptions
        {
            // Id 1, because the AccountId column's DEFAULT 1 files every older FinancialPatterns row under this account.
            Id = 1,
            Name = "primary",
            Balance = seedBalance,
            IdealSafetyCushion = seedCushion,
        });

        Save(primary);
        return primary;
    }

    /// <summary>
    /// [DELETES] Removes an account, without checking whether anything still uses it.
    /// MainWindow checks HasPatternsInAccount and IsAccountReferenced first.
    /// </summary>
    /// <param name="id">The account to delete.</param>
    public void Delete(int id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Accounts WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>[CALC] Builds an Account from one row of an Accounts query result.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
    private static Account Read(SqliteDataReader reader) =>
        Account.Create(new AccountOptions
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Balance = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            IdealSafetyCushion = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
        });
}
