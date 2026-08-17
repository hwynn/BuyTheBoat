using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// planning/27's Phase 1 — the FinancialPattern-chain mirror of
// FinancePatternSaveConfirmationEarmarkTests, driving Run()'s own
// FinancialPattern-editing path (not RunForPlan) against real chain
// neighbors. Every scenario keeps the pattern being edited starting AFTER
// AsOf, deliberately — a past occurrence would make the edit IsChangeCritical
// too, and DetermineChainConditionsIfApplicable's own gate suppresses every
// Phase 1 question whenever that's true (see its own field comment for why).
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
        patterns.Single(p => p.FinanceId == 2).DatePattern.Start.ShouldBe(new DateOnly(2025, 6, 1));
        var adjustedPredecessor = patterns.Single(p => p.FinanceId == 1);
        adjustedPredecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 5, 31));
        adjustedPredecessor.DatePattern.Start.ShouldBe(new DateOnly(2025, 1, 1)); // unchanged
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
        confirmation.ConfirmImplicitChanges = _ => new ImplicitChangeConfirmationAnswer { Proceed = true, ChoseStayLinked = false };

        confirmation.Run().ShouldBeTrue();

        var untouchedPredecessor = _financialPatterns.GetAll().Single(p => p.FinanceId == 1);
        untouchedPredecessor.DatePattern.Start.ShouldBe(new DateOnly(2025, 1, 1));
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
        // when Item C's own break-off question is what fires instead.
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
            if (request.TouchesChainBoundary)
            {
                chainQuestionAsked = true;
            }

            return new ImplicitChangeConfirmationAnswer { Proceed = true, ChooseAlterPast = true };
        };

        confirmation.Run().ShouldBeTrue();

        chainQuestionAsked.ShouldBeFalse();
    }

    [Fact]
    public void A_concurrent_same_source_pattern_is_never_mistaken_for_a_chain_neighbor()
    {
        // Both start AFTER AsOf (2025-06-15), same discipline as every other
        // test in this file — main's own Start moving is what's under test
        // here, not Item C's own break-off question, which a past-occurrence
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

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.Start.ShouldBe(new DateOnly(2025, 9, 1)); // untouched
    }

    [Fact]
    public void Amount_change_cascades_forward_by_default_when_driven_directly_against_a_successor()
    {
        // Run() itself is unreachable this way through the real app today —
        // ExpenseFormPanel.LoadPattern's own silent redirect (planning/24)
        // never lets a segment with a successor be loaded for editing in the
        // first place, so ChangeCanCascade never actually fires via the UI.
        // Still real, correct, and worth testing directly the way this
        // project already drives other reachable-only-by-test paths (see
        // this test project's own NarrowSurvivingPlanIfNeeded precedent).
        var current = Bill(1, "Rent", -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var successor = Bill(2, "Rent", -1_600m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var editedPlan = Bill(1, "Rent", -1_650m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30));
        var confirmation = Confirmation(1, editedPlan);

        confirmation.Run().ShouldBeTrue();

        var cascaded = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        cascaded.Amount.ShouldBe(-1_650m);
        cascaded.DatePattern.Start.ShouldBe(new DateOnly(2025, 10, 1)); // its own, untouched
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
        confirmation.ConfirmImplicitChanges = _ => new ImplicitChangeConfirmationAnswer { Proceed = true, ChoseCascadeForward = false };

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
        confirmation.ConfirmImplicitChanges = _ => new ImplicitChangeConfirmationAnswer { Proceed = true, ChoseCascadeTrivialFields = true };

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
            capturedWarning = request.SourceChangeWarning;
            return new ImplicitChangeConfirmationAnswer { Proceed = true };
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
        confirmation.ConfirmImplicitChanges = _ => new ImplicitChangeConfirmationAnswer { Proceed = false };

        confirmation.Run().ShouldBeFalse();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.Start.ShouldBe(new DateOnly(2025, 7, 1));
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
        Start = start,
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
