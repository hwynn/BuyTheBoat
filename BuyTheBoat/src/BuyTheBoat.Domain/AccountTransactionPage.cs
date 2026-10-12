namespace BuyTheBoat.Domain;

/// <summary>
/// One account's part of a TransactionLogPage's date range.
/// The TransactionLogBook holds all of a user's finance information, each TransactionLogPage covers a stretch of the calendar for every account, and this is one account within that stretch.
/// BalanceRecord maps each event date to the BalanceSnapshot holding that day's complete picture.
/// </summary>
public sealed record AccountTransactionPage
{
    /// <summary>The account's name; it should match the account name in exported transaction files.</summary>
    public required string Account { get; init; }

    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>
    /// "An expired transactions page cannot be edited. No information can cascade outside of an expired transaction page."
    /// Always false until saved history exists, since a fresh forecast has nothing to expire.
    /// </summary>
    public bool Expired { get; init; }

    /// <summary>How much the user wants held in the safety cushion (3.8.a1: can't be None).</summary>
    public decimal IdealSafetyCushion { get; init; }

    /// <summary>
    /// The cushion's priority (3.9.a1: can't be None), always 0 so it's drained first.
    /// Not yet enforced: no finance pattern may share this exact value.
    /// </summary>
    public int SafetyPriority { get; init; }

    public required IReadOnlyList<FinancialPattern> FinancePatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarmarkPatterns { get; init; }

    /// <summary>
    /// The dateless starting snapshot the cascade begins from, kept outside BalanceRecord as in the original model.
    /// Its FullAmount is the user-entered balance, the one number entered by hand; every later value is worked out from it.
    /// </summary>
    public required BalanceSnapshot InitialSnapshot { get; init; }

    /// <summary>Each date's snapshot, one per date with at least one event (adjust_snapshots), sorted so the cascade and the display both read forward in time.</summary>
    public required SortedDictionary<DateOnly, BalanceSnapshot> BalanceRecord { get; init; }

    /// <summary>
    /// ASSUMED-PAIRING(unpaid-expected): 1.2.3.6.a1's "total amount of past unfulfilled expected transactions", a "right now" value worked out on request, never by the cascade.
    /// It's always 0 for now, since every expected transaction up to the as-of date is assumed already paid and reflected in the entered balance; real pairing will make it a real calculation.
    /// Known gap: if the user missed or underpaid a bill before the as-of date, the real value is above 0, and that debt is silently dropped until actual transactions exist.
    /// </summary>
    public decimal CurrentUnpaidExpected { get; init; }

    /// <summary>1.2.3.5.a1's free amount as of the as-of date; with CurrentUnpaidExpected at 0, it's the initial snapshot's ExpectedFreeAmount (balance minus everything in jars).</summary>
    public decimal? CurrentFreeAmount { get; init; }

    /// <summary>
    /// [CALC] Bundles what the next page's initial snapshot would need to carry forward: this page's patterns and its closing balance snapshot.
    /// DIVERGENCE(runoff): each forecast builds one page spanning the whole window, so nothing calls this yet.
    /// </summary>
    public PageRunoff PageRunoffData() => new()
    {
        FinancePatterns = FinancePatterns,
        EarmarkPatterns = EarmarkPatterns,
        ClosingSnapshot = BalanceRecord.Count > 0 ? BalanceRecord.Values.Last() : InitialSnapshot,
    };

    /// <summary>
    /// [CALC] Returns this account's balance snapshot as of a date: the latest one on or before it, or the initial snapshot when none is that early.
    /// This lets the funds methods ask about a date with no snapshot of its own.
    /// </summary>
    /// <param name="date">The date to read the account's state as of.</param>
    public BalanceSnapshot SnapshotAsOf(DateOnly date)
    {
        var snapshot = InitialSnapshot;
        // Walks the date-sorted BalanceRecord, keeping the last snapshot on or before the date.
        foreach (var (snapshotDate, dated) in BalanceRecord)
        {
            if (snapshotDate > date)
            {
                break;
            }
            snapshot = dated;
        }

        return snapshot;
    }

    /// <summary>
    /// [CALC] Reports how much money is free to draw on as of a date without touching goal savings: the day's free amount, optionally with the safety cushion added back for a little leeway.
    /// For suggestions that don't hinge on a particular bill or goal (e.g. sizing a starting earmark); use AvailableFundsFor when a specific expense's priority should widen what counts.
    /// Null when that date's free amount hasn't been worked out (e.g. a starting snapshot).
    /// </summary>
    /// <remarks>
    /// TODO (actual-transaction history): this and the methods below assume no snapshot is earlier than the as-of date (3.13.1.a1).
    /// Once past-dated snapshots exist, a suggestion sized from these must not drive a past day's ExpectedFreeAmount below zero.
    /// </remarks>
    /// <param name="date">The date to measure available funds as of.</param>
    /// <param name="includeSafetyCushion">Also count the safety-cushion jar's balance as available.</param>
    public decimal? AvailableFunds(DateOnly date, bool includeSafetyCushion = false)
    {
        var snapshot = SnapshotAsOf(date);
        if (snapshot.ExpectedFreeAmount is not decimal free)
        {
            return null;
        }

        if (!includeSafetyCushion)
        {
            return free;
        }

        // Adds back the cushion jar (the one with no finance id, 9.5.a1), which ExpectedFreeAmount already subtracted.
        var cushion = snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId is null);
        return free + (cushion?.ExpectedAmount ?? 0m);
    }

    /// <summary>
    /// [CALC] Reports how much could be found for an expense of the given priority as of a date: free funds plus what's in jars for non-mandatory expenses, and, when digging, jars for lower-priority expenses too.
    /// The bolder counterpart to AvailableFunds, for suggestions that know what they're funding; it only reports a total and never moves money.
    /// Null when that date's free amount hasn't been worked out.
    /// </summary>
    /// <remarks>
    /// Two limits are left for a later design pass on how far suggestions should reach.
    /// The plain Priority &lt; forPriority test is a little bolder than DeallocationCalculator's drain order, where every non-mandatory jar drains before any mandatory one.
    /// It never leaves out the target's own jar, so a non-mandatory target counts its own jar; the caller, which knows the target's finance id, subtracts that itself.
    /// </remarks>
    /// <param name="date">The date to measure available funds as of.</param>
    /// <param name="forPriority">The priority of the expense funds are being found for; only consulted when digIntoLowerPriority is set.</param>
    /// <param name="digIntoLowerPriority">Also count jars for expenses whose priority is strictly below forPriority, not just non-mandatory ones.</param>
    public decimal? AvailableFundsFor(DateOnly date, int forPriority, bool digIntoLowerPriority = false)
    {
        var snapshot = SnapshotAsOf(date);
        if (snapshot.ExpectedFreeAmount is not decimal free)
        {
            return null;
        }

        // Jars have no priority or Mandatory of their own, so each is looked up through its finance pattern; that's why these methods live on the page, not on BalanceSnapshot.
        var patternByFinanceId = FinancePatterns.ToDictionary(pattern => pattern.FinanceId);

        var reclaimable = 0m;
        // Adds each jar that could be reclaimed: non-mandatory ones always, lower-priority ones only when digging.
        foreach (var jar in snapshot.FundJars)
        {
            if (jar.FinanceId is not int financeId ||
                !patternByFinanceId.TryGetValue(financeId, out var pattern))
            {
                // Skips the cushion and any jar with no matching pattern, so the total never overstates what's available.
                continue;
            }

            if (!pattern.Mandatory || (digIntoLowerPriority && pattern.Priority < forPriority))
            {
                reclaimable += jar.ExpectedAmount;
            }
        }

        return free + reclaimable;
    }

    /// <summary>
    /// [CALC] Reports the least money available on any day across a date range at one frugality tier: the tight spot a long-term plan must fit under.
    /// For checking a proposed repeating plan; run it on a page already re-forecast with that plan, since it only reads this page's snapshots.
    /// Null when any day in the window has no worked-out free amount, since the real minimum can't be known then.
    /// </summary>
    /// <param name="start">First day of the window (inclusive).</param>
    /// <param name="end">Last day of the window (inclusive).</param>
    /// <param name="frugality">How much to count as available on each day — see the Frugality tiers.</param>
    /// <param name="forPriority">The target expense's priority; required for the Thrifty and Miserly tiers, ignored by Relaxed and Considerate.</param>
    public decimal? MinimumAvailableFunds(DateOnly start, DateOnly end, Frugality frugality, int? forPriority = null)
    {
        if (end < start)
        {
            throw new ArgumentException("The window's end cannot be before its start.", nameof(end));
        }

        if (frugality is Frugality.Thrifty or Frugality.Miserly && forPriority is null)
        {
            throw new ArgumentException(
                "A target priority is required for the Thrifty and Miserly tiers.", nameof(forPriority));
        }

        var samples = new List<decimal?> { AvailableAt(start, frugality, forPriority) };

        // Samples each snapshot day inside the window; free funds only change on those days, so the low point is one of them or the value entering the window.
        foreach (var (date, _) in BalanceRecord)
        {
            if (date <= start)
            {
                continue;
            }
            if (date > end)
            {
                break;
            }
            samples.Add(AvailableAt(date, frugality, forPriority));
        }

        // Any unknown day makes the minimum unknown.
        return samples.Any(sample => sample is null) ? null : samples.Min();
    }

    /// <summary>
    /// [CALC] Returns available funds on a single date at a given frugality tier, using AvailableFunds or AvailableFundsFor.
    /// Used to size a one-day set-aside, such as a starting earmark.
    /// </summary>
    /// <param name="date">The date to measure as of.</param>
    /// <param name="frugality">Which tier to measure at.</param>
    /// <param name="forPriority">The target priority, for the tiers that consult one.</param>
    public decimal? AvailableAt(DateOnly date, Frugality frugality, int? forPriority) => frugality switch
    {
        Frugality.Relaxed => AvailableFunds(date, includeSafetyCushion: false),
        Frugality.Considerate => AvailableFunds(date, includeSafetyCushion: true),
        Frugality.Thrifty => AvailableFundsFor(date, forPriority!.Value, digIntoLowerPriority: false),
        Frugality.Miserly => AvailableFundsFor(date, forPriority!.Value, digIntoLowerPriority: true),
        _ => throw new ArgumentOutOfRangeException(nameof(frugality)),
    };
}

/// <summary>What PageRunoffData hands to the next page's initial snapshot.</summary>
public sealed record PageRunoff
{
    public required IReadOnlyList<FinancialPattern> FinancePatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarmarkPatterns { get; init; }
    public required BalanceSnapshot ClosingSnapshot { get; init; }
}
