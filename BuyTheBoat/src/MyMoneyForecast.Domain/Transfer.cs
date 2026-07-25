namespace MyMoneyForecast.Domain;

// A user-scheduled movement of money between two of their own accounts. There
// is no "transfer" in the documented class model (planning/10 item 3): a
// transfer is a philosophy-3 abstraction built from a PAIR of ordinary
// FinancialPatterns — a withdrawal from the source account and a matching
// deposit into the destination. This record is the canonical definition those
// two patterns are generated from and validated against. It is owned by the book,
// not by any single account, because a transfer spans two.
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

    private Transfer(TransferOptions options)
    {
        Id = options.Id;
        FromAccountId = options.FromAccountId;
        ToAccountId = options.ToAccountId;
        Amount = options.Amount;
        DatePattern = options.DatePattern;
    }

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
}
