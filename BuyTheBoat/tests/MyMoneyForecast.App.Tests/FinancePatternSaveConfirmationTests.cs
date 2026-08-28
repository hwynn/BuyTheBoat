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
// of planning/25's Items C-F actually fires, and each test wires a
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
            request.PlanShapeCandidates.Count.ShouldBeGreaterThan(1);
            var sameSchedule = request.PlanShapeCandidates.Single(c => c.Label == "Keep the same schedule");
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

    // Found 2026-08-17 while grounding the paycheck-association cascade:
    // BuildSuccessorSchedule copies the edited pattern's own Frequency/
    // Interval/ByDay/ByMonthDay but sets DtStart = cutDate directly — for a
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
            var recommended = request.PlanShapeCandidates.Single(c => c.Label == "Recommended").Plan.Plan;
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
            var sameSchedule = request.PlanShapeCandidates.Single(c => c.Label == "Keep the same schedule");
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

        var editedBill = Bill(1, bill.Source, -60m, bill.DatePattern.ActiveStart, bill.DatePattern.Until); // amount is normally Critical...

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

    // planning/25's Item F, the keep-them-separate sub-case — now honored
    // (2026-08-27). With more than one surviving plan and nothing forcing
    // consolidation (amount-only, so the schedule/start are untouched), the
    // user's default "keep them separate" stands: the successor gets one plan
    // per surviving plan, each continuing its own rate at its own cadence,
    // rather than folding into one. The finance_id's one combined jar balance
    // rides on a single successor plan. Was a documented no-op before it was
    // built, then a fall-back-to-consolidating stopgap; this is the real thing.
    [Fact]
    public void A_break_off_with_multiple_surviving_plans_keeps_them_separate_by_default()
    {
        var bill = Bill(1, "Car Lease Payment", -420m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        _financialPatterns.Save(bill, accountId: 1);

        // Two concurrent funders on the same goal (F27) — different rates, so
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

    // The other side of the same Item F question: when the user explicitly picks
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

    // The keep-separate funding question (2026-08-27): raising the amount leaves
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

    // The goal-health suggestion (planning/25's deferred picker, 2026-08-27):
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

    // planning/25's Item F, the forced-consolidation sub-case: the
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
        var editedBill = Bill(1, bill.Source, bill.Amount, bill.DatePattern.ActiveStart, newUntil);

        Confirmation(1, editedBill, accountId: 1, forecast).Run();

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem(); // the not-yet-started plan is gone entirely, not left dangling
        plans[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
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
        var editedBill = Bill(1, bill.Source, -150m, bill.DatePattern.ActiveStart, bill.DatePattern.Until);

        var confirmation = Confirmation(1, editedBill, accountId: 1, forecast);
        confirmation.ConfirmImplicitChanges = request =>
        {
            request.HasRow(ConfirmationRowIds.AlterPast).ShouldBeTrue(); // the request itself is built correctly
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
    // GoalShortfall agree that the concern is resolved." New 2026-08-14,
    // prompted directly by the user's own question about testing whether a
    // saved resolution actually satisfies what it was meant to fix.

    // The plan's health heads-up — was a separate post-save "Worth a look"
    // MessageBox, now an announcement row in the confirmation (centralized
    // 2026-08-27 at the author's request, so all of a save's messaging lives in
    // this one system). Independent of everything else that might fire this save:
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
            return plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2026, 1, 1)); // deliberately not the first one
        };
        confirmation.NavigateToEarmarkForm = (plan, _) => navigatedTo = plan;

        confirmation.Run().ShouldBeTrue();

        offeredPlans.ShouldNotBeNull();
        offeredPlans!.Count.ShouldBe(2);
        navigatedTo.ShouldNotBeNull();
        navigatedTo!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2026, 1, 1)); // the picker's own choice, not savingsPlan[0]
    }

    // ---- shared scenario-building helpers ----------------------------------

    // M2's front-truncation — the Start-side twin of the back-truncation
    // crash fix. A future bill (starts after AsOf, so editing its Start is
    // non-Critical and saves in place) whose Start is pushed later leaves its
    // plan starting before it — a 3.11.2.a2 violation that used to crash the
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

    // Completing planning/28's Q3 -> Q4 -> Q6 tree: when an edit touches the chain
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
