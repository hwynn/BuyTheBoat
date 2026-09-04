namespace MyMoneyForecast.Domain;

// Input to TransactionLogBookFactory.CreateForecast. HorizonEndDate is a
// plain input rather than a constant baked into the engine — the UI defaults
// it to 3 months out but imposes no ceiling; the engine itself stays
// horizon-agnostic.
public sealed record ForecastOptions
{
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }

    // User-created one-off jar adjustments. Not `required`: defaults to
    // none, so existing callers are unaffected.
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];
    public required decimal StartingBalance { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required DateOnly HorizonEndDate { get; init; }

    // How much the user wants held in the safety cushion (the finance_id = null
    // jar, always priority 0 / drained first). Occupies free funds so the
    // reported free figure reflects what's comfortable to spend, not every last
    // dollar. Not `required` — defaults to 0 (cushion inert), so existing
    // callers/tests are unaffected. Firm target: reserved in full even when the
    // balance can't cover it (free then goes negative, like an unaffordable
    // bill).
    public decimal IdealSafetyCushion { get; init; }

    // The finance ids of patterns that are one half of a transfer. Storage
    // knows this (a TransferId column); the domain FinancialPattern
    // deliberately does not carry it, so the caller passes the set in.
    //
    // Used for one thing: a transfer's withdrawal reserves in the account it
    // leaves, because per-account solvency is the point of accounts — but the
    // household view must not count it as set aside, since the household is
    // not down a cent. Empty means "no transfers", which is the correct
    // behaviour for every caller that doesn't have any.
    public IReadOnlySet<int> TransferWithdrawalFinanceIds { get; init; } = new HashSet<int>();

    // The per-account breakdown. When provided, the engine builds one
    // AccountTransactionPage per entry — each its own silo with its own
    // balance, cushion, and patterns — instead of a single "Primary" page.
    // When null/empty, the flat fields above drive one combined account
    // exactly as before, so the single-account API and its tests are
    // unaffected.
    public IReadOnlyList<AccountForecastInput>? Accounts { get; init; }

    /// <summary>[CALC] Returns a copy of these options with the savings plans (EarMarkPatterns) for the given goals left out — the bills/goals themselves, manual earmarks, and everything else stay. Forecasting the result gives the "room if those goals had no plan" picture an affordability check needs: those plans' scheduled contributions no longer reserve free funds, so the free figure is the room available to fund them. An empty set returns the same plans, so a caller omitting nothing is unaffected.</summary>
    /// <param name="financeIds">The goals/bills whose EarMarkPatterns to leave out.</param>
    public ForecastOptions WithoutPlansFor(IReadOnlySet<int> financeIds) => this with
    {
        EarMarkPatterns = EarMarkPatterns.Where(pattern => !financeIds.Contains(pattern.FinanceId)).ToList(),
        Accounts = Accounts?.Select(account => account with
        {
            EarMarkPatterns = account.EarMarkPatterns.Where(pattern => !financeIds.Contains(pattern.FinanceId)).ToList(),
        }).ToList(),
    };

    /// <summary>[CALC] Returns a copy of these options with one manual (one-off) earmark folded in — added, or replaced if one already sits at the same finance id and date — in whichever account holds that finance id's pattern (or the flat single-account set when Accounts isn't used). Forecasting the result previews "what if this one-off were saved," so a live preview can read the real jar off the same day-by-day walk the saved forecast would do, rather than approximating it.</summary>
    /// <param name="earmark">The proposed one-off earmark to fold in.</param>
    public ForecastOptions WithManualEarmark(ManualEarmark earmark)
    {
        bool SameSlot(ManualEarmark existing) => existing.FinanceId == earmark.FinanceId && existing.Date == earmark.Date;

        return this with
        {
            ManualEarmarks = [.. ManualEarmarks.Where(existing => !SameSlot(existing)), earmark],
            Accounts = Accounts?.Select(account =>
                account.FinancialPatterns.Any(pattern => pattern.FinanceId == earmark.FinanceId)
                    ? account with { ManualEarmarks = [.. account.ManualEarmarks.Where(existing => !SameSlot(existing)), earmark] }
                    : account).ToList(),
        };
    }

    /// <summary>[CALC] Returns a copy of these options with one savings plan swapped for a not-yet-saved version — the plan for the proposed plan's finance id whose active span starts on <paramref name="replacedActiveStart"/> is dropped and the proposed one added, in the account holding that goal's pattern (or the flat set when Accounts isn't used); every other plan, this goal's own other segments included, stays. Forecasting the result previews "what if I saved this plan," so a live edit's warning can read real free funds off the same day-by-day walk the saved forecast would do, rather than a no-forecast shortcut. A null <paramref name="replacedActiveStart"/> drops nothing — a brand-new plan replacing no saved segment.</summary>
    /// <param name="proposed">The not-yet-saved plan to fold in.</param>
    /// <param name="replacedActiveStart">The active start of the saved segment being replaced, or null when the proposed plan is brand new.</param>
    public ForecastOptions WithProposedPlan(EarMarkPattern proposed, DateOnly? replacedActiveStart)
    {
        bool IsReplaced(EarMarkPattern existing) =>
            existing.FinanceId == proposed.FinanceId
            && replacedActiveStart is { } start
            && existing.DatePattern.ActiveStart == start;

        return this with
        {
            EarMarkPatterns = EarMarkPatterns.Where(existing => !IsReplaced(existing)).Append(proposed).ToList(),
            Accounts = Accounts?.Select(account =>
            {
                var kept = account.EarMarkPatterns.Where(existing => !IsReplaced(existing));
                var holdsGoal = account.FinancialPatterns.Any(pattern => pattern.FinanceId == proposed.FinanceId);
                return account with { EarMarkPatterns = holdsGoal ? kept.Append(proposed).ToList() : kept.ToList() };
            }).ToList(),
        };
    }
}

// One account's slice of a forecast: its own seed balance and cushion, and the
// patterns/earmarks/manual adjustments filed under it. A pattern belongs to an
// account by being in this list — the pattern itself still carries no account.
public sealed record AccountForecastInput
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }
    public required decimal StartingBalance { get; init; }
    public decimal IdealSafetyCushion { get; init; }
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];
}
