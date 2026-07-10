namespace MyMoneyForecast.Domain;

// Builds one forecast's TransactionLogBook from patterns + a seed balance —
// the original model's organization (Book -> Page -> AccountPage ->
// BalanceSnapshot -> FundJar/EarMarkEvent/ExpectedTransaction) computed as a
// stateless, in-memory pass, replacing the old flat ForecastCalculator.
//
// The internal steps carry the documented cascade's names:
// - AdjustSnapshots role: materialize events and decide which dates get a
//   BalanceSnapshot (exactly the dates with >= 1 event — 3.13.a3).
// - CascadePageBalanceRecord role: walk those dates in order, deriving every
//   snapshot's values from the previous one (never from anything later — the
//   documented cascade's hard rule), seeded by the dateless initial snapshot
//   whose FullAmount is the user-entered balance.
//
// The whole run lives in the docs' "expected_*" value family: with no
// ActualTransactions, FullAmount/CurrentAmount stay null for every future
// date exactly as documented, and all forecasting flows through
// ExpectedAmount — this is faithful, not a divergence. The divergences that
// DO exist are tagged ASSUMED-PAIRING / DIVERGENCE inline and cataloged in
// redesign/MyMoneyForecast/planning/05-original-structure-restructure.md.
public static class TransactionLogBookFactory
{
    public const string PrimaryAccountName = "Primary";

    public static ForecastResult CreateForecast(ForecastOptions options)
    {
        var patternsById = options.FinancialPatterns.ToDictionary(pattern => pattern.FinanceId);
        var earmarkedIds = options.EarMarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

        // ~1.2.3.5.a1's "money_in_fund_jars_ready_for_unpaid_bills" carve-out:
        // a mandatory bill implicitly earmarks toward itself even without a
        // user-created EarMarkPattern — but only if one doesn't already
        // exist, so a bill the user chose to earmark explicitly isn't
        // double-counted.
        var autoBills = GetAutomaticallyEarmarkedBills(options.FinancialPatterns, options.EarMarkPatterns);

        // Occurrences over each bill's own full lifetime (not clipped to the
        // window): accrual toward a due date past the horizon still needs to
        // know that due date. Binary-searched per day — a linear rescan is
        // O(dates x occurrences), noticeable once a forecast spans years.
        var billOccurrences = autoBills.ToDictionary(
            bill => bill.FinanceId,
            bill => bill.DatePattern.GetOccurrences(bill.DatePattern.Start, bill.DatePattern.Until).ToList());

        // Every income date across each pattern's full lifetime — feeds
        // BillAccrualAt's "is anything still arriving before this bill is
        // due" check (see that method).
        var incomeOccurrences = options.FinancialPatterns
            .Where(pattern => pattern.Amount > 0)
            .SelectMany(pattern => pattern.DatePattern.GetOccurrences(pattern.DatePattern.Start, pattern.DatePattern.Until))
            .Distinct()
            .OrderBy(occurrence => occurrence)
            .ToList();

        // === AdjustSnapshots role: materialize the window's events ===

        // ExpectedTransactions: one per FinancialPattern occurrence in the
        // window. These, not raw occurrence counts, now drive the balance
        // math — restoring the documented model's event layer.
        var expectedByDate = new Dictionary<DateOnly, List<ExpectedTransaction>>();
        foreach (var pattern in options.FinancialPatterns)
        {
            foreach (var date in pattern.DatePattern.GetOccurrences(options.AsOfDate, options.HorizonEndDate))
            {
                GetOrAdd(expectedByDate, date).Add(new ExpectedTransaction
                {
                    FinanceId = pattern.FinanceId,
                    ExpectedDate = date,
                    ExpectedAmount = pattern.Amount,
                });
            }
        }

        var earmarkEventsByDate = new Dictionary<DateOnly, List<EarMarkEvent>>();

        // Repeated earmark events from each EarMarkPattern's schedule. The
        // stored pattern amount is negative (its sign convention is "effect
        // on free balance"); an event's ExpectedAmount is positive-into-jar,
        // so the sign flips here.
        foreach (var earmark in options.EarMarkPatterns)
        {
            foreach (var date in earmark.DatePattern.GetOccurrences(options.AsOfDate, options.HorizonEndDate))
            {
                GetOrAdd(earmarkEventsByDate, date).Add(new EarMarkEvent
                {
                    FinanceId = earmark.FinanceId,
                    EarmarkDate = date,
                    RepeatedEarmark = true,
                    ExpectedAmount = -earmark.Amount,
                    ExplicitAmount = null,
                });
            }
        }

        // ASSUMED-PAIRING(3.13c.a10): on a goal's own occurrence the money
        // leaves the account via its ExpectedTransaction, so the jar must
        // release the same amount or it would be double-counted. In the
        // documented model this implicit withdrawal is created when a PAIRED
        // ACTUAL transaction lands ("if this is not a deallocation day, and
        // an expected transaction with a fund jar has a paired actual
        // transaction on this day, create an implicit earmark for that fund
        // jar with the actual transaction's amount") — with pairing assumed,
        // the expected occurrence itself triggers it.
        foreach (var earmark in options.EarMarkPatterns)
        {
            var goal = patternsById[earmark.FinanceId];
            foreach (var date in goal.DatePattern.GetOccurrences(options.AsOfDate, options.HorizonEndDate))
            {
                GetOrAdd(earmarkEventsByDate, date).Add(new EarMarkEvent
                {
                    FinanceId = earmark.FinanceId,
                    EarmarkDate = date,
                    RepeatedEarmark = false,
                    ExpectedAmount = -Math.Abs(goal.Amount),
                    ExplicitAmount = 0m,
                });
            }
        }

        // Snapshot dates: exactly the dates with at least one event
        // (adjust_snapshots / 3.13.a3). The as-of day itself is represented
        // by the dateless initial snapshot unless events land on it.
        var snapshotDates = new SortedSet<DateOnly>();
        snapshotDates.UnionWith(expectedByDate.Keys);
        snapshotDates.UnionWith(earmarkEventsByDate.Keys);

        // === Initial snapshot: the manually-seeded starting point ===

        // Goal jars start from their pre-as-of state: StartingAllocation
        // plus contributions already scheduled before/on the as-of date,
        // minus goal occurrences that already released money
        // (~3.13.5.3.a1, seeded from the pattern's own Start so a goal
        // already partway through its schedule shows a non-zero jar today).
        var jarValues = new Dictionary<int, decimal>();
        var milestones = new Dictionary<int, decimal>();
        foreach (var earmark in options.EarMarkPatterns)
        {
            var goal = patternsById[earmark.FinanceId];
            var contributed = earmark.StartingAllocation
                - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, options.AsOfDate).Count;
            var withdrawn = Math.Abs(goal.Amount) * goal.DatePattern.GetOccurrences(goal.DatePattern.Start, options.AsOfDate).Count;
            jarValues[earmark.FinanceId] = Math.Max(0m, contributed - withdrawn);

            // 3.13.5.4.a1: milestone counts scheduled contributions only —
            // StartingAllocation is money already saved, not target.
            milestones[earmark.FinanceId] =
                -earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, options.AsOfDate).Count;
        }

        // Auto-reserved bills seed at their as-of accrual target.
        var autoBillTargets = new Dictionary<int, decimal>();
        foreach (var bill in autoBills)
        {
            var target = BillAccrualAt(bill, billOccurrences[bill.FinanceId], incomeOccurrences, options.AsOfDate);
            jarValues[bill.FinanceId] = target;
            autoBillTargets[bill.FinanceId] = target;
        }

        var initialSnapshot = new BalanceSnapshot
        {
            SnapshotDate = null,
            // The one manually-entered number: the user's real balance as of
            // the as-of date, assumed to already reflect everything that
            // happened up to and including that day.
            FullAmount = options.StartingBalance,
            ExpectedAmount = options.StartingBalance,
            ExpectedFreeAmount = options.StartingBalance - jarValues.Values.Sum(),
            FundJars = BuildJars(jarValues, milestones, currentIsKnown: true),
            ActualTransactions = [],
            ExpectedTransactions = [],
            EarMarkEvents = [],
        };

        // === CascadePageBalanceRecord role: the forward value pass ===

        var balanceRecord = new SortedDictionary<DateOnly, BalanceSnapshot>();
        var previousExpected = initialSnapshot.ExpectedAmount!.Value;
        DateOnly? firstNegativeDate = initialSnapshot.ExpectedFreeAmount < 0m ? options.AsOfDate : null;

        foreach (var date in snapshotDates)
        {
            var expectedTransactions = expectedByDate.GetValueOrDefault(date) ?? [];
            var earMarkEvents = earmarkEventsByDate.GetValueOrDefault(date) ?? [];

            // Auto-bill reservation events: implicit isolated earmarks
            // stepping each auto-reserved bill's jar to its accrual target
            // for this date (DIVERGENCE(positive-implicit) — see
            // EarMarkEvent). The accrual curve only ever mattered on
            // snapshot dates, so stepping between them loses nothing.
            foreach (var bill in autoBills)
            {
                var target = BillAccrualAt(bill, billOccurrences[bill.FinanceId], incomeOccurrences, date);
                var delta = target - autoBillTargets[bill.FinanceId];
                autoBillTargets[bill.FinanceId] = target;
                if (delta != 0m)
                {
                    earMarkEvents.Add(new EarMarkEvent
                    {
                        FinanceId = bill.FinanceId,
                        EarmarkDate = date,
                        RepeatedEarmark = false,
                        ExpectedAmount = delta,
                        ExplicitAmount = 0m,
                    });
                }
            }

            // ASSUMED-PAIRING(as-of-day-settled): the entered balance is
            // assumed to already include anything happening ON the as-of
            // date, so a snapshot dated exactly there attaches its events
            // for display but contributes no deltas — its values equal the
            // initial snapshot's. (Same convention the flat engine used.)
            var isSeedDay = date == options.AsOfDate;

            // 10.3: previous snapshot's amount + today's expected
            // transactions.
            var expected = previousExpected
                + (isSeedDay ? 0m : expectedTransactions.Where(t => !t.Cancelled).Sum(t => t.ExpectedAmount));

            // 3.13.5.3.a1 per jar: previous day's amount + today's earmark
            // events, floored at 0 (a jar can be emptied, never negative).
            if (!isSeedDay)
            {
                foreach (var earMarkEvent in earMarkEvents)
                {
                    if (earMarkEvent.FinanceId is not { } financeId)
                    {
                        continue; // cushion events don't exist yet
                    }

                    jarValues[financeId] = Math.Max(0m, jarValues[financeId] + earMarkEvent.ExpectedAmount);

                    // 3.13.5.4.a1: milestone accumulates repeated
                    // (pattern-scheduled) contributions only.
                    if (earMarkEvent.RepeatedEarmark)
                    {
                        milestones[financeId] += earMarkEvent.ExpectedAmount;
                    }
                }
            }

            // 3.13.4.a1: free = expected minus everything sitting in jars.
            var expectedFree = expected - jarValues.Values.Sum();

            balanceRecord[date] = new BalanceSnapshot
            {
                SnapshotDate = date,
                FullAmount = null, // 10.2: unknowable until the day occurs
                ExpectedAmount = expected,
                ExpectedFreeAmount = expectedFree,
                FundJars = BuildJars(jarValues, milestones, currentIsKnown: false),
                ActualTransactions = [],
                ExpectedTransactions = expectedTransactions,
                EarMarkEvents = earMarkEvents,
            };

            previousExpected = expected;
            firstNegativeDate ??= expectedFree < 0m ? date : null;
        }

        // === Assemble the onion ===

        var accountPage = new AccountTransactionPage
        {
            Account = PrimaryAccountName,
            StartDate = options.AsOfDate,
            EndDate = options.HorizonEndDate,
            Expired = false,
            IdealSafetyCushion = 0m, // placeholder until the cushion phase
            SafetyPriority = 0,      // placeholder until the cushion phase
            FinancePatterns = options.FinancialPatterns,
            EarmarkPatterns = options.EarMarkPatterns,
            InitialSnapshot = initialSnapshot,
            BalanceRecord = balanceRecord,
            // ASSUMED-PAIRING(unpaid-expected): see AccountTransactionPage.
            CurrentUnpaidExpected = 0m,
            CurrentFreeAmount = initialSnapshot.ExpectedFreeAmount,
        };

        var book = new TransactionLogBook
        {
            PageLength = null, // DIVERGENCE(page-length): one window-sized page
            LogPages =
            [
                new TransactionLogPage
                {
                    StartDate = options.AsOfDate,
                    EndDate = options.HorizonEndDate,
                    AccountPages = new Dictionary<string, AccountTransactionPage>
                    {
                        [PrimaryAccountName] = accountPage,
                    },
                },
            ],
        };

        return new ForecastResult
        {
            AsOfDate = options.AsOfDate,
            HorizonEndDate = options.HorizonEndDate,
            Book = book,
            GoalShortfalls = CalculateGoalShortfalls(options.EarMarkPatterns, patternsById),
            JarLabels = patternsById.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Description ?? pair.Value.Source),
            HasNegativeFreeBalance = firstNegativeDate is not null,
            FirstNegativeFreeBalanceDate = firstNegativeDate,
        };
    }

    // Exposed so UI layers can identify which bills get the automatic
    // reservation treatment (e.g. the "(Automatic)" rows on the Allocations
    // tab) without duplicating — and risking drift from — the exact rule the
    // engine is driven by.
    public static IReadOnlyList<FinancialPattern> GetAutomaticallyEarmarkedBills(
        IReadOnlyList<FinancialPattern> financialPatterns,
        IReadOnlyList<EarMarkPattern> earMarkPatterns)
    {
        var earmarkedFinanceIds = earMarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();
        return financialPatterns
            .Where(pattern => pattern.Mandatory && !earmarkedFinanceIds.Contains(pattern.FinanceId))
            .ToList();
    }

    private static List<T> GetOrAdd<T>(Dictionary<DateOnly, List<T>> map, DateOnly date)
    {
        if (!map.TryGetValue(date, out var list))
        {
            list = [];
            map[date] = list;
        }

        return list;
    }

    // Jar order is stable (insertion order of jarValues: goals, then auto
    // bills), with the safety cushion appended last. Exactly one null-id jar
    // per snapshot (9.5.a1); its real math is the cushion phase — 0 for now.
    private static List<FundJar> BuildJars(
        Dictionary<int, decimal> jarValues,
        Dictionary<int, decimal> milestones,
        bool currentIsKnown)
    {
        var jars = new List<FundJar>(jarValues.Count + 1);
        foreach (var (financeId, value) in jarValues)
        {
            jars.Add(new FundJar
            {
                FinanceId = financeId,
                // ASSUMED-PAIRING(as-of-day): known (equal to expected) only
                // on the seed snapshot; null for future dates per the docs.
                CurrentAmount = currentIsKnown ? value : null,
                ExpectedAmount = value,
                // null milestone = no savings plan drives this jar (an
                // auto-reserved bill) — nothing to be "behind" on.
                MilestoneAmount = milestones.TryGetValue(financeId, out var milestone) ? milestone : null,
            });
        }

        jars.Add(new FundJar
        {
            FinanceId = null,
            CurrentAmount = currentIsKnown ? 0m : null,
            ExpectedAmount = 0m,
            MilestoneAmount = null, // "the fund jar for safety cushion will not have a milestone amount"
        });

        return jars;
    }

    // Linear interpolation between consecutive bill occurrences — 0 exactly
    // on (and right after) a due date, rising toward the full bill amount as
    // the *next* due date approaches. Anchoring to the most recent
    // occurrence *at or before* `date` matters: the bill's own due-date
    // ExpectedTransaction already reduces the balance that day, so the jar
    // must already read 0 that day too, or the bill's amount would be
    // subtracted twice.
    //
    // The ramp only makes sense if more income is still coming before the
    // bill is due — it's a pacing curve, implicitly assuming a future
    // paycheck completes the reservation by the due date. Once the last
    // income event before the due date has passed (or there never was one),
    // pacing has nothing left to pace against: the full remaining amount is
    // reserved immediately. This only affects the automatic bill mechanism —
    // a user's own EarMarkPattern is a deliberate, hand-chosen schedule the
    // engine leaves alone even if it runs out of runway (that shows up as a
    // GoalShortfall instead).
    private static decimal BillAccrualAt(FinancialPattern bill, List<DateOnly> occurrences, List<DateOnly> incomeOccurrences, DateOnly date)
    {
        var searchResult = occurrences.BinarySearch(date);
        var paidThroughIndex = searchResult >= 0 ? searchResult : ~searchResult - 1;

        if (paidThroughIndex < 0 || paidThroughIndex + 1 >= occurrences.Count)
        {
            // Before the bill's very first occurrence, or after its last —
            // either way there's no current cycle to accrue within.
            return 0m;
        }

        var paidThrough = occurrences[paidThroughIndex];
        var next = occurrences[paidThroughIndex + 1];
        var amount = Math.Abs(bill.Amount);

        if (!HasOccurrenceInRange(incomeOccurrences, date.AddDays(1), next))
        {
            return amount;
        }

        var cycleDays = next.DayNumber - paidThrough.DayNumber;
        var elapsedDays = date.DayNumber - paidThrough.DayNumber;

        var fraction = (decimal)elapsedDays / cycleDays;
        return Math.Round(amount * fraction, 2);
    }

    // `sortedDates` is ascending and pre-deduplicated (built once in
    // CreateForecast) — binary search for the same O(log n)-per-day reason
    // BillAccrualAt's own occurrence lookup is.
    private static bool HasOccurrenceInRange(List<DateOnly> sortedDates, DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            return false;
        }

        var searchResult = sortedDates.BinarySearch(start);
        var firstAtOrAfterStart = searchResult >= 0 ? searchResult : ~searchResult;
        return firstAtOrAfterStart < sortedDates.Count && sortedDates[firstAtOrAfterStart] <= end;
    }

    // ~3.13.5.4.a1: milestone vs. saved — but "saved" and "milestone" are
    // structurally identical without real transactions, so the only way a
    // goal can look short is if the savings schedule itself doesn't reach
    // the goal amount by its own due date. Evaluated independently of any
    // display horizon, and always includes every goal that has a savings
    // plan (ShortfallAmount is 0 when fully on track) so a UI can render one
    // row per goal without a separate lookup.
    private static IReadOnlyList<GoalShortfall> CalculateGoalShortfalls(
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyDictionary<int, FinancialPattern> goalsByFinanceId)
    {
        var shortfalls = new List<GoalShortfall>();

        foreach (var earmark in earMarkPatterns)
        {
            var goal = goalsByFinanceId[earmark.FinanceId];
            var dueDate = goal.DatePattern.Until;
            var allocated = earmark.StartingAllocation
                - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, dueDate).Count;

            shortfalls.Add(new GoalShortfall
            {
                FinanceId = goal.FinanceId,
                Label = goal.Description ?? goal.Source,
                DueDate = dueDate,
                AmountNeeded = Math.Abs(goal.Amount),
                AmountAllocatedByDueDate = allocated,
            });
        }

        return shortfalls;
    }
}
