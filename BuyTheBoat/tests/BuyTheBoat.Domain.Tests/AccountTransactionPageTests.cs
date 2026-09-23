using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

// Covers the two "how much can a suggestion safely draw on this day" query methods
// (AvailableFunds / AvailableFundsFor) and the SnapshotAsOf date lookup they lean on.
public class AccountTransactionPageTests
{
    private static readonly DateOnly Jan10 = new(2026, 1, 10);
    private static readonly DateOnly Mar10 = new(2026, 3, 10);

    // Only Priority and Mandatory matter to the funds methods; the schedule/amount are filler needed
    // just to make a valid pattern.
    private static RecurrenceRule AnyRule() => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        DtStart = new DateOnly(2026, 1, 1),
        Until = new DateOnly(2027, 1, 1),
    });

    private static FinancialPattern Pattern(int financeId, int priority, bool mandatory) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = $"pattern-{financeId}",
            DatePattern = AnyRule(),
            Amount = mandatory ? -100m : 100m,
            Priority = priority,
            Mandatory = mandatory,
        });

    private static FundJar Jar(int? financeId, decimal expected) => new()
    {
        FinanceId = financeId,
        CurrentAmount = null,
        ExpectedAmount = expected,
        MilestoneAmount = null,
    };

    private static BalanceSnapshot Snapshot(DateOnly? date, decimal? free, params FundJar[] jars) => new()
    {
        SnapshotDate = date,
        FullAmount = null,
        ExpectedAmount = free,
        ExpectedFreeAmount = free,
        FundJars = jars,
        ActualTransactions = [],
        ExpectedTransactions = [],
        EarMarkEvents = [],
    };

    private static AccountTransactionPage Page(
        IReadOnlyList<FinancialPattern> patterns,
        BalanceSnapshot initial,
        params BalanceSnapshot[] dated) => new()
    {
        Account = "checking",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2027, 1, 1),
        FinancePatterns = patterns,
        EarmarkPatterns = [],
        InitialSnapshot = initial,
        BalanceRecord = new SortedDictionary<DateOnly, BalanceSnapshot>(
            dated.ToDictionary(snapshot => snapshot.SnapshotDate!.Value)),
    };

    // finance 1: a bill above the target's priority; 2: discretionary; 3: a lower-priority bill.
    private static AccountTransactionPage PageWithMixedJars(decimal free) => Page(
        [
            Pattern(financeId: 1, priority: 5, mandatory: true),
            Pattern(financeId: 2, priority: 3, mandatory: false),
            Pattern(financeId: 3, priority: 2, mandatory: true),
        ],
        Snapshot(null, 0m),
        Snapshot(Jan10, free,
            Jar(financeId: null, expected: 40m), // safety cushion
            Jar(financeId: 1, expected: 100m),
            Jar(financeId: 2, expected: 60m),
            Jar(financeId: 3, expected: 30m)));

    // === SnapshotAsOf ===

    [Fact]
    public void SnapshotAsOf_returns_the_snapshot_on_an_exact_event_date()
    {
        var jan = Snapshot(Jan10, 100m);
        var feb = Snapshot(new DateOnly(2026, 2, 10), 200m);
        var page = Page([], Snapshot(null, 0m), jan, feb);

        page.SnapshotAsOf(new DateOnly(2026, 2, 10)).ShouldBeSameAs(feb);
    }

    [Fact]
    public void SnapshotAsOf_falls_back_to_the_nearest_earlier_snapshot_for_a_gap_date()
    {
        var jan = Snapshot(Jan10, 100m);
        var feb = Snapshot(new DateOnly(2026, 2, 10), 200m);
        var page = Page([], Snapshot(null, 0m), jan, feb);

        // No snapshot on Jan 20 — nothing happened since Jan 10, so January's state still stands.
        page.SnapshotAsOf(new DateOnly(2026, 1, 20)).ShouldBeSameAs(jan);
    }

    [Fact]
    public void SnapshotAsOf_returns_the_initial_snapshot_before_any_dated_one()
    {
        var initial = Snapshot(null, 0m);
        var page = Page([], initial, Snapshot(Jan10, 100m));

        page.SnapshotAsOf(new DateOnly(2025, 12, 31)).ShouldBeSameAs(initial);
    }

    // === AvailableFunds (context-blind: tiers 1-2) ===

    [Fact]
    public void AvailableFunds_returns_the_days_free_amount()
    {
        var page = Page([], Snapshot(null, 0m), Snapshot(Jan10, 250m, Jar(null, 40m)));

        page.AvailableFunds(Jan10).ShouldBe(250m);
    }

    [Fact]
    public void AvailableFunds_adds_the_safety_cushion_when_asked()
    {
        // The 40 in the cushion was already set aside out of free funds; counting it makes 290 reachable.
        var page = Page([], Snapshot(null, 0m), Snapshot(Jan10, 250m, Jar(null, 40m)));

        page.AvailableFunds(Jan10, includeSafetyCushion: true).ShouldBe(290m);
    }

    [Fact]
    public void AvailableFunds_reads_the_nearest_earlier_day_for_a_date_with_no_snapshot()
    {
        var page = Page([], Snapshot(null, 0m), Snapshot(Jan10, 250m));

        page.AvailableFunds(new DateOnly(2026, 1, 25)).ShouldBe(250m);
    }

    [Fact]
    public void AvailableFunds_is_null_when_the_free_amount_has_not_been_computed()
    {
        var page = Page([], Snapshot(null, null), Snapshot(Jan10, null));

        page.AvailableFunds(Jan10).ShouldBeNull();
    }

    // === AvailableFundsFor (context-aware: tiers 3-4) ===

    [Fact]
    public void AvailableFundsFor_counts_free_funds_plus_non_mandatory_jars_by_default()
    {
        var page = PageWithMixedJars(free: 250m);

        // 250 free + 60 in the discretionary jar; both bills and the cushion are left alone.
        page.AvailableFundsFor(Jan10, forPriority: 4).ShouldBe(310m);
    }

    [Fact]
    public void AvailableFundsFor_also_counts_lower_priority_jars_when_digging()
    {
        var page = PageWithMixedJars(free: 250m);

        // + the 30 in the priority-2 bill (below the target's 4); the priority-5 bill stays protected.
        page.AvailableFundsFor(Jan10, forPriority: 4, digIntoLowerPriority: true).ShouldBe(340m);
    }

    [Fact]
    public void AvailableFundsFor_ignores_forPriority_until_digging_is_requested()
    {
        var page = PageWithMixedJars(free: 250m);

        // Same non-mandatory-only total whatever the priority: without digging, forPriority is unused.
        page.AvailableFundsFor(Jan10, forPriority: 1).ShouldBe(310m);
        page.AvailableFundsFor(Jan10, forPriority: 999).ShouldBe(310m);
    }

    [Fact]
    public void AvailableFundsFor_does_not_double_count_a_jar_that_is_both_non_mandatory_and_lower_priority()
    {
        // Jar 2 is non-mandatory AND below the target priority — it must add once, not twice.
        var page = PageWithMixedJars(free: 0m);

        // jar 2 (60, once) + jar 3 (30, the lower-priority bill); jar 1's bill outranks the target.
        page.AvailableFundsFor(Jan10, forPriority: 4, digIntoLowerPriority: true).ShouldBe(90m);
    }

    [Fact]
    public void AvailableFundsFor_never_counts_the_safety_cushion()
    {
        // Even digging past every jar's priority, the cushion (null finance id) stays out of these tiers.
        var page = PageWithMixedJars(free: 250m);

        // free 250 + jar2 60 + jar1 100 (prio 5<100) + jar3 30 (prio 2<100); the cushion's 40 is excluded.
        page.AvailableFundsFor(Jan10, forPriority: 100, digIntoLowerPriority: true).ShouldBe(440m);
    }

    [Fact]
    public void AvailableFundsFor_skips_a_jar_whose_finance_id_has_no_matching_pattern()
    {
        // A jar with no finance pattern can't be classified — the cautious choice leaves it uncounted
        // rather than assuming it's fair game to reclaim.
        var page = Page(
            [Pattern(financeId: 2, priority: 3, mandatory: false)],
            Snapshot(null, 0m),
            Snapshot(Jan10, 100m,
                Jar(financeId: null, expected: 40m),
                Jar(financeId: 2, expected: 60m),
                Jar(financeId: 99, expected: 500m)));

        // 100 free + 60 (non-mandatory jar 2); the unclassifiable 500 jar is skipped, dig or not.
        page.AvailableFundsFor(Jan10, forPriority: 4, digIntoLowerPriority: true).ShouldBe(160m);
    }

    [Fact]
    public void AvailableFundsFor_is_null_when_the_free_amount_has_not_been_computed()
    {
        var page = Page(
            [Pattern(financeId: 2, priority: 3, mandatory: false)],
            Snapshot(null, null),
            Snapshot(Jan10, null, Jar(financeId: 2, expected: 60m)));

        page.AvailableFundsFor(Jan10, forPriority: 4).ShouldBeNull();
    }

    // === MinimumAvailableFunds (range, measured at one Frugality tier) ===

    // Three event months plus an April cliff for the window-boundary tests. Free amounts dip in February
    // (the tight spot); jar 2 is discretionary, jar 3 a lower-priority bill, jar 1 a higher-priority bill.
    // The initial seed's free amount is null, so a window that opens before January reads as uncomputed.
    private static AccountTransactionPage RangePage() => Page(
        [
            Pattern(financeId: 1, priority: 5, mandatory: true),
            Pattern(financeId: 2, priority: 3, mandatory: false),
            Pattern(financeId: 3, priority: 2, mandatory: true),
        ],
        Snapshot(null, null),
        Snapshot(Jan10, 300m, Jar(null, 40m), Jar(1, 100m), Jar(2, 50m), Jar(3, 10m)),
        Snapshot(new DateOnly(2026, 2, 10), 120m, Jar(null, 40m), Jar(1, 80m), Jar(2, 20m), Jar(3, 15m)),
        Snapshot(Mar10, 500m, Jar(null, 40m), Jar(1, 200m), Jar(2, 90m), Jar(3, 25m)),
        Snapshot(new DateOnly(2026, 4, 10), 5m, Jar(null, 40m), Jar(1, 10m), Jar(2, 10m), Jar(3, 10m)));

    [Fact]
    public void MinimumAvailableFunds_relaxed_is_the_lowest_free_amount_across_the_window()
    {
        // Free amounts Jan 300, Feb 120, Mar 500 — February is the low point.
        RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Relaxed).ShouldBe(120m);
    }

    [Fact]
    public void MinimumAvailableFunds_considerate_adds_the_cushion_at_each_sampled_day()
    {
        // Feb still lowest: 120 free + 40 cushion.
        RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Considerate).ShouldBe(160m);
    }

    [Fact]
    public void MinimumAvailableFunds_thrifty_adds_non_mandatory_jars()
    {
        // Feb: 120 free + 20 in the discretionary jar.
        RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Thrifty, forPriority: 4).ShouldBe(140m);
    }

    [Fact]
    public void MinimumAvailableFunds_miserly_also_adds_lower_priority_jars()
    {
        // Feb: 120 free + 20 (discretionary) + 15 (the priority-2 bill, below the target's 4); the
        // priority-5 bill stays protected.
        RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Miserly, forPriority: 4).ShouldBe(155m);
    }

    [Fact]
    public void MinimumAvailableFunds_only_samples_days_inside_the_window()
    {
        // April crashes to 5, but a Jan–Mar window must not see it.
        RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Relaxed).ShouldBe(120m);
    }

    [Fact]
    public void MinimumAvailableFunds_counts_the_value_carried_into_the_window()
    {
        // The window opens Feb 15 with no snapshot of its own; February's 120 carries in and is the low
        // point, below March's 500 — so the carried-in value, not only the snapshot days inside, is sampled.
        RangePage().MinimumAvailableFunds(new DateOnly(2026, 2, 15), Mar10, Frugality.Relaxed).ShouldBe(120m);
    }

    [Fact]
    public void MinimumAvailableFunds_over_a_flat_window_returns_the_entry_value()
    {
        // No snapshot between Jan 15 and Feb 5, so the whole window sits at January's 300.
        RangePage().MinimumAvailableFunds(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 5), Frugality.Relaxed)
            .ShouldBe(300m);
    }

    [Fact]
    public void MinimumAvailableFunds_is_null_when_any_window_day_is_uncomputed()
    {
        // The window opens before the first dated snapshot, so it carries in the initial seed's null free amount.
        RangePage().MinimumAvailableFunds(new DateOnly(2026, 1, 1), Mar10, Frugality.Relaxed).ShouldBeNull();
    }

    [Fact]
    public void MinimumAvailableFunds_requires_a_priority_for_the_thrifty_and_miserly_tiers()
    {
        Should.Throw<ArgumentException>(() => RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Thrifty));
        Should.Throw<ArgumentException>(() => RangePage().MinimumAvailableFunds(Jan10, Mar10, Frugality.Miserly));
    }

    [Fact]
    public void MinimumAvailableFunds_rejects_a_window_that_ends_before_it_starts()
    {
        Should.Throw<ArgumentException>(() => RangePage().MinimumAvailableFunds(Mar10, Jan10, Frugality.Relaxed));
    }
}
