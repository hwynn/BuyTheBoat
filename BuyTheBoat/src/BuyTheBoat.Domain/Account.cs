namespace BuyTheBoat.Domain;

/// <summary>The fields for building an Account with Account.Create.</summary>
public sealed record AccountOptions
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public decimal Balance { get; init; }
    public decimal IdealSafetyCushion { get; init; }
}

/// <summary>
/// A real bank account the user tracks, such as checking or savings, with its own balance and safety cushion.
/// A bill, paycheck, or goal belongs to exactly one account, and money only moves between accounts through a Transfer.
/// The user only sees the Name, which is unique; Id is internal.
/// Balance is the one number entered by hand per account, as of the forecast's as-of date.
/// </summary>
public sealed class Account
{
    public int Id { get; }
    public string Name { get; }
    public decimal Balance { get; }

    /// <summary>This account's safety cushion (3.8.a1); 0 means none is set, and the UI hides it.</summary>
    public decimal IdealSafetyCushion { get; }

    /// <summary>[CALC] Builds an Account from already-validated options.</summary>
    /// <param name="options">The account's identity, balance, and cushion.</param>
    private Account(AccountOptions options)
    {
        Id = options.Id;
        Name = options.Name;
        Balance = options.Balance;
        IdealSafetyCushion = options.IdealSafetyCushion;
    }

    /// <summary>[CALC] Creates an account, validating that the name isn't blank and the safety cushion isn't negative.</summary>
    /// <param name="options">The account's identity, balance, and cushion.</param>
    public static Account Create(AccountOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            // 3.1.a1: the account name can't be None; the database enforces that it's unique.
            throw new AccountNameMissingException("Account name cannot be empty.", nameof(options));
        }

        if (options.IdealSafetyCushion < 0)
        {
            throw new ArgumentException("Safety cushion cannot be negative.", nameof(options));
        }

        // Balance can be negative, since a real account can be overdrawn.
        return new Account(options);
    }
}
