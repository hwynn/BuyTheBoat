using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

// Scenarios lifted directly from mini_fund_project/goals.xlsx so the expected
// answers are real numbers someone actually budgeted with, not invented fixtures.
public class RecurrenceRuleTests
{
    [Fact]
    public void Daily_interval_produces_evenly_spaced_occurrences()
    {
        // "paycheck": DAILY, interval=14, dstart=2025-01-24, until=2027-12-25
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Daily,
            DtStart = new DateOnly(2025, 1, 24),
            Interval = 14,
            Until = new DateOnly(2025, 4, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 1, 24),
            new DateOnly(2025, 2, 7),
            new DateOnly(2025, 2, 21),
            new DateOnly(2025, 3, 7),
            new DateOnly(2025, 3, 21),
        ]);
    }

    [Fact]
    public void Weekly_with_no_explicit_byday_defaults_to_the_start_dates_weekday()
    {
        // "groceries": WEEKLY, dstart=2025-01-19 (a Sunday), no byday column populated.
        // wkst=MO in the source only affects week-boundary counting for interval>1 —
        // it does not mean "occurs on Monday." This pins down that RFC 5545 default.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            DtStart = new DateOnly(2025, 1, 19),
            Until = new DateOnly(2025, 2, 16),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldAllBe(date => date.DayOfWeek == DayOfWeek.Sunday);
        occurrences.ShouldBe([
            new DateOnly(2025, 1, 19),
            new DateOnly(2025, 1, 26),
            new DateOnly(2025, 2, 2),
            new DateOnly(2025, 2, 9),
            new DateOnly(2025, 2, 16),
        ]);
    }

    [Fact]
    public void Monthly_with_single_bymonthday_lands_on_that_day_each_month()
    {
        // "bill pool": MONTHLY, dstart=2025-01-19, bymonthday=10, until=2025-12-25
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 1, 19),
            ByMonthDay = [10],
            Until = new DateOnly(2025, 5, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 2, 10),
            new DateOnly(2025, 3, 10),
            new DateOnly(2025, 4, 10),
        ]);
    }

    [Fact]
    public void Monthly_with_bymonthday_list_lands_on_every_listed_day()
    {
        // paycheck shape from class documentation.ods's worked example: 9th and 25th of each month.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2019, 1, 2),
            ByMonthDay = [9, 25],
            Until = new DateOnly(2019, 3, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2019, 1, 9),
            new DateOnly(2019, 1, 25),
            new DateOnly(2019, 2, 9),
            new DateOnly(2019, 2, 25),
        ]);
    }

    [Fact]
    public void Weekly_with_explicit_byday_list_lands_on_each_listed_weekday()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            DtStart = new DateOnly(2025, 1, 6), // a Monday
            ByDay = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            Until = new DateOnly(2025, 1, 20),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 1, 6),
            new DateOnly(2025, 1, 8),
            new DateOnly(2025, 1, 13),
            new DateOnly(2025, 1, 15),
            new DateOnly(2025, 1, 20),
        ]);
    }

    [Fact]
    public void Count_is_resolved_to_an_equivalent_until_date_and_never_survives_construction()
    {
        // Mirrors mini_fund_project/MmfUtility.py's count_to_until_rrule, and the
        // chart-only rule in planning/05-assumption-chart-full-text.md requiring
        // date_pattern to use `until`, never `count`.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            DtStart = new DateOnly(2025, 1, 6),
            Count = 3,
        });

        rule.Until.ShouldBe(new DateOnly(2025, 1, 20));
        rule.GetOccurrences().Count.ShouldBe(3);
    }

    [Fact]
    public void Create_throws_when_neither_until_nor_count_is_set()
    {
        Should.Throw<ArgumentException>(() => RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            DtStart = new DateOnly(2025, 1, 6),
        }));
    }

    [Fact]
    public void Create_throws_when_both_until_and_count_are_set()
    {
        Should.Throw<ArgumentException>(() => RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            DtStart = new DateOnly(2025, 1, 6),
            Until = new DateOnly(2025, 2, 1),
            Count = 3,
        }));
    }

    [Fact]
    public void Active_from_defaults_to_null_and_active_start_falls_back_to_start()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 3, 1),
            ByMonthDay = [1],
            Until = new DateOnly(2025, 6, 1),
        });

        rule.ToOptions().ActiveFrom.ShouldBeNull();
        rule.ActiveStart.ShouldBe(new DateOnly(2025, 3, 1));
    }

    [Fact]
    public void Active_from_stretches_the_span_earlier_without_adding_occurrences()
    {
        // A future-starting bill whose jar should exist from earlier: ActiveFrom
        // reaches back before the first occurrence, but the occurrences are
        // untouched — GetOccurrences never reads it.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 3, 1),
            ByMonthDay = [1],
            Until = new DateOnly(2025, 5, 1),
            ActiveFrom = new DateOnly(2025, 1, 15),
        });

        rule.ActiveStart.ShouldBe(new DateOnly(2025, 1, 15));
        rule.ActiveSpanContains(new DateOnly(2025, 1, 20)).ShouldBeTrue(); // in the lead-in, not an occurrence
        rule.GetOccurrences().ShouldBe([
            new DateOnly(2025, 3, 1),
            new DateOnly(2025, 4, 1),
            new DateOnly(2025, 5, 1),
        ]);
    }

    [Fact]
    public void Active_span_is_bounded_by_active_start_and_until()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 3, 1),
            ByMonthDay = [1],
            Until = new DateOnly(2025, 5, 1),
            ActiveFrom = new DateOnly(2025, 1, 15),
        });

        rule.ActiveSpanContains(new DateOnly(2025, 1, 14)).ShouldBeFalse(); // before the lead-in
        rule.ActiveSpanContains(new DateOnly(2025, 1, 15)).ShouldBeTrue();  // the lead-in day
        rule.ActiveSpanContains(new DateOnly(2025, 5, 1)).ShouldBeTrue();   // the last day
        rule.ActiveSpanContains(new DateOnly(2025, 5, 2)).ShouldBeFalse();  // past Until
    }

    [Fact]
    public void Create_throws_when_active_from_is_after_start()
    {
        Should.Throw<ArgumentException>(() => RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 3, 1),
            Until = new DateOnly(2025, 6, 1),
            ActiveFrom = new DateOnly(2025, 4, 1),
        }));
    }

    [Fact]
    public void Excluded_dates_are_left_out_of_generated_occurrences()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 4, 1),
            ExcludedDates = [new DateOnly(2025, 3, 1)],
        });

        rule.GetOccurrences().ShouldBe([
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 4, 1),
        ]);
    }

    [Fact]
    public void Multiple_excluded_dates_are_all_left_out_and_nothing_else_is_disturbed()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 7, 1),
            ExcludedDates = [new DateOnly(2025, 3, 1), new DateOnly(2025, 5, 1)],
        });

        rule.GetOccurrences().ShouldBe([
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 4, 1),
            new DateOnly(2025, 6, 1),
            new DateOnly(2025, 7, 1),
        ]);
    }

    [Fact]
    public void An_excluded_date_that_is_not_a_real_occurrence_is_a_harmless_no_op()
    {
        // Deliberate design choice, not an oversight — see ExcludedDates' own
        // doc comment on RecurrenceRuleOptions: validating this would make
        // WithUntil/truncation newly crash-prone the moment a shortened
        // range left a stale excluded date behind.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 3, 1),
            ExcludedDates = [new DateOnly(2025, 6, 15)], // outside the range, and never a real occurrence
        });

        rule.GetOccurrences().ShouldBe([
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 3, 1),
        ]);
    }

    [Fact]
    public void WithExcludedDates_replaces_the_whole_list_and_leaves_everything_else_unchanged()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 4, 1),
            ExcludedDates = [new DateOnly(2025, 2, 1)],
        });

        var updated = rule.WithExcludedDates([new DateOnly(2025, 3, 1)]);

        updated.ActiveStart.ShouldBe(rule.ActiveStart);
        updated.Until.ShouldBe(rule.Until);
        updated.GetOccurrences().ShouldBe([
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 2, 1), // no longer excluded — the old list was replaced, not appended to
            new DateOnly(2025, 4, 1),
        ]);
    }

    [Fact]
    public void WithUntil_and_WithActiveFrom_carry_excluded_dates_through_unchanged()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 3, 1),
            Until = new DateOnly(2025, 8, 1),
            ExcludedDates = [new DateOnly(2025, 4, 1)],
        });

        rule.WithUntil(new DateOnly(2025, 6, 1)).ExcludedDates.ShouldBe(rule.ExcludedDates);
        rule.WithActiveFrom(new DateOnly(2025, 1, 1)).ExcludedDates.ShouldBe(rule.ExcludedDates);
    }

    // An explicit ByDay does NOT, by itself, protect a Weekly rule's
    // own Interval > 1 cadence from drifting when Start is pinned to a date
    // that isn't itself part of the original series. RFC 5545's "every Nth
    // week" is counted from DTSTART's own calendar week — pinning Start at
    // 2025-06-15 (a Sunday, in the "wrong" week relative to a series
    // anchored 2025-01-03) lands the very first occurrence a full week late
    // (2025-06-27) even with the correct weekday spelled out explicitly.
    // Locks this in as a real, permanent regression test — not scratch code
    // — since it's the reason BuildSuccessorSchedule's own fix couldn't
    // just add an explicit ByDay and call it done.
    [Fact]
    public void Explicit_byday_alone_does_not_protect_an_intervals_own_week_phase_when_start_is_pinned_elsewhere()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            ByDay = [DayOfWeek.Friday],
            DtStart = new DateOnly(2025, 6, 15), // Sunday — not itself part of the Jan-3-anchored series
            Until = new DateOnly(2025, 12, 31),
        });

        // The CORRECT continuation of a biweekly-Friday series anchored
        // 2025-01-03 is 2025-06-20, 2025-07-04, ... — pinning Start away
        // from the series lands a week later instead.
        rule.GetOccurrences().Take(3).ToList().ShouldBe([
            new DateOnly(2025, 6, 27),
            new DateOnly(2025, 7, 11),
            new DateOnly(2025, 7, 25),
        ]);
    }

    [Fact]
    public void ImmediatelyPrecedes_is_true_only_when_this_ends_the_day_before_the_next_begins()
    {
        var earlier = MonthlySpan(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var contiguous = MonthlySpan(new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 31));
        var withGap = MonthlySpan(new DateOnly(2025, 2, 5), new DateOnly(2025, 3, 31));

        earlier.ImmediatelyPrecedes(contiguous).ShouldBeTrue();
        contiguous.ImmediatelyPrecedes(earlier).ShouldBeFalse(); // direction matters
        earlier.ImmediatelyPrecedes(withGap).ShouldBeFalse();
    }

    [Fact]
    public void ImmediatelyPrecedes_uses_the_active_span_start_not_the_rrule_anchor()
    {
        // Contiguity must
        // key on where the pattern's span STARTS (ActiveStart), not on its
        // rrule anchor (DtStart), which a lead-in — or a phase-preserved
        // relink — can push later than the span's real start.
        var earlier = MonthlySpan(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var leadIn = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2025, 2, 15),     // rrule anchor — a later real occurrence
            ActiveFrom = new DateOnly(2025, 2, 1), // but the span is active from Feb 1
            Until = new DateOnly(2025, 6, 30),
        });

        // Contiguous on the active span (Jan 31 → Feb 1), even though the rrule
        // anchor (Feb 15) sits two weeks past the boundary.
        earlier.ImmediatelyPrecedes(leadIn).ShouldBeTrue();
    }

    [Fact]
    public void ActiveSpanWithin_is_true_only_when_fully_contained()
    {
        var outer = MonthlySpan(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var inside = MonthlySpan(new DateOnly(2025, 3, 1), new DateOnly(2025, 9, 30));
        var pokesPastTheEnd = MonthlySpan(new DateOnly(2025, 3, 1), new DateOnly(2026, 1, 31));

        inside.ActiveSpanWithin(outer).ShouldBeTrue();
        pokesPastTheEnd.ActiveSpanWithin(outer).ShouldBeFalse();
        outer.ActiveSpanWithin(inside).ShouldBeFalse();
    }

    [Fact]
    public void ActiveSpansOverlap_is_true_for_shared_days_false_for_adjacent_or_gapped()
    {
        var a = MonthlySpan(new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var overlapping = MonthlySpan(new DateOnly(2025, 6, 1), new DateOnly(2025, 12, 31));
        var adjacent = MonthlySpan(new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        a.ActiveSpansOverlap(overlapping).ShouldBeTrue();
        a.ActiveSpansOverlap(adjacent).ShouldBeFalse();
    }

    [Fact]
    public void ReanchoredToStartOn_forward_keeps_an_interval_gt_1_cadences_phase()
    {
        // Biweekly Fridays anchored 2025-01-03. Re-anchoring the span to begin
        // mid-cycle (a Monday) must keep landing on the SAME Fridays, not
        // re-phase to every-other-week-from-Monday the way raw WithStart would.
        var biweeklyFridays = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            ByDay = [DayOfWeek.Friday],
            DtStart = new DateOnly(2025, 1, 3),
            Until = new DateOnly(2025, 6, 30),
        });

        var reanchored = biweeklyFridays.ReanchoredToStartOn(new DateOnly(2025, 1, 20)); // a Monday, mid-cycle

        reanchored.ActiveStart.ShouldBe(new DateOnly(2025, 1, 20)); // the span begins exactly where asked
        reanchored.GetOccurrences()[0].ShouldBe(new DateOnly(2025, 1, 31)); // the next real Friday on the same grid
        reanchored.GetOccurrences().ShouldAllBe(date => biweeklyFridays.GetOccurrences().Contains(date)); // same phase, a subset
    }

    [Fact]
    public void ReanchoredToStartOn_backward_extends_the_same_phase_earlier()
    {
        // Biweekly Fridays anchored 2025-03-07. Growing the span backward to
        // 2025-01-20 must add the EARLIER Fridays on the same grid, not re-phase.
        var biweeklyFridays = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            ByDay = [DayOfWeek.Friday],
            DtStart = new DateOnly(2025, 3, 7),
            Until = new DateOnly(2025, 6, 30),
        });

        var reanchored = biweeklyFridays.ReanchoredToStartOn(new DateOnly(2025, 1, 20));

        reanchored.ActiveStart.ShouldBe(new DateOnly(2025, 1, 20));
        var occurrences = reanchored.GetOccurrences();
        occurrences.ShouldContain(new DateOnly(2025, 1, 24)); // an earlier Friday, added on the same grid
        occurrences.ShouldContain(new DateOnly(2025, 3, 7)); // the original anchor, phase intact
        occurrences.ShouldAllBe(date => date.DayOfWeek == DayOfWeek.Friday);
    }

    private static RecurrenceRule MonthlySpan(DateOnly start, DateOnly until) => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        DtStart = start,
        Until = until,
    });
}
