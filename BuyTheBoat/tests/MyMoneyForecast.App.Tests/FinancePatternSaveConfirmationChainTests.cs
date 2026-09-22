using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// The FinancialPattern-chain mirror of
// FinancePatternSaveConfirmationEarmarkTests, driving Run()'s own
// FinancialPattern-editing path (not RunForPlan) against real chain
// neighbors. Every scenario keeps the pattern being edited starting AFTER
// AsOf, deliberately — a past occurrence would make the edit IsChangeCritical
// too, and DetermineChainConditionsIfApplicable's own gate suppresses every
// chain question whenever that's true (see its own field comment for why).
public class FinancePatternSaveConfirmationChainTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2025, 6, 15);
    private static readonly DateOnly HorizonEnd = new(2026, 12, 31);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-chain-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public FinancePatternSaveConfirmationChainTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    [Fact]
    public void Extending_start_nudges_the_predecessors_own_until_by_default_without_asking_to_break()
    {
        var predecessor = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var editedPlan = Bill(2, "Rent", -1_800m, new DateOnly(2025, 6, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);
        // No ConfirmImplicitChanges wired up — proves the DEFAULT (stay
        // linked) is what runs, not a hard-coded test answer.

        confirmation.Run().ShouldBeTrue();

        var patterns = _financialPatterns.GetAll();
        patterns.Single(p => p.FinanceId == 2).DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 6, 1));
        var adjustedPredecessor = patterns.Single(p => p.FinanceId == 1);
        adjustedPredecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 5, 31));
        adjustedPredecessor.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1)); // unchanged
    }

    [Fact]
    public void Extending_start_far_enough_absorbs_the_predecessor_and_everything_under_its_own_finance_id()
    {
        var predecessor = Bill(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        var predecessorPlan = Plan(predecessor, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        _earMarkPatterns.Save(predecessorPlan);
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2025, 2, 10), Amount = 40m }, predecessorPlan));

        // newStart lands EXACTLY on the predecessor's own Start (not before
        // it) — deliberately the exact-boundary case, not just "comfortably
        // past it": this is what caught ExtendStart's own off-by-one filter
        // bug (found here, fixed in both BreakOffFactory and RestructureFactory).
        var editedPlan = Bill(2, "Rent", -1_800m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().ShouldHaveSingleItem().FinanceId.ShouldBe(2); // predecessor's own FinanceId is gone entirely
        _earMarkPatterns.GetAll().ShouldBeEmpty(); // its own plan went with it
        _manualEarmarks.GetAll().ShouldBeEmpty(); // and its own manual earmark
    }

    [Fact]
    public void Choosing_to_let_the_chain_break_leaves_the_predecessor_completely_untouched()
    {
        var predecessor = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var editedPlan = Bill(2, "Rent", -1_800m, new DateOnly(2025, 6, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseToLetChainBreak();

        confirmation.Run().ShouldBeTrue();

        var untouchedPredecessor = _financialPatterns.GetAll().Single(p => p.FinanceId == 1);
        untouchedPredecessor.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
        untouchedPredecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // own overlap with current left in place, not resolved
    }

    [Fact]
    public void No_chain_question_at_all_is_asked_when_there_is_no_predecessor_or_successor()
    {
        var standalone = Bill(1, "Netflix", -20m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        _financialPatterns.Save(standalone, accountId: 1);

        var editedPlan = Bill(1, "Netflix", -25m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        var confirmation = Confirmation(1, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — no chain neighbor at all");

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single().Amount.ShouldBe(-25m);
    }

    [Fact]
    public void No_chain_question_is_asked_when_the_edit_also_touches_already_occurred_history()
    {
        // Deliberately the ONE test in this file where the pattern being
        // edited starts BEFORE AsOf — proving DetermineChainConditionsIfApplicable's
        // own !IsChangeCritical gate actually suppresses the chain question
        // when the break-off question is what fires instead.
        var predecessor = Bill(1, "Rent", -1_600m, new DateOnly(2024, 1, 1), new DateOnly(2025, 3, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        // current already has occurrences before AsOf (2025-06-15) — editing
        // its own Start makes this IsChangeCritical, not just chain-touching.
        var editedPlan = Bill(2, "Rent", -1_800m, new DateOnly(2025, 3, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);
        var chainQuestionAsked = false;
        confirmation.ConfirmImplicitChanges = request =>
        {
            if (request.HasRow(ConfirmationRowIds.ChainBoundary))
            {
                chainQuestionAsked = true;
            }

            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        chainQuestionAsked.ShouldBeFalse();
    }

    [Fact]
    public void A_concurrent_same_source_pattern_is_never_mistaken_for_a_chain_neighbor()
    {
        // Both start AFTER AsOf (2025-06-15), same discipline as every other
        // test in this file — main's own Start moving is what's under test
        // here, not the break-off question, which a past-occurrence
        // Start edit would otherwise also (correctly) trigger, muddying the
        // result this test is actually checking.
        var main = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        var concurrent = Bill(2, "Storage Unit Rental", -50m, new DateOnly(2025, 9, 1), new DateOnly(2026, 2, 28));
        _financialPatterns.Save(main, accountId: 1);
        _financialPatterns.Save(concurrent, accountId: 1);

        var editedPlan = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 8, 1), new DateOnly(2026, 6, 30));
        var confirmation = Confirmation(1, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — concurrent, not a chain neighbor");

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 9, 1)); // untouched
    }

    [Fact]
    public void Amount_change_cascades_forward_by_default_when_driven_directly_against_a_successor()
    {
        // Run() itself is unreachable this way through the real app today —
        // ExpenseFormPanel.LoadPattern's own silent redirect
        // never lets a segment with a successor be loaded for editing in the
        // first place, so ChangeCanCascade never actually fires via the UI.
        // Still real, correct, and worth testing directly the way this
        // project already drives other reachable-only-by-test paths.
        var current = Bill(1, "Rent", -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var successor = Bill(2, "Rent", -1_600m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var editedPlan = Bill(1, "Rent", -1_650m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var confirmation = Confirmation(1, editedPlan);

        confirmation.Run().ShouldBeTrue();

        var cascaded = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        cascaded.Amount.ShouldBe(-1_650m);
        cascaded.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 10, 1)); // its own, untouched
    }

    [Fact]
    public void Choosing_just_this_segment_leaves_the_successors_own_amount_alone()
    {
        var current = Bill(1, "Rent", -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var successor = Bill(2, "Rent", -1_600m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var editedPlan = Bill(1, "Rent", -1_650m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var confirmation = Confirmation(1, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseJustThisSegment();

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_600m);
    }

    [Fact]
    public void Trivial_fields_do_not_cascade_by_default_unlike_amount_and_shape()
    {
        var current = Bill(1, "Rent", -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var successor = Bill(2, "Rent", -1_600m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var editedPlan = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -1_600m,
            Priority = 9,
            Description = "Rent — landlord raised it",
            DatePattern = Monthly(new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30)),
        });
        var confirmation = Confirmation(1, editedPlan);
        // No ConfirmImplicitChanges wired up — proves the DEFAULT ("just
        // this segment") is what runs for trivial fields, unlike Amount/shape.

        confirmation.Run().ShouldBeTrue();

        var untouchedSuccessor = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        untouchedSuccessor.Priority.ShouldBe(0);
        untouchedSuccessor.Description.ShouldBeNull();
    }

    [Fact]
    public void Choosing_to_cascade_trivial_fields_forward_applies_them_to_the_successor()
    {
        var current = Bill(1, "Rent", -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var successor = Bill(2, "Rent", -1_600m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var editedPlan = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -1_600m,
            Priority = 9,
            Description = "Rent — landlord raised it",
            DatePattern = Monthly(new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30)),
        });
        var confirmation = Confirmation(1, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseCascadeTrivialFields();

        confirmation.Run().ShouldBeTrue();

        var cascadedSuccessor = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        cascadedSuccessor.Priority.ShouldBe(9);
        cascadedSuccessor.Description.ShouldBe("Rent — landlord raised it");
        cascadedSuccessor.Amount.ShouldBe(-1_600m); // its own, untouched — trivial fields only
    }

    [Fact]
    public void Changing_source_on_a_chained_segment_warns_but_still_saves()
    {
        var predecessor = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var editedPlan = Bill(2, "Rent (new landlord)", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);
        string? capturedWarning = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedWarning = request.AnnouncementText(ConfirmationRowIds.SourceChange);
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        capturedWarning.ShouldNotBeNullOrEmpty();
        capturedWarning.ShouldContain("predecessor");
        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Source.ShouldBe("Rent (new landlord)");
    }

    [Fact]
    public void Cancelling_the_confirmation_saves_nothing_at_all()
    {
        var predecessor = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var editedPlan = Bill(2, "Rent", -1_800m, new DateOnly(2025, 6, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(2, editedPlan);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Cancel();

        confirmation.Run().ShouldBeFalse();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1));
    }

    // ---- editing an EARLIER segment (has a later one): edit in place +
    //      cascade forward, never a break-off (open the earliest
    //      segment; the change flows forward from there) ---------------------

    [Fact]
    public void Editing_an_earlier_segments_amount_edits_it_in_place_and_cascades_forward_by_default()
    {
        var earlier = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)); // fully past, has a successor
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(earlier, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var edited = Bill(1, "Rent", -1_650m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)); // amount only
        var confirmation = Confirmation(1, edited);
        // No delegate — proves the DEFAULT (cascade forward) runs, and that
        // editing an earlier segment does NOT crash the way the old break-off
        // path did.

        confirmation.Run().ShouldBeTrue();

        var all = _financialPatterns.GetAll();
        all.Count.ShouldBe(2); // edited in place + cascaded — no break-off, no new segment
        all.Single(p => p.FinanceId == 1).Amount.ShouldBe(-1_650m); // the earlier segment itself changed
        all.Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_650m); // and the change carried forward
    }

    [Fact]
    public void Editing_an_earlier_segment_and_declining_the_cascade_leaves_the_later_segment_untouched()
    {
        var earlier = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(earlier, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        var edited = Bill(1, "Rent", -1_650m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(1, edited);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseJustThisSegment();

        confirmation.Run().ShouldBeTrue();

        var all = _financialPatterns.GetAll();
        all.Count.ShouldBe(2);
        all.Single(p => p.FinanceId == 1).Amount.ShouldBe(-1_650m); // the earlier segment changed
        all.Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_800m); // the later one deliberately left alone
    }

    [Fact]
    public void Editing_an_earlier_segment_asks_to_cascade_and_does_not_announce_a_break_off()
    {
        var earlier = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(earlier, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);

        ImplicitChangeConfirmationRequest? captured = null;
        var edited = Bill(1, "Rent", -1_650m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(1, edited);
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.HasRow(ConfirmationRowIds.Cascade).ShouldBeTrue(); // offered to carry the change forward
        captured.HasRow(ConfirmationRowIds.WarnAboutBreakOff).ShouldBeFalse(); // NOT a break-off announcement
        captured.Description.ShouldContain("earlier segment");
    }

    [Fact]
    public void Editing_an_earlier_segment_that_has_its_own_savings_plan_does_not_crash()
    {
        // Mirrors the real break-off chain (the seeded Car Lease): every segment
        // has its own savings plan. The plan-shape picker must not run for a
        // Critical edit on an earlier segment and propose a break-off successor
        // plan starting today against that segment's own past-starting schedule —
        // that throws "can't begin allocating before its goal's span starts."
        var earlier = Bill(1, "Rent", -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(earlier, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(earlier, -1_600m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)));
        _earMarkPatterns.Save(Plan(current, -1_800m, new DateOnly(2025, 4, 1), new DateOnly(2025, 12, 31)));

        var edited = Bill(1, "Rent", -1_650m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(1, edited);

        confirmation.Run().ShouldBeTrue(); // must not throw

        _financialPatterns.GetAll().Single(p => p.FinanceId == 1).Amount.ShouldBe(-1_650m); // edited in place
        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_650m); // cascaded forward
    }

    // ---- shared scenario-building helpers ----------------------------------

    private static FinancialPattern Bill(int financeId, string source, decimal amount, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            Amount = amount,
            DatePattern = Monthly(start, until),
        });

    private static EarMarkPattern Plan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions { FinanceId = goal.FinanceId, Amount = amount, DatePattern = Monthly(start, until) },
            goal);

    private static RecurrenceRule Monthly(DateOnly start, DateOnly until) => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [start.Day],
        DtStart = start,
        Until = until,
    });

    private ForecastResult Forecast() => TransactionLogBookFactory.CreateForecast(new ForecastOptions
    {
        FinancialPatterns = _financialPatterns.GetAll(),
        EarMarkPatterns = _earMarkPatterns.GetAll(),
        ManualEarmarks = _manualEarmarks.GetAll(),
        StartingBalance = 10_000m,
        AsOfDate = AsOf,
        HorizonEndDate = HorizonEnd,
    });

    private FinancePatternSaveConfirmation Confirmation(int financeId, FinancialPattern proposedPattern) =>
        new(financeId, proposedPattern, accountId: 1, userSkippedPlanning: true, () => Forecast(), new FinancePatternRepositories
        {
            FinancialPatterns = _financialPatterns,
            EarMarkPatterns = _earMarkPatterns,
            ManualEarmarks = _manualEarmarks,
        });

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
