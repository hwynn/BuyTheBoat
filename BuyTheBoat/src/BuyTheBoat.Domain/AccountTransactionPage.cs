namespace BuyTheBoat.Domain;

// One account's slice of a TransactionLogPage's date range. The organization
// scheme, per the design's author: the TransactionLogBook is a
// book of ALL of a user's finance information; each TransactionLogPage is a
// chunk of the calendar showing every account inside that range; this class
// is a SINGLE account inside that chunk; and BalanceRecord maps each
// event-date to the BalanceSnapshot holding that day's complete picture.
// Users will eventually have multiple accounts (checking/savings, with
// transfers between them) — this layer exists now, single-account, so that
// feature slots in without reshaping anything.
public sealed record AccountTransactionPage
{
    // "This is the name of the account. It should match the account name
    // used in exported... transaction files." One hardcoded account for now.
    public required string Account { get; init; }

    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    // "An expired transactions page cannot be edited. No information can
    // cascade outside of an expired transaction page." Always false until
    // persisted history exists — a fresh forecast has nothing to expire.
    public bool Expired { get; init; }

    // Safety-cushion configuration (3.8.a1: ideal_safety_cushion cannot be
    // None; 3.9.a1: safety_priority cannot be None). Placeholders this pass:
    // the cushion engine arrives with the deallocation/priority phase; these
    // exist now so snapshots already carry the cushion jar and that phase
    // changes values, not shapes. Original rule to enforce then: no finance
    // pattern may share safety_priority's exact value.
    public decimal IdealSafetyCushion { get; init; }
    public int SafetyPriority { get; init; }

    public required IReadOnlyList<FinancialPattern> FinancePatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarmarkPatterns { get; init; }

    // The dateless seed snapshot the cascade starts from, kept OUTSIDE
    // BalanceRecord per the original model. Its FullAmount is the
    // user-entered balance — the one manually-supplied number in the whole
    // structure; every later value cascades from it (seed the first snapshot
    // manually, cascade the rest).
    public required BalanceSnapshot InitialSnapshot { get; init; }

    // Date -> that day's snapshot. One entry per date with >= 1 event
    // (adjust_snapshots rule); sorted so the cascade and the display both
    // read forward in time.
    public required SortedDictionary<DateOnly, BalanceSnapshot> BalanceRecord { get; init; }

    // The "right now" dynamic values (1.2.3.5.a1 / 1.2.3.6.a1) — per the
    // docs these are manual-trigger calculations, never cascade-computed.
    //
    // ASSUMED-PAIRING(unpaid-expected): current_unpaid_expected is "total
    // amount of past unfufilled expected transactions." With every expected
    // transaction on or before the as-of date assumed already fulfilled and
    // reflected in the entered balance, this is definitionally 0 for now.
    // Real pairing makes it a real calculation.
    public decimal CurrentUnpaidExpected { get; init; }

    // 1.2.3.5.a1's value as of the as-of date: with CurrentUnpaidExpected
    // pinned to 0 (above), this reduces to the initial snapshot's
    // ExpectedFreeAmount (balance minus everything sitting in jars).
    public decimal? CurrentFreeAmount { get; init; }

    /// <summary>[CALC] Bundles everything the next page's initial snapshot would need to carry forward — this page's patterns and its closing balance snapshot. DIVERGENCE(runoff): each forecast currently builds one page spanning the whole window, so nothing calls this yet; it exists for when cross-page continuity is built.</summary>
    public PageRunoff PageRunoffData() => new()
    {
        FinancePatterns = FinancePatterns,
        EarmarkPatterns = EarmarkPatterns,
        ClosingSnapshot = BalanceRecord.Count > 0 ? BalanceRecord.Values.Last() : InitialSnapshot,
    };

    /// <summary>[CALC] Returns this account's balance snapshot as of a date: the latest one on or before it, or the initial seed snapshot when none is that early. Lets the funds methods (and future range checks) ask about a date that has no snapshot of its own, standing in the nearest earlier day; it never reaches forward past the date.</summary>
    /// <param name="date">The date to read the account's state as of.</param>
    public BalanceSnapshot SnapshotAsOf(DateOnly date)
    {
        var snapshot = InitialSnapshot;
        // BalanceRecord is date-sorted; keep walking to the last snapshot at or before the date.
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

    // TODO (actual-transaction history): the
    // affordability methods below, and the suggestion-sizing that will use them, assume no snapshot
    // predates the as-of date (a page runs [asOfDate, horizon], 3.13.1.a1) and that ExpectedFreeAmount is
    // future-only. Once actual-transaction history adds past-dated snapshots, a suggestion sized or placed
    // from these must not drive a past day's ExpectedFreeAmount below zero.
    /// <summary>[CALC] Reports how much money is free to draw on as of a date without touching goal savings — the day's free amount, optionally with the safety cushion added back for a little leeway. Feeds affordability checks for suggestions that don't hinge on a particular bill or goal (e.g. sizing a starting earmark); use AvailableFundsFor instead when a specific expense's priority should widen what counts. Null when that date's free amount hasn't been computed (e.g. a seed snapshot).</summary>
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

        // The cushion is the one jar with no finance id (9.5.a1 guarantees exactly one); its ExpectedAmount
        // was already subtracted out of ExpectedFreeAmount, so adding it back is what "with cushion" means.
        var cushion = snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId is null);
        return free + (cushion?.ExpectedAmount ?? 0m);
    }

    /// <summary>[CALC] Reports how much could be found for an expense of the given priority as of a date — its free funds (per AvailableFunds) plus what sits in jars for non-mandatory expenses, and, when digging, jars for lower-priority expenses too. The bolder counterpart to AvailableFunds, for suggestions that know what they're funding; it only reports a reclaimable total and never moves any money itself. Null when that date's free amount hasn't been computed.</summary>
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

        // Jars carry no priority/mandatory of their own — those live on the finance pattern, so each jar
        // is classified through its own finance id. (This is why the funds methods sit on the page, not on
        // BalanceSnapshot, which can't see the patterns.)
        var patternByFinanceId = FinancePatterns.ToDictionary(pattern => pattern.FinanceId);

        // Two limits deliberately left for the later "how nosey should suggestions be" design pass rather
        // than silently settled here: (1) the plain Priority < forPriority test runs a touch bolder than
        // DeallocationCalculator's own drain order, where a non-mandatory jar outranks a mandatory one
        // whatever their numbers say — reconcile the two when suggestions and deallocation get unified;
        // (2) this never excludes the target's OWN jar (it shares forPriority, so the lower-priority dig
        // already skips it, but a non-mandatory target would count its own jar) — the caller, which knows
        // the target's finance id, is expected to subtract that itself.
        var reclaimable = 0m;
        // Add back every jar we'd reclaim from: non-mandatory always, lower-priority ones only when digging.
        foreach (var jar in snapshot.FundJars)
        {
            if (jar.FinanceId is not int financeId ||
                !patternByFinanceId.TryGetValue(financeId, out var pattern))
            {
                // The cushion (null finance id) is out of this method's tiers, and an unclassifiable jar is
                // left uncounted — the cautious direction, so the total never overstates what's available.
                continue;
            }

            if (!pattern.Mandatory || (digIntoLowerPriority && pattern.Priority < forPriority))
            {
                reclaimable += jar.ExpectedAmount;
            }
        }

        return free + reclaimable;
    }

    /// <summary>[CALC] Reports the LEAST money available on any day across a date range, measured at one chosen frugality tier — the tight spot a long-term plan must fit under. Feeds affordability checks for a proposed multi-occurrence earmark pattern; run it on a page already re-forecast with the proposed change, since it only reads that page's own snapshots. Null when any day in the window has an uncomputed free amount, since the true minimum can't be pinned down then.</summary>
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

        // Free funds only change on snapshot (event) days, so the window's low point sits at either the
        // value entering the window (sampled above) or a snapshot day inside it — no need to walk every date.
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

        // One unknown day and the real low point could be anywhere — report unknown rather than a minimum
        // that silently skipped it.
        return samples.Any(sample => sample is null) ? null : samples.Min();
    }

    /// <summary>[CALC] Returns available funds on a single date at a given frugality tier, routing to AvailableFunds or AvailableFundsFor. The single-date counterpart to MinimumAvailableFunds's range sampling — used to size a one-day set-aside (e.g. a starting earmark) at whatever tier governs that day.</summary>
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

// The bundle page_runoff_data() hands to the next page's initial snapshot.
public sealed record PageRunoff
{
    public required IReadOnlyList<FinancialPattern> FinancePatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarmarkPatterns { get; init; }
    public required BalanceSnapshot ClosingSnapshot { get; init; }
}
