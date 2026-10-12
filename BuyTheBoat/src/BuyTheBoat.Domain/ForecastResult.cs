namespace BuyTheBoat.Domain;

/// <summary>
/// One forecast run's complete output: the TransactionLogBook down to each BalanceSnapshot, plus the summaries the UI and export need without walking the book again.
/// </summary>
public sealed record ForecastResult
{
    public required DateOnly AsOfDate { get; init; }
    public required DateOnly HorizonEndDate { get; init; }
    public required TransactionLogBook Book { get; init; }
    public required IReadOnlyList<GoalShortfall> GoalShortfalls { get; init; }

    /// <summary>One entry per GoalShortfalls row, in the same order, with each plan's full health picture.</summary>
    public required IReadOnlyList<PlanHealthState> PlanHealthStates { get; init; }

    /// <summary>The display label for every finance id that can appear on a jar, expected transaction, or earmark event; the safety cushion isn't included, since display code labels it directly.</summary>
    public required IReadOnlyDictionary<int, string> JarLabels { get; init; }

    public required bool HasNegativeFreeBalance { get; init; }
    public required DateOnly? FirstNegativeFreeBalanceDate { get; init; }

    /// <summary>Days where a manual withdrawal asked for more than its jar held and only took what was there, so the UI can flag that the user's intent didn't fully happen.</summary>
    public IReadOnlyList<(DateOnly Date, int FinanceId)> FlooredManualEarmarks { get; init; } = [];

    /// <summary>One entry per account in the forecast, in input order (just "Primary" for a single account); the forecast views show per-account detail from these.</summary>
    public required IReadOnlyList<AccountForecast> Accounts { get; init; }

    /// <summary>The household roll-up across all accounts: each day's summed free and set-aside amounts and which accounts are short.</summary>
    public required HouseholdSummary Household { get; init; }

    /// <summary>
    /// The "Primary" account's page, or the first account's when there's no "Primary", so the single-account spreadsheet export always finds a page.
    /// The exporter has no multi-account form yet.
    /// </summary>
    public AccountTransactionPage PrimaryAccountPage =>
        Book.LogPages[0].AccountPages.TryGetValue(TransactionLogBookFactory.PrimaryAccountName, out var primary)
            ? primary
            : Book.LogPages[0].AccountPages.Values.First();

    /// <summary>[CALC] Returns the primary account's page rendered as timeline rows.</summary>
    public IReadOnlyList<TimelineEntry> GetTimeline() => BuildTimeline(PrimaryAccountPage);

    /// <summary>
    /// [CALC] Returns timeline rows for the account whose page holds this finance id's savings plan, rather than the primary account.
    /// Empty when the id isn't on any account's page.
    /// </summary>
    /// <param name="financeId">Which goal's own account page to read the timeline from.</param>
    public IReadOnlyList<TimelineEntry> GetTimeline(int financeId)
    {
        var account = Accounts.FirstOrDefault(candidate =>
            candidate.Page.EarmarkPatterns.Any(pattern => pattern.FinanceId == financeId));
        return account is null ? [] : BuildTimeline(account.Page);
    }

    /// <summary>
    /// [CALC] Turns a page into timeline rows: the initial snapshot as the as-of row, then every dated snapshot.
    /// If an event landed on the as-of date, that day's snapshot is the as-of row instead (same values, plus that day's events), so there's never a duplicate.
    /// </summary>
    /// <param name="page">The account page to render.</param>
    private IReadOnlyList<TimelineEntry> BuildTimeline(AccountTransactionPage page)
    {
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

/// <summary>One display row: a snapshot and the date it shows under, since the initial snapshot has no date of its own but shows as the as-of row.</summary>
public sealed record TimelineEntry
{
    public required DateOnly Date { get; init; }
    public required BalanceSnapshot Snapshot { get; init; }
}

/// <summary>One account's forecast: which account it is, plus its page in the book with its balance record, jars, and events.</summary>
public sealed record AccountForecast
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }
    public required AccountTransactionPage Page { get; init; }
    public required DateOnly? FirstNegativeFreeBalanceDate { get; init; }
}

/// <summary>
/// One day of the household roll-up: Free and SetAside are summed across every account, each as of that day (its latest snapshot on or before it).
/// ShortAccounts names the accounts whose own free funds went negative, which a positive household Free can hide.
/// </summary>
public sealed record HouseholdDay
{
    public required DateOnly Date { get; init; }
    public required decimal Free { get; init; }
    public required decimal SetAside { get; init; }
    public required IReadOnlyList<string> ShortAccounts { get; init; }

    /// <summary>
    /// Accounts whose safety cushion is below its target on this day: not over-committed, but the cushion took a hit.
    /// Shown more quietly than being short.
    /// </summary>
    public IReadOnlyList<string> CushionDippedAccounts { get; init; } = [];

    public bool AnyAccountShort => ShortAccounts.Count > 0;

    /// <summary>Is a cushion dipped with no account short? A day that's both reads as short.</summary>
    public bool AnyCushionDipped => ShortAccounts.Count == 0 && CushionDippedAccounts.Count > 0;
}

public sealed record HouseholdSummary
{
    /// <summary>Free to spend across all accounts as of the forecast date.</summary>
    public required decimal AsOfFree { get; init; }

    /// <summary>Every date any account has an event, with that day's household roll-up.</summary>
    public required IReadOnlyList<HouseholdDay> Days { get; init; }
}
