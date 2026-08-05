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

    // One entry per GoalShortfalls row, same order, carrying its full
    // diagnostic picture (planning/21, four plan-health states) rather than
    // just the due-date shortfall/overfund pair.
    public required IReadOnlyList<PlanHealthState> PlanHealthStates { get; init; }

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

    // One entry per account this forecast covers, in input order. For the
    // single-account path this is just the "Primary" account. The forecast
    // views (planning/10 item 5) render per-account detail from these.
    public required IReadOnlyList<AccountForecast> Accounts { get; init; }

    // The household roll-up across all accounts: per day, the summed free +
    // set-aside and which accounts are short (item 4-C). The overview reads its
    // household numbers and its "any account short" flag from here.
    public required HouseholdSummary Household { get; init; }

    // Falls back to the first account when there is no "Primary" (a multi-account
    // run), so the single-account spreadsheet export still finds a page rather
    // than throwing. The exporter's own multi-account form is deferred (doc 11).
    public AccountTransactionPage PrimaryAccountPage =>
        Book.LogPages[0].AccountPages.TryGetValue(TransactionLogBookFactory.PrimaryAccountName, out var primary)
            ? primary
            : Book.LogPages[0].AccountPages.Values.First();

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

// One account's forecast: its identity plus its own page in the book. The page
// carries the full per-account cascade (its balance record, jars, events).
public sealed record AccountForecast
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }
    public required AccountTransactionPage Page { get; init; }
    public required DateOnly? FirstNegativeFreeBalanceDate { get; init; }
}

// The household roll-up. Free/SetAside on a given day are the sums across every
// account of that account's value as of that day (its latest snapshot on or
// before it). ShortAccounts names the accounts whose own free went negative —
// the "enough in the right account" signal that a positive household Free can
// still hide.
public sealed record HouseholdDay
{
    public required DateOnly Date { get; init; }
    public required decimal Free { get; init; }
    public required decimal SetAside { get; init; }
    public required IReadOnlyList<string> ShortAccounts { get; init; }

    // Accounts whose safety cushion is not whole on this day — it sits below
    // the target they set. This is the middle warning state (planning/14 item
    // C): not over-committed, but the buffer took the hit. Surfaced more
    // quietly than being short, and empty for anyone whose cushion is 0, since
    // there is then nothing to dip into.
    public IReadOnlyList<string> CushionDippedAccounts { get; init; } = [];

    public bool AnyAccountShort => ShortAccounts.Count > 0;

    // "Short" outranks it: a day that is both over-committed and cushion-dipped
    // reads as short, so this only reports the lesser state on its own.
    public bool AnyCushionDipped => ShortAccounts.Count == 0 && CushionDippedAccounts.Count > 0;
}

public sealed record HouseholdSummary
{
    // Free to spend across all accounts as of the forecast date.
    public required decimal AsOfFree { get; init; }

    // Every date any account has an event, with the household roll-up there.
    public required IReadOnlyList<HouseholdDay> Days { get; init; }
}
