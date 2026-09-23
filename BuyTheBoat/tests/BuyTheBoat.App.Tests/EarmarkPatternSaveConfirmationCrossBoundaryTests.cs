using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// The "fourth relationship" — does an Amount/shape edit on the
// LAST EarMarkPattern in one finance_id's own chain reach across a
// FinancialPattern-level break-off into the successor segment's own,
// different-finance_id plan. Distinct scenario from
// EarmarkPatternSaveConfirmationTests (which never involves more than
// one FinancialPattern), so its own file — same real-SQLite-temp-file shape,
// same Goal/Plan/Monthly/Forecast/Confirmation helper style, plus a second
// goal helper (SuccessorGoal) for the far side of the break.
public class EarmarkPatternSaveConfirmationCrossBoundaryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-crossboundary-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public EarmarkPatternSaveConfirmationCrossBoundaryTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    [Fact]
    public void An_amount_change_on_the_last_plan_offers_to_cascade_across_a_break_off_into_the_successors_own_plan()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = SuccessorGoal(original, 2, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        var originalPlan = Plan(original, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successorPlan = Plan(successor, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(originalPlan);
        _earMarkPatterns.Save(successorPlan);

        var editedPlan = Plan(original, -1_550m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, originalPlan.DatePattern.ActiveStart, original);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed();

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_550m);
    }

    [Fact]
    public void Declining_the_cross_boundary_cascade_leaves_the_successors_own_plan_untouched()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = SuccessorGoal(original, 2, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        var originalPlan = Plan(original, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successorPlan = Plan(successor, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(originalPlan);
        _earMarkPatterns.Save(successorPlan);

        var editedPlan = Plan(original, -1_550m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, originalPlan.DatePattern.ActiveStart, original);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseJustThisSegment();

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 1).Amount.ShouldBe(-1_550m); // the direct edit itself
        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_600m); // untouched
    }

    [Fact]
    public void No_cascade_is_offered_when_the_successor_goal_has_no_plan_of_its_own_yet()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = SuccessorGoal(original, 2, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        var originalPlan = Plan(original, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(originalPlan);
        // No plan saved for the successor at all — nothing to cascade onto.

        var editedPlan = Plan(original, -1_550m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, originalPlan.DatePattern.ActiveStart, original);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — nothing on the far side to cascade onto");

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().ShouldHaveSingleItem();
    }

    [Fact]
    public void No_cascade_is_offered_when_the_successor_goals_own_plans_are_a_concurrent_set()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = SuccessorGoal(original, 2, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        var originalPlan = Plan(original, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(originalPlan);
        // Two overlapping (concurrent) plans on the successor's own side —
        // FindCurrentPlan returns null for these, same as everywhere else.
        _earMarkPatterns.Save(Plan(successor, -800m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31)));
        _earMarkPatterns.Save(Plan(successor, -800m, new DateOnly(2025, 7, 2), new DateOnly(2025, 12, 31)));

        var editedPlan = Plan(original, -1_550m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, originalPlan.DatePattern.ActiveStart, original);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — the far side is a concurrent set, no single current plan");

        confirmation.Run().ShouldBeTrue();
    }

    [Fact]
    public void A_same_finance_id_successor_takes_priority_over_a_cross_boundary_one_when_both_exist()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var successorFinancialPattern = SuccessorGoal(original, 2, -1_600m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successorFinancialPattern, accountId: 1);
        var current = Plan(original, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var sameChainSuccessor = Plan(original, -100m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        var crossBoundaryPlan = Plan(successorFinancialPattern, -1_600m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(sameChainSuccessor);
        _earMarkPatterns.Save(crossBoundaryPlan);

        var editedPlan = Plan(original, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, original);
        // No ConfirmImplicitChanges wired up — proves the DEFAULT (cascade
        // forward) is what runs, matching the same-chain tests.

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 7, 1)).Amount.ShouldBe(-120m); // same-chain successor cascaded onto
        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1_600m); // cross-boundary plan untouched — never even reached
    }

    [Fact]
    public void The_cascade_description_names_the_successor_segment_when_the_cascade_is_cross_boundary()
    {
        var original = Goal(1, "Rent", -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = SuccessorGoal(original, 2, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(original, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        var originalPlan = Plan(original, -1_500m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successorPlan = Plan(successor, -1_600m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(originalPlan);
        _earMarkPatterns.Save(successorPlan);

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(original, -1_550m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, originalPlan.DatePattern.ActiveStart, original);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.Cascade, 0).ShouldContain("newer segment");
        captured.OptionConsequence(ConfirmationRowIds.Cascade, 0).ShouldContain("Dec 31, 2025"); // how far the successor's own plan reaches
    }

    // ---- shared scenario-building helpers ----------------------------------

    private static FinancialPattern Goal(int financeId, string source, decimal amount, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            Amount = amount,
            DatePattern = Monthly(start, until),
        });

    // Mirrors BreakOffFactory.FindSuccessor's own exact matching rule: same
    // Source, a different FinanceId, and a Start that begins the day right
    // after predecessor's own Until — anything looser and FindSuccessor
    // itself wouldn't recognize the link either.
    private static FinancialPattern SuccessorGoal(FinancialPattern predecessor, int financeId, decimal amount, DateOnly start, DateOnly until)
    {
        if (start != predecessor.DatePattern.Until.AddDays(1))
        {
            throw new ArgumentException("start must begin the day after predecessor's own Until, matching FindSuccessor's own rule.", nameof(start));
        }

        return Goal(financeId, predecessor.Source, amount, start, until);
    }

    private static EarMarkPattern Plan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = Monthly(start, until),
                StartingAllocation = startingAllocation,
            },
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
        AsOfDate = new DateOnly(2025, 1, 1),
        HorizonEndDate = new DateOnly(2026, 12, 31),
    });

    private EarmarkPatternSaveConfirmation Confirmation(EarMarkPattern proposedPlan, DateOnly savedStart, FinancialPattern goal)
    {
        var forecast = Forecast();
        return new EarmarkPatternSaveConfirmation(proposedPlan, savedStart, goal, () => forecast, new FinancePatternRepositories
        {
            FinancialPatterns = _financialPatterns,
            EarMarkPatterns = _earMarkPatterns,
            ManualEarmarks = _manualEarmarks,
        });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
