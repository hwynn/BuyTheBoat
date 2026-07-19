namespace MyMoneyForecast.Domain;

// One forecast run's complete output: the faithful onion structure
// (TransactionLogBook -> ... -> BalanceSnapshot) plus the derived summaries
// the UI and export need without re-walking the book. Replaces the old
// Forecast record.
public sealed record ForecastResult
{
    public required DateOnly AsOfDate { get; init; }
    public required DateOnly HorizonEndDate { get; init; }
    public required TransactionLogBook Book { get; init; }
    public required IReadOnlyList<GoalShortfall> GoalShortfalls { get; init; }

    // Display label for every finance id that can appear on a jar, expected
    // transaction, or earmark event. The safety cushion (null id) is not in
    // here — it has no FinancialPattern; display layers label it directly.
    public required IReadOnlyDictionary<int, string> JarLabels { get; init; }

    public required bool HasNegativeFreeBalance { get; init; }
    public required DateOnly? FirstNegativeFreeBalanceDate { get; init; }

    // Days where a manual withdrawal exceeded what its jar held and got
    // floored (delivered only what was available). Mechanically safe — the
    // floor can't create money — but the user's stated intent didn't fully
    // happen, so the UI flags these in place (planning/09, validation policy).
    public IReadOnlyList<(DateOnly Date, int FinanceId)> FlooredManualEarmarks { get; init; } = [];

    public AccountTransactionPage PrimaryAccountPage =>
        Book.LogPages[0].AccountPages[TransactionLogBookFactory.PrimaryAccountName];

    // The page rendered as rows: the initial snapshot as the as-of row,
    // followed by every dated snapshot. If events land ON the as-of date, a
    // dated snapshot exists there and IS the as-of row (its values equal the
    // initial's by construction; it additionally carries that day's events).
    public IReadOnlyList<TimelineEntry> GetTimeline()
    {
        var page = PrimaryAccountPage;
        var rows = new List<TimelineEntry>(page.BalanceRecord.Count + 1);

        if (!page.BalanceRecord.ContainsKey(AsOfDate))
        {
            rows.Add(new TimelineEntry { Date = AsOfDate, Snapshot = page.InitialSnapshot });
        }

        foreach (var (date, snapshot) in page.BalanceRecord)
        {
            rows.Add(new TimelineEntry { Date = date, Snapshot = snapshot });
        }

        return rows;
    }
}

// One display row: a snapshot with the date it renders under (needed because
// the initial snapshot is dateless but renders as the as-of row).
public sealed record TimelineEntry
{
    public required DateOnly Date { get; init; }
    public required BalanceSnapshot Snapshot { get; init; }
}
