namespace MyMoneyForecast.Domain;

// A real bank account the user tracks — checking, savings, and so on. Each is an
// independent silo with its own current balance and its own safety cushion; a
// bill, paycheck, or goal belongs to exactly one account, and money only crosses
// between accounts through a Transfer. See planning/10-multiple-accounts.md,
// item 1. Id is a hidden surrogate (the user only ever sees/enters the Name,
// which is unique); balances are the one hand-entered number per account, all as
// of the forecast's single global as-of date (no bank import yet).
public sealed record AccountOptions
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public decimal Balance { get; init; }
    public decimal IdealSafetyCushion { get; init; }
}

public sealed class Account
{
    public int Id { get; }
    public string Name { get; }
    public decimal Balance { get; }

    // Per-account safety cushion (3.8.a1). 0 means "no cushion set" — the UI
    // hides it for accounts the user hasn't given one.
    public decimal IdealSafetyCushion { get; }

    private Account(AccountOptions options)
    {
        Id = options.Id;
        Name = options.Name;
        Balance = options.Balance;
        IdealSafetyCushion = options.IdealSafetyCushion;
    }

    public static Account Create(AccountOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            // 3.1.a1: account (name) cannot be None. Uniqueness of the name is
            // enforced at the storage layer (a UNIQUE column).
            throw new ArgumentException("Account name cannot be empty.", nameof(options));
        }

        if (options.IdealSafetyCushion < 0)
        {
            throw new ArgumentException("Safety cushion cannot be negative.", nameof(options));
        }

        // Balance is intentionally unconstrained — a real account can be
        // overdrawn (negative), and that is a truth worth showing, not blocking.
        return new Account(options);
    }
}
