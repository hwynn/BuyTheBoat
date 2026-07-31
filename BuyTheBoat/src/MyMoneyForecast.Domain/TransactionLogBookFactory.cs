namespace MyMoneyForecast.Domain;

// Builds one forecast's TransactionLogBook from patterns + a seed balance —
// the original model's organization (Book -> Page -> AccountPage ->
// BalanceSnapshot -> FundJar/EarMarkEvent/ExpectedTransaction) computed as a
// stateless, in-memory pass, replacing the old flat ForecastCalculator.
//
// Multi-account (planning/10 item 4): each account is its own silo. CreateForecast
// runs the per-account cascade (BuildAccountPage) once per account, then rolls the
// results up into a household summary. The single-account path (no ForecastOptions
// .Accounts) synthesizes one "Primary" account from the flat fields, so its output
// — and every existing test's numbers — is unchanged.
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
        var accountInputs = ResolveAccountInputs(options);

        // One page per account, each its own independent cascade (item 4-A).
        var pages = new Dictionary<string, AccountTransactionPage>();
        var accountForecasts = new List<AccountForecast>();
        DateOnly? firstNegative = null;
        var floored = new List<(DateOnly Date, int FinanceId)>();

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
        }

        // Global roll-ups (jar labels, goal shortfalls) span every account —
        // finance ids are unique across the whole book, so the union is safe.
        var allPatterns = accountInputs.SelectMany(account => account.FinancialPatterns).ToList();
        var allEarmarks = accountInputs.SelectMany(account => account.EarMarkPatterns).ToList();
        var allManualEarmarks = accountInputs.SelectMany(account => account.ManualEarmarks).ToList();
        var patternsById = allPatterns.ToDictionary(pattern => pattern.FinanceId);

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
            GoalShortfalls = CalculateGoalShortfalls(allEarmarks, patternsById, allManualEarmarks),
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

    // The per-account breakdown when given; otherwise one "Primary" account from
    // the flat fields — the pre-multi-account behaviour, byte-for-byte.
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

    // The household roll-up (item 4-C). On each date any account has an event,
    // sample every account's free/set-aside as of that date (its latest snapshot
    // on or before it) and sum — flagging any account whose own free went
    // negative, since a positive household total can hide a locally-short account.
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

                // planning/14 item A-1: a transfer's withdrawal reserves in the
                // account it leaves, so that account's own free reflects money
                // already committed to going. Household-wide it is not spending
                // — the money is still in the household — so it is moved back
                // out of set-aside and into free here. Total is untouched
                // either way, which is why this is a reclassification and the
                // free + set-aside = total identity still holds.
                free += sample.Free + sample.TransferReserved;
                setAside += sample.SetAside - sample.TransferReserved;

                // "Short" stays a per-account test on the account's OWN free —
                // a transfer it cannot fund is a real problem for it, so the
                // household add-back deliberately does not soften this.
                if (sample.Free < 0m)
                {
                    shortAccounts.Add(account.Name);
                }

                // planning/14 item C: the buffer is not whole. Never fires for
                // an account with no cushion, since 0 can't sit below 0.
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

    // An account's state as of `date`: the latest snapshot on or before it, else
    // the initial snapshot. Set-aside is the reserved portion (expected minus
    // free); TransferReserved is the part of that sitting in transfer-withdrawal
    // jars; Cushion is the null-id jar's amount. BalanceRecord is sorted, so we
    // stop at the first later date.
    private static (decimal Free, decimal SetAside, decimal TransferReserved, decimal Cushion) SampleAsOf(
        AccountTransactionPage page,
        DateOnly date,
        IReadOnlySet<int> transferWithdrawalFinanceIds)
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
        IReadOnlyList<(DateOnly Date, int FinanceId)> Floored);

    // Builds ONE account's page — the per-account silo cascade. This is the body
    // the single-account engine used to be; everything here is scoped to this
    // account's own patterns, balance and cushion.
    private static AccountPageBuild BuildAccountPage(AccountForecastInput input, DateOnly asOfDate, DateOnly horizonEndDate)
    {
        var patternsById = input.FinancialPatterns.ToDictionary(pattern => pattern.FinanceId);
        var earmarkedIds = input.EarMarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

        // Stage-1 revision (planning/14 "Revision 2026-07-24"): the computed
        // A/B ramp is RETIRED. Every outflow that reserves against free funds
        // now does so through a real EarMarkPattern (its Allocation Plan),
        // created at pattern-creation time by AllocationPlanProposer and passed
        // in via input.EarMarkPatterns — so the engine treats a bill's plan
        // exactly like a goal's savings plan, and an outflow with no plan
        // simply reduces free funds on its due date (and reads short).

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
        // planning/17, item 8 (F27/F29): more than one EarMarkPattern may now
        // share a finance_id (a "Restructure" predecessor + successor), so
        // every plan funding a goal is summed here — one jar/milestone value
        // per finance_id, not one per plan. Each plan's own GetOccurrences
        // call is bounded by ITS OWN Until, so a truncated predecessor and
        // its successor never double-count the same day.
        foreach (var group in input.EarMarkPatterns.ToLookup(earmark => earmark.FinanceId))
        {
            var financeId = group.Key;
            var goal = patternsById[financeId];
            var contributed = group.Sum(earmark =>
                    earmark.StartingAllocation
                    - earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, asOfDate).Count)
                // Manual adjustments already made on/before the as-of date are
                // part of the jar's settled history (planning/09) — dated
                // StartingAllocation, effectively.
                + input.ManualEarmarks
                    .Where(manual => manual.FinanceId == financeId && manual.Date <= asOfDate)
                    .Sum(manual => manual.Amount);
            var withdrawn = Math.Abs(goal.Amount) * goal.DatePattern.GetOccurrences(goal.DatePattern.Start, asOfDate).Count;
            jarValues[financeId] = Math.Max(0m, contributed - withdrawn);

            // 3.13.5.4.a1: milestone counts scheduled contributions only —
            // StartingAllocation is money already saved, not target.
            milestones[financeId] = group.Sum(earmark =>
                -earmark.Amount * earmark.DatePattern.GetOccurrences(earmark.DatePattern.Start, asOfDate).Count);
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
                isDeallocationDay = AppendDeallocationOrGoalReleases(
                    earMarkEvents, date, previousExpected, jarValues, cushionValue,
                    expectedTransactions, patternsById, earmarkedIds);

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
                    // flag it in place (planning/09). System-only events never
                    // over-pull (deallocation's W = Max(-Fa, N) is bounded), so
                    // this only fires on manual withdrawals.
                    if (unfloored < 0m && earMarkEvent.ExplicitAmount is < 0m)
                    {
                        flooredManualEarmarks.Add((date, financeId));
                    }

                    jarValues[financeId] = Math.Max(0m, unfloored);

                    // 3.13.5.4.a1: milestone accumulates repeated
                    // (pattern-scheduled) contributions only.
                    if (earMarkEvent.RepeatedEarmark)
                    {
                        milestones[financeId] += earMarkEvent.ExpectedAmount;
                    }
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

        return new AccountPageBuild(accountPage, firstNegativeDate, flooredManualEarmarks);
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

    // The Q2 engine (07 Step 2): on a deallocation day drain the lowest-priority
    // jars first to cap allocation at available funds; otherwise release each
    // goal's jar on its own occurrence. Either way the result is appended to the
    // day's earMarkEvents (mutated in place) and applied by the caller's floor
    // loop — deallocation never rewrites jar values directly.
    //
    // Proof-term mapping (06): c = previousExpected; f = each jar's PREVIOUS-day
    // balance (jarValues, not yet updated for today); ap = today's goal
    // occurrences (finance id has an EarMarkPattern); au = every other expected
    // transaction; er + ei = the day's already-scheduled earmark events.
    // Returns true if this was a deallocation day (spending overdrew free funds
    // and jars were drained) — surfaced onto the snapshot for the UI drain
    // highlight and the "Deallocation" event label.
    private static bool AppendDeallocationOrGoalReleases(
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
                // planning/14 item B: Mandatory now means "the user has to pay
                // this", and its only job is protecting the jar — everything
                // skippable is drained before anything unskippable is touched.
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

        return isDeallocationDay;
    }

    // planning/17, item 9 (F30): two EarMarkPatterns sharing a finance_id
    // (concurrent funders, e.g. two household partners each funding the same
    // goal) can generate an occurrence on the same day — merged into one
    // event, summing the amounts, so 3.13.8.1.a2 ("only one repeated earmark
    // with finance_id x can exist on a single day") holds literally. The
    // same treatment MergeOrAppendIsolatedEarmark already gives isolated
    // earmarks on a collision; a repeated and an isolated earmark for the
    // same jar/day still coexist as two separate events (unchanged).
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

    // A deallocation give-back merges into any ISOLATED earmark the jar already
    // carries that day (an automatic funding delta), preserving "one
    // isolated earmark per finance id per day" and avoiding a duplicate
    // detail-pane row; otherwise it is appended. Repeated earmarks are left
    // alone — an isolated and a repeated earmark for the same jar/day coexist.
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

    // Jar order is stable (insertion order of jarValues: goals, then auto
    // bills), with the safety cushion appended last. Exactly one null-id jar
    // per snapshot (9.5.a1), carrying the running cushion value.
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

    // ~3.13.5.4.a1: milestone vs. saved — but "saved" and "milestone" are
    // structurally identical without real transactions, so the only way an
    // outflow can look short is if its Allocation Plan doesn't put in enough by
    // its due date. Evaluated independently of any display horizon, and always
    // includes every outflow that has a plan (ShortfallAmount is 0 when fully on
    // track) so a UI can render one row per plan without a separate lookup.
    //
    // F21 (planning/14 revision): the SAME formula serves a one-time goal and a
    // repeating bill. The need scales by occurrence count — a repeating bill
    // must have its whole stream covered, not one occurrence (a one-time goal
    // has exactly one, so it is unchanged there). And "allocated" now counts
    // isolated earmarks (manual adjustments and the starting earmark) as well as
    // the plan's own contributions. Gross-vs-gross: it can't see a plan that
    // back-loads its contributions within the span; the proposer never produces
    // that shape.
    private static IReadOnlyList<GoalShortfall> CalculateGoalShortfalls(
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyDictionary<int, FinancialPattern> goalsByFinanceId,
        IReadOnlyList<ManualEarmark> manualEarmarks)
    {
        var shortfalls = new List<GoalShortfall>();

        // planning/17, item 8 (F27/F29): one row per GOAL, not one per plan —
        // more than one EarMarkPattern may now share a finance_id (a
        // "Restructure" predecessor + successor), and every plan funding a
        // goal must be summed into its one shortfall row.
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
}
