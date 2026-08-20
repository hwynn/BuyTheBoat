using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// Exercises FinancePatternSaveConfirmation's OTHER constructor — the
// EarMarkPattern-editing entry point (planning/27), distinct from the
// FinancialPattern-editing one FinancePatternSaveConfirmationTests already
// covers. Same real-SQLite-temp-file shape as that file; every [Fact] drives
// Run() itself.
public class FinancePatternSaveConfirmationEarmarkTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-earmark-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public FinancePatternSaveConfirmationEarmarkTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    [Fact]
    public void A_brand_new_plan_is_saved_directly_with_no_question_asked()
    {
        var goal = Goal(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var newPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        var confirmation = Confirmation(newPlan, newPlan.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked for a brand-new plan");

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().ShouldHaveSingleItem();
    }

    [Fact]
    public void Extending_until_into_a_successor_nudges_it_by_default_without_asking_to_break()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        // No ConfirmImplicitChanges wired up — proves the DEFAULT (stay
        // linked) is what fires, not a hard-coded test answer.

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 1)).DatePattern.Until.ShouldBe(new DateOnly(2025, 4, 15));
        var adjustedSuccessor = plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 16));
        adjustedSuccessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // unchanged
        adjustedSuccessor.Amount.ShouldBe(-80m); // unchanged — no cascade was in play here
    }

    [Fact]
    public void Choosing_to_let_the_chain_break_leaves_the_successor_completely_untouched()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseToLetChainBreak();

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);
        var untouchedSuccessor = plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 1));
        untouchedSuccessor.Amount.ShouldBe(successor.Amount);
        untouchedSuccessor.DatePattern.Until.ShouldBe(successor.DatePattern.Until); // own overlap with current left in place, not resolved
    }

    [Fact]
    public void Extending_until_far_enough_absorbs_the_successor_entirely()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), startingAllocation: 40m);
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem();
        plans[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        plans[0].StartingAllocation.ShouldBe(40m); // carried forward from the absorbed successor
    }

    [Fact]
    public void Cascading_forward_updates_a_later_plans_own_amount_and_shape_by_default()
    {
        var goal = Goal(-220m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        // No ConfirmImplicitChanges wired up — proves cascading forward is
        // the actual default, not something only a test-supplied answer does.

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        var cascaded = plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 1));
        cascaded.Amount.ShouldBe(-120m);
        cascaded.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // its own dates, untouched
    }

    [Fact]
    public void Choosing_just_this_plan_leaves_the_later_plans_own_amount_alone()
    {
        var goal = Goal(-220m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseJustThisSegment();

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 1)).Amount.ShouldBe(-100m);
    }

    // The composed case — a boundary change AND a cascade-eligible change in
    // the same save, both touching the SAME successor: its own dates move
    // (from the boundary resolution) AND its own amount changes (from the
    // cascade), landing on ONE final row, not two conflicting writes.
    [Fact]
    public void A_boundary_change_and_a_cascade_together_both_land_on_the_same_successor()
    {
        var goal = Goal(-220m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        // Until extends 15 days into the successor AND the amount changes —
        // both defaults (stay linked, cascade forward) apply.
        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);
        var adjustedSuccessor = plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 16));
        adjustedSuccessor.Amount.ShouldBe(-120m); // cascaded
        adjustedSuccessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // its own, unaffected by the cascade
    }

    // The bug this test specifically guards against: an amount-only edit
    // (no Start/Until change) must update the existing row in place, never
    // delete it — the "only delete when the key actually changed" check has
    // to run AFTER boundary resolution, not be assumed up front.
    [Fact]
    public void An_amount_only_change_updates_the_existing_row_instead_of_dropping_it()
    {
        var goal = Goal(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);

        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.ShouldHaveSingleItem();
        plans[0].Amount.ShouldBe(-120m);
    }

    // Found while grounding the UI-wiring work, not asked for — a genuinely
    // concurrent plan (F27: two funders both live at once, e.g. the "Storage
    // Unit Rental" seed scenario) must NEVER be mistaken for a sequential
    // chain neighbor. Before this was guarded, otherPlans filtered purely on
    // Start comparison, so a concurrent plan with a later Start than the one
    // being edited satisfied hasSuccessor too — meaning extending Until into
    // a concurrent plan's own overlapping span would silently truncate it
    // (RestructureFactory.ExtendUntil has no concept of "this isn't really a
    // successor," it just does what it's told). ConfirmImplicitChanges
    // throws if invoked, same as the brand-new-plan test above — proves no
    // question was asked, not just that the final state happens to look right.
    [Fact]
    public void A_concurrent_plans_own_span_is_never_mistaken_for_a_chain_successor()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var main = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var concurrent = Plan(goal, -50m, new DateOnly(2025, 3, 1), new DateOnly(2025, 8, 31));
        _earMarkPatterns.Save(main);
        _earMarkPatterns.Save(concurrent);

        // Shrinks main's own Until to Jun 30 — still well inside concurrent's
        // own Mar 1 - Aug 31 span, so the two plans still overlap either way.
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, main.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — concurrent, not a chain neighbor");

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Count.ShouldBe(2);
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 1)).DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // main, edited
        var untouchedConcurrent = plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 3, 1));
        untouchedConcurrent.DatePattern.Until.ShouldBe(new DateOnly(2025, 8, 31)); // NOT truncated to Jul 1
        untouchedConcurrent.Amount.ShouldBe(-50m);
    }

    // Same concern as the test above, for the cascade side instead of the
    // boundary side — a concurrent plan's own independent rate must not get
    // silently overwritten by an unrelated plan's Amount edit either.
    [Fact]
    public void A_concurrent_plans_own_amount_is_never_cascaded_onto()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var main = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var concurrent = Plan(goal, -50m, new DateOnly(2025, 3, 1), new DateOnly(2025, 8, 31));
        _earMarkPatterns.Save(main);
        _earMarkPatterns.Save(concurrent);

        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var confirmation = Confirmation(editedPlan, main.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — concurrent, not a chain neighbor");

        confirmation.Run().ShouldBeTrue();

        var plans = _earMarkPatterns.GetAll();
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 1)).Amount.ShouldBe(-120m); // main, edited
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 3, 1)).Amount.ShouldBe(-50m); // untouched
    }

    // planning/27's own "let the chain break" case: the gap left behind
    // orphans a ManualEarmark that used to sit inside the shrunk span. Left
    // undeleted, the very next ManualEarmarkRepository.GetAll() (the next
    // forecast rebuild, or this same save's own post-save refresh) would
    // throw — this isn't just a data hygiene nicety, it's a crash guard.
    [Fact]
    public void Letting_the_chain_break_deletes_a_manual_earmark_left_in_the_new_gap()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = Plan(goal, -80m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);
        _manualEarmarks.Save(ManualEarmarkEntry(current, new DateOnly(2025, 6, 15), 25m));

        // Shrinks current's own Until to Apr 30 — May/Jun is now a real gap
        // (successor's own Start stays Jul 1, untouched by "let it break"),
        // stranding the Jun 15 earmark with nothing covering it.
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Proceed().ChoseToLetChainBreak();

        confirmation.Run().ShouldBeTrue();

        _manualEarmarks.GetAll().ShouldNotContain(e => e.Date == new DateOnly(2025, 6, 15));
    }

    // The general case beyond the chain scenario above: a standalone plan
    // with no neighbor at all never even triggers PlanTouchesChainBoundary
    // (nothing to ask about), but shrinking its own span away from a manual
    // earmark is just as capable of orphaning it. ConfirmImplicitChanges
    // throws if invoked, proving this is caught with no question asked.
    [Fact]
    public void A_standalone_plans_own_shrinking_span_deletes_a_manual_earmark_it_no_longer_covers()
    {
        var goal = Goal(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);
        _manualEarmarks.Save(ManualEarmarkEntry(current, new DateOnly(2025, 8, 15), 25m));

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => throw new InvalidOperationException("should never be asked — no neighbor to touch a boundary with");

        confirmation.Run().ShouldBeTrue();

        _manualEarmarks.GetAll().ShouldNotContain(e => e.Date == new DateOnly(2025, 8, 15));
    }

    // The mirror check, proving the fix isn't overzealous: staying linked
    // must never delete a manual earmark just because the plan it was
    // originally tied to moved out from under it — as long as SOME plan in
    // the final set still covers the date, it survives. Extends current's
    // own Until into the successor's own span without fully absorbing it,
    // so the earmark ends up covered by current itself instead of by the
    // (now nudged-forward) successor it was originally saved against.
    [Fact]
    public void Staying_linked_never_deletes_a_manual_earmark_thats_still_covered_by_the_final_set()
    {
        var goal = Goal(-220m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = Plan(goal, -80m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);
        _manualEarmarks.Save(ManualEarmarkEntry(successor, new DateOnly(2025, 7, 15), 25m));

        // current's own Until grows to Aug 31 — into, but not through, the
        // successor's own span, so the successor is nudged (Start -> Sep 1),
        // not absorbed. Jul 15 is inside current's own new span now.
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 8, 31));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        // No ConfirmImplicitChanges wired up — proves the DEFAULT (stay
        // linked) is what runs, matching the plain nudge test above.

        confirmation.Run().ShouldBeTrue();

        _manualEarmarks.GetAll().ShouldContain(e => e.Date == new DateOnly(2025, 7, 15));
    }

    // Same mirror check for absorption specifically — planning/27's own
    // settled claim ("nothing is orphaned by absorption itself, the
    // survivor's span covers the union of both old ranges") backed by a
    // real ManualEarmark this time, not just StartingAllocation.
    [Fact]
    public void Absorbing_a_successor_never_deletes_a_manual_earmark_inside_its_old_range()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);
        _manualEarmarks.Save(ManualEarmarkEntry(successor, new DateOnly(2025, 5, 15), 25m));

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);

        confirmation.Run().ShouldBeTrue();

        _earMarkPatterns.GetAll().ShouldHaveSingleItem(); // successor absorbed, as in the domain-level test
        _manualEarmarks.GetAll().ShouldContain(e => e.Date == new DateOnly(2025, 5, 15));
    }

    // ---- concrete-consequence wording on the confirmation request ----------
    // planning/27's own "must name the concrete consequence, not a generic
    // notice" requirement. Every test here captures the real
    // ImplicitChangeConfirmationRequest ConfirmImplicitChanges receives and
    // asserts on its own StayLinkedWarning/LetItBreakWarning/CascadeDescription
    // text, not just the eventual saved state.

    [Fact]
    public void A_plain_nudge_gets_no_stay_linked_warning_at_all()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 0).ShouldBe("");
    }

    [Fact]
    public void An_absorb_names_the_segment_it_would_delete_before_the_user_even_chooses()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 0).ShouldContain("Apr 1, 2025");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 0).ShouldContain("Jun 30, 2025");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 0).ShouldContain("delete");
    }

    [Fact]
    public void Breaking_the_chain_names_the_gap_it_would_open()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = Plan(goal, -80m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed().ChoseToLetChainBreak();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("gap");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("May 1, 2025");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("Jun 30, 2025");
    }

    [Fact]
    public void Breaking_the_chain_names_an_overlap_instead_of_a_gap_when_the_new_span_reaches_into_the_neighbor()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        ImplicitChangeConfirmationRequest? captured = null;
        // Reaches past the successor's own Start (Apr 1) without fully
        // reaching its Until (Jun 30) — an overlap, not an absorb candidate
        // in the "stay linked" sense, and not a gap in the "let it break" sense.
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed().ChoseToLetChainBreak();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("overlap");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldNotContain("gap");
    }

    [Fact]
    public void Breaking_the_chain_names_how_many_manual_earmarks_the_gap_would_strand()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var successor = Plan(goal, -80m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);
        _manualEarmarks.Save(ManualEarmarkEntry(current, new DateOnly(2025, 6, 15), 25m));

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed().ChoseToLetChainBreak();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("1 manual earmark");
        captured.OptionConsequence(ConfirmationRowIds.ChainBoundary, 1).ShouldContain("Jun 15, 2025");
    }

    [Fact]
    public void The_cascade_description_names_the_edits_own_range_and_how_far_it_would_reach()
    {
        var goal = Goal(-220m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        ImplicitChangeConfirmationRequest? captured = null;
        var editedPlan = Plan(goal, -120m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = request =>
        {
            captured = request;
            return Confirm.Proceed();
        };

        confirmation.Run().ShouldBeTrue();

        captured.ShouldNotBeNull();
        captured.OptionConsequence(ConfirmationRowIds.Cascade, 0).ShouldContain("Jan 1, 2025");
        captured.OptionConsequence(ConfirmationRowIds.Cascade, 0).ShouldContain("Mar 31, 2025");
        captured.OptionConsequence(ConfirmationRowIds.Cascade, 0).ShouldContain("Jun 30, 2025"); // how far the cascade reaches
    }

    [Fact]
    public void Cancelling_the_confirmation_saves_nothing_at_all()
    {
        var goal = Goal(-150m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        _financialPatterns.Save(goal, accountId: 1);
        var current = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var successor = Plan(goal, -80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        _earMarkPatterns.Save(current);
        _earMarkPatterns.Save(successor);

        var editedPlan = Plan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 15));
        var confirmation = Confirmation(editedPlan, current.DatePattern.ActiveStart, goal);
        confirmation.ConfirmImplicitChanges = _ => Confirm.Cancel();

        confirmation.Run().ShouldBeFalse();

        var plans = _earMarkPatterns.GetAll();
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 1, 1)).DatePattern.Until.ShouldBe(new DateOnly(2025, 3, 31)); // unchanged
        plans.Single(p => p.DatePattern.ActiveStart == new DateOnly(2025, 4, 1)).Amount.ShouldBe(successor.Amount); // unchanged
    }

    // ---- shared scenario-building helpers ----------------------------------

    private static FinancialPattern Goal(decimal amount, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Storage Unit Rental",
            Amount = amount,
            DatePattern = Monthly(start, until),
        });

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

    private static ManualEarmark ManualEarmarkEntry(EarMarkPattern pattern, DateOnly date, decimal amount) =>
        ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = pattern.FinanceId, Date = date, Amount = amount }, pattern);

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
        HorizonEndDate = new DateOnly(2025, 12, 31),
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
