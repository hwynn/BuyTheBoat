using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// Exercises FinancePatternSaveConfirmation end to end against a real
// (temporary) SQLite file — same shape as Scenario.Tests' own repository
// tests, just for the App-layer orchestrator instead of the repositories
// directly. Most [Fact]s drive Run() itself, never a private method
// directly — a scenario's own data (which fields changed, whether
// history/a plan/multiple plans exist) determines which of planning/25's
// Items C-F actually fires, since AskForGuidanceOnImplicitChanges'
// placeholder always answers every question with its safest,
// least-destructive default until a real popup exists (see that class's own
// STATUS note). The one exception is the retroactive-correction narrowing
// test near the bottom: Run() can never reach that path today (its own
// gate, UserChooseAlterPast, is permanently false under the placeholder), so
// it calls DetermineConditions/NarrowSurvivingPlanIfNeeded directly instead —
// both internal specifically for this, via MyMoneyForecast.App.csproj's
// InternalsVisibleTo grant. One [Fact] per named case — add to this set
// rather than growing any one test as more of Items C-F get built for real.
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
        _earMarkPatterns.Save(Plan(bill, -120m, bill.DatePattern.Start, bill.DatePattern.Until));

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
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.Start, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2);

        var truncatedOriginal = patterns.Single(p => p.FinanceId == 1);
        truncatedOriginal.Amount.ShouldBe(-100m); // the proposed edit never lands on the original id
        truncatedOriginal.DatePattern.Until.ShouldBe(AsOf.AddDays(-1));

        var successorBill = patterns.Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorBill.Amount.ShouldBe(-150m); // the proposed edit lands here instead
        successorBill.DatePattern.Start.ShouldBe(AsOf);
        successorBill.DatePattern.Until.ShouldBe(bill.DatePattern.Until);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);

        plans.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));

        var successorPlan = plans.Single(p => p.FinanceId == 2);
        successorPlan.DatePattern.Start.ShouldBe(AsOf);
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance);
    }

    // planning/25's Item G, new 2026-08-14: the existing plan's own shape
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
                    Start = new DateOnly(2025, 1, 3), // a Friday
                    Until = bill.DatePattern.Until,
                }),
            },
            bill);
        _earMarkPatterns.Save(existingPlan);

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.Start, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            // The fake "popup" itself — reads the real candidates this Run()
            // actually built, the same way a real one would, rather than a
            // value the test just hands back blind.
            request.PlanShapeCandidates.Count.ShouldBeGreaterThan(1);
            var sameSchedule = request.PlanShapeCandidates.Single(c => c.Label == "Keep the same schedule");
            return new ImplicitChangeConfirmationAnswer
            {
                Proceed = true,
                ChooseAlterPast = false,
                ChosenPlanShape = sameSchedule.Plan.Plan,
            };
        };

        confirmation.Run().ShouldBeTrue();

        var successorPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorPlan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        successorPlan.DatePattern.Interval.ShouldBe(2);
        // Same phase as the existing plan's own Friday cycle, re-anchored —
        // not the bill's own monthly cadence Propose's default would have used.
        existingPlan.DatePattern.GetOccurrences().ShouldContain(successorPlan.DatePattern.Start);
    }

    // Found 2026-08-17 while grounding the paycheck-association cascade:
    // BuildSuccessorSchedule copies the edited pattern's own Frequency/
    // Interval/ByDay/ByMonthDay but sets Start = cutDate directly — for a
    // Weekly pattern with an empty ByDay (RecurrenceRuleEditor's own
    // checkboxes let a real user leave every one unchecked), RFC 5545 ties
    // an omitted BYDAY to DTSTART's own weekday, so the successor's own
    // occurrences silently land on cutDate's weekday instead of the pattern's
    // originally-intended one — the same bug class already fixed in
    // AllocationPlanProposer.ProposePaced on 2026-08-14. AsOf (2025-06-15) is
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
                Start = new DateOnly(2025, 1, 3), // a Friday
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

    // The broader half of the same 2026-08-17 finding — an EXPLICIT ByDay
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
                Start = new DateOnly(2025, 1, 3),
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

        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.Start, bill.DatePattern.Until);

        Confirmation(1, editedBill, accountId: 1, forecast).Run().ShouldBeTrue();

        var successorBill = _financialPatterns.GetAll().Single(p => p.FinanceId == 2);
        successorBill.DatePattern.Start.ShouldBe(AsOf);
        successorBill.DatePattern.ActiveFrom.ShouldBeNull();
    }

    // Mechanism-C follow-on (redesign/planning/26, "the glut case,"
    // 2026-08-15) — a detail flagged early in that thread ("keep the glut as
    // an up-front earmark event should be a valid option") that got set
    // aside while building mechanism C and only surfaced again later. Before
    // the fix, the "Recommended" candidate's own preview always read
    // StartingAllocation = 0, even though BreakOffFactory.BreakOff already
    // unconditionally overrides it with the real carried-over balance once
    // any candidate is actually saved — a live preview-vs-saved mismatch,
    // the same class of bug EarmarkFormLivePreviewTests already found
    // elsewhere. Explicitly chooses "Recommended" (by the same object
    // reference the candidate itself carried) so this exercises
    // DeterminePlanShapeCandidatesIfApplicable's own fixed construction, not
    // just BreakOff's already-correct fallback override.
    [Fact]
    public void Item_G_the_Recommended_candidates_own_preview_matches_what_actually_gets_saved_for_StartingAllocation()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        // Over-contributes every month, so a real, verifiable balance has
        // built up by AsOf — the exact scenario this fix protects.
        _earMarkPatterns.Save(Plan(bill, -150m, bill.DatePattern.Start, bill.DatePattern.Until));

        var forecast = Forecast();
        var realCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(j => j.FinanceId == 1).ExpectedAmount;
        realCarriedOverBalance.ShouldBeGreaterThan(0m); // confirms this scenario actually exercises the fix, not a $0 no-op

        var editedBill = Bill(1, bill.Source, -120m, bill.DatePattern.Start, bill.DatePattern.Until);
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);

        EarMarkPattern? recommendedPreview = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            var recommended = request.PlanShapeCandidates.Single(c => c.Label == "Recommended").Plan.Plan;
            recommendedPreview = recommended;
            return new ImplicitChangeConfirmationAnswer { Proceed = true, ChooseAlterPast = false, ChosenPlanShape = recommended };
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

    // planning/24's own Item-G gap, fixed 2026-08-16: a Savings Plan that's
    // already been restructured once (two sequential EarMarkPatterns sharing
    // one finance_id — an earlier, since-superseded segment plus the one
    // that's actually current) used to disable Item G entirely, since
    // HasMultipleEarmarkPatterns bailed before any candidate was ever built,
    // even though exactly one segment is genuinely current.
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
        _earMarkPatterns.Save(Plan(bill, -50m, bill.DatePattern.Start, new DateOnly(2025, 3, 31)));
        var currentPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = bill.FinanceId,
                Amount = -20m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    Interval = 2,
                    Start = new DateOnly(2025, 4, 4), // a Friday
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
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.Start, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            var sameSchedule = request.PlanShapeCandidates.Single(c => c.Label == "Keep the same schedule");
            return new ImplicitChangeConfirmationAnswer
            {
                Proceed = true,
                ChooseAlterPast = false,
                // Amount-only change, so ConsolidationNeeded is naturally
                // false (that row is meant to be feasible to keep separate)
                // — has to be chosen explicitly to reach PerformMultiPlanBreakOff
                // at all, same as any other multi-plan break-off.
                ChooseConsolidation = true,
                ChosenPlanShape = sameSchedule.Plan.Plan,
            };
        };

        confirmation.Run().ShouldBeTrue();

        var successorPlan = _earMarkPatterns.GetAll().Single(p => p.FinanceId == 2); // NextFinanceId() with only id 1 in play
        successorPlan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        successorPlan.DatePattern.Interval.ShouldBe(2);
        // Same phase as the current segment's own Friday cycle, re-anchored —
        // not the superseded segment's own $80 rate, and not the bill's own
        // monthly cadence Propose's default would have used.
        currentPlan.DatePattern.GetOccurrences().ShouldContain(successorPlan.DatePattern.Start);
    }

    // Regression lock, 2026-08-16: the fix above must not reach into F27's
    // own concurrent-funder case, where the author's own ruling (planning/25's
    // Item G closing note) says no shape choice should be offered at all —
    // "it'll already be complicated enough" once the not-yet-built
    // size-both-plans-in-unison mechanism exists.
    [Fact]
    public void Item_G_still_offers_no_candidates_for_a_genuinely_concurrent_predecessor()
    {
        var bill = Bill(1, "Storage Unit Rental", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        // Two concurrent funders (F27), staggered by a day, both active
        // across nearly the whole range — same shape this file's own
        // A_break_off_with_multiple_surviving_plans_kept_separate_is_a_safe_no_op_for_now
        // already uses for the concurrent case.
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.Start, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        ImplicitChangeConfirmationRequest? capturedRequest = null;
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedRequest = request;
            return new ImplicitChangeConfirmationAnswer { Proceed = true, ChooseAlterPast = false, ChooseConsolidation = true };
        };

        confirmation.Run().ShouldBeTrue();

        capturedRequest.ShouldNotBeNull();
        capturedRequest!.PlanShapeCandidates.ShouldBeEmpty();
    }

    // 03's 1.2.3.10.a5 only restricts start_date/amount/recurrence shape —
    // description/source/priority/mandatory stay plain edits regardless of
    // history (planning/25's "Final field categorization" table). Changing
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

    // Item A's own carve-out (03, Ch.20): a pattern with no expected
    // transaction on or before the as-of date isn't protected at all, no
    // matter which field changes — there's no history yet to alter. A
    // future-dated bill already saved in the database is exactly that case.
    [Fact]
    public void A_critical_field_change_on_a_pattern_with_no_past_occurrence_yet_is_a_plain_edit()
    {
        var bill = Bill(1, "New Gym Membership", -45m, new DateOnly(2025, 9, 1), new DateOnly(2026, 9, 1)); // starts after AsOf
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();

        var editedBill = Bill(1, bill.Source, -60m, bill.DatePattern.Start, bill.DatePattern.Until); // amount is normally Critical...

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.ShouldHaveSingleItem(); // ...but there's nothing to protect yet, so still a plain edit
        patterns[0].FinanceId.ShouldBe(1);
        patterns[0].Amount.ShouldBe(-60m);
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

        var editedBill = Bill(1, bill.Source, -18m, bill.DatePattern.Start, bill.DatePattern.Until);

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

        var raise = Bill(1, paycheck.Source, 3200m, paycheck.DatePattern.Start, paycheck.DatePattern.Until, byMonthDay: 25);

        Confirmation(1, raise, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2);
        patterns.Single(p => p.FinanceId == 1).Amount.ShouldBe(3000m); // predecessor keeps the old rate
        patterns.Single(p => p.FinanceId == 2).Amount.ShouldBe(3200m); // successor carries the raise

        _earMarkPatterns.GetAll().ShouldBeEmpty(); // income never gets a jar, predecessor or successor
    }

    // planning/25's Item F, the keep-them-separate sub-case — FIXED
    // 2026-08-17. Used to be a deliberate, documented no-op: keeping plans
    // separate under a break-off's new finance_id is a materially
    // different, still-unbuilt mechanism (each plan would need its own
    // successor, not one combined fresh one), so nothing was saved at all
    // — not even the FinancialPattern itself — whenever the user picked
    // (or, via DefaultConfirmationAnswer, defaulted to) "keep separate."
    // Found and fixed the same day, once that silence turned out to be a
    // real gap rather than a safe placeholder: a Save button that silently
    // does nothing is worse than one that combines plans the user didn't
    // explicitly ask to combine. PerformImplicitEarmarkChanges now always
    // consolidates on the break-off side, matching the recurrence-shape
    // case below exactly — see that method's own comment.
    [Fact]
    public void A_break_off_with_multiple_surviving_plans_kept_separate_falls_back_to_consolidating_rather_than_a_silent_no_op()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);

        // Two concurrent funders on the same goal (F27) — same shape as
        // PatternRepositoryTests' own multi-plan coverage, just concurrent
        // rather than sequential.
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var expectedCarriedOverBalance = forecast.GetTimeline(1)
            .Last(entry => entry.Date <= AsOf).Snapshot.FundJars
            .Single(jar => jar.FinanceId == 1).ExpectedAmount;
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.Start, bill.DatePattern.Until); // amount only — recurrence shape untouched

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2); // the break-off happened — no longer a silent no-op
        patterns.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));
        patterns.Single(p => p.FinanceId == 2).Amount.ShouldBe(-500m);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(3); // both original plans, truncated, plus ONE consolidated successor
        plans.Count(p => p.FinanceId == 1).ShouldBe(2);

        var successorPlan = plans.Single(p => p.FinanceId == 2); // exactly one — consolidated, not two, and NOT the "keep separate" the user asked for but this mechanism can't yet honor
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance);
    }

    // planning/25's Item F, the forced-consolidation sub-case: the
    // recurrence shape changing makes ConsolidationNeeded true regardless of
    // UserChooseConsolidation's own (placeholder) value, so this — unlike
    // the amount-only case above — actually reaches PerformMultiPlanBreakOff.
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
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.Start, bill.DatePattern.Until, byMonthDay: 15);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var patterns = _financialPatterns.GetAll();
        patterns.Count.ShouldBe(2); // the break-off happened this time
        patterns.Single(p => p.FinanceId == 1).DatePattern.Until.ShouldBe(AsOf.AddDays(-1));
        patterns.Single(p => p.FinanceId == 2).DatePattern.Start.ShouldBe(AsOf);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(3); // both original plans, truncated, plus ONE consolidated successor
        plans.Count(p => p.FinanceId == 1).ShouldBe(2);
        plans.Where(p => p.FinanceId == 1).ShouldAllBe(p => p.DatePattern.Until == AsOf.AddDays(-1));

        var successorPlan = plans.Single(p => p.FinanceId == 2); // exactly one — consolidated, not two
        successorPlan.DatePattern.Start.ShouldBe(AsOf);
        successorPlan.StartingAllocation.ShouldBe(expectedCarriedOverBalance); // the ONE combined jar's balance, not either plan's own share
    }

    // planning/25 Item A's own back-boundary invariant (3.11.2.a2): end_date
    // is always a plain, never-Critical edit (03's own categorization
    // table), but that only ever meant it's exempt from NEEDING TO ASK — not
    // from keeping every existing EarMarkPattern still fitting inside the
    // goal's own, possibly-just-shortened Until afterward. Found via the
    // user's own real, manual use of the app (2026-08-13): shortening only a
    // multi-plan goal's own end date used to save the goal's new, shorter
    // Until with no such check, leaving the database in a state where
    // EarMarkPatternRepository.GetAll() threw on every subsequent read —
    // including the app's own startup RefreshGrids. DetermineBackTruncationsIfApplicable/
    // ApplyBackTruncationsIfNeeded fix this; the three tests below lock in
    // the fix rather than the bug that motivated it.
    [Fact]
    public void Shortening_only_the_end_date_truncates_every_surviving_plan_that_would_otherwise_exceed_it()
    {
        var bill = Bill(1, "Storage Unit Rental", -80m, new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -60m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -20m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var newUntil = new DateOnly(2027, 11, 11); // shorter than both plans' own Until
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.Start, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2); // both survive — neither plan's own Start was past the new Until
        plans.ShouldAllBe(p => p.DatePattern.Until == newUntil);
        plans.ShouldContain(p => p.DatePattern.Start == new DateOnly(2025, 1, 1));
        plans.ShouldContain(p => p.DatePattern.Start == new DateOnly(2025, 1, 2)); // both survive as separate rows — not merged
    }

    [Fact]
    public void Shortening_the_end_date_deletes_manual_earmarks_that_now_fall_after_it()
    {
        var bill = Bill(1, "Storage Unit Rental", -80m, new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31));
        _financialPatterns.Save(bill, accountId: 1);
        var plan = Plan(bill, -60m, bill.DatePattern.Start, bill.DatePattern.Until);
        _earMarkPatterns.Save(plan);
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2027, 12, 1), Amount = 200m }, // after the new Until, below
            plan));

        var forecast = Forecast();
        var newUntil = new DateOnly(2027, 11, 11);
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.Start, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        _financialPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);
        _earMarkPatterns.GetAll().Single().DatePattern.Until.ShouldBe(newUntil);
        _manualEarmarks.GetAll().ShouldBeEmpty(); // the Dec 1 earmark is now past the goal's own end
    }

    // The degenerate case EndOn itself can't handle: a plan that hasn't even
    // started contributing yet under the new, shorter range (F27's
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
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.Start, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem(); // the not-yet-started plan is gone entirely, not left dangling
        plans[0].DatePattern.Start.ShouldBe(new DateOnly(2025, 1, 1));
        plans[0].DatePattern.Until.ShouldBe(newUntil); // the surviving plan was also truncated to match
    }

    // planning/25's Item B: the confirmation is always a real choice, never a
    // forced continue. Cancelling it must leave everything exactly as it was
    // — Run() itself, not a private method, since this is testing the
    // ConfirmImplicitChanges wiring, not any one mechanism behind it.
    [Fact]
    public void Cancelling_the_confirmation_leaves_everything_unsaved()
    {
        var bill = Bill(1, "Electric Co", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.Start, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            request.IsChangeCritical.ShouldBeTrue(); // the request itself is built correctly
            return new ImplicitChangeConfirmationAnswer { Proceed = false };
        };

        var proceeded = confirmation.Run();

        proceeded.ShouldBeFalse();
        _financialPatterns.GetAll().Single().Amount.ShouldBe(-100m); // untouched — the cancel took effect
    }

    // The caveat that makes the break-off-side fallback above (and the
    // amount-only "keep separate" offer it applies to) an honest choice
    // rather than a silent trap — only relevant when "keep separate" is a
    // real, currently-offered option (amount-only, multi-plan, Critical).
    [Fact]
    public void The_break_off_keep_separate_caveat_shows_whenever_keep_separate_is_actually_offered()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);
        _earMarkPatterns.Save(Plan(bill, -300m, new DateOnly(2025, 1, 1), bill.DatePattern.Until));
        _earMarkPatterns.Save(Plan(bill, -120m, new DateOnly(2025, 1, 2), bill.DatePattern.Until));

        var forecast = Forecast();
        var editedBill = Bill(1, bill.Source, -500m, bill.DatePattern.Start, bill.DatePattern.Until); // amount only

        string? capturedCaveat = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            capturedCaveat = request.ConsolidationCaveat;
            return new ImplicitChangeConfirmationAnswer { Proceed = true };
        };

        confirmation.Run().ShouldBeTrue();

        capturedCaveat.ShouldNotBeNullOrEmpty();
        capturedCaveat.ShouldContain("today forward");
    }

    // The three tests below answer a different question than the three
    // above: not "did the saved plan's own raw fields come out right" but
    // "once that save actually lands and the forecast is rebuilt off it —
    // the same way the Summary region or a fresh app launch would — does
    // GoalShortfall agree that the concern is resolved." New 2026-08-14,
    // prompted directly by the user's own question about testing whether a
    // saved resolution actually satisfies what it was meant to fix.

    // The Concerning popup — BUILT 2026-08-17, minimal (see ShowSuggestion's
    // own field comment on FinancePatternSaveConfirmation). Independent of
    // everything else that might also fire this save: the edit here is
    // purely Trivial (Description only), proving ShowSuggestion fires on
    // its own trigger (ChangeWarrantsSuggestions), not as a side effect of
    // some other confirmation already being shown.
    [Fact]
    public void Save_and_plan_shows_a_concerning_suggestion_when_the_resulting_plan_is_worth_warning_about()
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
                Start = new DateOnly(2026, 6, 1),
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
                    Start = new DateOnly(2025, 1, 1),
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

        string? capturedSuggestion = null;
        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast, userSkippedPlanning: false);
        confirmation.ShowSuggestion = message => capturedSuggestion = message;

        confirmation.Run().ShouldBeTrue();

        capturedSuggestion.ShouldNotBeNullOrEmpty();
    }

    // AskWhichEarmarkPatternToOpen's own real disambiguation — BUILT
    // 2026-08-17 (EarmarkPatternPickerWindow), replacing "the first match."
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
            return plans.Single(p => p.DatePattern.Start == new DateOnly(2026, 1, 1)); // deliberately not the first one
        };
        confirmation.NavigateToEarmarkForm = plan => navigatedTo = plan;

        confirmation.Run().ShouldBeTrue();

        offeredPlans.ShouldNotBeNull();
        offeredPlans!.Count.ShouldBe(2);
        navigatedTo.ShouldNotBeNull();
        navigatedTo!.DatePattern.Start.ShouldBe(new DateOnly(2026, 1, 1)); // the picker's own choice, not savingsPlan[0]
    }

    // ---- shared scenario-building helpers ----------------------------------

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

    private FinancePatternSaveConfirmation Confirmation(int financeId, FinancialPattern proposedPattern, int accountId, ForecastResult forecast, bool userSkippedPlanning = true) =>
        new(financeId, proposedPattern, accountId, userSkippedPlanning, () => forecast, new FinancePatternRepositories
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
