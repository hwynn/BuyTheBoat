using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// Exercises FinancePatternSaveConfirmation end to end against a real
// (temporary) SQLite file — same shape as Scenario.Tests' own repository
// tests, just for the App-layer orchestrator instead of the repositories
// directly. Every [Fact] drives Run(): a scenario's own data (which fields
// changed, whether history / a plan / multiple plans exist) determines which
// of the history-aware confirmations actually fires, and each test wires a
// ConfirmImplicitChanges double to answer the questions it expects — or leaves
// it unset, in which case DefaultOutcome answers every question with its
// safest, least-destructive default. One [Fact] per named case — add to this
// set rather than growing any one test as more of Items C-F get built for real.
public class FinancePatternSaveConfirmationTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2025, 6, 15);
    private static readonly DateOnly HorizonEnd = new(2025, 12, 31);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public FinancePatternSaveConfirmationTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    [Fact]
    public void Amount_changed_after_history_exists_breaks_off_into_a_new_finance_id_and_leaves_the_original_untouched()
    {
        var bill = Bill(1, "Electric Co", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);

        // Funds slightly ahead of the bill's own $100/month need, so a real,
        // non-zero buffer has built up by AsOf below — a $0 carry-over would
        // be indistinguishable from a broken one.
        _earMarkPatterns.Save(Plan(bill, -120m, bill.DatePattern.ActiveStart, bill.DatePattern.Until));

        var forecast = Forecast();

        // Independently read what the jar actually holds at the cut date, off
        // the same forecast the class under test reads — this is the "did the
        // carry-over actually get plumbed through" oracle below, not a
        // hand-derived cascade calculation (that math is Domain's own to
        // test, not this class's).
        var expectedCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars
            .Single(jar => jar.FinanceId == 1).ExpectedAmount;
        expectedCarriedOverBalance.ShouldBeGreaterThan(0m);

        // Only Amount changed — Start/Until/schedule are untouched, so this is
        // a clean test of the amount-change trigger specifically.
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2);

        var truncatedOriginal = patterns.Single(p => p.FinanceId == 1);
        truncatedOriginal.Amount.ShouldBe(-100m); // the proposed edit never lands on the original id
        truncatedOriginal.DatePattern.Until.ShouldBe(AsOf.AddDays(-1));

        var successorBill = patterns.Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorBill.Amount.ShouldBe(-150m); // the proposed edit lands here instead
        successorBill.DatePattern.ActiveStart.ShouldBe(AsOf);
        successorBill.DatePattern.Until.ShouldBe(bill.DatePattern.Until);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);

        plans.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));

        var successorPlan = plans.Single(p => p.FinanceId == 2);
        successorPlan.DatePattern.ActiveStart.ShouldBe(AsOf);
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance);
    }

    // The plan-shape candidates: the existing plan's own shape
    // (biweekly, not the bill's own monthly cadence) makes ProposeSameSchedule's
    // candidate structurally distinct from Propose's own default (which, with
    // no income pattern in this scenario, falls back to the bill's own
    // monthly cadence) — proves the popup's own choice actually reaches the
    // saved successor, not just that a candidate list gets built.
    [Fact]
    public void Item_G_a_chosen_plan_shape_candidate_is_the_one_that_actually_gets_saved()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        var existingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = bill.FinanceId,
                Amount = -30m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    Interval = 2,
                    DtStart = new DateOnly(2025, 1, 3), // a Friday
                    Until = bill.DatePattern.Until,
                }),
            },
            bill);
        _earMarkPatterns.Save(existingPlan);

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            // The fake "popup" itself — reads the real candidates this Run()
            // actually built, the same way a real one would, rather than a
            // value the test just hands back blind.
            request.PlanShapeCandidates().Count.ShouldBeGreaterThan(1);
            var sameSchedule = request.PlanShapeCandidates().Single(c => c.Label == "Keep the same schedule");
            return Confirm.Proceed().WithPlanShape(sameSchedule.Plan.Plan);
        };

        confirmation.Run().ShouldBeTrue();

        var successorPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorPlan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        successorPlan.DatePattern.Interval.ShouldBe(2);
        // Same phase as the existing plan's own Friday cycle, re-anchored —
        // not the bill's own monthly cadence Propose's default would have used.
        existingPlan.DatePattern.GetOccurrences().ShouldContain(successorPlan.DatePattern.DtStart);
    }

    // BuildSuccessorSchedule copies the edited pattern's own Frequency/
    // Interval/ByDay/ByMonthDay but must not set DtStart = cutDate directly — for a
    // Weekly pattern with an empty ByDay (RecurrenceRuleEditor's own
    // checkboxes let a real user leave every one unchecked), RFC 5545 ties
    // an omitted BYDAY to DTSTART's own weekday, so the successor's own
    // occurrences would silently land on cutDate's weekday instead of the pattern's
    // intended one — the same weekday-drift ProposePaced guards against.
    // AsOf (2025-06-15) is
    // deliberately a Sunday, and the edited bill's own intended cadence is
    // biweekly Fridays (anchored 2025-01-03) — a day that never coincides
    // with a Sunday, so any drift shows up unmistakably.
    [Fact]
    public void Breaking_off_a_biweekly_bill_with_an_implicit_weekday_keeps_its_own_original_phase()
    {
        var bill = Bill(1, "Storage Unit Rental", -50m, new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = bill.Source,
            Amount = -50m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                // ByDay left empty on purpose — the exact real-world shape
                // RecurrenceRuleEditor lets a user save.
                DtStart = new DateOnly(2025, 1, 3), // a Friday
                Until = new DateOnly(2026, 12, 31),
            }),
        });

        Confirmation(1, editedBill, accountId: 1, forecast).Run().ShouldBeTrue();

        var successorBill = _financialPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        var firstOccurrence = successorBill.DatePattern.GetOccurrences()[0];
        // The biweekly-from-Jan-3 series' own next real Friday on/after the
        // cut date (AsOf, 2025-06-15) is 2025-06-20 — not AsOf itself, and
        // never a Sunday.
        firstOccurrence.ShouldBe(new DateOnly(2025, 6, 20));
        firstOccurrence.DayOfWeek.ShouldBe(DayOfWeek.Friday);
    }

    // The broader case — an EXPLICIT ByDay
    // does not, on its own, protect an Interval > 1 Weekly pattern from the
    // same drift (RecurrenceRuleTests.Explicit_byday_alone_does_not_protect_an_intervals_own_week_phase_when_start_is_pinned_elsewhere
    // proves this at the raw ical.net level; this proves it flows correctly
    // through the real fix end to end). Same scenario as the test above,
    // ByDay spelled out this time — if BuildSuccessorSchedule's own fix
    // only special-cased the empty-ByDay case, this would still fail.
    [Fact]
    public void Breaking_off_a_biweekly_bill_with_an_explicit_weekday_still_keeps_its_own_original_phase()
    {
        var bill = Bill(1, "Storage Unit Rental", -50m, new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = bill.Source,
            Amount = -50m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                ByDay = [DayOfWeek.Friday], // explicit this time
                DtStart = new DateOnly(2025, 1, 3),
                Until = new DateOnly(2026, 12, 31),
            }),
        });

        Confirmation(1, editedBill, accountId: 1, forecast).Run().ShouldBeTrue();

        var successorBill = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        successorBill.DatePattern.GetOccurrences()[0].ShouldBe(new DateOnly(2025, 6, 20));
    }

    // Locks in the deliberate scoping decision behind the two fixes above:
    // Monthly always carries an explicit ByMonthDay, so ical.net already
    // finds the right day regardless of where Start falls — the successor's
    // own Start stays exactly on the cut date, unlike the Weekly case.
    // Already covered incidentally by Amount_changed_after_history_exists_
    // breaks_off_into_a_new_finance_id_and_leaves_the_original_untouched
    // above; this names the invariant directly so a future change to
    // BuildSuccessorSchedule can't silently widen the Weekly-only branch
    // without a test catching it.
    [Fact]
    public void Breaking_off_a_monthly_bill_still_starts_the_successor_exactly_on_the_cut_date()
    {
        var bill = Bill(1, "Electric Co", -100m, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run().ShouldBeTrue();

        var successorBill = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        successorBill.DatePattern.ActiveStart.ShouldBe(AsOf);
        successorBill.DatePattern.ToOptions().ActiveFrom.ShouldBeNull();
    }

    // The "glut case" for candidate previews: the "Recommended" candidate's
    // own preview must read the real carried-over balance as its
    // StartingAllocation, matching what BreakOffFactory.BreakOff saves once
    // any candidate is chosen — not 0, which would be a preview-vs-saved
    // mismatch. Explicitly chooses "Recommended" (by the same object
    // reference the candidate itself carried) so this exercises
    // DeterminePlanShapeCandidatesIfApplicable's own construction, not
    // just BreakOff's fallback override.
    [Fact]
    public void Item_G_the_Recommended_candidates_own_preview_matches_what_actually_gets_saved_for_StartingAllocation()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        // Over-contributes every month, so a real, verifiable balance has
        // built up by AsOf — the exact scenario this fix protects.
        _earMarkPatterns.Save(Plan(bill, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until));

        var forecast = Forecast();
        var realCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(j => j.FinanceId == 1).ExpectedAmount;
        realCarriedOverBalance.ShouldBeGreaterThan(0m); // confirms this scenario actually exercises the fix, not a $0 no-op

        var editedBill = Bill(1, bill.Source, -120m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);

        EarMarkPattern? recommendedPreview = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            var recommended = request.PlanShapeCandidates().Single(c => c.Label == "Recommended").Plan.Plan;
            recommendedPreview = recommended;
            return Confirm.Proceed().WithPlanShape(recommended);
        };

        confirmation.Run().ShouldBeTrue();

        var savedSuccessor = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play

        // The whole point: what the picker showed BEFORE the user chose
        // anything matches what actually landed in storage — not $0 in the
        // preview with the real balance only appearing after the fact.
        recommendedPreview.ShouldNotBeNull();
        recommendedPreview!.StartingAllocation.ShouldBe(realCarriedOverBalance);
        savedSuccessor.StartingAllocation.ShouldBe(realCarriedOverBalance);
    }

    // The break-off "Recommended" candidate is held to what the funds can afford, the same as every other
    // suggestion — and, crucially, taking the DEFAULT (not picking a candidate explicitly) saves that same
    // capped plan rather than an uncapped re-derivation. Raising the bill wants a ~2000/cycle plan, but with
    // only ~600 free the plan is knowingly underfunded rather than reserving money that isn't there; the "Keep
    // the same schedule/amount" candidates beside it deliberately stay uncapped. Income covers the bill so free
    // funds hold near the injected balance, keeping the ceiling stable.
    [Fact]
    public void The_break_off_recommended_candidate_is_held_to_what_the_free_funds_can_afford()
    {
        var income = Bill(100, "Job", 3000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var bill = Bill(1, "Storage Unit", -1000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(income, accountId: 1);
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -1000m, bill.DatePattern.ActiveStart, bill.DatePattern.Until));

        // ~600 free in the room re-forecast (income covers the bill, so free funds hold near this balance).
        Func<IReadOnlySet<int>, ForecastResult> tightRoom = omitIds =>
            TransactionLogBookFactory.CreateForecast((ForecastOptionsForTest() with { StartingBalance = 600m }).WithoutPlansFor(omitIds));

        var editedBill = Bill(1, bill.Source, -2000m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), tightRoom);

        decimal? shownRecommendedRate = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            shownRecommendedRate = Math.Abs(request.PlanShapeCandidates().Single(c => c.Label == "Recommended").Plan.Plan.Amount);
            return Confirm.Proceed(); // take the default — no explicit pick, the common path
        };

        confirmation.Run().ShouldBeTrue();

        // The picker shows a capped "Recommended", and taking the default saves that same capped plan.
        shownRecommendedRate!.Value.ShouldBeLessThan(1000m);
        var savedRate = Math.Abs(_earMarkPatterns.GetAll().Single(p => p.FinanceId != 1).Amount); // the successor (a new id)
        savedRate.ShouldBeLessThan(1000m);          // held below the ~2000/cycle the raise wants — the ceiling bound it
        savedRate.ShouldBeGreaterThan(0m);          // still a real, positive contribution
        savedRate.ShouldBe(shownRecommendedRate.Value); // saved == shown, no preview/save divergence
    }

    // The break-off FALLBACK — a genuinely concurrent multi-plan set builds no candidate picker, so
    // BreakOffFactory proposes the one consolidated successor plan itself. That fresh proposal is capped too:
    // with only ~600 free, the raised bill's ~2000/cycle consolidated plan is held down rather than
    // over-reserving. This is the path with no shown candidate to fall back to.
    [Fact]
    public void A_break_off_with_no_candidate_picker_still_caps_the_fallback_plan()
    {
        var income = Bill(100, "Job", 3000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var bill = Bill(1, "Storage Unit", -1000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(income, accountId: 1);
        _financialPatterns.Save(bill, accountId: 1);
        // Two concurrent plans → no single "current" plan → no candidate picker → the fallback path.
        _earMarkPatterns.Save(Plan(bill, -600m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -400m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        Func<IReadOnlySet<int>, ForecastResult> tightRoom = omitIds =>
            TransactionLogBookFactory.CreateForecast((ForecastOptionsForTest() with { StartingBalance = 600m }).WithoutPlansFor(omitIds));

        var editedBill = Bill(1, bill.Source, -2000m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), tightRoom);

        ImplicitChangeConfirmationRequest? captured = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed().ChoseConsolidation();
        };

        confirmation.Run().ShouldBeTrue();

        captured!.PlanShapeCandidates().ShouldBeEmpty(); // confirms this really is the no-picker fallback path
        var savedRate = Math.Abs(_earMarkPatterns.GetAll().Single(p => p.FinanceId != 1).Amount); // the consolidated successor
        savedRate.ShouldBeLessThan(1000m); // ~2000/cycle wanted, held under the ~600 ceiling
        savedRate.ShouldBeGreaterThan(0m);
    }

    // A Savings Plan that's
    // already been restructured once (two sequential EarMarkPatterns sharing
    // one finance_id — an earlier, since-superseded segment plus the one
    // that's actually current) still gets the shape choice: it has exactly one
    // genuinely current segment, so HasMultipleEarmarkPatterns being true
    // must not disable candidates.
    // RestructureFactory.FindCurrentPlan is what tells this apart from a
    // concurrent set (the regression test right below). Proves candidates
    // build from the CURRENT segment's own shape specifically, not the
    // superseded one, and that the chosen one actually gets saved.
    [Fact]
    public void Item_G_candidates_build_from_the_current_segment_of_an_already_restructured_plan()
    {
        var bill = Bill(1, "Storage Unit Rental", -50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        // Matches the bill's own original rate exactly, so no meaningful
        // glut or shortfall builds up before the restructure — an earlier,
        // since-superseded segment.
        _earMarkPatterns.Save(Plan(bill, -50m, bill.DatePattern.ActiveStart, new DateOnly(2025, 3, 31)));
        var currentPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = bill.FinanceId,
                Amount = -20m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    Interval = 2,
                    DtStart = new DateOnly(2025, 4, 4), // a Friday
                    Until = bill.DatePattern.Until,
                }),
            },
            bill);
        _earMarkPatterns.Save(currentPlan);

        var forecast = Forecast();
        // A large jump, not just a bump — guarantees the goal's own
        // remaining need vastly exceeds whatever's already banked, so
        // ProposeSameSchedule/ProposeSameAmount have real work to do
        // regardless of the exact pre-edit balance.
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            var sameSchedule = request.PlanShapeCandidates().Single(c => c.Label == "Keep the same schedule");
            // Amount-only change, so ConsolidationNeeded is naturally
            // false (that row is meant to be feasible to keep separate)
            // — has to be chosen explicitly to reach PerformMultiPlanBreakOff
            // at all, same as any other multi-plan break-off.
            return Confirm.Proceed().ChoseConsolidation().WithPlanShape(sameSchedule.Plan.Plan);
        };

        confirmation.Run().ShouldBeTrue();

        var successorPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorPlan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        successorPlan.DatePattern.Interval.ShouldBe(2);
        // Same phase as the current segment's own Friday cycle, re-anchored —
        // not the superseded segment's own $80 rate, and not the bill's own
        // monthly cadence Propose's default would have used.
        currentPlan.DatePattern.GetOccurrences().ShouldContain(successorPlan.DatePattern.DtStart);
    }

    // The candidate-building above must not reach into the
    // concurrent earmark pattern case, where the author's own ruling
    // says no shape choice should be offered at all —
    // "it'll already be complicated enough" once the not-yet-built
    // size-both-plans-in-unison mechanism exists.
    [Fact]
    public void Item_G_still_offers_no_candidates_for_a_genuinely_concurrent_predecessor()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        // Two concurrent earmark patterns, staggered by a day, both active
        // across nearly the whole range — same shape this file's own
        // A_break_off_with_multiple_surviving_plans_kept_separate_is_a_safe_no_op_for_now
        // already uses for the concurrent case.
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        ImplicitChangeConfirmationRequest? capturedRequest = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedRequest = request;
            return Confirm.Proceed().ChoseConsolidation();
        };

        confirmation.Run().ShouldBeTrue();

        capturedRequest.ShouldNotBeNull();
        capturedRequest!.PlanShapeCandidates().ShouldBeEmpty();
    }

    // Assumption 1.2.3.10.a5 only restricts start_date/amount/recurrence shape —
    // description/source/priority/mandatory stay plain edits regardless of
    // history (the field categorization). Changing
    // ONLY a Trivial field must never trip a break-off, even with a full
    // history of past occurrences behind it.
    [Fact]
    public void A_trivial_only_edit_never_breaks_off_even_with_history()
    {
        var bill = Bill(1, "Streaming Service", -15.99m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = bill.FinanceId,
            Source = bill.Source,
            Amount = bill.Amount,
            DatePattern = bill.DatePattern,
            Description = "Family plan, added a member", // the only thing that changed
        });

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.ShouldHaveSingleItem(); // no break-off — still one row, same finance_id
        patterns[0].FinanceId.ShouldBe(1);
        patterns[0].Description.ShouldBe("Family plan, added a member");
        patterns[0].Amount.ShouldBe(-15.99m);
    }

    // The end_date carve-out: a pattern with no expected
    // transaction on or before the as-of date isn't protected at all, no
    // matter which field changes — there's no history yet to alter. A
    // future-dated bill already saved in the database is exactly that case.
    [Fact]
    public void A_critical_field_change_on_a_pattern_with_no_past_occurrence_yet_is_a_plain_edit()
    {
        var bill = Bill(1, "New Gym Membership", -45m, new DateOnly(2025, 9, 1), new DateOnly(2026, 9, 1)); // starts after AsOf
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -60m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // amount is normally Critical...

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.ShouldHaveSingleItem(); // ...but there's nothing to protect yet, so still a plain edit
        patterns[0].FinanceId.ShouldBe(1);
        patterns[0].Amount.ShouldBe(-60m);
    }

    // A Critical field change on a pattern whose only occurrence
    // is the as-of day has no settled history to preserve — a break-off there
    // would leave a zero-day predecessor and BreakOffFactory would reject the cut,
    // so it's a plain in-place replace instead.
    // So it's a plain in-place replace, not a break-off, and nothing throws.
    [Fact]
    public void A_critical_edit_on_a_pattern_that_started_today_replaces_in_place_instead_of_breaking_off()
    {
        var bill = Bill(1, "House Payment", -887m, AsOf, new DateOnly(2035, 6, 15)); // starts exactly on the as-of day
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -900m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.ShouldHaveSingleItem();     // no break-off — the negligible old segment was simply overwritten
        patterns[0].FinanceId.ShouldBe(1);   // same identity, not a fresh successor id
        patterns[0].Amount.ShouldBe(-900m);
    }

    // The boundary: one day of history is still "no more than one day," so it also
    // replaces in place rather than breaking off.
    [Fact]
    public void A_critical_edit_with_only_one_day_of_history_replaces_in_place()
    {
        var bill = Bill(1, "House Payment", -887m, AsOf.AddDays(-1), new DateOnly(2035, 6, 14));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -900m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().ShouldHaveSingleItem();
    }

    // Just past the boundary: two days of history IS worth preserving, so the edit
    // breaks off exactly as before — the replace shortcut is deliberately narrow.
    [Fact]
    public void A_critical_edit_with_two_days_of_history_still_breaks_off()
    {
        var bill = Bill(1, "House Payment", -887m, AsOf.AddDays(-2), new DateOnly(2035, 6, 13));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -900m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Count.ShouldBe(2); // truncated predecessor + successor
    }

    // BreakOffFactory.BreakOff always fresh-proposes the successor's plan for
    // an outflow, regardless of whether the predecessor had one — existing
    // factory behavior, not new here. Worth locking in explicitly: editing a
    // Critical field on a bill that was never given a plan doesn't just break
    // it off, it also gives the successor a plan the original never had.
    [Fact]
    public void An_outflow_with_no_existing_plan_still_gets_a_fresh_zero_seeded_plan_on_break_off()
    {
        var bill = Bill(1, "Music Subscription", -12m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1); // no EarMarkPattern saved for it
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -18m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Count.ShouldBe(2); // the break-off still happened

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem(); // predecessor never had one; only the successor's fresh plan exists
        var successorPlan = plans[0];
        successorPlan.FinanceId.ShouldBe(2);
        successorPlan.StartingAllocation.ShouldBe(0m); // nothing to carry over — there was no predecessor jar
    }

    // BreakOffFactory.BreakOff's income shortcut: Amount >= 0 skips the jar
    // machinery entirely, PredecessorPlan and AllocationPlanProposer both
    // unused. A raise (income's amount going up) after history exists is the
    // natural real-world trigger for this path.
    [Fact]
    public void Income_breaks_off_with_no_jar_involved_at_all()
    {
        var paycheck = Bill(1, "Employer Inc", 3000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), byMonthDay: 25);
        _financialPatterns.Save(paycheck, accountId: 1);
        var forecast = Forecast();

        var raise = Bill(1, paycheck.Source, 3200m, paycheck.DatePattern.ActiveStart, paycheck.DatePattern.Until, byMonthDay: 25);

        Confirmation(1, raise, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2);
        patterns.Single(p => p.FinanceId == 1).Amount.ShouldBe(3000m); // predecessor keeps the old rate
        patterns.Single(p => p.FinanceId == 2).Amount.ShouldBe(3200m); // successor carries the raise

        _earMarkPatterns.GetAll().ShouldBeEmpty(); // income never gets a jar, predecessor or successor
    }

    // The keep-them-separate sub-case. With more than one surviving plan and
    // nothing forcing consolidation (amount-only, so the schedule/start are
    // untouched), the user's default "keep them separate" stands: the successor
    // gets one plan per surviving plan, each continuing its own rate at its own
    // cadence, rather than folding into one. The finance_id's one combined jar
    // balance rides on a single successor plan.
    [Fact]
    public void A_break_off_with_multiple_surviving_plans_keeps_them_separate_by_default()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);

        // Two concurrent earmark patterns on the same goal — different rates, so
        // they'd never be merged back together even if offered the chance.
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var expectedCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars
            .Single(jar => jar.FinanceId == 1).ExpectedAmount;
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // amount only — recurrence shape untouched

        Confirmation(1, editedBill, accountId: 1, forecast).Run(); // no delegate — takes the "keep separate" default

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2); // the break-off happened
        patterns.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));
        patterns.Single(p => p.FinanceId == 2).Amount.ShouldBe(-500m);

        var plans = _earMarkPatterns.GetAll();
        plans.Count(p => p.FinanceId == 1).ShouldBe(2); // both originals, truncated
        plans.Where(p => p.FinanceId == 1).ShouldAllBe(p => p.DatePattern.Until == AsOf.AddDays(-1));

        var successorPlans = plans.Where(p => p.FinanceId == 2).ToList();
        successorPlans.Count.ShouldBe(2); // one per surviving plan — kept separate, not folded into one
        successorPlans.Select(p => p.Amount).ShouldBe(new[] { -300m, -120m }, ignoreOrder: true); // each keeps its own rate
        successorPlans.Sum(p => p.StartingAllocation).ShouldBe(expectedCarriedOverBalance); // the one combined jar's balance, carried once
    }

    // The other side of the same consolidation question: when the user explicitly picks
    // "combine them into one," the break-off folds every surviving plan into a
    // single freshly-proposed successor plan seeded with the combined jar balance
    // — the same result a forced (shape/start-change) consolidation produces.
    [Fact]
    public void A_break_off_combines_multiple_surviving_plans_when_the_user_chooses_to()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var expectedCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars
            .Single(jar => jar.FinanceId == 1).ExpectedAmount;
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // amount only

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().ChoseConsolidation();

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(3); // both original plans, truncated, plus ONE consolidated successor
        plans.Count(p => p.FinanceId == 1).ShouldBe(2);

        var successorPlan = plans.Single(p => p.FinanceId == 2); // exactly one — folded into the combined plan the user asked for
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance);
    }

    // The keep-separate funding question: raising the amount leaves
    // the kept-separate plans contributing at the old, now-too-low total, so the
    // confirmation offers to re-rate them to meet the new amount — its OWN nested
    // question under "keep them separate," never a top-level row and never merged
    // into the keep-separate/combine choice.
    [Fact]
    public void Keeping_plans_separate_that_would_misfund_the_new_amount_offers_to_adjust_them()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // bigger — the old rates fall short

        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured!.HasRow(ConfirmationRowIds.KeepSeparateFunding).ShouldBeTrue(); // offered
        captured.Rows.OfType<ChoiceRow>().ShouldNotContain(r => r.Id == ConfirmationRowIds.KeepSeparateFunding); // not a top-level row
        var consolidation = captured.Rows.OfType<ChoiceRow>().Single(r => r.Id == ConfirmationRowIds.Consolidation);
        consolidation.Options[0].Children.OfType<ChoiceRow>().ShouldContain(r => r.Id == ConfirmationRowIds.KeepSeparateFunding); // nested under "keep them separate"
    }

    // Taking that offer re-rates the kept-separate plans by one shared ratio so
    // they still keep their own relative split but together meet the new amount —
    // the plans stay several, their amounts move off the raw carried-over rates,
    // and their 300:120 proportion is preserved. (That the re-rated total actually
    // funds the goal is EarmarkScalingTests' job; here we prove the wiring applies
    // the scale, where the default "leave them" — tested above — keeps them raw.)
    [Fact]
    public void Adjusting_kept_separate_plans_re_rates_them_proportionally_off_their_raw_rates()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().ChoseToAdjustKeptSeparatePlans();

        confirmation.Run().ShouldBeTrue();

        var successorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 2).ToList();
        successorPlans.Count.ShouldBe(2); // still separate — one per surviving plan
        successorPlans.ShouldNotContain(p => p.Amount == -300m); // re-rated off...
        successorPlans.ShouldNotContain(p => p.Amount == -120m); // ...the raw carried-over rates

        var magnitudes = successorPlans.Select(p => Math.Abs(p.Amount)).OrderByDescending(x => x).ToList();
        (magnitudes[0] / magnitudes[1]).ShouldBe(2.5m, 0.02m); // the 300:120 split is preserved through the scale
    }

    // The goal-health suggestion (the deferred picker):
    // editing a FUTURE goal so its single plan no longer meets it offers a
    // correction — an accept/reject question whose "accept" pre-fills that plan's
    // own form with the fix as an unsaved edit (not saved implicitly, since there
    // IS a single form we can open). userSkippedPlanning: false throughout — the
    // suggestion only matters when a form actually opens.
    [Fact]
    public void Editing_a_goal_so_its_single_plan_falls_short_offers_a_correction_suggestion()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)); // future — non-Critical
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)));

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // now needs far more

        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured!.HasRow(ConfirmationRowIds.GoalHealthSuggestion).ShouldBeTrue();
    }

    [Fact]
    public void The_goal_health_suggestions_reject_option_warns_what_leaving_it_would_cost()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)));

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // now needs more → underfunds

        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        var suggestion = captured!.Rows.OfType<ChoiceRow>().Single(row => row.Id == ConfirmationRowIds.GoalHealthSuggestion);
        suggestion.Options[1].Label.ShouldBe("Leave it as is");
        suggestion.Options[1].Consequence.ShouldContain("fall short"); // the reject option now names the cost
        suggestion.Options[0].Consequence.ShouldBeNullOrEmpty();       // ...while accepting stays consequence-free
    }

    [Fact]
    public void Accepting_the_goal_health_suggestion_pre_fills_the_plan_form_with_the_corrected_amount()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)));

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        IReadOnlyDictionary<string, object?>? capturedOverrides = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().AcceptedGoalHealthSuggestion();
        confirmation.NavigateToEarmarkForm = (_, overrides) => capturedOverrides = overrides;

        confirmation.Run().ShouldBeTrue();

        capturedOverrides.ShouldNotBeNull();
        ((decimal)capturedOverrides![EarmarkFieldOverrideKeys.Amount]!).ShouldBe(-100m); // -40 scaled x2.5 to meet the -100 goal

        _earMarkPatterns.GetAll().Single().Amount.ShouldBe(-40m); // the plan itself is NOT saved — the fix rides into the form for the user to save
    }

    [Fact]
    public void The_goal_health_suggestion_is_held_to_what_the_free_funds_can_afford()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)));

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        // A near-empty balance: the "room for this plan" ceiling can't reach the -100/cycle the goal now
        // needs, so the suggested contribution is held below it (the same edit with generous funds reads
        // -100, in Accepting_the_goal_health_suggestion... above).
        Func<IReadOnlySet<int>, ForecastResult> tightRoom = omitIds =>
            TransactionLogBookFactory.CreateForecast((ForecastOptionsForTest() with { StartingBalance = 250m }).WithoutPlansFor(omitIds));

        IReadOnlyDictionary<string, object?>? capturedOverrides = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), tightRoom, userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().AcceptedGoalHealthSuggestion();
        confirmation.NavigateToEarmarkForm = (_, overrides) => capturedOverrides = overrides;

        confirmation.Run().ShouldBeTrue();

        capturedOverrides.ShouldNotBeNull();
        var suggested = Math.Abs((decimal)capturedOverrides![EarmarkFieldOverrideKeys.Amount]!);
        suggested.ShouldBeLessThan(100m); // capped below the -100 goal — the affordability ceiling bound it
        suggested.ShouldBeGreaterThan(0m); // ...but a real, positive contribution, not zeroed out
    }

    [Fact]
    public void Declining_the_goal_health_suggestion_opens_the_form_with_no_overrides()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)));

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var navigated = false;
        IReadOnlyDictionary<string, object?>? capturedOverrides = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().DeclinedGoalHealthSuggestion();
        confirmation.NavigateToEarmarkForm = (_, overrides) => { navigated = true; capturedOverrides = overrides; };

        confirmation.Run().ShouldBeTrue();

        navigated.ShouldBeTrue();         // the form still opens...
        capturedOverrides.ShouldBeNull(); // ...just with nothing pre-filled
    }

    [Fact]
    public void No_goal_health_suggestion_when_the_single_plan_already_meets_the_edited_goal()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -100m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1))); // already saving -100/mo

        var editedBill = Bill(1, bill.Source, -100m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // raise the goal to exactly what it's saving

        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false);
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run();

        (captured?.HasRow(ConfirmationRowIds.GoalHealthSuggestion) ?? false).ShouldBeFalse(); // plan already meets it — nothing to offer
    }

    // The OVERfunded goal-health case ("skip some events"): shrinking
    // a goal so its plan now over-saves offers a NESTED question — lower the rate,
    // or keep the rate and (a sub-question) skip some upcoming contributions.
    private (FinancialPattern edited, FinancePatternSaveConfirmation confirmation) OverfundedGoalHealthScenario()
    {
        var bill = Bill(1, "Gym Membership", -100m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1)); // future — non-Critical
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -100m, new DateOnly(2025, 8, 1), new DateOnly(2026, 8, 1))); // saving -100/mo

        var editedBill = Bill(1, bill.Source, -40m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // goal shrinks → plan overfunds
        return (editedBill, Confirmation(1, editedBill, accountId: 1, Forecast(), userSkippedPlanning: false));
    }

    [Fact]
    public void An_overfunded_goal_offers_lower_the_rate_or_keep_it_and_skip_events()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        ImplicitChangeConfirmationRequest? captured = null;
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        var question = captured!.Rows.OfType<ChoiceRow>().Single(row => row.Id == ConfirmationRowIds.GoalHealthSuggestion);
        question.Options.Count.ShouldBe(2);
        question.Options[0].Label.ShouldStartWith("Lower the contribution"); // recommended, pre-selected
        question.DefaultIndex.ShouldBe(0);
        question.Options[1].Label.ShouldBe("Keep saving at this rate");
        question.Options[1].Consequence.ShouldContain("tying up money"); // the cost of keeping the rate

        // The skip sub-question nests under "keep saving at this rate."
        var skip = question.Options[1].Children.OfType<ChoiceRow>().Single(row => row.Id == ConfirmationRowIds.GoalHealthSkip);
        skip.Options.Select(option => option.Label).ShouldContain("Skip the next contribution");
        skip.Options.Select(option => option.Label).ShouldContain("Skip a stretch to clear the surplus");
        skip.Options[^1].Label.ShouldBe("Don't skip any");
        skip.DefaultIndex.ShouldBe(skip.Options.Count - 1); // "Don't skip any" is the default
    }

    [Fact]
    public void A_single_plan_goal_health_edit_is_not_described_as_having_multiple_plans()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        ImplicitChangeConfirmationRequest? captured = null;
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        captured!.Description.ShouldNotContain("more than one savings plan"); // it's a single-plan goal
        captured.Description.ShouldContain("amount");                          // states plainly what changed
    }

    [Fact]
    public void Lowering_an_overfunded_plans_rate_pre_fills_the_form_with_the_reduced_amount()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        IReadOnlyDictionary<string, object?>? overrides = null;
        confirmation.ConfirmImplicitChanges = request => Confirm.Proceed().AcceptedGoalHealthSuggestion(); // index 0 = lower the rate
        confirmation.NavigateToEarmarkForm = (_, captured) => overrides = captured;

        confirmation.Run().ShouldBeTrue();

        ((decimal)overrides![EarmarkFieldOverrideKeys.Amount]!).ShouldBe(-40m); // -100 scaled down to meet the -40 goal
        _earMarkPatterns.GetAll().Single().Amount.ShouldBe(-100m);             // not saved — rides into the form for review
    }

    [Fact]
    public void Keeping_the_rate_and_skipping_the_next_contribution_pre_fills_the_excluded_date()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        IReadOnlyDictionary<string, object?>? overrides = null;
        confirmation.ConfirmImplicitChanges = request => new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = new Dictionary<string, int>
            {
                [ConfirmationRowIds.GoalHealthSuggestion] = 1, // keep the rate
                [ConfirmationRowIds.GoalHealthSkip] = 0,       // skip the next contribution
            },
        };
        confirmation.NavigateToEarmarkForm = (_, captured) => overrides = captured;

        confirmation.Run().ShouldBeTrue();

        var excluded = (IReadOnlyList<DateOnly>)overrides![EarmarkFieldOverrideKeys.ExcludedDates]!;
        excluded.ShouldBe([new DateOnly(2025, 8, 1)]); // just the soonest upcoming contribution
    }

    [Fact]
    public void Keeping_the_rate_and_skipping_a_stretch_pre_fills_several_excluded_dates()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        IReadOnlyDictionary<string, object?>? overrides = null;
        confirmation.ConfirmImplicitChanges = request => new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = new Dictionary<string, int>
            {
                [ConfirmationRowIds.GoalHealthSuggestion] = 1, // keep the rate
                [ConfirmationRowIds.GoalHealthSkip] = 1,       // skip a stretch
            },
        };
        confirmation.NavigateToEarmarkForm = (_, captured) => overrides = captured;

        confirmation.Run().ShouldBeTrue();

        // Surplus 780 / 100 per contribution = 7 whole contributions safe to skip.
        var excluded = (IReadOnlyList<DateOnly>)overrides![EarmarkFieldOverrideKeys.ExcludedDates]!;
        excluded.Count.ShouldBe(7);
        excluded[0].ShouldBe(new DateOnly(2025, 8, 1)); // soonest first
    }

    [Fact]
    public void Keeping_an_overfunded_rate_without_skipping_pre_fills_nothing()
    {
        var (_, confirmation) = OverfundedGoalHealthScenario();
        var navigated = false;
        IReadOnlyDictionary<string, object?>? overrides = null;
        confirmation.ConfirmImplicitChanges = request => new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = new Dictionary<string, int> { [ConfirmationRowIds.GoalHealthSuggestion] = 1 }, // keep the rate, skip left at its default
        };
        confirmation.NavigateToEarmarkForm = (_, captured) => { navigated = true; overrides = captured; };

        confirmation.Run().ShouldBeTrue();

        navigated.ShouldBeTrue();
        overrides.ShouldBeNull(); // "don't skip any" is the default → nothing pre-filled
    }

    // The forced-consolidation sub-case: the
    // recurrence shape changing makes ConsolidationNeeded true, which combines
    // regardless of the user's keep-separate/combine pick — the plans can't keep
    // their own occurrence dates onto a differently shaped successor. So this,
    // unlike the amount-only case above, consolidates even on the default answer.
    [Fact]
    public void A_recurrence_shape_change_with_multiple_surviving_plans_forces_a_consolidated_break_off()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var expectedCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars
            .Single(jar => jar.FinanceId == 1).ExpectedAmount;

        // The due date itself moves (1st -> 15th) — a recurrence-shape
        // change, not just an amount change.
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.ActiveStart, bill.DatePattern.Until, byMonthDay: 15);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2); // the break-off happened this time
        patterns.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));
        patterns.Single(p => p.FinanceId == 2).DatePattern.ActiveStart.ShouldBe(AsOf);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(3); // both original plans, truncated, plus ONE consolidated successor
        plans.Count(p => p.FinanceId == 1).ShouldBe(2);
        plans.Where(p => p.FinanceId == 1).ShouldAllBe(p => p.DatePattern.Until == AsOf.AddDays(-1));

        var successorPlan = plans.Single(p => p.FinanceId == 2); // exactly one — consolidated, not two
        successorPlan.DatePattern.ActiveStart.ShouldBe(AsOf);
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance); // the ONE combined jar's balance, not either plan's own share
    }

    // The end_date back-boundary invariant (3.11.2.a2): end_date
    // is always a plain, never-Critical edit (the field categorization
    // table), but that only ever meant it's exempt from NEEDING TO ASK — not
    // from keeping every existing EarMarkPattern still fitting inside the
    // goal's own, possibly-just-shortened Until afterward. Without truncating
    // every surviving plan that would exceed the new, shorter Until, the
    // database lands in a state where EarMarkPatternRepository.GetAll() throws
    // on every subsequent read — including the app's own startup RefreshGrids.
    // DetermineBackTruncationsIfApplicable/ApplyBackTruncationsIfNeeded handle
    // it; the three tests below lock that in.
    [Fact]
    public void Shortening_only_the_end_date_truncates_every_surviving_plan_that_would_otherwise_exceed_it()
    {
        var bill = Bill(1, "Storage Unit Rental", -80m, new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -60m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -20m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var newUntil = new DateOnly(2027, 11, 11); // shorter than both plans' own Until
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.ActiveStart, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2); // both survive — neither plan's own Start was past the new Until
        plans.ShouldAllBe(p => p.DatePattern.Until == newUntil);
        plans.ShouldContain(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 1));
        plans.ShouldContain(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 2)); // both survive as separate rows — not merged
    }

    [Fact]
    public void Shortening_the_end_date_deletes_manual_earmarks_that_now_fall_after_it()
    {
        var bill = Bill(1, "Storage Unit Rental", -80m, new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        var plan = Plan(bill, -60m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);
        _earMarkPatterns.Save(plan);
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2027, 12, 1), Amount = 200m }, // after the new Until, below
            plan));

        var forecast = Forecast();
        var newUntil = new DateOnly(2027, 11, 11);
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.ActiveStart, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);
        _earMarkPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);
        _manualEarmarks.GetAll().ShouldBeEmpty(); // the Dec 1 earmark is now past the goal's own end
    }

    // The degenerate case EndOn itself can't handle: a plan that hasn't even
    // started contributing yet under the new, shorter range (the
    // sequential-plans shape — one already active, one still ahead).
    // "Truncate to fit" isn't well-formed there (it would ask for a plan
    // ending before its own start), so ApplyBackTruncationsIfNeeded deletes
    // it outright instead — see that method's own comment.
    [Fact]
    public void Shortening_the_end_date_past_a_not_yet_started_plans_own_start_deletes_that_plan_outright()
    {
        var bill = Bill(1, "Multi-Phase Goal", -50m, new DateOnly(2025, 1, 1), new DateOnly(2028, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31)));
        _earMarkPatterns.Save(Plan(bill, -60m, new DateOnly(2027, 1, 1), bill.DatePattern.Until)); // not due to start for over a year

        var forecast = Forecast();
        var newUntil = new DateOnly(2026, 6, 30); // before the second plan's own Start
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.ActiveStart, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem(); // the not-yet-started plan is gone entirely, not left dangling
        plans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
        plans[0].DatePattern.Until.ShouldBe(newUntil); // the surviving plan was also truncated to match
    }

    // The confirmation is always a real choice, never a
    // forced continue. Cancelling it must leave everything exactly as it was
    // — Run() itself, not a private method, since this is testing the
    // ConfirmImplicitChanges wiring, not any one mechanism behind it.
    [Fact]
    public void Cancelling_the_confirmation_leaves_everything_unsaved()
    {
        var bill = Bill(1, "Electric Co", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            request.HasRow(ConfirmationRowIds.WarnAboutBreakOff).ShouldBeTrue(); // the request itself is built correctly
            return Confirm.Cancel();
        };

        var proceeded = confirmation.Run();

        proceeded.ShouldBeFalse();
        _financialPatterns.GetAll().Single().Amount.ShouldBe(-100m); // untouched — the cancel took effect
    }

    // The three tests below answer a different question than the three
    // above: not "did the saved plan's own raw fields come out right" but
    // "once that save actually lands and the forecast is rebuilt off it —
    // the same way the Summary region or a fresh app launch would — does
    // GoalShortfall agree that the concern is resolved.

    // The plan's health heads-up — an announcement row in the confirmation, so
    // all of a save's messaging lives in this one system. Independent of
    // everything else that might fire this save:
    // the edit here is purely Trivial (Description only), so the notice row stands
    // on its own trigger (ChangeWarrantsSuggestions), not as a side effect of some
    // other question already showing. Shown on BOTH save buttons — "Save and plan"
    // (false) and "Save and skip planning" (true) — a plan-health warning is worth
    // seeing whether or not the plan form is about to open (author's call).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_plan_worth_warning_about_shows_a_concerning_notice_row(bool skippedPlanning)
    {
        // A one-time, distant-due-date goal steadily accumulating toward it
        // — the same shape TransactionLogBookFactoryTests' own LiveGoal
        // helper uses for IsWorthWarningAbout_true_for_currently_short_
        // regardless_of_anything_else, deliberately mirrored here rather
        // than improvised: a recurring bill's own contributions and
        // releases roughly cancel out month to month, which washes out a
        // one-time manual release far more easily than a steadily-growing
        // goal does.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Car Lease Payment",
            Amount = -10_000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        _financialPatterns.Save(goal, accountId: 1);
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                StartingAllocation = 100m,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);
        _earMarkPatterns.Save(plan);
        // A manual release creating a real, CURRENT shortfall.
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = AsOf.AddDays(-5), Amount = -300m }, plan));

        var forecast = Forecast();
        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = goal.Source,
            Amount = goal.Amount,
            Description = "Car lease — reminder to autopay",
            DatePattern = goal.DatePattern,
        });

        string? capturedNotice = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast, userSkippedPlanning: skippedPlanning);
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedNotice = request.AnnouncementText(ConfirmationRowIds.ConcerningPlan);
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        capturedNotice.ShouldNotBeNullOrEmpty();
    }

    // AskWhichEarmarkPatternToOpen's own disambiguation
    // (EarmarkPatternPickerWindow), rather than just taking the first match.
    [Fact]
    public void Save_and_plan_asks_which_plan_to_open_when_more_than_one_survives()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        _financialPatterns.Save(bill, accountId: 1);
        var planA = Plan(bill, -60m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        var planB = Plan(bill, -40m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        _earMarkPatterns.Save(planA);
        _earMarkPatterns.Save(planB);

        var forecast = Forecast();
        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = bill.Source,
            Amount = bill.Amount,
            Description = "Storage — checked the gate code",
            DatePattern = bill.DatePattern,
        });

        IReadOnlyList<EarMarkPattern>? offeredPlans = null;
        EarMarkPattern? navigatedTo = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast, userSkippedPlanning: false);
        confirmation.PickEarmarkPattern = plans =>
        {
            offeredPlans = plans;
            return plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2026, 1, 1)); // deliberately not the first one
        };
        confirmation.NavigateToEarmarkForm = (plan, _) => navigatedTo = plan;

        confirmation.Run().ShouldBeTrue();

        offeredPlans.ShouldNotBeNull();
        offeredPlans!.Count.ShouldBe(2);
        navigatedTo.ShouldNotBeNull();
        navigatedTo!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2026, 1, 1)); // the picker's own choice, not savingsPlan[0]
    }

    [Fact]
    public void Cancelling_the_which_plan_picker_aborts_navigation_instead_of_opening_the_first_plan()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -60m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31)));
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));

        var forecast = Forecast();
        var editedBill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = bill.Source,
            Amount = bill.Amount,
            Description = "Storage — checked the gate code",
            DatePattern = bill.DatePattern,
        });

        var navigated = false;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast, userSkippedPlanning: false);
        confirmation.PickEarmarkPattern = _ => null; // the user cancels the picker
        confirmation.NavigateToEarmarkForm = (_, _) => navigated = true;

        confirmation.Run().ShouldBeTrue(); // the save still commits
        navigated.ShouldBeFalse();         // ...but no plan form opens
    }

    // ---- shared scenario-building helpers ----------------------------------

    // Front-truncation — the Start-side twin of the back-truncation clamp. A
    // future bill (starts after AsOf, so editing its Start is
    // non-Critical and saves in place) whose Start is pushed later would leave its
    // plan starting before it — a 3.11.2.a2 violation that crashes the
    // next EarMarkPatternRepository.GetAll(). The clamp brings the plan in line.
    [Fact]
    public void Moving_a_future_goals_start_forward_clamps_its_plan_and_doesnt_crash_the_next_read()
    {
        var bill = Bill(1, "Gym", -40m, new DateOnly(2025, 8, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 8, 1), new DateOnly(2025, 12, 1)));

        var editedBill = Bill(1, "Gym", -40m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        Confirmation(1, editedBill, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 1).ToList(); // reads back without throwing
        plans.ShouldHaveSingleItem();
        plans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 9, 1)); // clamped to the goal's new Start
    }

    // M2 piece 2 — the reverse-break-off. Future chain: old rent P (Jul-Aug) ->
    // new rent C (Sep-Dec), C has a plan. Pushing C's Start to Nov stays linked,
    // so P stretches to cover Sep-Oct. C's Sep/Oct plan contributions are dropped
    // from C, but survive as a new plan under P — the user's own conscious
    // contribution schedule for those dates, not silently let go.
    [Fact]
    public void Moving_a_future_segments_start_forward_preserves_its_plans_dropped_occurrences_under_the_predecessor()
    {
        var predecessor = Bill(1, "Rent", -1_500m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var current = Bill(2, "Rent", -1_800m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(current, -1_800m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var editedCurrent = Bill(2, "Rent", -1_800m, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        Confirmation(2, editedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2); // C's clamped plan plus the one preserved under P

        plans.Single(p => p.FinanceId == 2).DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 11, 1)); // C's plan clamped forward

        var preserved = plans.Single(p => p.FinanceId == 1); // migrated under the predecessor
        preserved.Amount.ShouldBe(-1_800m); // the same contribution rate the user set up
        preserved.DatePattern.GetOccurrences().ShouldBe([new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 1)]); // exactly the dropped days
    }

    // M2 piece 3 — extend-outward. A plan whose Until matched its goal's own Until
    // shared that boundary, so pushing the goal's end_date later carries the plan
    // out with it, adding contributions at the plan's own rhythm.
    [Fact]
    public void Extending_a_goals_end_date_grows_a_plan_that_shared_that_end_to_match()
    {
        var bill = Bill(1, "Gym", -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 1)));

        var extendedBill = Bill(1, "Gym", -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 11, 1));
        Confirmation(1, extendedBill, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var plan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 1);
        plan.DatePattern.Until.ShouldBe(new DateOnly(2025, 11, 1)); // grew to the goal's new end
        plan.DatePattern.GetOccurrences().ShouldContain(new DateOnly(2025, 11, 1)); // and the new months are really there
    }

    // The extend-outward announcement: growing a plan by moving the
    // goal's boundary out is otherwise silent, so the save surfaces a row
    // naming how many more times the goal occurs.
    [Fact]
    public void Extending_a_goals_end_date_announces_how_many_more_times_it_occurs()
    {
        var bill = Bill(1, "Gym Membership", -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 1))); // shares the goal's end

        var extendedBill = Bill(1, bill.Source, -40m, bill.DatePattern.ActiveStart, new DateOnly(2025, 11, 1)); // three months later

        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, extendedBill, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        var announcement = captured!.AnnouncementText(ConfirmationRowIds.BoundaryExtension);
        announcement.ShouldContain("3 more times"); // Sep/Oct/Nov
        announcement.ShouldContain("Gym Membership");
    }

    // The Start-side twin: a future goal's Start pulled earlier carries a plan
    // that shared it back too, phase-preserving.
    [Fact]
    public void Moving_a_future_goals_start_earlier_grows_a_plan_that_shared_that_start_to_match()
    {
        var bill = Bill(1, "Gym", -40m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var extendedBill = Bill(1, "Gym", -40m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 1));
        Confirmation(1, extendedBill, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var plan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 1);
        plan.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1)); // grew back to the goal's new start
        plan.DatePattern.GetOccurrences().ShouldContain(new DateOnly(2025, 7, 1)); // the newly-covered months are really there
    }

    // The boundary really has to be SHARED — a plan that deliberately ended
    // before its goal never tracked the goal's end, so extending the goal leaves
    // it exactly where it was.
    [Fact]
    public void Extending_a_goals_end_date_leaves_a_plan_that_ended_earlier_alone()
    {
        var bill = Bill(1, "Gym", -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1))); // ends Apr; goal ends Aug

        var extendedBill = Bill(1, "Gym", -40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 11, 1));
        Confirmation(1, extendedBill, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var plan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 1);
        plan.DatePattern.Until.ShouldBe(new DateOnly(2025, 4, 1)); // untouched — it never tracked the goal's end
    }

    // Affordability on the cross-boundary cascade: the re-rate below (EarmarkScaling.Scale) and the
    // shape-change fold further down (EarmarkConsolidation.Consolidate) both pass an affordability ceiling, so
    // the successor's re-rated or folded plan never reserves more free money than there is. Under these tests'
    // generous 10,000 balance the cap is inert, so the numbers here are unchanged; the binding case is
    // A_carried_forward_re_rate_is_held_to_what_the_free_funds_can_afford (below), which injects a tight balance.

    // Cross-boundary Q6, slice 1 — the single-plan re-rate. A two-segment Rent
    // chain whose later segment is funded by one plan: raising the current
    // segment's amount and carrying it forward must re-rate that later plan to
    // match, not leave it saving toward the old figure.
    [Fact]
    public void Carrying_an_amount_change_forward_re_rates_a_later_segments_single_savings_plan()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        Confirmation(1, raisedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1200m); // the later bill cascaded
        _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1200m); // and its plan was re-rated to match
    }

    // The same carry-forward re-rate, but the household can't afford the raised figure. Raising Rent and
    // carrying it forward wants the later segment's plan re-rated up to -1200 to match — but if there's only so
    // much free money, the affordability ceiling holds that plan below what the raise wants, so it reserves
    // only what the funds can cover (knowingly underfunding) instead of blindly matching -1200. A steady income
    // covers the rent, so free funds hold near the starting balance; a low balance injected into the "room for
    // these plans" re-forecast is what makes the ceiling actually bind.
    [Fact]
    public void A_carried_forward_re_rate_is_held_to_what_the_free_funds_can_afford()
    {
        var income = Bill(100, "Job", 3000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 1));
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(income, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        // Only ~800 free in the "room for these plans" re-forecast (income covers the rent, so free funds hold
        // near this balance) — the raised -1200 rate can't fit under it.
        Func<IReadOnlySet<int>, ForecastResult> tightRoom = omitIds =>
            TransactionLogBookFactory.CreateForecast((ForecastOptionsForTest() with { StartingBalance = 800m }).WithoutPlansFor(omitIds));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        Confirmation(1, raisedCurrent, accountId: 1, Forecast(), tightRoom).Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1200m); // the later BILL still cascaded in full
        var replanned = Math.Abs(_earMarkPatterns.GetAll().Single(p => p.FinanceId == 2).Amount);
        replanned.ShouldBeLessThan(1000m); // ...but its PLAN was held below even the old -1000 rate — the ceiling bound it
        replanned.ShouldBeGreaterThan(0m);  // still a real, positive contribution, not zeroed out
    }

    // Cross-boundary Q6, slice 2 — the shape-change forced consolidation. The
    // later segment is funded by two concurrent plans; changing the current
    // segment's recurrence shape and carrying it forward moves the later
    // segment's occurrence dates, so keeping its plans separate isn't workable —
    // they're folded into one plan re-aligned to the new schedule.
    [Fact]
    public void Carrying_a_shape_change_forward_folds_a_later_segments_plans_into_one()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31), byMonthDay: 1);
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1), byMonthDay: 1);
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var reshapedCurrent = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31), byMonthDay: 15);
        Confirmation(1, reshapedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Count(p => p.FinanceId == 2).ShouldBe(1); // the two concurrent plans were folded into one
    }

    // Cross-boundary Q6, slice 3 — the multi-plan combine-or-keep-separate choice,
    // asked as its OWN row (never the break-off Consolidation one). Combine folds
    // the later segment's plans in place.
    [Fact]
    public void Carrying_an_amount_change_forward_combines_a_later_segments_plans_when_the_user_chooses_to()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var confirmation = Confirmation(1, raisedCurrent, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request =>
        {
            request.HasRow(ConfirmationRowIds.CrossBoundaryConsolidation(2)).ShouldBeTrue(); // asked as its own distinct row
            return new ConfirmationOutcome
            {
                Proceed = true,
                ChosenOptionIndex = new Dictionary<string, int> { [ConfirmationRowIds.CrossBoundaryConsolidation(2)] = 1 }, // combine
            };
        };

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Count(p => p.FinanceId == 2).ShouldBe(1); // folded into one
    }

    // The default (and headless) answer is keep-separate — each plan scaled
    // proportionally to the new amount, both rows preserved.
    [Fact]
    public void Carrying_an_amount_change_forward_keeps_a_later_segments_plans_separate_by_default()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        Confirmation(1, raisedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue(); // no delegate — default keep-separate

        var plans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 2).ToList();
        plans.Count.ShouldBe(2); // both kept, scaled by the same 1.2 ratio
        plans.ShouldContain(p => p.Amount == -720m); // -600 * 1.2
        plans.ShouldContain(p => p.Amount == -480m); // -400 * 1.2
    }

    // Dynamic reveal — the cross-boundary question is nested under the cascade
    // "apply going forward" option, so the popup shows it only when the user
    // actually carries the change forward, not as a flat, always-visible row.
    [Fact]
    public void The_cross_boundary_question_is_nested_under_the_carry_forward_option()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, raisedCurrent, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };
        confirmation.Run();

        captured.ShouldNotBeNull();
        captured!.Rows.OfType<ChoiceRow>().ShouldNotContain(r => r.Id == ConfirmationRowIds.CrossBoundaryConsolidation(2)); // not a top-level row
        var cascade = captured.Rows.OfType<ChoiceRow>().Single(r => r.Id == ConfirmationRowIds.Cascade);
        cascade.Options[0].Children.OfType<ChoiceRow>().ShouldContain(r => r.Id == ConfirmationRowIds.CrossBoundaryConsolidation(2)); // nested under "apply going forward"
        cascade.Options[1].Children.ShouldBeEmpty(); // nothing under "only this segment"
    }

    // Completing the Q3 -> Q4 -> Q6 tree: when an edit touches the chain
    // boundary AND the amount, the cascade question nests under "keep it linked" —
    // breaking the chain leaves nothing forward to carry the change onto.
    [Fact]
    public void The_cascade_question_is_nested_under_keep_it_linked_when_the_edit_also_touches_the_boundary()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var edited = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 15)); // end date AND amount changed
        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, edited, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };
        confirmation.Run();

        captured.ShouldNotBeNull();
        captured!.Rows.OfType<ChoiceRow>().ShouldNotContain(r => r.Id == ConfirmationRowIds.Cascade); // not a top-level row
        var chainBoundary = captured.Rows.OfType<ChoiceRow>().Single(r => r.Id == ConfirmationRowIds.ChainBoundary);
        chainBoundary.Options[0].Children.OfType<ChoiceRow>().ShouldContain(r => r.Id == ConfirmationRowIds.Cascade); // nested under "keep it linked"
        chainBoundary.Options[1].Children.ShouldBeEmpty(); // nothing under "let the chain break"
    }

    [Fact]
    public void Breaking_the_chain_stops_the_amount_from_carrying_forward()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var edited = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 15));
        var confirmation = Confirmation(1, edited, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = new Dictionary<string, int> { [ConfirmationRowIds.ChainBoundary] = 1 }, // let the chain break
        };

        confirmation.Run().ShouldBeTrue();

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1000m); // the later segment keeps its old amount — the change didn't carry forward
    }

    [Fact]
    public void Keeping_the_chain_linked_still_carries_the_amount_forward()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);

        var edited = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 15));
        Confirmation(1, edited, accountId: 1, Forecast()).Run().ShouldBeTrue(); // default: keep linked + apply going forward

        _financialPatterns.GetAll().Single(p => p.FinanceId == 2).Amount.ShouldBe(-1200m); // carried forward, since the chain stayed linked
    }

    // #5 — a chain neighbor stretched by keeping the boundary linked grows its OWN
    // plan too. Predecessor case: pushing C's start to November stretches P's end
    // forward to Oct 31, and P's own plan grows with it.
    [Fact]
    public void Keeping_the_chain_linked_grows_a_stretched_predecessors_own_plan()
    {
        var predecessor = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var current = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(predecessor, -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31)));

        var movedCurrent = Bill(2, "Rent", -1000m, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        Confirmation(2, movedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var predecessorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 1).ToList();
        predecessorPlans.ShouldHaveSingleItem();
        predecessorPlans[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 10, 31)); // grew to the predecessor's new end
    }

    // Successor case: pulling S's end back to June stretches T's start back to
    // July 1, and T's own plan grows back with it.
    [Fact]
    public void Keeping_the_chain_linked_grows_a_stretched_successors_own_plan()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var shortened = Bill(1, "Rent", -1000m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        Confirmation(1, shortened, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var successorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 2).ToList();
        successorPlans.ShouldHaveSingleItem();
        successorPlans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1)); // grew back to the successor's new start
    }

    // #6 — M1's silent join. Both segments save at the same rate on the same
    // rhythm, so the stretched predecessor's grown plan and the reverse-break-off's
    // migrated plan end up identical over the same span and fold into one.
    [Fact]
    public void An_identical_stretched_plan_and_migrated_plan_are_silently_merged()
    {
        var predecessor = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var current = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(predecessor, -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31)));
        _earMarkPatterns.Save(Plan(current, -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var movedCurrent = Bill(2, "Rent", -1000m, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        Confirmation(2, movedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var predecessorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 1).ToList();
        predecessorPlans.ShouldHaveSingleItem(); // grown + migrated folded into one
        predecessorPlans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1));
        predecessorPlans[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 10, 31));
    }

    // When they differ, they stay as two separate concurrent plans — not folded.
    [Fact]
    public void A_stretched_plan_and_a_differently_rated_migrated_plan_stay_separate()
    {
        var predecessor = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var current = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(predecessor, -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31)));
        _earMarkPatterns.Save(Plan(current, -800m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var movedCurrent = Bill(2, "Rent", -1000m, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        Confirmation(2, movedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Count(p => p.FinanceId == 1).ShouldBe(2); // grown $1000 plan + migrated $800 plan, kept separate
    }

    // The shrink mirror of the grow case: a neighbor pulled SMALLER by keeping the
    // chain linked has its own plan clamped, so it can't exceed the neighbor's new
    // span and crash the next read.
    [Fact]
    public void Keeping_the_chain_linked_clamps_a_shrunk_predecessors_own_plan()
    {
        var predecessor = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 10, 31));
        var current = Bill(2, "Rent", -1000m, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(predecessor, accountId: 1);
        _financialPatterns.Save(current, accountId: 1);
        _earMarkPatterns.Save(Plan(predecessor, -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 10, 31)));

        var movedCurrent = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)); // start pulled back to Sep
        Confirmation(2, movedCurrent, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var predecessorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 1).ToList(); // reads back without throwing
        predecessorPlans.ShouldHaveSingleItem();
        predecessorPlans[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 8, 31)); // clamped to the predecessor's new end
    }

    [Fact]
    public void Keeping_the_chain_linked_clamps_a_shrunk_successors_own_plan()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        var extended = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 10, 31)); // end extended into T's span
        Confirmation(1, extended, accountId: 1, Forecast()).Run().ShouldBeTrue();

        var successorPlans = _earMarkPatterns.GetAll().Where(p => p.FinanceId == 2).ToList(); // reads back without throwing
        successorPlans.ShouldHaveSingleItem();
        successorPlans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 11, 1)); // clamped to the successor's new start
    }

    // #2 — the consolidate-strategy picker. When a consolidation is on the table
    // (here a later segment with two plans that could be combined), the sizing
    // question appears nested under "apply going forward."
    [Fact]
    public void The_consolidate_sizing_question_appears_when_a_consolidation_is_possible()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        ImplicitChangeConfirmationRequest? captured = null;
        var confirmation = Confirmation(1, raisedCurrent, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => { captured = request; return Confirm.Proceed(); };
        confirmation.Run();

        captured.ShouldNotBeNull();
        var cascade = captured!.Rows.OfType<ChoiceRow>().Single(r => r.Id == ConfirmationRowIds.Cascade);
        cascade.Options[0].Children.OfType<ChoiceRow>().ShouldContain(r => r.Id == ConfirmationRowIds.ConsolidationSizing); // nested under "apply going forward"
    }

    [Fact]
    public void Combining_with_keep_current_rate_holds_the_plans_own_rate_not_the_goal()
    {
        var current = Bill(1, "Rent", -1000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var successor = Bill(2, "Rent", -1000m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1));
        _financialPatterns.Save(current, accountId: 1);
        _financialPatterns.Save(successor, accountId: 1);
        _earMarkPatterns.Save(Plan(successor, -600m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));
        _earMarkPatterns.Save(Plan(successor, -400m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 1)));

        var raisedCurrent = Bill(1, "Rent", -1200m, new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31));
        var confirmation = Confirmation(1, raisedCurrent, accountId: 1, Forecast());
        confirmation.ConfirmImplicitChanges = request => new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = new Dictionary<string, int>
            {
                [ConfirmationRowIds.CrossBoundaryConsolidation(2)] = 1, // combine
                [ConfirmationRowIds.ConsolidationSizing] = 1, // keep the current rate
            },
        };
        confirmation.Run().ShouldBeTrue();

        var plan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2);
        plan.Amount.ShouldBe(-900m); // combined current schedule ($3,600 over 4 months), not the goal's $1,200
    }

    private static FinancialPattern Bill(int financeId, string source, decimal amount, DateOnly start, DateOnly until, int? byMonthDay = null) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            Amount = amount,
            DatePattern = Monthly(start, until, byMonthDay),
        });

    private static EarMarkPattern Plan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions { FinanceId = goal.FinanceId, Amount = amount, DatePattern = Monthly(start, until) },
            goal);

    private static RecurrenceRule Monthly(DateOnly start, DateOnly until, int? byMonthDay = null) => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [byMonthDay ?? start.Day],
        DtStart = start,
        Until = until,
    });

    private ForecastOptions ForecastOptionsForTest() => new()
    {
        FinancialPatterns = _financialPatterns.GetAll(),
        EarMarkPatterns = _earMarkPatterns.GetAll(),
        ManualEarmarks = _manualEarmarks.GetAll(),
        StartingBalance = 10_000m,
        AsOfDate = AsOf,
        HorizonEndDate = HorizonEnd,
    };

    private ForecastResult Forecast() => TransactionLogBookFactory.CreateForecast(ForecastOptionsForTest());

    // Confirmation is given a real omitting-forecast source here (unlike headless callers that leave it
    // null), so the affordability ceiling actually runs in these tests. The generous 10,000 balance keeps
    // the cap inert in the existing cases — the cap-when-it-binds math is proven in EarmarkScalingTests /
    // AffordabilityCeilingTests; MainWindow's own ForecastOmitting (which builds the real options) is the
    // one link no test reaches.
    private FinancePatternSaveConfirmation Confirmation(int financeId, FinancialPattern proposedPattern, int accountId, ForecastResult forecast, bool userSkippedPlanning = true) =>
        new(financeId, proposedPattern, accountId, userSkippedPlanning, () => forecast, new FinancePatternRepositories
        {
            FinancialPatterns = _financialPatterns,
            EarMarkPatterns = _earMarkPatterns,
            ManualEarmarks = _manualEarmarks,
        },
        omitIds => TransactionLogBookFactory.CreateForecast(ForecastOptionsForTest().WithoutPlansFor(omitIds)));

    // Same, but with a caller-supplied "room for these plans" source — for exercising the affordability
    // ceiling when it actually binds (a deliberately tight re-forecast).
    private FinancePatternSaveConfirmation Confirmation(int financeId, FinancialPattern proposedPattern, int accountId, ForecastResult forecast, Func<IReadOnlySet<int>, ForecastResult> requestForecastOmitting, bool userSkippedPlanning = true) =>
        new(financeId, proposedPattern, accountId, userSkippedPlanning, () => forecast, new FinancePatternRepositories
        {
            FinancialPatterns = _financialPatterns,
            EarMarkPatterns = _earMarkPatterns,
            ManualEarmarks = _manualEarmarks,
        },
        requestForecastOmitting);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
