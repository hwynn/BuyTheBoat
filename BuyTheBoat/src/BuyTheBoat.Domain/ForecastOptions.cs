namespace BuyTheBoat.Domain;

/// <summary>
/// The inputs to one forecast run (TransactionLogBookFactory.CreateForecast).
/// HorizonEndDate is chosen by the caller: the UI defaults it to 3 months out, and the forecast itself sets no limit.
/// </summary>
public sealed record ForecastOptions
{
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }

    /// <summary>User-created one-off jar adjustments; none if not set.</summary>
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];

    /// <summary>The account's real balance on the as-of date, as the user entered it, including everything that happened that day.</summary>
    public required decimal StartingBalance { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required DateOnly HorizonEndDate { get; init; }

    /// <summary>
    /// How much the user wants held in the safety cushion, the jar with no finance id that's always drained first.
    /// It comes out of free funds, so the free figure shows what's comfortable to spend rather than every last dollar.
    /// It's held back in full even when the balance can't cover it, so free funds go negative, as with a bill the user can't afford.
    /// 0 if not set, which means no cushion.
    /// </summary>
    public decimal IdealSafetyCushion { get; init; }

    /// <summary>
    /// The per-account breakdown: one AccountTransactionPage is built per entry, each with its own balance, cushion, and patterns.
    /// When null or empty, the flat fields above make up a single "Primary" account.
    /// </summary>
    public IReadOnlyList<AccountForecastInput>? Accounts { get; init; }

    /// <summary>
    /// [CALC] Returns a copy of these options without the savings plans (EarMarkPatterns) for the given goals; the bills and goals themselves, manual earmarks, and everything else stay.
    /// Forecasting the result shows how much room there'd be if those goals had no plan, which is what an affordability check needs.
    /// An empty set leaves the plans as they are.
    /// </summary>
    /// <param name="financeIds">The goals/bills whose EarMarkPatterns to leave out.</param>
    public ForecastOptions WithoutPlansFor(IReadOnlySet<int> financeIds) => this with
    {
        EarMarkPatterns = EarMarkPatterns.Where(pattern => !financeIds.Contains(pattern.FinanceId)).ToList(),
        Accounts = Accounts?.Select(account => account with
        {
            EarMarkPatterns = account.EarMarkPatterns.Where(pattern => !financeIds.Contains(pattern.FinanceId)).ToList(),
        }).ToList(),
    };

    /// <summary>
    /// [CALC] Returns a copy of these options with one manual (one-off) earmark added, replacing any already at the same finance id and date.
    /// It goes into whichever account holds that finance id's pattern, or the flat set when Accounts isn't used.
    /// Forecasting the result previews "what if this one-off were saved", reading the real jar from the same day-by-day walk as the saved forecast.
    /// </summary>
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

    /// <summary>
    /// [CALC] Returns a copy of these options with one savings plan swapped for an unsaved version, in the account holding that goal's pattern (or the flat set when Accounts isn't used).
    /// The plan for the same finance id whose active span starts on <paramref name="replacedActiveStart"/> is dropped and the proposed one added; every other plan stays, including this goal's other segments.
    /// A null <paramref name="replacedActiveStart"/> drops nothing, for a brand-new plan.
    /// Forecasting the result previews "what if I saved this plan", so a live edit's warning can read real free funds from the same day-by-day walk as the saved forecast.
    /// </summary>
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

/// <summary>
/// One account's part of a forecast: its own starting balance and cushion, and the patterns, plans, and manual earmarks filed under it.
/// A pattern belongs to an account by being in these lists; the pattern itself doesn't record its account.
/// </summary>
public sealed record AccountForecastInput
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }

    /// <summary>The account's real balance on the as-of date, as the user entered it, including everything that happened that day.</summary>
    public required decimal StartingBalance { get; init; }
    public decimal IdealSafetyCushion { get; init; }
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];
}
