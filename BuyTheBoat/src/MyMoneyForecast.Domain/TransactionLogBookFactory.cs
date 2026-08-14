namespace MyMoneyForecast.Domain;

// Builds one forecast's TransactionLogBook from patterns + a seed balance —
// the original model's organization (Book -> Page -> AccountPage ->
// BalanceSnapshot -> FundJar/EarMarkEvent/ExpectedTransaction) computed as a
// stateless, in-memory pass.
//
// Multi-account: each account is its own silo. CreateForecast runs the
// per-account cascade (BuildAccountPage) once per account, then rolls the
// results up into a household summary. The single-account path (no
// ForecastOptions.Accounts) synthesizes one "Primary" account from the flat
// fields, so its output is unchanged.
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

    /// <summary>[CALC] Runs one forecast: builds each account's own day-by-day cascade from its patterns and seed balance, then rolls the results up into goal shortfalls, plan health, and a household summary. The one function every screen's forecast ultimately comes from.</summary>
    /// <param name="options">The patterns, seed balances, and date range to forecast — either the flat single-account fields, or a per-account breakdown via Accounts.</param>
    public static ForecastResult CreateForecast(ForecastOptions options)
    {
        var accountInputs = ResolveAccountInputs(options);

        // One page per account, each its own independent cascade (item 4-A).
        var pages = new Dictionary<string, AccountTransactionPage>();
        var accountForecasts = new List<AccountForecast>();
        DateOnly? firstNegative = null;
        var floored = new List<(DateOnly Date, int FinanceId)>();
        var underfundedReleases = new List<(DateOnly Date, int FinanceId)>();

        foreach (var input in accountInputs)
        {
            var built = BuildAccountPage(input, options.AsOfDate, options.HorizonEndDate);
            pages[input.Name] = built.Page;
            accountForecasts.Add(new AccountForecast
            {
                AccountId = input.AccountId,
                Name = input.Name,
                Page = built.Page,
                FirstNegativeFreeBalanceDate = built.FirstNegativeDate,
            });
            if (built.FirstNegativeDate is { } negativeDate && (firstNegative is null || negativeDate < firstNegative))
            {
                firstNegative = negativeDate;
            }
            floored.AddRange(built.Floored);
            underfundedReleases.AddRange(built.UnderfundedReleases);
        }

        // Global roll-ups (jar labels, goal shortfalls) span every account —
        // finance ids are unique across the whole book, so the union is safe.
        var allPatterns = accountInputs.SelectMany(account => account.FinancialPatterns).ToList();
        var allEarmarks = accountInputs.SelectMany(account => account.EarMarkPatterns).ToList();
        var allManualEarmarks = accountInputs.SelectMany(account => account.ManualEarmarks).ToList();
        var patternsById = allPatterns.ToDictionary(pattern => pattern.FinanceId);
        var goalShortfalls = CalculateGoalShortfalls(allEarmarks, patternsById, allManualEarmarks);

        // Today's jar per finance id, read off whichever account holds it —
        // PlanHealthState's "right now" questions all key off this one
        // snapshot, the same seed every account's InitialSnapshot already is.
        var jarsByFinanceId = accountForecasts
            .SelectMany(account => account.Page.InitialSnapshot.FundJars)
            .Where(jar => jar.FinanceId is not null)
            .ToDictionary(jar => jar.FinanceId!.Value);

        // planning/22 §3's two "not yet built" capabilities — the forward
        // per-occurrence walk and account-level free funds on a future date —
        // both turn out to be the same missing thread: each account's own
        // BalanceRecord already carries both (a jar's day-by-day state, and
        // ExpectedFreeAmount) by this point in CreateForecast. Same source/
        // iteration as jarsByFinanceId above, just keyed to the page instead
        // of one jar, so IsWorthWarningAbout can walk it for any future date.
        var pageByFinanceId = accountForecasts
            .SelectMany(account => account.Page.InitialSnapshot.FundJars
                .Where(jar => jar.FinanceId is not null)
                .Select(jar => (FinanceId: jar.FinanceId!.Value, account.Page)))
            .ToDictionary(pair => pair.FinanceId, pair => pair.Page);

        var book = new TransactionLogBook
        {
            PageLength = null, // DIVERGENCE(page-length): one window-sized page
            LogPages =
            [
                new TransactionLogPage
                {
                    StartDate = options.AsOfDate,
                    EndDate = options.HorizonEndDate,
                    AccountPages = pages,
                },
            ],
        };

        return new ForecastResult
        {
            AsOfDate = options.AsOfDate,
            HorizonEndDate = options.HorizonEndDate,
            Book = book,
            GoalShortfalls = goalShortfalls,
            PlanHealthStates = CalculatePlanHealthStates(
                goalShortfalls, jarsByFinanceId, patternsById, allEarmarks, allManualEarmarks, underfundedReleases,
                pageByFinanceId, options.AsOfDate),
            JarLabels = patternsById.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Description ?? pair.Value.Source),
            HasNegativeFreeBalance = firstNegative is not null,
            FirstNegativeFreeBalanceDate = firstNegative,
            FlooredManualEarmarks = floored,
            Accounts = accountForecasts,
            Household = BuildHouseholdSummary(accountForecasts, options.TransferWithdrawalFinanceIds),
        };
    }

    /// <summary>[CALC] Walks a finance id's MilestoneAmount day by day across a date range. Unlike ExpectedAmount, this needs no starting balance or transaction history — the reset-at-release rule is pure pattern math, so it can be walked for any range, including before today. Feeds the Earmark form's Summary chart. A deliberately separate implementation from BuildAccountPage's own per-day loop (too deeply interleaved with jar/cushion/deallocation state to share cleanly) — both are checked against the same tests.</summary>
    /// <param name="patterns">The savings plan(s) funding the goal.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="from">Start of the range to walk.</param>
    /// <param name="to">End of the range to walk.</param>
    /// <param name="startingAllocation">Seeds the walk instead of starting from 0, for a live "proposed" chart line before a plan is saved — safe, since the first release always resets the running total to 0 regardless of what it held, so the seed's influence disappears after that point (see ComputeMilestoneTrajectory_seed_only_survives_until_the_first_release_then_matches_the_unseeded_walk_exactly).</param>
    public static IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> ComputeMilestoneTrajectory(
        IReadOnlyList<EarMarkPattern> patterns, FinancialPattern goal, DateOnly from, DateOnly to, decimal startingAllocation = 0m)
    {
        var contributionsByDate = patterns
            .SelectMany(pattern => pattern.DatePattern.GetOccurrences(from, to).Select(date => (Date: date, Amount: -pattern.Amount)))
            .ToLookup(entry => entry.Date, entry => entry.Amount);
        var releaseDates = goal.DatePattern.GetOccurrences(from, to).ToHashSet();

        var eventDates = contributionsByDate.Select(group => group.Key).Concat(releaseDates).Distinct().OrderBy(date => date);

        var points = new List<(DateOnly Date, decimal MilestoneAmount)>();
        var running = startingAllocation;
        foreach (var date in eventDates)
        {
            // Accumulate first, THEN reset if this is also a release date —
            // same order BuildAccountPage's own loop uses, so a
            // contribution landing the same day as its own release washes
            // out with it rather than heading a fresh cycle (matches a plan
            // deliberately paced to land right when its bill is due).
            running += contributionsByDate[date].Sum();
            running = releaseDates.Contains(date) ? 0m : running;
            points.Add((date, running));
        }

        return points;
    }

    /// <summary>[CALC] Reports whether a goal's very first occurrence hasn't happened yet — today counts as not-yet. A pure date fact, usable both for a saved PlanHealthState and a proposed, not-yet-saved pattern straight from the Earmark form. TODO: once actual-transaction pairing exists, an occurrence paired to a real transaction should count as already-happened even if its own date is still today-or-later.</summary>
    /// <param name="goal">The goal to check.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    public static bool IsFirstOccurrencePending(FinancialPattern goal, DateOnly asOfDate) =>
        FirstOccurrence(goal.DatePattern) is { } date && date >= asOfDate;

    /// <summary>[CALC] Returns how short the given patterns' own contributions (StartingAllocation + repeated occurrences + manual earmarks) would be by the time the goal's first occurrence lands — 0 when it's already covered, or when the first occurrence isn't pending. Takes patterns/manualEarmarks as plain parameters rather than a saved EarMarkPattern, so the Earmark form can call this against whatever's currently proposed but unsaved, not just a saved plan.</summary>
    /// <param name="patterns">The savings plan(s) funding the goal.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="manualEarmarks">Every manual earmark, to include any covering this goal.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    public static decimal FirstOccurrenceShortfall(
        IReadOnlyList<EarMarkPattern> patterns, FinancialPattern goal, IReadOnlyList<ManualEarmark> manualEarmarks, DateOnly asOfDate)
    {
        if (!IsFirstOccurrencePending(goal, asOfDate))
        {
            return 0m;
        }

        var firstOccurrence = FirstOccurrence(goal.DatePattern)!.Value;
        var accumulated = patterns.Sum(pattern =>
                pattern.StartingAllocation
                - pattern.Amount * pattern.DatePattern.GetOccurrences(pattern.DatePattern.Start, firstOccurrence).Count)
            + manualEarmarks
                .Where(manual => manual.FinanceId == goal.FinanceId && manual.Date <= firstOccurrence)
                .Sum(manual => manual.Amount);

        return Math.Max(0m, Math.Abs(goal.Amount) - accumulated);
    }

    /// <summary>[CALC] Returns a pattern's first occurrence, or null if it has none.</summary>
    /// <param name="pattern">The pattern to search.</param>
    private static DateOnly? FirstOccurrence(RecurrenceRule pattern)
    {
        var occurrences = pattern.GetOccurrences(pattern.Start, pattern.Until);
        return occurrences.Count > 0 ? occurrences[0] : null;
    }

    /// <summary>[CALC] Returns the per-account breakdown when given; otherwise one "Primary" account synthesized from the flat fields — the pre-multi-account behavior, byte-for-byte.</summary>
    /// <param name="options">The forecast options, with either Accounts set or the flat single-account fields.</param>
    private static IReadOnlyList<AccountForecastInput> ResolveAccountInputs(ForecastOptions options)
    {
        if (options.Accounts is { Count: > 0 } accounts)
        {
            return accounts;
        }

        return
        [
            new AccountForecastInput
            {
                AccountId = 0,
                Name = PrimaryAccountName,
                StartingBalance = options.StartingBalance,
                IdealSafetyCushion = options.IdealSafetyCushion,
                FinancialPatterns = options.FinancialPatterns,
                EarMarkPatterns = options.EarMarkPatterns,
                ManualEarmarks = options.ManualEarmarks,
            },
        ];
    }

    /// <summary>[CALC] Builds the household roll-up: on each date any account has an event, sums every account's free/set-aside as of that date (its latest snapshot on or before it) — flagging any account whose own free went negative, since a positive household total can hide a locally-short account.</summary>
    /// <param name="accounts">Every account's own forecast.</param>
    /// <param name="transferWithdrawalFinanceIds">Finance ids of transfer withdrawals, so their reservations can be added back into household free.</param>
    private static HouseholdSummary BuildHouseholdSummary(
        IReadOnlyList<AccountForecast> accounts,
        IReadOnlySet<int> transferWithdrawalFinanceIds)
    {
        var asOfFree = accounts.Sum(account =>
        {
            var sample = SampleAsOf(account.Page, account.Page.StartDate, transferWithdrawalFinanceIds);
            return sample.Free + sample.TransferReserved;
        });

        var dates = new SortedSet<DateOnly>();
        foreach (var account in accounts)
        {
            dates.UnionWith(account.Page.BalanceRecord.Keys);
        }

        var days = new List<HouseholdDay>(dates.Count);
        foreach (var date in dates)
        {
            var free = 0m;
            var setAside = 0m;
            var shortAccounts = new List<string>();
            var cushionDipped = new List<string>();
            foreach (var account in accounts)
            {
                var sample = SampleAsOf(account.Page, date, transferWithdrawalFinanceIds);

                // A transfer's withdrawal reserves in the account it leaves,
                // so that account's own free reflects money already
                // committed to going. Household-wide it is not spending —
                // the money is still in the household — so it is moved back
                // out of set-aside and into free here. Total is untouched
                // either way, which is why this is a reclassification and
                // the free + set-aside = total identity still holds.
                free += sample.Free + sample.TransferReserved;
                setAside += sample.SetAside - sample.TransferReserved;

                // "Short" stays a per-account test on the account's OWN free —
                // a transfer it cannot fund is a real problem for it, so the
                // household add-back deliberately does not soften this.
                if (sample.Free < 0m)
                {
                    shortAccounts.Add(account.Name);
                }

                // The buffer is not whole. Never fires for an account with
                // no cushion, since 0 can't sit below 0.
                if (sample.Cushion < account.Page.IdealSafetyCushion)
                {
                    cushionDipped.Add(account.Name);
                }
            }

            days.Add(new HouseholdDay
            {
                Date = date,
                Free = free,
                SetAside = setAside,
                ShortAccounts = shortAccounts,
                CushionDippedAccounts = cushionDipped,
            });
        }

        return new HouseholdSummary { AsOfFree = asOfFree, Days = days };
    }

    /// <summary>[CALC] Samples an account's state as of a date: the latest snapshot on or before it, else the initial snapshot.</summary>
    /// <param name="page">The account to sample.</param>
    /// <param name="date">The date to sample as of.</param>
    /// <param name="transferWithdrawalFinanceIds">Finance ids of transfer withdrawals, to split their reservation out separately.</param>
    /// <returns>Free (spendable), SetAside (the reserved portion, expected minus free), TransferReserved (the part of SetAside sitting in transfer-withdrawal jars), and Cushion (the null-id jar's amount).</returns>
    private static (decimal Free, decimal SetAside, decimal TransferReserved, decimal Cushion) SampleAsOf(
        AccountTransactionPage page,
        DateOnly date,
        IReadOnlySet<int> transferWithdrawalFinanceIds)
    {
        var snapshot = SnapshotAsOf(page, date);

        var free = snapshot.ExpectedFreeAmount ?? 0m;
        var expected = snapshot.ExpectedAmount ?? 0m;

        var transferReserved = 0m;
        var cushion = 0m;
        foreach (var jar in snapshot.FundJars)
        {
            if (jar.FinanceId is not { } financeId)
            {
                cushion = jar.ExpectedAmount;
            }
            else if (transferWithdrawalFinanceIds.Contains(financeId))
            {
                transferReserved += jar.ExpectedAmount;
            }
        }

        return (free, expected - free, transferReserved, cushion);
    }

    private sealed record AccountPageBuild(
        AccountTransactionPage Page,
        DateOnly? FirstNegativeDate,
        IReadOnlyList<(DateOnly Date, int FinanceId)> Floored,
        IReadOnlyList<(DateOnly Date, int FinanceId)> UnderfundedReleases);

    /// <summary>[CALC] Builds one account's page — the per-account silo cascade that produces its own day-by-day balance record from its own patterns, balance, and cushion.</summary>
    /// <param name="input">The account's patterns, seed balance, and cushion.</param>
    /// <param name="asOfDate">The forecast's as-of date.</param>
    /// <param name="horizonEndDate">The forecast's horizon end date.</param>
    private static AccountPageBuild BuildAccountPage(AccountForecastInput input, DateOnly asOfDate, DateOnly horizonEndDate)
    {
        var patternsById = input.FinancialPatterns.ToDictionary(pattern => pattern.FinanceId);
        var earmarkedIds = input.EarMarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

        // Every outflow that reserves against free funds does so through a
        // real EarMarkPattern (its Allocation Plan), created at
        // pattern-creation time by AllocationPlanProposer and passed in via
        // input.EarMarkPatterns — so the engine treats a bill's plan exactly
        // like a goal's savings plan, and an outflow with no plan simply
        // reduces free funds on its due date (and reads short).

        // === AdjustSnapshots role: materialize the window's events ===

        // ExpectedTransactions: one per FinancialPattern occurrence in the
        // window. These, not raw occurrence counts, now drive the balance
        // math — restoring the documented model's event layer.
        var expectedByDate = new Dictionary<DateOnly, List<ExpectedTransaction>>();
        foreach (var pattern in input.FinancialPatterns)
        {
            foreach (var date in pattern.DatePattern.GetOccurrences(asOfDate, horizonEndDate))
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
        // so the sign flips here. planning/17, item 9 (F30): two patterns
        // sharing a finance_id (concurrent funders) can land on the same
        // day, so this merges rather than always appending — the repeated
        // counterpart to MergeOrAppendIsolatedEarmark below.
        foreach (var earmark in input.EarMarkPatterns)
        {
            foreach (var date in earmark.DatePattern.GetOccurrences(asOfDate, horizonEndDate))
            {
                MergeOrAppendRepeatedEarmark(GetOrAdd(earmarkEventsByDate, date), earmark.FinanceId, -earmark.Amount, date);
            }
        }

        // Manual (user-created) isolated earmarks in the window (planning/09):
        // ExplicitAmount = the user's number (8.4.a2 / 3.13.8.5.a1) — kept
        // distinct so a deallocation-day give-back can merge in without losing
        // the user's intent (3.13c.8.4.a2). One dated exactly on the as-of day
        // is already folded into the seed above; it's attached here too so the
        // day DISPLAYS it, and the seed-day rule (no deltas) prevents double
        // counting.
        foreach (var manual in input.ManualEarmarks)
        {
            if (manual.Date >= asOfDate && manual.Date <= horizonEndDate)
            {
                GetOrAdd(earmarkEventsByDate, manual.Date).Add(new EarMarkEvent
                {
                    FinanceId = manual.FinanceId,
                    EarmarkDate = manual.Date,
                    RepeatedEarmark = false,
                    ExpectedAmount = manual.Amount,
                    ExplicitAmount = manual.Amount,
                });
            }
        }

        // ASSUMED-PAIRING(3.13c.a10): a goal's own occurrence releases the
        // money its jar was holding, or it would be double-counted against the
        // occurrence's ExpectedTransaction (in the documented model this
        // implicit withdrawal is created when a PAIRED ACTUAL transaction
        // lands; with pairing assumed, the expected occurrence triggers it).
        // This is now decided PER DAY in the cascade below, because on a
        // deallocation day the release IS Step A's paired earmark (06/07
        // decision #1: the SAME earmark, mutually exclusive with the normal-day
        // release). A goal occurrence is simply an expected transaction whose
        // finance id has an EarMarkPattern (earmarkedIds), and it is already an
        // ExpectedTransaction in expectedByDate — so nothing is materialized
        // here; the cascade classifies it as a paired transaction on its day.

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
        // More than one EarMarkPattern may share a finance_id (a
        // "Restructure" predecessor + successor), so every plan funding a
        // goal is summed here — one jar/milestone value per finance_id, not
        // one per plan. Each plan's own GetOccurrences call is bounded by
        // ITS OWN Until, so a truncated predecessor and its successor never
        // double-count the same day.
        foreach (var group in input.EarMarkPatterns.ToLookup(earmark => earmark.FinanceId))
        {
            var financeId = group.Key;
            var goal = patternsById[financeId];
            var plans = group.ToList();
            var contributed = plans.Sum(earmark =>
                    earmark.StartingAllocation
                    - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, asOfDate).Count)
                // Manual adjustments already made on/before the as-of date
                // are part of the jar's settled history — dated
                // StartingAllocation, effectively.
                + input.ManualEarmarks
                    .Where(manual => manual.FinanceId == financeId && manual.Date <= asOfDate)
                    .Sum(manual => manual.Amount);
            var withdrawn = Math.Abs(goal.Amount) * goal.DatePattern.GetOccurrences(goal.DatePattern.Start, asOfDate).Count;
            jarValues[financeId] = Math.Max(0m, contributed - withdrawn);

            // 3.13.5.4.a1, reset-at-release: milestone counts scheduled
            // contributions since the goal's LAST release, not its lifetime
            // total — otherwise a repeating goal's milestone climbs forever
            // even though its jar returns to ~0 on every on-time payment (a
            // $500/month loan paid on schedule would read "$500 short," then
            // "$1,000 short," forever). Deliberately does not carry a
            // chronic, multi-cycle shortfall forward — GoalShortfall (the
            // whole-span, never-resets metric) is what catches that; this is
            // the per-cycle pacing signal.
            //
            // Fixed (2026-08-13): used to take a "lifetime contributed minus
            // lifetime withdrawn" shortcut instead of an actual walk. That
            // shortcut only equals "since the last release" when every prior
            // cycle's own contributions exactly matched what got released —
            // each such cycle then nets to zero and cancels out of the
            // lifetime sum, leaving just the current cycle's own residual.
            // The moment a goal's plans DON'T sum to its own rate (found via
            // Storage Unit Rental in the field: two concurrent plans, $35 +
            // $25, against a $50 bill), every prior cycle leaves a real
            // residual too, and the shortcut silently accumulates it across
            // every cycle instead of resetting — a stream running $10/month
            // ahead of its bill read as "$80 saved" after 8 months instead
            // of the ~$25 actually accrued since the last release (and the
            // underfunded mirror image floors at 0 every cycle instead of
            // showing what's genuinely accrued toward the current one).
            // ComputeMilestoneTrajectory already walks this correctly
            // (reset-at-release, proven by its own tests) — reused here for
            // its last point rather than re-deriving a second, narrower
            // formula. from is the earliest of this finance_id's own plans'
            // ActiveStart, matching how a concurrent second funder is
            // already handled everywhere else this needs an anchor date.
            var earliestActiveStart = plans.Min(plan => plan.DatePattern.ActiveStart);
            var milestoneTrajectory = ComputeMilestoneTrajectory(plans, goal, earliestActiveStart, asOfDate);
            milestones[financeId] = milestoneTrajectory.Count > 0 ? milestoneTrajectory[^1].MilestoneAmount : 0m;
        }

        // The safety cushion (finance_id = null jar, priority 0) can't live in
        // jarValues (its key is a non-nullable int), so it rides alongside as a
        // running value. Firm target: seeded to the full amount and refilled to
        // it each day; deallocation drains it first. 0 (the default) keeps the
        // pre-cushion behaviour exactly.
        var cushionTarget = input.IdealSafetyCushion;
        var cushionValue = cushionTarget;

        var initialSnapshot = new BalanceSnapshot
        {
            SnapshotDate = null,
            // The one manually-entered number: the user's real balance as of
            // the as-of date, assumed to already reflect everything that
            // happened up to and including that day.
            FullAmount = input.StartingBalance,
            ExpectedAmount = input.StartingBalance,
            ExpectedFreeAmount = input.StartingBalance - jarValues.Values.Sum() - cushionValue,
            FundJars = BuildJars(jarValues, milestones, cushionValue, currentIsKnown: true),
            ActualTransactions = [],
            ExpectedTransactions = [],
            EarMarkEvents = [],
        };

        // === CascadePageBalanceRecord role: the forward value pass ===

        var balanceRecord = new SortedDictionary<DateOnly, BalanceSnapshot>();
        var previousExpected = initialSnapshot.ExpectedAmount!.Value;
        DateOnly? firstNegativeDate = initialSnapshot.ExpectedFreeAmount < 0m ? asOfDate : null;
        var flooredManualEarmarks = new List<(DateOnly Date, int FinanceId)>();
        var underfundedReleases = new List<(DateOnly Date, int FinanceId)>();

        foreach (var date in snapshotDates)
        {
            var expectedTransactions = expectedByDate.GetValueOrDefault(date) ?? [];
            var earMarkEvents = earmarkEventsByDate.GetValueOrDefault(date) ?? [];

            // ASSUMED-PAIRING(as-of-day-settled): the entered balance is
            // assumed to already include anything happening ON the as-of
            // date, so a snapshot dated exactly there attaches its events
            // for display but contributes no deltas — its values equal the
            // initial snapshot's. (Same convention the flat engine used.)
            var isPageStartDate = date == asOfDate;

            // 10.3: previous snapshot's amount + today's expected
            // transactions.
            var expected = previousExpected
                + (isPageStartDate ? 0m : expectedTransactions.Where(t => !t.Cancelled).Sum(t => t.ExpectedAmount));

            var isDeallocationDay = false;
            if (!isPageStartDate)
            {
                // The safety cushion refills toward its standing target each day
                // via a positive isolated null-id earmark — the automatically funded expense
                // reservation pattern above, but toward a fixed target with no
                // due date and no reset (delta is 0 once at target). After a
                // deallocation drained it, this steps it back up.
                var cushionFill = cushionTarget - cushionValue;
                if (cushionFill != 0m)
                {
                    earMarkEvents.Add(new EarMarkEvent
                    {
                        FinanceId = null,
                        EarmarkDate = date,
                        RepeatedEarmark = false,
                        ExpectedAmount = cushionFill,
                        ExplicitAmount = 0m,
                    });
                }

                // At this point earMarkEvents holds exactly the day's SCHEDULED
                // earmarks (er + ei): repeated goal contributions + automatically funded expense and
                // cushion reservation deltas. jarValues + cushionValue still hold
                // the PREVIOUS day's balances (f) — the floor loop below applies
                // today's events. Deallocation runs first and appends its
                // give-backs (the cushion, priority 0, is drained before any real
                // jar); the page.s starting date is skipped on purpose (its over-allocation
                // is the correct Q2 "short right now" signal, not a thing to
                // drain away).
                var releaseResult = AppendDeallocationOrGoalReleases(
                    earMarkEvents, date, previousExpected, jarValues, cushionValue,
                    expectedTransactions, patternsById, earmarkedIds);
                isDeallocationDay = releaseResult.IsDeallocationDay;

                // 3.13.5.3.a1 per jar: previous day's amount + today's earmark
                // events, floored at 0 (a jar can be emptied, never negative).
                // Deallocation/release give-backs are ordinary earmark events, so
                // the jars — and the cushion — come down here for free: the ONLY
                // place balances move.
                foreach (var earMarkEvent in earMarkEvents)
                {
                    if (earMarkEvent.FinanceId is not { } financeId)
                    {
                        // The finance_id = null safety cushion (fill or give-back).
                        cushionValue = Math.Max(0m, cushionValue + earMarkEvent.ExpectedAmount);
                        continue;
                    }

                    var unfloored = jarValues[financeId] + earMarkEvent.ExpectedAmount;

                    // A clamped event whose user-entered portion is a withdrawal
                    // means the user's stated intent didn't fully happen — only
                    // what the jar held actually moved. Reported so the UI can
                    // flag it in place. System-only events never over-pull
                    // (deallocation's W = Max(-Fa, N) is bounded), so this
                    // only fires on manual withdrawals.
                    if (unfloored < 0m && earMarkEvent.ExplicitAmount is < 0m)
                    {
                        flooredManualEarmarks.Add((date, financeId));
                    }

                    // PlanHealthState's UnderfundedReleaseDates: this
                    // specific finance id's OWN release (its goal/bill
                    // occurrence today, per ReleasedFinanceIds — not some
                    // unrelated jar a deallocation day happened to raid)
                    // wanted to pay out more than the jar held. A release is
                    // a system event (ExplicitAmount == 0m), distinguishing
                    // it from a manual withdrawal above.
                    if (unfloored < 0m && earMarkEvent.ExplicitAmount is 0m && !earMarkEvent.RepeatedEarmark
                        && releaseResult.ReleasedFinanceIds.Contains(financeId))
                    {
                        underfundedReleases.Add((date, financeId));
                    }

                    jarValues[financeId] = Math.Max(0m, unfloored);

                    // 3.13.5.4.a1: milestone accumulates repeated
                    // (pattern-scheduled) contributions only.
                    if (earMarkEvent.RepeatedEarmark)
                    {
                        milestones[financeId] += earMarkEvent.ExpectedAmount;
                    }
                }

                // ASSUMED-PAIRING(3.13c.a10), reset-at-release: today's own
                // goal/bill occurrence(s) close out
                // their current cycle, so the pacing milestone starts fresh —
                // AFTER today's own accumulation above, so a same-day final
                // contribution (a plan intentionally paced to land right when
                // its bill is due) washes out together with the release instead
                // of reading as the next cycle's head start. Keyed on the
                // occurrence itself (ReleasedFinanceIds), not on which jars
                // moved money today, because a deallocation day can drain an
                // UNRELATED jar to cover the shortfall — that jar's own
                // occurrence isn't today, so its pacing must not reset.
                foreach (var releasedFinanceId in releaseResult.ReleasedFinanceIds)
                {
                    milestones[releasedFinanceId] = 0m;
                }
            }

            // 3.13.4.a1: free = expected minus everything sitting in jars,
            // including the safety cushion.
            var expectedFree = expected - jarValues.Values.Sum() - cushionValue;

            balanceRecord[date] = new BalanceSnapshot
            {
                SnapshotDate = date,
                FullAmount = null, // 10.2: unknowable until the day occurs
                ExpectedAmount = expected,
                ExpectedFreeAmount = expectedFree,
                FundJars = BuildJars(jarValues, milestones, cushionValue, currentIsKnown: false),
                ActualTransactions = [],
                ExpectedTransactions = expectedTransactions,
                EarMarkEvents = earMarkEvents,
                IsDeallocationDay = isDeallocationDay,
            };

            previousExpected = expected;
            firstNegativeDate ??= expectedFree < 0m ? date : null;
        }

        // === Assemble this account's page ===

        var accountPage = new AccountTransactionPage
        {
            Account = input.Name,
            StartDate = asOfDate,
            EndDate = horizonEndDate,
            Expired = false,
            IdealSafetyCushion = input.IdealSafetyCushion,
            SafetyPriority = 0, // fixed at 0 — the cushion is always drained first
            FinancePatterns = input.FinancialPatterns,
            EarmarkPatterns = input.EarMarkPatterns,
            InitialSnapshot = initialSnapshot,
            BalanceRecord = balanceRecord,
            // ASSUMED-PAIRING(unpaid-expected): see AccountTransactionPage.
            CurrentUnpaidExpected = 0m,
            CurrentFreeAmount = initialSnapshot.ExpectedFreeAmount,
        };

        return new AccountPageBuild(accountPage, firstNegativeDate, flooredManualEarmarks, underfundedReleases);
    }

    /// <summary>[CALC] Returns the list for a date in a date-keyed dictionary, creating and inserting an empty one if it isn't there yet.</summary>
    /// <param name="map">The dictionary to look up (and possibly insert into).</param>
    /// <param name="date">The date to look up.</param>
    private static List<T> GetOrAdd<T>(Dictionary<DateOnly, List<T>> map, DateOnly date)
    {
        if (!map.TryGetValue(date, out var list))
        {
            list = [];
            map[date] = list;
        }

        return list;
    }

    /// <summary>[CALC] The Q2 engine: on a deallocation day drains the lowest-priority jars first to cap allocation at available funds; otherwise releases each goal's jar on its own occurrence. Either way, appends the result to the day's earMarkEvents (mutated in place) — deallocation never rewrites jar values directly.</summary>
    /// <param name="earMarkEvents">The day's already-scheduled earmark events; the day's release/deallocation events are appended here.</param>
    /// <param name="date">The day being processed.</param>
    /// <param name="previousExpected">Yesterday's ExpectedAmount (c).</param>
    /// <param name="jarValues">Every jar's previous-day balance (f), keyed by finance id.</param>
    /// <param name="cushionValue">The cushion's previous-day balance.</param>
    /// <param name="expectedTransactions">Today's expected transactions.</param>
    /// <param name="patternsById">Every financial pattern, keyed by finance id.</param>
    /// <param name="earmarkedIds">Finance ids with a savings plan — their own occurrence today is a paired transaction (ap), not an unpaired one.</param>
    /// <returns>Whether this was a deallocation day (surfaced onto the snapshot for the UI drain highlight), plus which finance ids had their own occurrence today, so the caller can reset their milestone pacing regardless of which branch ran.</returns>
    private static (bool IsDeallocationDay, IReadOnlyList<int> ReleasedFinanceIds) AppendDeallocationOrGoalReleases(
        List<EarMarkEvent> earMarkEvents,
        DateOnly date,
        decimal previousExpected,
        IReadOnlyDictionary<int, decimal> jarValues,
        decimal cushionValue,
        IReadOnlyList<ExpectedTransaction> expectedTransactions,
        IReadOnlyDictionary<int, FinancialPattern> patternsById,
        IReadOnlySet<int> earmarkedIds)
    {
        // Paired (ap) vs. unpaired (au).
        var pairedTransactions = new List<PairedTransaction>();
        var unpaired = 0m;
        foreach (var transaction in expectedTransactions)
        {
            if (transaction.Cancelled)
            {
                continue;
            }

            if (earmarkedIds.Contains(transaction.FinanceId))
            {
                pairedTransactions.Add(new PairedTransaction(transaction.FinanceId, transaction.ExpectedAmount));
            }
            else
            {
                unpaired += transaction.ExpectedAmount;
            }
        }

        // Each jar's already-scheduled earmark total (er + ei) for the day;
        // the cushion's own fill delta is tracked separately (null finance id).
        var existingByJar = new Dictionary<int, decimal>();
        var cushionExisting = 0m;
        foreach (var earMarkEvent in earMarkEvents)
        {
            if (earMarkEvent.FinanceId is { } id)
            {
                existingByJar[id] = existingByJar.GetValueOrDefault(id) + earMarkEvent.ExpectedAmount;
            }
            else
            {
                cushionExisting += earMarkEvent.ExpectedAmount;
            }
        }

        // The finance_id=null safety cushion goes FIRST at priority 0 (drained
        // before every real jar), with its previous balance and today's fill
        // delta. Then every real jar with its previous balance (f) and
        // scheduled earmark total.
        var jars = new List<DeallocationJar>(jarValues.Count + 1)
        {
            new(FinanceId: null, Priority: 0, Balance: cushionValue, ExistingEarmark: cushionExisting),
        };
        foreach (var (financeId, balance) in jarValues)
        {
            jars.Add(new DeallocationJar(
                FinanceId: financeId,
                Priority: patternsById[financeId].Priority,
                Balance: balance,
                ExistingEarmark: existingByJar.GetValueOrDefault(financeId),
                // Mandatory means "the user has to pay this", and its only
                // job is protecting the jar — everything skippable is
                // drained before anything unskippable is touched.
                Skippable: !patternsById[financeId].Mandatory));
        }

        var isDeallocationDay =
            DeallocationCalculator.IsDeallocationDay(previousExpected, jars, pairedTransactions, unpaired);
        if (isDeallocationDay)
        {
            // Step A's paired earmark IS the goal release (decision #1), so the
            // normal-day release below must NOT also fire on a deallocation day.
            var deallocation = DeallocationCalculator.Deallocate(
                previousExpected, jars, pairedTransactions, unpaired);
            foreach (var (financeId, amount) in deallocation.EarmarkEvents)
            {
                MergeOrAppendIsolatedEarmark(earMarkEvents, financeId, amount, date);
            }
        }
        else
        {
            // Not a deallocation day: each goal occurrence releases its jar in
            // full, exactly as before deallocation existed.
            foreach (var paired in pairedTransactions)
            {
                earMarkEvents.Add(new EarMarkEvent
                {
                    FinanceId = paired.FinanceId,
                    EarmarkDate = date,
                    RepeatedEarmark = false,
                    ExpectedAmount = -Math.Abs(paired.Amount),
                    ExplicitAmount = 0m,
                });
            }
        }

        return (isDeallocationDay, pairedTransactions.Select(paired => paired.FinanceId).ToList());
    }

    /// <summary>[CALC] Merges an amount into an existing repeated earmark event for the same jar/day if one exists, otherwise appends a new one — two EarMarkPatterns sharing a finance id (concurrent funders) can generate an occurrence on the same day, and only one repeated earmark per finance id per day may exist (3.13.8.1.a2). A repeated and an isolated earmark for the same jar/day still coexist as two separate events.</summary>
    /// <param name="events">The day's earmark events; merged into or appended to in place.</param>
    /// <param name="financeId">Which jar the earmark is for.</param>
    /// <param name="amount">The amount to merge in.</param>
    /// <param name="date">The day being processed.</param>
    private static void MergeOrAppendRepeatedEarmark(List<EarMarkEvent> events, int financeId, decimal amount, DateOnly date)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].RepeatedEarmark && events[i].FinanceId == financeId)
            {
                events[i] = events[i] with { ExpectedAmount = events[i].ExpectedAmount + amount };
                return;
            }
        }

        events.Add(new EarMarkEvent
        {
            FinanceId = financeId,
            EarmarkDate = date,
            RepeatedEarmark = true,
            ExpectedAmount = amount,
            ExplicitAmount = null,
        });
    }

    /// <summary>[CALC] Merges an amount into an existing isolated earmark event for the same jar/day if one exists, otherwise appends a new one — preserves "one isolated earmark per finance id per day" and avoids a duplicate detail-pane row. Repeated earmarks are left alone.</summary>
    /// <param name="events">The day's earmark events; merged into or appended to in place.</param>
    /// <param name="financeId">Which jar the earmark is for; null for the safety cushion.</param>
    /// <param name="amount">The amount to merge in.</param>
    /// <param name="date">The day being processed.</param>
    private static void MergeOrAppendIsolatedEarmark(
        List<EarMarkEvent> events, int? financeId, decimal amount, DateOnly date)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (!events[i].RepeatedEarmark && events[i].FinanceId == financeId)
            {
                events[i] = events[i] with { ExpectedAmount = events[i].ExpectedAmount + amount };
                return;
            }
        }

        events.Add(new EarMarkEvent
        {
            FinanceId = financeId,
            EarmarkDate = date,
            RepeatedEarmark = false,
            ExpectedAmount = amount,
            ExplicitAmount = 0m,
        });
    }

    /// <summary>[CALC] Builds a snapshot's list of fund jars from the running per-jar values — one goal/bill jar per finance id, plus the safety cushion appended last. Order is stable (insertion order of jarValues).</summary>
    /// <param name="jarValues">Every jar's current balance, keyed by finance id.</param>
    /// <param name="milestones">Every jar's current milestone accrual, keyed by finance id.</param>
    /// <param name="cushionValue">The cushion's current balance.</param>
    /// <param name="currentIsKnown">Whether CurrentAmount is knowable yet (true only for the as-of day's seed snapshot).</param>
    private static List<FundJar> BuildJars(
        Dictionary<int, decimal> jarValues,
        Dictionary<int, decimal> milestones,
        decimal cushionValue,
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
                // automatically funded expense) — nothing to be "behind" on.
                MilestoneAmount = milestones.TryGetValue(financeId, out var milestone) ? milestone : null,
            });
        }

        jars.Add(new FundJar
        {
            FinanceId = null,
            CurrentAmount = currentIsKnown ? cushionValue : null,
            ExpectedAmount = cushionValue,
            MilestoneAmount = null, // "the fund jar for safety cushion will not have a milestone amount"
        });

        return jars;
    }

    /// <summary>[CALC] Computes each goal's shortfall: whether its Allocation Plan will put in enough by its due date. Always includes every outflow that has a plan (ShortfallAmount is 0 when fully on track), evaluated independently of any display horizon, so a UI can render one row per plan without a separate lookup. The same formula serves a one-time goal and a repeating bill — the need scales by occurrence count, and "allocated" counts isolated earmarks (manual adjustments, the starting earmark) as well as the plan's own contributions. Gross-vs-gross: can't detect a plan that back-loads its contributions within the span, though the proposer never actually produces that shape.</summary>
    /// <param name="earMarkPatterns">Every savings plan.</param>
    /// <param name="goalsByFinanceId">Every goal/bill pattern, keyed by finance id.</param>
    /// <param name="manualEarmarks">Every manual earmark.</param>
    private static IReadOnlyList<GoalShortfall> CalculateGoalShortfalls(
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyDictionary<int, FinancialPattern> goalsByFinanceId,
        IReadOnlyList<ManualEarmark> manualEarmarks)
    {
        var shortfalls = new List<GoalShortfall>();

        // One row per GOAL, not one per plan — more than one EarMarkPattern
        // may share a finance_id (a "Restructure" predecessor + successor),
        // and every plan funding a goal must be summed into its one
        // shortfall row.
        foreach (var group in earMarkPatterns.ToLookup(earmark => earmark.FinanceId))
        {
            var financeId = group.Key;
            var goal = goalsByFinanceId[financeId];
            var dueDate = goal.DatePattern.Until;

            var occurrenceCount = goal.DatePattern
                .GetOccurrences(goal.DatePattern.Start, goal.DatePattern.Until).Count;
            var amountNeeded = Math.Abs(goal.Amount) * occurrenceCount;

            // StartingAllocation (the entered opening balance) + every plan's
            // repeated contributions to date + any isolated earmarks dated on or
            // before the due date. The three don't overlap, so no double count.
            var allocated = group.Sum(earmark =>
                    earmark.StartingAllocation
                    - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, dueDate).Count)
                + manualEarmarks
                    .Where(manual => manual.FinanceId == financeId && manual.Date <= dueDate)
                    .Sum(manual => manual.Amount);

            shortfalls.Add(new GoalShortfall
            {
                FinanceId = goal.FinanceId,
                Label = goal.Description ?? goal.Source,
                DueDate = dueDate,
                AmountNeeded = amountNeeded,
                AmountAllocatedByDueDate = allocated,
            });
        }

        return shortfalls;
    }

    // Named, easily-adjustable constants for
    // DetermineIsWorthWarningAbout's thresholds — not literals, so they can
    // be tuned without hunting through the rule's own branches. Hunting down
    // *other* pre-existing magic numbers elsewhere is explicitly deferred to
    // a future pass, not this one.
    private const int WarnIfWithinMonths = 2;
    private const int LookaheadMonths = 6;
    private const decimal HalfOfFreeFundsRatio = 0.5m;
    private const decimal ExcessWarningMultiplier = 2m;

    /// <summary>[CALC] Builds one PlanHealthState row per GoalShortfalls entry, adding the questions a due-date-only shortfall/overfund pair can't answer — today's live pace, whether a catch-up would fix it for good, and which single state is worth showing when more than one applies at once.</summary>
    /// <param name="goalShortfalls">Every goal's due-date shortfall/overfund pair.</param>
    /// <param name="jarsByFinanceId">Today's fund jar for each goal, keyed by finance id.</param>
    /// <param name="goalsByFinanceId">Every goal/bill pattern, keyed by finance id.</param>
    /// <param name="earMarkPatterns">Every savings plan.</param>
    /// <param name="manualEarmarks">Every manual earmark.</param>
    /// <param name="underfundedReleases">Every date/finance-id pair where a release came up short.</param>
    /// <param name="pageByFinanceId">Each goal's own account page, keyed by finance id.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    private static IReadOnlyList<PlanHealthState> CalculatePlanHealthStates(
        IReadOnlyList<GoalShortfall> goalShortfalls,
        IReadOnlyDictionary<int, FundJar> jarsByFinanceId,
        IReadOnlyDictionary<int, FinancialPattern> goalsByFinanceId,
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyList<ManualEarmark> manualEarmarks,
        IReadOnlyList<(DateOnly Date, int FinanceId)> underfundedReleases,
        IReadOnlyDictionary<int, AccountTransactionPage> pageByFinanceId,
        DateOnly asOfDate)
    {
        var releaseDatesByFinanceId = underfundedReleases
            .ToLookup(release => release.FinanceId, release => release.Date);

        var states = new List<PlanHealthState>(goalShortfalls.Count);
        foreach (var shortfall in goalShortfalls)
        {
            var financeId = shortfall.FinanceId;
            var jar = jarsByFinanceId[financeId];

            // Today's live pace — the same two fields the Earmark form's
            // "current jar state" row already shows, just named from the
            // comparison's own two possible directions. At most one is ever
            // non-zero: they are Max(0, x) and Max(0, -x) of the same gap.
            var currentShortfall = Math.Max(0m, (jar.MilestoneAmount ?? 0m) - jar.ExpectedAmount);
            var currentOverfunded = Math.Max(0m, jar.ExpectedAmount - (jar.MilestoneAmount ?? 0m));

            // IsChronicShortfall: would this Savings Plan's own scheduled
            // contributions, run to completion with no further one-time
            // intervention, structurally reach AmountNeeded on their own?
            // Ignores StartingAllocation and manual earmarks on purpose —
            // those are exactly the one-time interventions being tested
            // against, so including them would make every plan look
            // "fixable," which is the question, not the answer.
            var plannedTotal = earMarkPatterns
                .Where(earmark => earmark.FinanceId == financeId)
                .Sum(earmark => -earmark.Amount
                    * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, shortfall.DueDate).Count);
            var isChronicShortfall = shortfall.ShortfallAmount > 0m && plannedTotal < shortfall.AmountNeeded;

            // IsChronicOverfund: the excess-side mirror, same plannedTotal —
            // would this Savings Plan's own scheduled contributions,
            // structurally, put in MORE than AmountNeeded? At most one of
            // IsChronicShortfall/IsChronicOverfund is ever true: they test
            // opposite sides of the same plannedTotal-vs-AmountNeeded
            // comparison, gated on ShortfallAmount/OverfundedAmount, which
            // are themselves Max(0, x)/Max(0, -x) of the same gap.
            var isChronicOverfund = shortfall.OverfundedAmount > 0m && plannedTotal > shortfall.AmountNeeded;

            // The earliest date this financeId's own release actually came
            // up short, from the same forward-walk data IsWorthWarningAbout
            // already uses. Falls back to the due date only when the walk
            // found no specific short occurrence to point at (e.g. the
            // horizon requested didn't reach one) but the whole-span
            // shortfall is still positive.
            var shortReleaseDates = releaseDatesByFinanceId[financeId];
            var projectedShortfallStartDate = shortReleaseDates.Any()
                ? shortReleaseDates.Min()
                : shortfall.ShortfallAmount > 0m ? shortfall.DueDate : (DateOnly?)null;

            // "Is the excess so much that we could skip a payment?" is
            // folded into DetermineIsWorthWarningAbout's own excess branch
            // below rather than kept as its own property — the threshold is
            // double the SMALLEST repeated EarMarkPattern amount in this
            // Savings Plan (not the goal's own amount), since a plan can
            // have more than one concurrent funder at different rates.
            var isWorthWarningAbout = DetermineIsWorthWarningAbout(
                goalsByFinanceId[financeId], shortfall, currentShortfall, currentOverfunded,
                pageByFinanceId[financeId], asOfDate,
                earMarkPatterns.Where(earmark => earmark.FinanceId == financeId).ToList(),
                shortReleaseDates);

            // See PlanHealthState's own doc comments on each for the full
            // reasoning.
            var goal = goalsByFinanceId[financeId];
            var isFirstOccurrencePending = IsFirstOccurrencePending(goal, asOfDate);
            var firstOccurrenceShortfall = FirstOccurrenceShortfall(
                earMarkPatterns.Where(earmark => earmark.FinanceId == financeId).ToList(), goal, manualEarmarks, asOfDate);

            states.Add(new PlanHealthState
            {
                FinanceId = financeId,
                Shortfall = shortfall,
                CurrentShortfallAmount = currentShortfall,
                CurrentOverfundedAmount = currentOverfunded,
                IsChronicShortfall = isChronicShortfall,
                IsChronicOverfund = isChronicOverfund,
                IsWorthWarningAbout = isWorthWarningAbout,
                ProjectedShortfallStartDate = projectedShortfallStartDate,
                MostImportantHealthState = DetermineMostImportantHealthState(
                    currentShortfall, shortfall.ShortfallAmount, currentOverfunded, shortfall.OverfundedAmount),
                UnderfundedReleaseDates = releaseDatesByFinanceId[financeId].ToList(),
                IsFirstOccurrencePending = isFirstOccurrencePending,
                FirstOccurrenceShortfall = firstOccurrenceShortfall,
            });
        }

        return states;
    }

    /// <summary>[CALC] Reports whether a detected shortage/excess is worth actually warning the user about. Non-repeated and repeated patterns get separate rule sets because the two genuinely differ — each branch is commented with the exact rule it implements. Only meaningful for a plan MostImportantHealthState would otherwise flag as non-Healthy.</summary>
    /// <param name="goal">The goal/bill pattern.</param>
    /// <param name="shortfall">The goal's due-date shortfall/overfund pair.</param>
    /// <param name="currentShortfall">How far behind the jar sits today.</param>
    /// <param name="currentOverfunded">How far ahead the jar sits today.</param>
    /// <param name="page">The goal's own account page.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    /// <param name="plansForThisGoal">Every savings plan funding this goal.</param>
    /// <param name="shortReleaseDatesForThisGoal">Dates this goal's own release came up short.</param>
    private static bool DetermineIsWorthWarningAbout(
        FinancialPattern goal,
        GoalShortfall shortfall,
        decimal currentShortfall,
        decimal currentOverfunded,
        AccountTransactionPage page,
        DateOnly asOfDate,
        IReadOnlyList<EarMarkPattern> plansForThisGoal,
        IEnumerable<DateOnly> shortReleaseDatesForThisGoal)
    {
        // A shortage TODAY is always worth surfacing — none of §5's rules
        // soften "already behind right now," only the further-off/excess cases.
        if (currentShortfall > 0m)
        {
            return true;
        }

        var isRepeated = goal.DatePattern.GetOccurrences(goal.DatePattern.Start, goal.DatePattern.Until).Count > 1;
        var warnByDate = asOfDate.AddMonths(WarnIfWithinMonths);

        if (shortfall.ShortfallAmount > 0m)
        {
            if (!isRepeated)
            {
                // Non-repeated rule 2: due soon — warn regardless of size.
                if (shortfall.DueDate <= warnByDate)
                {
                    return true;
                }

                // Non-repeated rules 3/4: further off — the half-of-free-funds
                // boundary (rule 4: the same shared calculation the repeated
                // branch below reuses, not two separate computations).
                return shortfall.ShortfallAmount >= HalfOfFreeFunds(page, shortfall.DueDate);
            }

            var nextOccurrence = goal.DatePattern.GetOccurrences(asOfDate, goal.DatePattern.Until).FirstOrDefault();
            if (nextOccurrence == default)
            {
                return true; // no more occurrences to project against — default to the visible side
            }

            // Repeated rule 3: any occurrence within six months being short
            // is always worth surfacing, independent of rules 1/2 — a
            // repeating pattern's future shortfall doesn't get to "hide" the
            // way a single far-off one-time goal's can. This also covers
            // rule 1 (next occurrence soon and short)
            // whenever that's the occurrence actually flagged, since "soon"
            // is always inside the six-month window.
            var lookaheadEnd = asOfDate.AddMonths(LookaheadMonths);
            if (shortReleaseDatesForThisGoal.Any(date => date <= lookaheadEnd))
            {
                return true;
            }

            // Repeated rule 2 — next occurrence further out than the
            // lookahead covers; still worth a look if the overall shortfall
            // is large relative to free funds that day.
            return shortfall.ShortfallAmount >= HalfOfFreeFunds(page, nextOccurrence);
        }

        // Excess/overfunded case (both kinds): worth warning when the
        // projected excess on the date of the next expected transaction
        // exceeds double the smallest repeated EarMarkPattern.Amount in the
        // Savings Plan.
        if (currentOverfunded > 0m || shortfall.OverfundedAmount > 0m)
        {
            var nextTransactionDate = isRepeated
                ? goal.DatePattern.GetOccurrences(asOfDate, goal.DatePattern.Until).FirstOrDefault()
                : shortfall.DueDate;
            if (nextTransactionDate == default)
            {
                return currentOverfunded > 0m; // no future occurrence to project to — fall back to today's own reading
            }

            var smallestContribution = plansForThisGoal
                .Where(plan => plan.DatePattern.GetOccurrences(plan.DatePattern.Start, plan.DatePattern.Until).Count > 1)
                .Select(plan => Math.Abs(plan.Amount))
                .DefaultIfEmpty(0m)
                .Min();
            if (smallestContribution <= 0m)
            {
                return currentOverfunded > 0m; // no repeated rate to size the threshold against
            }

            var projectedExcess = ProjectedJarExcess(page, nextTransactionDate, shortfall.FinanceId);
            return projectedExcess > smallestContribution * ExcessWarningMultiplier;
        }

        return false;
    }

    /// <summary>[CALC] Returns half of an account's free funds as of a date — the excess-warning threshold for a non-repeated goal projected further out than the lookahead window.</summary>
    /// <param name="page">The account to read free funds from.</param>
    /// <param name="date">The date to read free funds as of.</param>
    private static decimal HalfOfFreeFunds(AccountTransactionPage page, DateOnly date) =>
        (SnapshotAsOf(page, date).ExpectedFreeAmount ?? 0m) * HalfOfFreeFundsRatio;

    /// <summary>[CALC] Returns how far a jar's balance sits above its milestone on a given date, or 0 if it isn't ahead (or doesn't exist).</summary>
    /// <param name="page">The account to read the jar from.</param>
    /// <param name="date">The date to read the jar as of.</param>
    /// <param name="financeId">Which jar to read.</param>
    private static decimal ProjectedJarExcess(AccountTransactionPage page, DateOnly date, int financeId)
    {
        var jar = SnapshotAsOf(page, date).FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return jar is null ? 0m : Math.Max(0m, jar.ExpectedAmount - (jar.MilestoneAmount ?? 0m));
    }

    /// <summary>[CALC] Returns an account's own snapshot as of a date — its latest BalanceRecord entry on or before it, else the initial snapshot. Same "nearest at-or-before" lookup SampleAsOf uses for the household roll-up, extracted so DetermineIsWorthWarningAbout's per-date jar/free-funds lookups share it too.</summary>
    /// <param name="page">The account to read a snapshot from.</param>
    /// <param name="date">The date to read the snapshot as of.</param>
    private static BalanceSnapshot SnapshotAsOf(AccountTransactionPage page, DateOnly date)
    {
        var snapshot = page.InitialSnapshot;
        foreach (var (snapshotDate, dated) in page.BalanceRecord)
        {
            if (snapshotDate > date)
            {
                break;
            }
            snapshot = dated;
        }

        return snapshot;
    }

    /// <summary>[CALC] Ranks which single health state to show when more than one applies: a shortage today always wins; any future shortage beats any excess regardless of which is sooner; an excess only surfaces when no shortage exists anywhere in the window. "Today's excess beats a later one" is this method's own inferred extension of that rule, not separately confirmed.</summary>
    /// <param name="currentShortfall">How far behind the jar sits today.</param>
    /// <param name="projectedShortfall">The goal's due-date shortfall.</param>
    /// <param name="currentOverfunded">How far ahead the jar sits today.</param>
    /// <param name="projectedOverfunded">The goal's due-date overfund.</param>
    private static PlanHealthCategory DetermineMostImportantHealthState(
        decimal currentShortfall, decimal projectedShortfall, decimal currentOverfunded, decimal projectedOverfunded)
    {
        if (currentShortfall > 0m)
        {
            return PlanHealthCategory.AlreadyMissing;
        }

        if (projectedShortfall > 0m)
        {
            return PlanHealthCategory.WillMiss;
        }

        if (currentOverfunded > 0m)
        {
            return PlanHealthCategory.CurrentlyOverfunded;
        }

        if (projectedOverfunded > 0m)
        {
            return PlanHealthCategory.WillBeOverfunded;
        }

        return PlanHealthCategory.Healthy;
    }
}
