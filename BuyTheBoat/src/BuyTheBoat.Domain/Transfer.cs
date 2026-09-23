namespace BuyTheBoat.Domain;

// A user-scheduled movement of money between two of their own accounts. A
// transfer is built from a pair of ordinary FinancialPatterns — a withdrawal
// from the source account and a matching deposit into the destination. This
// record is the canonical definition those two patterns are generated from
// and validated against. It is owned by the book, not by any single account,
// because a transfer spans two.
public sealed record TransferOptions
{
    public required int Id { get; init; }
    public required int FromAccountId { get; init; }
    public required int ToAccountId { get; init; }
    public required decimal Amount { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
}

public sealed class Transfer
{
    public int Id { get; }
    public int FromAccountId { get; }
    public int ToAccountId { get; }

    // Always a positive magnitude — direction lives in From/To, not the sign.
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

    /// <summary>[CALC] Returns a copy of this transfer ending on the given (earlier) date instead — everything else, including which accounts and how much, unchanged. Used to end a transfer early, including as the predecessor half of a break-off.</summary>
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
