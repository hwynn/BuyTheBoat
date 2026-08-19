using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// The paycheck-association cascade (redesign/memory's own project_next_phase.md,
// 2026-08-16/17 blocks; built 2026-08-17 on explicit request) — driving Run()'s
// own FinancialPattern-editing path when the pattern being edited is INCOME,
// not a bill. Every scenario keeps the income's own Start AFTER AsOf,
// deliberately, same discipline as FinancePatternSaveConfirmationChainTests:
// a past occurrence would make the edit IsChangeCritical too, and a Critical
// edit that breaks off leaves two income-shaped FinancialPatterns behind
// (the truncated original plus the new successor) — a genuinely different,
// not-yet-explored scenario for AllocationPlanProposer.Propose's own "exactly
// one income stream" check, not what these tests are about.
public class FinancePatternSaveConfirmationPaycheckAssociationTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2025, 6, 15);
    private static readonly DateOnly HorizonEnd = new(2026, 12, 31);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-paycheck-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public FinancePatternSaveConfirmationPaycheckAssociationTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    [Fact]
    public void Editing_the_paycheck_offers_to_repace_a_bill_that_was_paced_against_its_old_schedule()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var bill = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        var pacedPlan = SavePacedBill(bill, [bill, income]).Plan;

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1)); // same payday count, different day
        var confirmation = Confirmation(1, editedIncome);
        string? capturedDescription = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            request.HasRow(ConfirmationRowIds.PacedBillsCascade).ShouldBeTrue();
            capturedDescription = request.OptionConsequence(ConfirmationRowIds.PacedBillsCascade, 0);
            return Confirm.Proceed(); // ChoseToRepaceBills defaults false
        };

        confirmation.Run().ShouldBeTrue();

        capturedDescription.ShouldNotBeNullOrEmpty();
        capturedDescription.ShouldContain("Rent");
        // Declined by default — the bill's own plan is untouched.
        var untouchedPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2);
        untouchedPlan.DatePattern.Start.ShouldBe(pacedPlan.DatePattern.Start);
        untouchedPlan.DatePattern.ByMonthDay.ShouldBe(pacedPlan.DatePattern.ByMonthDay);
    }

    [Fact]
    public void Accepting_the_cascade_repaces_the_bills_plan_to_match_the_new_schedule()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var bill = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        var oldPlan = SavePacedBill(bill, [bill, income]).Plan;

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var confirmation = Confirmation(1, editedIncome);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseToRepaceBills();

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 2).ToList();
        plans.ShouldHaveSingleItem(); // the old (FinanceId, Start) row is gone, not left behind
        var newPlan = plans.Single();
        newPlan.DatePattern.Start.ShouldNotBe(oldPlan.DatePattern.Start);
        newPlan.DatePattern.ByMonthDay.ShouldBe(new[] { 5 }); // paced to the NEW payday
        newPlan.Amount.ShouldBe(-300m); // still fully paced (same payday count either way)
    }

    [Fact]
    public void The_repace_carries_the_bills_own_jar_balance_forward()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var bill = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        SavePacedBill(bill, [bill, income], carriedOverJarBalance: 150m);

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var confirmation = Confirmation(1, editedIncome);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseToRepaceBills();

        confirmation.Run().ShouldBeTrue();

        var newPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2);
        newPlan.StartingAllocation.ShouldBeGreaterThan(0m); // real money, not silently dropped
    }

    [Fact]
    public void Multiple_invalidated_plans_are_named_together_and_all_updated_on_accept()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var rent = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        var utilities = Bill(3, "Utilities", -100m, new DateOnly(2025, 7, 15), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        var rentPlan = SavePacedBill(rent, [rent, utilities, income]).Plan;
        var utilitiesPlan = SavePacedBill(utilities, [rent, utilities, income]).Plan;

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var confirmation = Confirmation(1, editedIncome);
        string? capturedDescription = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedDescription = request.OptionConsequence(ConfirmationRowIds.PacedBillsCascade, 0);
            return Confirm.Proceed().ChoseToRepaceBills();
        };

        confirmation.Run().ShouldBeTrue();

        capturedDescription.ShouldNotBeNullOrEmpty();
        capturedDescription.ShouldContain("Rent");
        capturedDescription.ShouldContain("Utilities");
        capturedDescription.ShouldContain("2"); // named as a combined "update all" count, not one at a time

        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.ByMonthDay.ShouldBe(new[] { 5 });
        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 3).DatePattern.ByMonthDay.ShouldBe(new[] { 5 });
    }

    [Fact]
    public void Declining_leaves_every_invalidated_plan_completely_untouched()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var rent = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        var utilities = Bill(3, "Utilities", -100m, new DateOnly(2025, 7, 15), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        var rentPlan = SavePacedBill(rent, [rent, utilities, income]).Plan;
        var utilitiesPlan = SavePacedBill(utilities, [rent, utilities, income]).Plan;

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var confirmation = Confirmation(1, editedIncome);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed(); // ChoseToRepaceBills defaults false

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).DatePattern.Start.ShouldBe(rentPlan.DatePattern.Start);
        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 3).DatePattern.Start.ShouldBe(utilitiesPlan.DatePattern.Start);
    }

    [Fact]
    public void No_question_at_all_when_nothing_was_paced_against_the_old_schedule()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var bill = Bill(2, "Netflix", -20m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        _financialPatterns.Save(income, accountId: 1);
        _financialPatterns.Save(bill, accountId: 1);
        // Hand-built, unrelated weekly plan — never paced against income at all.
        var unrelatedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 2,
                Amount = -5m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    Start = new DateOnly(2025, 7, 4),
                    Until = new DateOnly(2026, 6, 30),
                }),
            },
            bill);
        _earMarkPatterns.Save(unrelatedPlan);

        var editedIncome = Income(1, 5, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var confirmation = Confirmation(1, editedIncome);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — nothing was paced against the old schedule");

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 1).DatePattern.ByMonthDay.ShouldBe(new[] { 5 });
    }

    [Fact]
    public void No_question_at_all_when_editing_a_bill_rather_than_income()
    {
        var income = Income(1, 25, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1));
        var bill = Bill(2, "Rent", -300m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(income, accountId: 1);
        var savedBill = SavePacedBill(bill, [bill, income]).Outflow;

        // Editing the BILL's own description only — a plain Trivial edit,
        // and _financeId here is the bill's, not the income's.
        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Rent",
            Amount = -300m,
            Description = "Rent — reminder to autopay",
            DatePattern = savedBill.DatePattern,
        });
        var confirmation = Confirmation(2, editedBill);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — the edited pattern isn't income");

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Description.ShouldBe("Rent — reminder to autopay");
    }

    // ---- shared scenario-building helpers ----------------------------------

    // Builds AND SAVES a real AllocationPlanProposer.Propose result for a
    // bill — both the plan AND proposal.Outflow (NOT the caller's own bill
    // variable): Propose sets ActiveFrom on the outflow whenever its own
    // Start is after AsOf (every bill in this file's own scenarios), and
    // EarMarkPatternRepository.GetAll() re-validates each plan against
    // whatever's actually saved for its own goal — saving the ORIGINAL,
    // un-prepared bill instead throws the moment the plan is read back
    // (found while writing this file: the real calling convention every
    // other caller of Propose already follows, e.g. BreakOffFactory.BreakOff
    // always persists result.Successor, never the pre-call value).
    private ProposedAllocationPlan SavePacedBill(FinancialPattern bill, IReadOnlyList<FinancialPattern> allPatterns, decimal carriedOverJarBalance = 0m)
    {
        var proposal = AllocationPlanProposer.Propose(bill, allPatterns, AsOf, carriedOverJarBalance: carriedOverJarBalance);
        _financialPatterns.Save(proposal.Outflow, accountId: 1);
        _earMarkPatterns.Save(proposal.Plan);
        return proposal;
    }

    private static FinancialPattern Income(int financeId, int dayOfMonth, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = "Paycheck",
            Amount = 3_000m,
            DatePattern = Monthly(dayOfMonth, start, until),
        });

    private static FinancialPattern Bill(int financeId, string source, decimal amount, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            Amount = amount,
            DatePattern = Monthly(start.Day, start, until),
        });

    private static RecurrenceRule Monthly(int dayOfMonth, DateOnly start, DateOnly until) => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [dayOfMonth],
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
