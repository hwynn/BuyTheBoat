using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.Scenario.Tests;

// End-to-end for the App wiring: a bill created
// with its proposed Allocation Plan (and any starting earmark) persists, reads
// back through the repositories — where the earmark repo re-validates each plan
// against its goal on the way out — and the forecast then reserves for it. This
// is the path the create windows drive; nothing UI-free covered it before.
public class AllocationPlanWiringTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;
    private readonly TransferRepository _transfers;

    public AllocationPlanWiringTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
        _transfers = new TransferRepository(database, _financialPatterns);
    }

    [Fact]
    public void A_bill_and_its_proposed_plan_persist_read_back_and_reserve()
    {
        var asOf = new DateOnly(2025, 1, 1);

        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Employer",
            Amount = 3000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [25],
                DtStart = asOf,
                Until = new DateOnly(2026, 1, 1),
            }),
        });
        _financialPatterns.Save(paycheck, accountId: 1);

        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Rent",
            Amount = -300m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 2, 1),
                Until = new DateOnly(2026, 1, 1),
            }),
        });
        _financialPatterns.Save(bill, accountId: 1);

        // The create-window flow: propose the plan from the current as-of date and
        // the user's own-account income, then persist the plan (and any starting
        // earmark) — mirrors AutoCreateAllocationPlan (scoped to
        // the outflow's own account, not household-wide).
        var proposal = AllocationPlanProposer.Propose(
            bill, _financialPatterns.GetByAccountExcludingTransferPatterns(1), asOf);
        _financialPatterns.Save(proposal.Outflow, accountId: 1); // the prepared bill (ActiveFrom set)
        _earMarkPatterns.Save(proposal.Plan);
        if (proposal.StartingEarmark is { } starting)
        {
            _manualEarmarks.Save(starting);
        }

        // Read everything back through the repositories and forecast — exactly
        // what the app does on the next forecast run. The earmark repo's GetAll
        // re-validates every plan against its goal, so a bad round-trip throws here.
        var forecast = TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            StartingBalance = 5000m,
            AsOfDate = asOf,
            HorizonEndDate = new DateOnly(2025, 12, 31),
            FinancialPatterns = _financialPatterns.GetAll(),
            EarMarkPatterns = _earMarkPatterns.GetAll(),
            ManualEarmarks = _manualEarmarks.GetAll(),
        });

        // The bill now has a jar that actually holds money — the whole point.
        var maxBillJar = forecast.Accounts
            .SelectMany(account => account.Page.BalanceRecord.Values)
            .SelectMany(snapshot => snapshot.FundJars)
            .Where(jar => jar.FinanceId == 2)
            .Select(jar => jar.ExpectedAmount)
            .DefaultIfEmpty(0m)
            .Max();
        maxBillJar.ShouldBeGreaterThan(0m);
    }

    // A paycheck filed under a DIFFERENT account than the
    // bill must not be treated as this bill's income. A paced plan copies the
    // income's own Frequency (Weekly here) — if the wrong-account paycheck
    // leaked in, the proposed plan would come back Weekly instead of Monthly.
    [Fact]
    public void An_outflows_plan_does_not_pace_against_another_accounts_income()
    {
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Employer",
            Amount = 3000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                DtStart = new DateOnly(2025, 1, 3),
                Until = new DateOnly(2026, 1, 1),
            }),
        });
        _financialPatterns.Save(paycheck, accountId: 2); // Savings

        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2027, 1, 1),
            }),
        });
        _financialPatterns.Save(bill, accountId: 1); // Checking — a different account

        var proposal = AllocationPlanProposer.Propose(
            bill, _financialPatterns.GetByAccountExcludingTransferPatterns(1), new DateOnly(2025, 1, 1));

        // Front-loaded against the bill's own cadence, not paced against the
        // other account's paycheck.
        proposal.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
    }

    [Fact]
    public void Deleting_a_transfer_also_removes_its_withdrawals_plan()
    {
        var transfer = TransferFactory.Create(new TransferRequest
        {
            TransferId = 1,
            WithdrawalFinanceId = 100,
            DepositFinanceId = 101,
            FromAccountId = 1,
            ToAccountId = 2,
            FromAccountName = "Checking",
            ToAccountName = "Savings",
            Amount = 500m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 3, 1),
                Count = 1,
            }),
        });
        // The wiring gives the withdrawal a front-loaded plan and persists the
        // prepared withdrawal (with ActiveFrom) via the transfer, so its plan fits.
        // spreadEvenlyWithNoIncome: false matches what MainWindow's transfer
        // creation actually passes — a transfer stays plain.
        var plan = AllocationPlanProposer.Propose(
            transfer.Withdrawal, [], new DateOnly(2025, 1, 1), spreadEvenlyWithNoIncome: false);
        _transfers.Save(transfer with { Withdrawal = plan.Outflow });
        _earMarkPatterns.Save(plan.Plan);
        _earMarkPatterns.GetAll().ShouldHaveSingleItem();

        _transfers.Delete(1);

        // The plan is gone with the transfer — and reading plans back does not
        // throw on an orphan whose goal pattern no longer exists.
        _earMarkPatterns.GetAll().ShouldBeEmpty();
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
