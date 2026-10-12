namespace BuyTheBoat.Domain;

/// <summary>The fields for building a Transfer with Transfer.Create.</summary>
public sealed record TransferOptions
{
    public required int Id { get; init; }
    public required int FromAccountId { get; init; }
    public required int ToAccountId { get; init; }
    public required decimal Amount { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
}

/// <summary>
/// A scheduled movement of money between two of the user's own accounts, made up of two ordinary FinancialPatterns: a withdrawal from one account and a matching deposit into the other.
/// This record is the definition both patterns are generated from and checked against; it belongs to the book, not to either account, since it spans two.
/// </summary>
public sealed class Transfer
{
    public int Id { get; }
    public int FromAccountId { get; }
    public int ToAccountId { get; }

    /// <summary>Always positive; From and To give the direction.</summary>
    public decimal Amount { get; }
    public RecurrenceRule DatePattern { get; }

    /// <summary>[CALC] Builds a Transfer from already-validated options.</summary>
    /// <param name="options">The transfer's accounts, amount, and schedule.</param>
    private Transfer(TransferOptions options)
    {
        Id = options.Id;
        FromAccountId = options.FromAccountId;
        ToAccountId = options.ToAccountId;
        Amount = options.Amount;
        DatePattern = options.DatePattern;
    }

    /// <summary>[CALC] Creates a transfer, validating that the two accounts differ and the amount is positive.</summary>
    /// <param name="options">The transfer's accounts, amount, and schedule.</param>
    public static Transfer Create(TransferOptions options)
    {
        if (options.FromAccountId == options.ToAccountId)
        {
            throw new ArgumentException("A transfer must be between two different accounts.", nameof(options));
        }

        if (options.Amount <= 0m)
        {
            throw new ArgumentException("Transfer amount must be greater than zero.", nameof(options));
        }

        return new Transfer(options);
    }

    /// <summary>[CALC] Returns a copy of this transfer ending on the given earlier date instead, for ending it early or as the first half of a break-off; everything else is unchanged.</summary>
    /// <param name="until">The new, earlier end date.</param>
    public Transfer WithUntil(DateOnly until) => Create(new TransferOptions
    {
        Id = Id,
        FromAccountId = FromAccountId,
        ToAccountId = ToAccountId,
        Amount = Amount,
        DatePattern = DatePattern.WithUntil(until),
    });
}
