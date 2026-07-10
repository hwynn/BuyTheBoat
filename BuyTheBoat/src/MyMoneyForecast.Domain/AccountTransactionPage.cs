namespace MyMoneyForecast.Domain;

// One account's slice of a TransactionLogPage's date range. The organization
// scheme, per the design's author (2026-07-10): the TransactionLogBook is a
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
    // structure; every later value cascades from it (design decision
    // 2026-07-10: seed the first snapshot manually, cascade the rest).
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

    // page_runoff_data(): "All the information needed for the next
    // account_transaction_page — finance_patterns, earmark_patterns, balance
    // snapshot, fund jars."
    // DIVERGENCE(runoff): each forecast currently builds ONE page spanning
    // the whole requested window, so nothing consumes this yet. It exists to
    // mark where cross-page continuity plugs in when fixed page lengths and
    // persisted history arrive.
    public PageRunoff PageRunoffData() => new()
    {
        FinancePatterns = FinancePatterns,
        EarmarkPatterns = EarmarkPatterns,
        ClosingSnapshot = BalanceRecord.Count > 0 ? BalanceRecord.Values.Last() : InitialSnapshot,
    };
}

// The bundle page_runoff_data() hands to the next page's initial snapshot.
public sealed record PageRunoff
{
    public required IReadOnlyList<FinancialPattern> FinancePatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarmarkPatterns { get; init; }
    public required BalanceSnapshot ClosingSnapshot { get; init; }
}
