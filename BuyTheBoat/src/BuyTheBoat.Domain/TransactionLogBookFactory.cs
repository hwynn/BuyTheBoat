namespace BuyTheBoat.Domain;

// Builds one forecast's TransactionLogBook from patterns + a seed balance —
// the original model's organization (Book -> Page -> AccountPage ->
// BalanceSnapshot -> FundJar/EarMarkEvent/ExpectedTransaction) computed as a
// stateless, in-memory pass.
//
// Multi-account: each account is its own silo. CreateForecast runs the
// per-account cascade (BuildAccountPage) once per account, then rolls the
// results up into a household summary. The single-account path (no
// ForecastOptions.Accounts) synthesizes one "Primary" account from the flat
// fields.
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
// DO exist are tagged ASSUMED-PAIRING / DIVERGENCE inline (grep them).
public static class TransactionLogBookFactory
{
    public const string PrimaryAccountName = "Primary";

    /// <summary>[CALC] Runs one forecast: builds each account's own day-by-day cascade from its patterns and seed balance, then rolls the results up into goal shortfalls, plan health, and a household summary. The one function every screen's forecast ultimately comes from.</summary>
    /// <param name="options">The patterns, seed balances, and date range to forecast — either the flat single-account fields, or a per-account breakdown via Accounts.</param>
    public static ForecastResult CreateForecast(ForecastOptions options)
    {
        if (options.HorizonEndDate <= options.AsOfDate)
        {
            throw new ForecastWindowBackwardsException(
                "The forecast's end date must be after its as-of date.", nameof(options));
        }

        var accountInputs = ResolveAccountInputs(options);
        EnsureUniqueFinanceIds(accountInputs.SelectMany(account => account.FinancialPatterns));

        // One page per account, each its own independent cascade.
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

        // Every account's patterns and plans combined, for the forecast-wide jar labels and goal shortfalls.
        var allPatterns = accountInputs.SelectMany(account => account.FinancialPatterns).ToList();
        var allEarmarks = accountInputs.SelectMany(account => account.EarMarkPatterns).ToList();
        var allManualEarmarks = accountInputs.SelectMany(account => account.ManualEarmarks).ToList();
        var patternsById = allPatterns.ToDictionary(pattern => pattern.FinanceId);
        var goalShortfalls = CalculateGoalShortfalls(allEarmarks, patternsById, allManualEarmarks);

        // Today's jar for each bill or goal, from whichever account holds it; CalculatePlanHealthStates reads today's pace from these.
        var jarsByFinanceId = accountForecasts
            .SelectMany(account => account.Page.InitialSnapshot.FundJars)
            .Where(jar => jar.FinanceId is not null)
            .ToDictionary(jar => jar.FinanceId!.Value);

        // The account page holding each savings plan, so IsWorthWarningAbout can read its jar and free funds on any future date — even for a goal whose jar isn't open today.
        var pageByFinanceId = accountForecasts
            .SelectMany(account => account.Page.EarmarkPatterns
                .Select(plan => plan.FinanceId)
                .Distinct()
                .Select(financeId => (FinanceId: financeId, account.Page)))
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
            Household = BuildHouseholdSummary(accountForecasts),
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
            // Adds the day's contributions before resetting on a release, the same order BuildAccountPage uses, so a contribution on its own release day washes out with it.
            running += contributionsByDate[date].Sum();
            running = releaseDates.Contains(date) ? 0m : running;
            points.Add((date, running));
        }

        return points;
    }

    /// <summary>[CALC] Walks a finance id's projected jar ExpectedAmount day by day across a date range — the Earmark form's Summary chart's "proposed" line. Unlike ComputeMilestoneTrajectory, a release does NOT reset to 0: it subtracts the goal's own per-occurrence payout and floors at 0, so a starting balance and any structural glut carry forward past the first occurrence, exactly as a real forecast's fund jar does (see A_structural_glut_accumulates_across_releases_instead_of_being_reset). A deliberately rough single-jar estimate: it pays out each release in full rather than modelling cross-jar deallocation, the same simplification ComputeMilestoneTrajectory makes. Always emits a point at `from` seeded with startingAllocation, so a starting balance is visible at the chart's left edge even before the first contribution lands.</summary>
    /// <param name="patterns">The savings plan(s) funding the goal.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="from">Start of the range to walk — the point seeded with startingAllocation.</param>
    /// <param name="to">End of the range to walk.</param>
    /// <param name="startingAllocation">What the jar already holds at `from`, before this plan's own contributions begin.</param>
    public static IReadOnlyList<(DateOnly Date, decimal ExpectedAmount)> ComputeExpectedTrajectory(
        IReadOnlyList<EarMarkPattern> patterns, FinancialPattern goal, DateOnly from, DateOnly to, decimal startingAllocation = 0m)
    {
        var contributionsByDate = patterns
            .SelectMany(pattern => pattern.DatePattern.GetOccurrences(from, to).Select(date => (Date: date, Amount: -pattern.Amount)))
            .ToLookup(entry => entry.Date, entry => entry.Amount);
        var releaseAmount = Math.Abs(goal.Amount);
        var releaseDates = goal.DatePattern.GetOccurrences(from, to).ToHashSet();

        var eventDates = contributionsByDate.Select(group => group.Key).Concat(releaseDates).Distinct().OrderBy(date => date);

        // The opening point anchors the line at `from` at whatever the jar
        // already holds — so a starting balance shows at the chart's left
        // edge, not only from the first contribution onward.
        var points = new List<(DateOnly Date, decimal ExpectedAmount)> { (from, startingAllocation) };
        var running = startingAllocation;
        foreach (var date in eventDates)
        {
            // Adds the day's contributions before paying out a release, the same order BuildAccountPage uses, so a contribution on its own release day nets against it.
            running += contributionsByDate[date].Sum();
            if (releaseDates.Contains(date))
            {
                running = Math.Max(0m, running - releaseAmount);
            }

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
                - pattern.Amount * pattern.DatePattern.GetOccurrences(pattern.DatePattern.ActiveStart, firstOccurrence).Count)
            + manualEarmarks
                .Where(manual => manual.FinanceId == goal.FinanceId && manual.Date <= firstOccurrence)
                .Sum(manual => manual.Amount);

        return Math.Max(0m, Math.Abs(goal.Amount) - accumulated);
    }

    /// <summary>[CALC] Returns a pattern's first occurrence, or null if it has none.</summary>
    /// <param name="pattern">The pattern to search.</param>
    private static DateOnly? FirstOccurrence(RecurrenceRule pattern)
    {
        var occurrences = pattern.GetOccurrences();
        return occurrences.Count > 0 ? occurrences[0] : null;
    }

    /// <summary>[CALC] Returns the per-account breakdown when given; otherwise one "Primary" account synthesized from the flat fields.</summary>
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

    /// <summary>[CALC] Builds the household summary: on each date any account has an event, sums every account's free/set-aside as of that date (its latest snapshot on or before it) — flagging any account whose own free went negative, since a positive combined total can hide a locally-short account.</summary>
    /// <param name="accounts">Every account's own forecast.</param>
    private static HouseholdSummary BuildHouseholdSummary(IReadOnlyList<AccountForecast> accounts)
    {
        var asOfFree = accounts.Sum(account => SampleAsOf(account.Page, account.Page.StartDate).Free);

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
                var sample = SampleAsOf(account.Page, date);

                free += sample.Free;
                setAside += sample.SetAside;

                // "Short" is a per-account test on the account's own free.
                if (sample.Free < 0m)
                {
                    shortAccounts.Add(account.Name);
                }

                // The account's cushion is below its target.
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
    /// <returns>Free (spendable), SetAside (the reserved portion, expected minus free), and Cushion (the null-id jar's amount).</returns>
    private static (decimal Free, decimal SetAside, decimal Cushion) SampleAsOf(
        AccountTransactionPage page,
        DateOnly date)
    {
        var snapshot = SnapshotAsOf(page, date);

        var free = snapshot.ExpectedFreeAmount ?? 0m;
        var expected = snapshot.ExpectedAmount ?? 0m;

        var cushion = 0m;
        foreach (var jar in snapshot.FundJars)
        {
            if (jar.FinanceId is null)
            {
                cushion = jar.ExpectedAmount;
            }
        }

        return (free, expected - free, cushion);
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
        // An expense with an EarMarkPattern is paid from its jar; one without comes straight out of free funds on its due date.
        var earmarkedIds = input.EarMarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

        // === AdjustSnapshots role: materialize the window's events ===

        // Creates an ExpectedTransaction for each FinancialPattern occurrence in the window and files it under its date in expectedByDate.
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

        // Creates a repeated earmark event for each EarMarkPattern occurrence in the window and files it under its date in earmarkEventsByDate.
        // The amount is flipped because EarMarkPattern.Amount is stored negative (its effect on free funds).
        foreach (var earmark in input.EarMarkPatterns)
        {
            foreach (var date in earmark.DatePattern.GetOccurrences(asOfDate, horizonEndDate))
            {
                MergeOrAppendRepeatedEarmark(GetOrAdd(earmarkEventsByDate, date), earmark.FinanceId, -earmark.Amount, date);
            }
        }

        // Creates an isolated earmark event for each manual earmark in the window and files it under its date in earmarkEventsByDate.
        // One on the as-of day is already in the starting jars; it's added here only so that day displays it (the as-of day applies no changes).
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

        // ASSUMED-PAIRING(3.13c.a10): a bill or goal's own occurrence releases the money its jar was holding, or it would be counted twice against its ExpectedTransaction.
        // The documented model releases it when a paired actual transaction lands; here the expected occurrence triggers it, per day in the cascade below (AppendDeallocationOrGoalReleases).

        // Snapshot dates: exactly the dates with at least one event
        // (adjust_snapshots / 3.13.a3). The as-of day itself is represented
        // by the dateless initial snapshot unless events land on it.
        var snapshotDates = new SortedSet<DateOnly>();
        snapshotDates.UnionWith(expectedByDate.Keys);
        snapshotDates.UnionWith(earmarkEventsByDate.Keys);

        // === Initial snapshot: the manually-seeded starting point ===

        // Each open jar's running balance, and its milestone for the current cycle, keyed by finance id; the cascade below updates both day by day.
        var jarValues = new Dictionary<int, decimal>();
        var milestones = new Dictionary<int, decimal>();

        var startingAllocationApplied = new HashSet<EarMarkPattern>(ReferenceEqualityComparer.Instance);

        // Sets each open jar's starting balance in jarValues and its milestone in milestones, summing every plan that funds it (e.g. a Restructure predecessor and successor).
        foreach (var group in input.EarMarkPatterns.ToLookup(earmark => earmark.FinanceId))
        {
            var financeId = group.Key;
            var goal = patternsById[financeId];

            // The bill or goal hasn't started yet, or has already ended: its jar isn't open on the as-of date.
            if (!goal.DatePattern.ActiveSpanContains(asOfDate))
            {
                continue;
            }

            var plans = group.ToList();
            var startedPlans = plans.Where(plan => plan.DatePattern.ActiveStart <= asOfDate).ToList();
            startingAllocationApplied.UnionWith(startedPlans);
            var contributed = startedPlans.Sum(earmark =>
                    earmark.StartingAllocation
                    - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.ActiveStart, asOfDate).Count)
                // Manual adjustments already made on/before the as-of date
                // are part of the jar's settled history — dated
                // StartingAllocation, effectively.
                + input.ManualEarmarks
                    .Where(manual => manual.FinanceId == financeId && manual.Date <= asOfDate)
                    .Sum(manual => manual.Amount);
            var withdrawn = Math.Abs(goal.Amount) * goal.DatePattern.GetOccurrences(goal.DatePattern.ActiveStart, asOfDate).Count;
            jarValues[financeId] = Math.Max(0m, contributed - withdrawn);

            // 3.13.5.4.a1, reset-at-release: the milestone counts only contributions since the goal's last release, so an on-time repeating bill doesn't read further behind every cycle.
            // Uses ComputeMilestoneTrajectory's walk, because lifetime contributed minus lifetime withdrawn drifts whenever a goal's plans don't add up to its amount.
            var earliestActiveStart = plans.Min(plan => plan.DatePattern.ActiveStart);
            var milestoneTrajectory = ComputeMilestoneTrajectory(plans, goal, earliestActiveStart, asOfDate);
            milestones[financeId] = milestoneTrajectory.Count > 0 ? milestoneTrajectory[^1].MilestoneAmount : 0m;
        }

        // The safety cushion (finance_id = null) can't live in jarValues, whose keys are non-nullable ints, so it's tracked here and starts full.
        var cushionTarget = input.IdealSafetyCushion;
        var cushionValue = cushionTarget;

        // ASSUMED-PAIRING(unpaid-expected): see AccountTransactionPage.
        var currentUnpaidExpected = 0m;

        var initialSnapshot = new BalanceSnapshot
        {
            SnapshotDate = null,
            FullAmount = input.StartingBalance,
            ExpectedAmount = input.StartingBalance,
            ExpectedFreeAmount = input.StartingBalance - currentUnpaidExpected - jarValues.Values.Sum() - cushionValue,
            ExpectedUnpaidAmount = Math.Max(0m, currentUnpaidExpected - input.StartingBalance),
            FundJars = BuildJars(jarValues, milestones, cushionValue, currentIsKnown: true),
            ActualTransactions = [],
            ExpectedTransactions = [],
            EarMarkEvents = [],
        };
        EnsureOneCushionJar(null, initialSnapshot.FundJars);
        EnsureJarsOnlyWithinTheirFinancePattern(asOfDate, initialSnapshot.FundJars, patternsById);

        // === CascadePageBalanceRecord role: the forward value pass ===

        var balanceRecord = new SortedDictionary<DateOnly, BalanceSnapshot>();
        var previousExpected = initialSnapshot.ExpectedAmount!.Value;
        DateOnly? firstNegativeDate = initialSnapshot.ExpectedFreeAmount < 0m ? asOfDate : null;
        var flooredManualEarmarks = new List<(DateOnly Date, int FinanceId)>();
        var underfundedReleases = new List<(DateOnly Date, int FinanceId)>();

        // Builds a BalanceSnapshot for each snapshot date from the previous day's values and adds it to balanceRecord.
        foreach (var date in snapshotDates)
        {
            var expectedTransactions = expectedByDate.GetValueOrDefault(date) ?? [];
            var earMarkEvents = earmarkEventsByDate.GetValueOrDefault(date) ?? [];

            // ASSUMED-PAIRING(as-of-day-settled): the entered balance already includes the as-of day's transactions.
            // A snapshot on that date shows its events but changes nothing; its values equal the initial snapshot's.
            var isPageStartDate = date == asOfDate;

            // 10.3: previous snapshot's amount + today's expected
            // transactions.
            var expected = previousExpected
                + (isPageStartDate ? 0m : expectedTransactions.Where(t => !t.Cancelled).Sum(t => t.ExpectedAmount));

            var isDeallocationDay = false;
            if (!isPageStartDate)
            {
                // DIVERGENCE(jar-lifetime): must run before AppendDeallocationOrGoalReleases and today's earmark events, so a closed jar is never drained and a newly opened one can take today's contributions.
                OpenAndCloseJars(date, jarValues, milestones, input.EarMarkPatterns, patternsById, startingAllocationApplied);

                // Adds an isolated earmark that tops the cushion back up to its target (none when it's already full).
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

                // Must run before AppendDeallocationOrGoalReleases: 3.13c.8.a5 checks the day's scheduled earmarks before releases and give-backs merge in, and a merge may legitimately land on 0.
                EnsureNoZeroIsolatedEarmark(date, earMarkEvents);

                var releaseResult = AppendDeallocationOrGoalReleases(
                    earMarkEvents, date, previousExpected, jarValues, cushionValue,
                    expectedTransactions, patternsById, earmarkedIds);
                isDeallocationDay = releaseResult.IsDeallocationDay;

                // 3.13.5.3.a1: adds each of today's earmark events to its jar in jarValues (or to cushionValue), flooring at 0.
                // This is the only place jar and cushion balances change; releases and give-backs arrive here as ordinary earmark events.
                foreach (var earMarkEvent in earMarkEvents)
                {
                    if (earMarkEvent.FinanceId is not { } financeId)
                    {
                        // The finance_id = null safety cushion (fill or give-back).
                        cushionValue = Math.Max(0m, cushionValue + earMarkEvent.ExpectedAmount);
                        continue;
                    }

                    var unfloored = jarValues[financeId] + earMarkEvent.ExpectedAmount;

                    // Records a manual withdrawal that asked for more than the jar held, so the UI can flag it.
                    // Judged on ExplicitAmount alone, since a release or give-back may be merged into the same event.
                    if (earMarkEvent.ExplicitAmount is { } explicitAmount && explicitAmount < 0m
                        && jarValues[financeId] + explicitAmount < 0m)
                    {
                        flooredManualEarmarks.Add((date, financeId));
                    }

                    // Records this bill or goal's own release coming up short, including any manual earmark merged into it.
                    // Checks ReleasedFinanceIds so a jar a deallocation day drained isn't counted.
                    if (unfloored < 0m && !earMarkEvent.RepeatedEarmark
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

                // ASSUMED-PAIRING(3.13c.a10): sets the milestone back to 0 for each bill or goal that occurred today.
                // Must run after the loop above, so a same-day final contribution washes out with the release.
                foreach (var releasedFinanceId in releaseResult.ReleasedFinanceIds)
                {
                    milestones[releasedFinanceId] = 0m;
                }
            }

            // 3.13.4.a1: free = expected minus everything sitting in jars,
            // including the safety cushion, and minus bills still owed from before the as-of date (1.2.3.5.a1).
            var expectedFree = expected - currentUnpaidExpected - jarValues.Values.Sum() - cushionValue;

            var fundJars = BuildJars(jarValues, milestones, cushionValue, currentIsKnown: false);
            EnsureJarForEveryActivePlan(date, fundJars, input.EarMarkPatterns);
            EnsureJarsOnlyWithinTheirFinancePattern(date, fundJars, patternsById);
            EnsureOneCushionJar(date, fundJars);
            EnsureOneRepeatedEarmarkPerJar(date, earMarkEvents);
            EnsureOneIsolatedEarmarkPerJar(date, earMarkEvents);

            balanceRecord[date] = new BalanceSnapshot
            {
                SnapshotDate = date,
                FullAmount = null, // 10.2: unknowable until the day occurs
                ExpectedAmount = expected,
                ExpectedFreeAmount = expectedFree,
                ExpectedUnpaidAmount = Math.Max(0m, currentUnpaidExpected - expected),
                FundJars = fundJars,
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
            CurrentUnpaidExpected = currentUnpaidExpected,
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

    /// <summary>[CALC] The "can I afford X?" engine: on a deallocation day drains the lowest-priority jars first to cap allocation at available funds; otherwise releases each goal's jar on its own occurrence. Either way, appends the result to the day's earMarkEvents (mutated in place) — deallocation never rewrites jar values directly.</summary>
    /// <remarks>DIVERGENCE(release-outside-plan): 3.13.8.a2 keeps an earmark inside its earmark pattern's dates, but a savings plan can end before its bill or goal is due. The isolated earmarks made here (releases and deallocation give-backs) may fall anywhere in the bill or goal's active span instead. User-entered manual earmarks still stay inside the plan's own dates (ManualEarmark.Create).</remarks>
    /// <param name="earMarkEvents">The day's already-scheduled earmark events; the day's release/deallocation events are appended here.</param>
    /// <param name="date">The day being processed.</param>
    /// <param name="previousExpected">Yesterday's ExpectedAmount (c).</param>
    /// <param name="jarValues">Every jar's previous-day balance (f), keyed by finance id.</param>
    /// <param name="cushionValue">The cushion's previous-day balance.</param>
    /// <param name="expectedTransactions">Today's expected transactions.</param>
    /// <param name="patternsById">Every financial pattern, keyed by finance id.</param>
    /// <param name="earmarkedIds">Finance ids with a savings plan — their own occurrence today is a paired transaction (ap), not an unpaired one.</param>
    /// <returns>Whether this was a deallocation day (surfaced onto the snapshot for the UI drain highlight), plus which finance ids had their own occurrence today, so the caller can reset their milestone pacing regardless of which branch ran. This is not every jar that gave money back: a deallocation day can drain jars whose occurrence isn't today, and their pacing must not reset.</returns>
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
        // Adds each of today's expected transactions to pairedTransactions (ap) if it has a savings plan, otherwise to the unpaired total (au).
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

        // Adds each of today's scheduled earmarks (er + ei) to its jar's total in existingByJar, or to cushionExisting for the cushion.
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

        // Creates a DeallocationJar for the cushion first (priority 0, drained before every real jar), then adds one for each real jar.
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
                // Bills the user has to pay are drained only after every skippable jar.
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

            // Adds each deallocation earmark to today's earMarkEvents.
            foreach (var (financeId, amount) in deallocation.EarmarkEvents)
            {
                MergeOrAppendIsolatedEarmark(earMarkEvents, financeId, amount, date);
            }
        }
        else
        {
            // Not a deallocation day: adds a release to today's earMarkEvents for each bill or goal occurring today, taking its amount out of its jar.
            foreach (var paired in pairedTransactions)
            {
                MergeOrAppendIsolatedEarmark(earMarkEvents, paired.FinanceId, -Math.Abs(paired.Amount), date);
            }
        }

        return (isDeallocationDay, pairedTransactions.Select(paired => paired.FinanceId).ToList());
    }

    /// <summary>[CALC] Merges an amount into an existing repeated earmark event for the same jar/day if one exists, otherwise appends a new one — two EarMarkPatterns sharing a finance id (concurrent earmark patterns) can generate an occurrence on the same day, and only one repeated earmark per finance id per day may exist (3.13.8.1.a2). A repeated and an isolated earmark for the same jar/day still coexist as two separate events.</summary>
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

    /// <summary>[CALC] Stops the forecast if a savings plan is active on this day but the day has no fund jar for its goal — a guard against the forecast's jar-building and its plans drifting apart.</summary>
    /// <param name="date">The snapshot's day.</param>
    /// <param name="fundJars">The fund jars that day's snapshot will hold.</param>
    /// <param name="earMarkPatterns">Every savings plan in the account.</param>
    internal static void EnsureJarForEveryActivePlan(DateOnly date, IReadOnlyList<FundJar> fundJars, IReadOnlyList<EarMarkPattern> earMarkPatterns)
    {
        var jarIds = fundJars.Select(jar => jar.FinanceId).ToHashSet();
        foreach (var plan in earMarkPatterns)
        {
            if (plan.DatePattern.ActiveSpanContains(date) && !jarIds.Contains(plan.FinanceId))
            {
                throw new FundJarMissingException(
                    $"Finance id {plan.FinanceId} has a savings plan active on {date:MMM d, yyyy} but no fund jar that day.",
                    nameof(fundJars));
            }
        }
    }

    /// <summary>[CALC] Closes the jars of bills and goals that have ended (their leftover becomes free funds), opens jars for ones that have started, and adds each savings plan's starting balance once its plan has started.</summary>
    /// <remarks>DIVERGENCE(jar-lifetime): 3.13.5.a2 keeps a jar inside its earmark pattern's dates; here a jar lives for its bill or goal's whole active span instead (3.13.5.a6), so it can outlive its savings plan. A break-off successor gets its own new jar, funded by its plan's StartingAllocation.</remarks>
    /// <param name="date">The day being processed.</param>
    /// <param name="jarValues">Every open jar's balance, keyed by finance id; jars are added and removed in place.</param>
    /// <param name="milestones">Every open jar's milestone accrual, kept in step with jarValues.</param>
    /// <param name="earMarkPatterns">Every savings plan in the account.</param>
    /// <param name="patternsById">Every financial pattern, keyed by finance id.</param>
    /// <param name="startingAllocationApplied">The plans whose starting balance is already in their jar; updated in place.</param>
    private static void OpenAndCloseJars(
        DateOnly date,
        Dictionary<int, decimal> jarValues,
        Dictionary<int, decimal> milestones,
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyDictionary<int, FinancialPattern> patternsById,
        HashSet<EarMarkPattern> startingAllocationApplied)
    {
        foreach (var financeId in jarValues.Keys.Where(id => patternsById[id].DatePattern.Until < date).ToList())
        {
            jarValues.Remove(financeId);
            milestones.Remove(financeId);
        }

        foreach (var plan in earMarkPatterns)
        {
            if (!patternsById[plan.FinanceId].DatePattern.ActiveSpanContains(date))
            {
                continue;
            }

            if (!jarValues.ContainsKey(plan.FinanceId))
            {
                jarValues[plan.FinanceId] = 0m;
                milestones[plan.FinanceId] = 0m;
            }

            if (plan.DatePattern.ActiveStart <= date && startingAllocationApplied.Add(plan))
            {
                jarValues[plan.FinanceId] += plan.StartingAllocation;
            }
        }
    }

    /// <summary>[CALC] Stops the forecast if a snapshot holds a jar for a bill or goal whose active span doesn't cover that day.</summary>
    /// <param name="date">The snapshot's day (the as-of date for the starting snapshot).</param>
    /// <param name="fundJars">The fund jars that snapshot will hold.</param>
    /// <param name="patternsById">Every financial pattern, keyed by finance id.</param>
    internal static void EnsureJarsOnlyWithinTheirFinancePattern(DateOnly date, IReadOnlyList<FundJar> fundJars, IReadOnlyDictionary<int, FinancialPattern> patternsById)
    {
        foreach (var jar in fundJars)
        {
            if (jar.FinanceId is { } financeId && !patternsById[financeId].DatePattern.ActiveSpanContains(date))
            {
                throw new FundJarOutsideFinancePatternException(
                    $"Finance id {financeId} has a fund jar on {date:MMM d, yyyy}, outside its bill or goal's active dates.",
                    nameof(fundJars));
            }
        }
    }

    /// <summary>[CALC] Stops the forecast if two bills, paychecks, or goals across all accounts share one finance id.</summary>
    /// <param name="patterns">Every financial pattern going into the forecast.</param>
    internal static void EnsureUniqueFinanceIds(IEnumerable<FinancialPattern> patterns)
    {
        var seen = new HashSet<int>();
        foreach (var pattern in patterns)
        {
            if (!seen.Add(pattern.FinanceId))
            {
                throw new DuplicateFinanceIdException(
                    $"More than one bill, paycheck, or goal has finance id {pattern.FinanceId}.", nameof(patterns));
            }
        }
    }

    /// <summary>[CALC] Stops the forecast if a day's one-off earmarks include a $0 one, before that day's releases and deallocation are merged in.</summary>
    /// <param name="date">The day being processed.</param>
    /// <param name="earMarkEvents">That day's earmark events so far.</param>
    internal static void EnsureNoZeroIsolatedEarmark(DateOnly date, IReadOnlyList<EarMarkEvent> earMarkEvents)
    {
        if (earMarkEvents.FirstOrDefault(e => !e.RepeatedEarmark && e.ExpectedAmount == 0m) is { } zero)
        {
            throw new ZeroIsolatedEarmarkException(
                $"{(zero.FinanceId is { } id ? $"Finance id {id}" : "The safety cushion")} has a $0 isolated earmark on {date:MMM d, yyyy}.",
                nameof(earMarkEvents));
        }
    }

    /// <summary>[CALC] Stops the forecast if a snapshot doesn't hold exactly one safety cushion jar.</summary>
    /// <param name="date">The snapshot's day; null for the starting snapshot.</param>
    /// <param name="fundJars">The fund jars that snapshot will hold.</param>
    internal static void EnsureOneCushionJar(DateOnly? date, IReadOnlyList<FundJar> fundJars)
    {
        var cushionJars = fundJars.Count(jar => jar.FinanceId is null);
        if (cushionJars != 1)
        {
            throw new CushionJarCountException(
                $"The snapshot for {(date is { } day ? day.ToString("MMM d, yyyy") : "the starting balance")} has {cushionJars} safety cushion jars instead of one.",
                nameof(fundJars));
        }
    }

    /// <summary>[CALC] Stops the forecast if a day has two scheduled savings-plan contributions to the same jar instead of one merged contribution.</summary>
    /// <param name="date">The snapshot's day.</param>
    /// <param name="earMarkEvents">That day's earmark events.</param>
    internal static void EnsureOneRepeatedEarmarkPerJar(DateOnly date, IReadOnlyList<EarMarkEvent> earMarkEvents)
    {
        var seen = new HashSet<int?>();
        foreach (var earMarkEvent in earMarkEvents)
        {
            if (earMarkEvent.RepeatedEarmark && !seen.Add(earMarkEvent.FinanceId))
            {
                throw new DuplicateRepeatedEarmarkException(
                    $"Finance id {earMarkEvent.FinanceId} has more than one repeated earmark on {date:MMM d, yyyy}.",
                    nameof(earMarkEvents));
            }
        }
    }

    /// <summary>[CALC] Stops the forecast if a day has two one-off earmarks for the same jar (the safety cushion included) instead of one merged earmark.</summary>
    /// <param name="date">The snapshot's day.</param>
    /// <param name="earMarkEvents">That day's earmark events.</param>
    internal static void EnsureOneIsolatedEarmarkPerJar(DateOnly date, IReadOnlyList<EarMarkEvent> earMarkEvents)
    {
        var seen = new HashSet<int?>();
        foreach (var earMarkEvent in earMarkEvents)
        {
            if (!earMarkEvent.RepeatedEarmark && !seen.Add(earMarkEvent.FinanceId))
            {
                throw new DuplicateIsolatedEarmarkException(
                    $"{(earMarkEvent.FinanceId is { } id ? $"Finance id {id}" : "The safety cushion")} has more than one isolated earmark on {date:MMM d, yyyy}.",
                    nameof(earMarkEvents));
            }
        }
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
                // null milestone = no savings plan drives this jar — nothing to
                // be "behind" on.
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

    /// <summary>[CALC] Computes each goal's shortfall: whether its Allocation Plan will put in enough by its due date. Always includes every expense that has a plan (ShortfallAmount is 0 when fully on track), evaluated independently of any display horizon, so a UI can render one row per plan without a separate lookup. The same formula serves a one-time goal and a repeating bill — the need scales by occurrence count, and "allocated" counts isolated earmarks (manual adjustments, the starting earmark) as well as the plan's own contributions. Gross-vs-gross: can't detect a plan that back-loads its contributions within the span, though the proposer never actually produces that shape.</summary>
    /// <param name="earMarkPatterns">Every savings plan.</param>
    /// <param name="goalsByFinanceId">Every goal/bill pattern, keyed by finance id.</param>
    /// <param name="manualEarmarks">Every manual earmark.</param>
    private static IReadOnlyList<GoalShortfall> CalculateGoalShortfalls(
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyDictionary<int, FinancialPattern> goalsByFinanceId,
        IReadOnlyList<ManualEarmark> manualEarmarks)
    {
        var shortfalls = new List<GoalShortfall>();

        // Adds a GoalShortfall to shortfalls for each goal, summing every plan that funds it.
        foreach (var group in earMarkPatterns.ToLookup(earmark => earmark.FinanceId))
        {
            var financeId = group.Key;
            var goal = goalsByFinanceId[financeId];
            var dueDate = goal.DatePattern.Until;

            var occurrenceCount = goal.DatePattern
                .GetOccurrences(goal.DatePattern.ActiveStart, goal.DatePattern.Until).Count;
            var amountNeeded = Math.Abs(goal.Amount) * occurrenceCount;

            // StartingAllocation (the entered opening balance) + every plan's
            // repeated contributions to date + any isolated earmarks dated on or
            // before the due date.
            var allocated = group.Sum(earmark =>
                    earmark.StartingAllocation
                    - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.ActiveStart, dueDate).Count)
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

    // DetermineIsWorthWarningAbout's thresholds.
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
            // DIVERGENCE(jar-lifetime): a goal that hasn't started yet, or has already ended, has no jar today; it reads as an empty one.
            var jar = jarsByFinanceId.GetValueOrDefault(financeId)
                ?? new FundJar { FinanceId = financeId, CurrentAmount = 0m, ExpectedAmount = 0m, MilestoneAmount = 0m };

            // How far behind or ahead of its milestone the jar is today; at most one is non-zero.
            var currentShortfall = Math.Max(0m, (jar.MilestoneAmount ?? 0m) - jar.ExpectedAmount);
            var currentOverfunded = Math.Max(0m, jar.ExpectedAmount - (jar.MilestoneAmount ?? 0m));

            // IsChronicShortfall: would the plan's scheduled contributions alone, run to the due date, fall short of AmountNeeded?
            // Leaves out StartingAllocation and manual earmarks: those are the one-time fixes this checks whether the plan needs.
            var plannedTotal = earMarkPatterns
                .Where(earmark => earmark.FinanceId == financeId)
                .Sum(earmark => -earmark.Amount
                    * earmark.DatePattern.GetOccurrences(earmark.DatePattern.ActiveStart, shortfall.DueDate).Count);
            var isChronicShortfall = shortfall.ShortfallAmount > 0m && plannedTotal < shortfall.AmountNeeded;

            // IsChronicOverfund: would the plan's scheduled contributions alone put in more than AmountNeeded?
            var isChronicOverfund = shortfall.OverfundedAmount > 0m && plannedTotal > shortfall.AmountNeeded;

            // The first date this goal's own release came up short, or its due date if the forecast didn't reach one but the whole span is still short.
            var shortReleaseDates = releaseDatesByFinanceId[financeId];
            var projectedShortfallStartDate = shortReleaseDates.Any()
                ? shortReleaseDates.Min()
                : shortfall.ShortfallAmount > 0m ? shortfall.DueDate : (DateOnly?)null;

            var isWorthWarningAbout = DetermineIsWorthWarningAbout(
                goalsByFinanceId[financeId], shortfall, currentShortfall, currentOverfunded,
                pageByFinanceId[financeId], asOfDate,
                earMarkPatterns.Where(earmark => earmark.FinanceId == financeId).ToList(),
                shortReleaseDates);

            var goal = goalsByFinanceId[financeId];
            var isFirstOccurrencePending = IsFirstOccurrencePending(goal, asOfDate);
            var firstOccurrenceShortfall = FirstOccurrenceShortfall(
                earMarkPatterns.Where(earmark => earmark.FinanceId == financeId).ToList(), goal, manualEarmarks, asOfDate);

            // Free funds the day before the first payment, and that payment's date; set only while the first payment is still ahead.
            // Free funds tell "the money's there, just not set aside" apart from "genuinely short".
            decimal? firstOccurrenceFreeFunds = null;
            DateOnly? firstOccurrenceDate = null;
            if (isFirstOccurrencePending && FirstOccurrence(goal.DatePattern) is { } firstPaymentDate)
            {
                firstOccurrenceDate = firstPaymentDate;
                firstOccurrenceFreeFunds = pageByFinanceId[financeId].SnapshotAsOf(firstPaymentDate.AddDays(-1)).ExpectedFreeAmount;
            }

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
                FirstOccurrenceFreeFunds = firstOccurrenceFreeFunds,
                FirstOccurrenceDate = firstOccurrenceDate,
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
        // Already behind today: always worth a warning.
        if (currentShortfall > 0m)
        {
            return true;
        }

        var isRepeated = goal.DatePattern.GetOccurrences(goal.DatePattern.ActiveStart, goal.DatePattern.Until).Count > 1;
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

                // Non-repeated rules 3/4: due further off, so warn only if the shortfall is at least half the free funds on the due date.
                return shortfall.ShortfallAmount >= HalfOfFreeFunds(page, shortfall.DueDate);
            }

            var nextOccurrence = goal.DatePattern.GetOccurrences(asOfDate, goal.DatePattern.Until).FirstOrDefault();
            if (nextOccurrence == default)
            {
                return true; // no more occurrences to project against — default to the visible side
            }

            // Repeated rule 3: warn if any release in the next six months comes up short.
            // This also covers rule 1 (the next occurrence is soon and short).
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
                .Where(plan => plan.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, plan.DatePattern.Until).Count > 1)
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
