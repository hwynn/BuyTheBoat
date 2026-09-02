using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// Verifies the tier-selection table AffordabilityCeiling applies: which Frugality tier(s) and window(s) it
// picks from mandatory-ness, the same-Source chain's reach past the 3-month split, and suggestion vs
// implicit. The standard jars below make the four tiers separate by known amounts at free F —
// relaxed F, considerate F+10, thrifty F+20 (adds jar 3), miserly F+50 (adds jars 3 and 2) — so each
// branch's ceiling is a checkable number.
//
// How the App wires this (FinancePatternSaveConfirmation, exercised by FinancePatternSaveConfirmationTests
// with the cap inert under a generous balance, and bound once under a tight one in
// The_goal_health_suggestion_is_held_to_what_the_free_funds_can_afford). Every site sizes against the "room
// for these plans" basis — a re-forecast with the target's whole chain of plans omitted
// (TransactionLogBook.ChainFinanceIds + ForecastOptions.WithoutPlansFor, both tested):
//   - Goal-health suggestion → ScaleToMeetGoal, ChangeKind.Suggestion.
//   - Keep-separate funding correction (dry-run preview and real save) → ScaleToMeetGoal, ChangeKind.Implicit.
//   - Cross-boundary carry-forward cascade (ReconcileCascadedSuccessorSavingsPlans): an amount-only re-rate
//     → EarmarkScaling.Scale; a shape-change or combine fold → EarmarkConsolidation.Consolidate; both
//     ChangeKind.Implicit, keyed on the successor (which shares the edited chain's Source, so the same
//     AffordabilityCeilingFor omission covers it). Silent caps — no suggestion prompt.
//   - Starting (front-load) earmarks → AffordabilityCeiling.ForStartingEarmark, ChangeKind.Implicit:
//     AutoCreateAllocationPlan and the paycheck re-pace cascade.
//   - A proposed plan's ongoing per-cycle rate → AffordabilityCeiling.For (the range ceiling), ChangeKind.
//     Implicit: AutoCreateAllocationPlan and the paycheck re-pace cascade (OngoingRateCeilingFor).
//   - Break-off successor plans → the "Recommended" candidate (AllocationPlanProposer.Propose) and, for the
//     no-candidate cases (no existing plan, or a concurrent set), BreakOffFactory's own fallback via the two
//     SuccessorPlan ceilings on its requests — both ChangeKind.Suggestion, keyed on the edited chain (the
//     successor isn't saved yet, so it shares the predecessor as the tier/window proxy). The default
//     (unpicked) break-off reuses the capped Recommended candidate rather than an uncapped re-derivation.
// Every wrapper routes through one core, CeilingOmitting(omitFinanceId, target, changeKind, startingEarmark).
// The domain cap math is proven per-function (EarmarkScalingTests, EarmarkConsolidationTests,
// AllocationPlanProposerTests); the App wiring is bound under a tight balance in The_goal_health_suggestion...,
// A_carried_forward_re_rate..., The_break_off_recommended_candidate..., and A_break_off_with_no_candidate_picker....
// The links no test reaches are in MainWindow (AutoCreateAllocationPlan and ForecastOmitting, which builds the
// real ForecastOptions the re-forecasts run on) — those stay hand-verified.
public class AffordabilityCeilingTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly DateOnly Feb1 = new(2026, 2, 1); // inside AsOf + 3 months
    private static readonly DateOnly May1 = new(2026, 5, 1); // past AsOf + 3 months

    private static RecurrenceRule Monthly(DateOnly start, DateOnly until) => RecurrenceRule.Create(
        new RecurrenceRuleOptions { Frequency = RecurrenceFrequency.Monthly, DtStart = start, Until = until });

    private static FinancialPattern Pattern(int financeId, string source, int priority, bool mandatory, RecurrenceRule rule) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            DatePattern = rule,
            Amount = mandatory ? -100m : 100m,
            Priority = priority,
            Mandatory = mandatory,
        });

    // The patterns behind the standard snapshot jars: one discretionary (prio 3), one lower-priority-than-
    // the-target bill (prio 2), one higher-priority bill (prio 5). Distinct Sources so none joins the chain.
    private static readonly FinancialPattern DiscretionaryJarPattern = Pattern(3, "j3", 3, false, Monthly(AsOf, May1));
    private static readonly FinancialPattern LowerBillPattern = Pattern(2, "j2", 2, true, Monthly(AsOf, May1));
    private static readonly FinancialPattern HigherBillPattern = Pattern(5, "j5", 5, true, Monthly(AsOf, May1));

    private static FundJar Jar(int? financeId, decimal expected) => new()
    {
        FinanceId = financeId,
        CurrentAmount = null,
        ExpectedAmount = expected,
        MilestoneAmount = null,
    };

    private static readonly FundJar[] StandardJars =
        [Jar(null, 10m), Jar(3, 20m), Jar(2, 30m), Jar(5, 40m)];

    private static BalanceSnapshot Snapshot(DateOnly? date, decimal? free) => new()
    {
        SnapshotDate = date,
        FullAmount = null,
        ExpectedAmount = free,
        ExpectedFreeAmount = free,
        FundJars = StandardJars,
        ActualTransactions = [],
        ExpectedTransactions = [],
        EarMarkEvents = [],
    };

    // Two event days: Jan 1 (= AsOf) and May 1 (past the 3-month split). Target priority is 4, so jar 3
    // (non-mandatory) and jar 2 (mandatory, priority 2 < 4) are reachable by the bolder tiers; jar 5 is not.
    private static AccountTransactionPage Page(FinancialPattern target, decimal? freeJan, decimal? freeMay,
        params FinancialPattern[] extraChain) => new()
    {
        Account = "checking",
        StartDate = AsOf,
        EndDate = new DateOnly(2027, 1, 1),
        FinancePatterns = [target, DiscretionaryJarPattern, LowerBillPattern, HigherBillPattern, .. extraChain],
        EarmarkPatterns = [],
        InitialSnapshot = Snapshot(date: null, free: 0m),
        BalanceRecord = new SortedDictionary<DateOnly, BalanceSnapshot>
        {
            [AsOf] = Snapshot(AsOf, freeJan),
            [May1] = Snapshot(May1, freeMay),
        },
    };

    [Fact]
    public void Mandatory_plan_past_the_split_paces_short_bold_long_gentle_for_a_suggestion()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, May1)); // chain ends May 1 (past +3mo)

        // short [AsOf,+3mo] miserly = 1000+50 = 1050; long [+3mo,May1] thrifty = min(1020, 520) = 520;
        // overall min = 520.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBe(520m);
    }

    [Fact]
    public void Mandatory_plan_past_the_split_is_gentler_for_an_implicit_change()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, May1));

        // short thrifty 1020; long considerate = min(1010, 510) = 510; overall min = 510.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Implicit).ShouldBe(510m);
    }

    [Fact]
    public void Mandatory_plan_inside_the_split_uses_one_bold_tier_for_a_suggestion()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, Feb1)); // whole chain ends Feb 1 (inside +3mo)

        // miserly over [AsOf, Feb1] (only the January snapshot): 1000+50 = 1050.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBe(1050m);
    }

    [Fact]
    public void Mandatory_plan_inside_the_split_is_gentler_for_an_implicit_change()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, Feb1));

        // considerate over [AsOf, Feb1]: 1000+10 = 1010.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Implicit).ShouldBe(1010m);
    }

    [Fact]
    public void A_later_same_source_segment_pushes_a_short_target_into_the_past_the_split_rules()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, Feb1)); // on its own, ends inside 3 months
        var successor = Pattern(6, "target", 4, true, Monthly(new DateOnly(2026, 2, 2), May1)); // same Source, ends May 1

        // Chain end is now May 1, so it takes the past-the-split path — same 520 as the long-chain suggestion.
        AffordabilityCeiling.For(Page(target, 1000m, 500m, successor), target, AsOf, ChangeKind.Suggestion)
            .ShouldBe(520m);
    }

    [Fact]
    public void Non_mandatory_recurring_stays_relaxed_over_its_whole_span()
    {
        var target = Pattern(1, "target", 4, false, Monthly(AsOf, May1)); // recurring, not one-time

        // relaxed over [AsOf, May1] = min(1000, 500) = 500.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBe(500m);
    }

    [Fact]
    public void Non_mandatory_one_time_far_out_is_relaxed_now_and_considerate_later()
    {
        var target = Pattern(1, "target", 4, false, Monthly(May1, new DateOnly(2026, 5, 15))); // one occurrence, May 1 (past +3mo)

        // relaxed [AsOf,+3mo] = 1000; considerate [+3mo, May1] = min(1010, 510) = 510; overall min = 510.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBe(510m);
    }

    [Fact]
    public void Non_mandatory_one_time_within_the_split_stays_relaxed()
    {
        var target = Pattern(1, "target", 4, false, Monthly(Feb1, new DateOnly(2026, 2, 15))); // one occurrence, Feb 1 (inside +3mo)

        // rule 2, not rule 1: relaxed over [AsOf, Feb1] (only the January snapshot) = 1000.
        AffordabilityCeiling.For(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBe(1000m);
    }

    [Fact]
    public void Change_kind_does_not_shift_non_mandatory_tiers()
    {
        var target = Pattern(1, "target", 4, false, Monthly(AsOf, May1));
        var page = Page(target, 1000m, 500m);

        // Both read the same relaxed 500 — the suggestion/implicit axis only moves the mandatory tiers.
        AffordabilityCeiling.For(page, target, AsOf, ChangeKind.Suggestion).ShouldBe(500m);
        AffordabilityCeiling.For(page, target, AsOf, ChangeKind.Implicit).ShouldBe(500m);
    }

    [Fact]
    public void Ceiling_is_null_when_a_window_day_has_no_computed_free_amount()
    {
        var target = Pattern(1, "target", 4, false, Monthly(AsOf, May1));

        // January's free is uncomputed and the relaxed window covers it → the whole ceiling is unknown.
        AffordabilityCeiling.For(Page(target, null, 500m), target, AsOf, ChangeKind.Suggestion).ShouldBeNull();
    }

    // === ForStartingEarmark (single-day, at whatever tier governs AsOf) ===
    // At AsOf, free 1000: relaxed 1000, considerate 1010, thrifty 1020, miserly 1050.

    [Fact]
    public void Starting_earmark_ceiling_for_a_mandatory_suggestion_is_miserly_at_the_day()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, May1));

        AffordabilityCeiling.ForStartingEarmark(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion)
            .ShouldBe(1050m);
    }

    [Fact]
    public void Starting_earmark_ceiling_for_a_mandatory_implicit_change_past_the_split_is_thrifty()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, May1));

        AffordabilityCeiling.ForStartingEarmark(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Implicit)
            .ShouldBe(1020m);
    }

    [Fact]
    public void Starting_earmark_ceiling_for_a_mandatory_implicit_change_inside_the_split_is_considerate()
    {
        var target = Pattern(1, "target", 4, true, Monthly(AsOf, Feb1));

        AffordabilityCeiling.ForStartingEarmark(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Implicit)
            .ShouldBe(1010m);
    }

    [Fact]
    public void Starting_earmark_ceiling_for_a_non_mandatory_expense_is_relaxed()
    {
        var target = Pattern(1, "target", 4, false, Monthly(AsOf, May1));

        AffordabilityCeiling.ForStartingEarmark(Page(target, 1000m, 500m), target, AsOf, ChangeKind.Suggestion)
            .ShouldBe(1000m);
    }
}
