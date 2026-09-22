using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;

// [WRITES FILE] Resets the app's real local database and repopulates it with a varied
// household's worth of sample data — accounts, bills, a paycheck, one-time
// and repeating goals, savings plans, manual adjustments, a recurring
// transfer, a break-off chain, and a second concurrent earmark pattern on one goal —
// built entirely through the domain's own Create() methods, the real
// factories (AllocationPlanProposer, BreakOffFactory), and the real
// repositories, so nothing here can be invalid in a way hand-written SQL
// could accidentally be.
//
// Don't trust the inline comments below about which PlanHealthCategory a
// scenario lands in — read the report printed at the end, which runs a real
// forecast and states the actual result.

var dbPath = PatternDatabase.DefaultDatabasePath();
Console.WriteLine($"Database: {dbPath}");

// Microsoft.Data.Sqlite pools native file handles even after every
// SqliteConnection using them is disposed (PatternDatabase's own comment on
// ReleasePooledConnections) — without clearing them first, deleting the file
// here can leave a stale handle that the next PatternDatabase() reconnects
// to instead of the fresh file this just tried to create. Same reason the
// app's own Import flow calls this before overwriting the live file.
PatternDatabase.ReleasePooledConnections();

foreach (var sidecar in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
{
    if (File.Exists(sidecar))
    {
        File.Delete(sidecar);
    }
}

Console.WriteLine("Existing database removed — starting from a clean, first-launch-equivalent state.");

var database = new PatternDatabase();
var accounts = new AccountRepository(database);
var financialPatterns = new FinancialPatternRepository(database);
var earMarkPatterns = new EarMarkPatternRepository(database, financialPatterns);
var manualEarmarks = new ManualEarmarkRepository(database, earMarkPatterns);
var transfers = new TransferRepository(database, financialPatterns);
var currentBalance = new CurrentBalanceRepository(database);

// "Today" for this whole dataset — every relative date below ("due in N
// days," "already a few payments in") is anchored off this. Reads the real
// clock rather than a fixed date, so re-running the tool on a later day
// doesn't need this line touched by hand to keep "today"-dated entries
// working (the date-picker's own "mark existing entries" feature is
// future-only, so a stale hardcoded date would silently stop having
// anything to mark).
var asOfDate = DateOnly.FromDateTime(DateTime.Today);
var horizonEndDate = asOfDate.AddMonths(12);

// ---- Accounts -----------------------------------------------------------

var checking = Account.Create(new AccountOptions { Id = accounts.NextId(), Name = "Checking", Balance = 3200.00m, IdealSafetyCushion = 500.00m });
accounts.Save(checking);
var savings = Account.Create(new AccountOptions { Id = accounts.NextId(), Name = "Savings", Balance = 8500.00m, IdealSafetyCushion = 0m });
accounts.Save(savings);
var joint = Account.Create(new AccountOptions { Id = accounts.NextId(), Name = "Joint Household", Balance = 1450.00m, IdealSafetyCushion = 200.00m });
accounts.Save(joint);
Console.WriteLine($"Accounts: {checking.Name}, {savings.Name}, {joint.Name}");

var farUntil = new DateOnly(2027, 12, 31);
var financeId = 1;
int NextFinanceId() => financeId++;

RecurrenceRule Monthly(DateOnly start, int interval = 1, DateOnly? until = null) => RecurrenceRule.Create(new RecurrenceRuleOptions
{
    Frequency = RecurrenceFrequency.Monthly,
    DtStart = start,
    Interval = interval,
    ByMonthDay = [start.Day],
    Until = until ?? farUntil,
});

RecurrenceRule Biweekly(DateOnly start, DayOfWeek day, DateOnly? until = null) => RecurrenceRule.Create(new RecurrenceRuleOptions
{
    Frequency = RecurrenceFrequency.Weekly,
    DtStart = start,
    Interval = 2,
    ByDay = [day],
    Until = until ?? farUntil,
});

// No explicit ByDay — RecurrenceRule.Create leaves that facet unset when
// empty, so Ical.Net falls back to its RFC5545 default for Weekly: the same
// weekday as Start, every Interval weeks. Simpler and avoids needing to
// hand-verify what weekday a given date falls on.
RecurrenceRule Weekly(DateOnly start, DateOnly until) => RecurrenceRule.Create(new RecurrenceRuleOptions
{
    Frequency = RecurrenceFrequency.Weekly,
    DtStart = start,
    Until = until,
});

// activeFrom lets a plan legitimately start saving before the goal's own
// (single) occurrence date — EarMarkPattern.Create checks the earmark's span
// against the goal's ACTIVE span, not just its occurrence, exactly so this
// is possible (see EarMarkPattern.cs's own comment on the rule).
RecurrenceRule OneTime(DateOnly date, DateOnly? activeFrom = null) => RecurrenceRule.Create(new RecurrenceRuleOptions
{
    Frequency = RecurrenceFrequency.Yearly,
    DtStart = date,
    Count = 1,
    ActiveFrom = activeFrom,
});

// ---- Paycheck (income, repeating) — created early so every proposer/
// break-off call below that scans for income actually finds one ------------

var paycheck = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Employer Inc", DatePattern = Biweekly(new DateOnly(2026, 1, 2), DayOfWeek.Friday), Amount = 2600m });
financialPatterns.Save(paycheck, checking.Id);

// ---- Bills (mandatory, repeating) ----------------------------------------

var rent = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Landlord LLC", Description = "Rent", DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -1800m, Priority = 10 });
financialPatterns.Save(rent, checking.Id);

var electric = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Electric Company", DatePattern = Monthly(new DateOnly(2026, 1, 15)), Amount = -145m, Priority = 8 });
financialPatterns.Save(electric, checking.Id);

var internet = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Internet Provider", DatePattern = Monthly(new DateOnly(2026, 1, 20)), Amount = -70m, Priority = 7 });
financialPatterns.Save(internet, checking.Id);

var carInsurance = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Car Insurance Co", DatePattern = Monthly(new DateOnly(2026, 1, 1), interval: 3), Amount = -300m, Priority = 9 });
financialPatterns.Save(carInsurance, joint.Id);

var streaming = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Streaming Service", DatePattern = Monthly(new DateOnly(2026, 1, 5)), Amount = -15.99m, Priority = 2, Mandatory = false });
financialPatterns.Save(streaming, checking.Id);

var gym = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Gym Membership", DatePattern = Monthly(new DateOnly(2026, 2, 1)), Amount = -45m, Priority = 3, Mandatory = false });
financialPatterns.Save(gym, checking.Id);

// A "keeps going" bill plus a DELIBERATELY thin plan: $45/mo against a
// $65/mo bill can never catch up on its own rate alone — a structural,
// chronic shortfall (IsChronicShortfall = true), distinct from Car Repair's
// one-off gap below that a single catch-up earmark would fully close.
var phoneBill = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Mobile Carrier", DatePattern = Monthly(new DateOnly(2026, 1, 12)), Amount = -65m, Priority = 6, AutoRenew = true });
financialPatterns.Save(phoneBill, checking.Id);
var phonePlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = phoneBill.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 12)), Amount = -45m }, phoneBill);
earMarkPatterns.Save(phonePlan);
Console.WriteLine("Bill: Mobile Carrier — AutoRenew ('keeps going'), plan deliberately underfunded (chronic shortfall)");

// A bill due soon enough that AllocationPlanProposer's own logic should
// front-load it with a starting earmark before the paced plan's first
// contribution catches up — an "implicit urgency front-load" example. Built
// through the real proposer, not a hand-set number. Whether "3 days out"
// actually lands before the next paced contribution depends on
// calendar/payday phase — see the printed report rather than trusting this
// comment; nudge the day offset here and re-run if it didn't fire.
var carRegistration = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "DMV Registration", DatePattern = OneTime(asOfDate.AddDays(1)), Amount = -180m, Priority = 8 });
var carRegProposal = AllocationPlanProposer.Propose(carRegistration, financialPatterns.GetAll(), asOfDate);
financialPatterns.Save(carRegProposal.Outflow, checking.Id);
earMarkPatterns.Save(carRegProposal.Plan);

// FINDING, not resolved: MaybeStartingEarmark never actually fires for this
// asOfDate, for any due date. ProposePaced always anchors the plan's
// DatePattern.Start to asOfDate itself, and the RRULE library still yields
// asOfDate as the first occurrence even when it doesn't fall on the
// income's own payday — so the plan always "already covers" any due date on
// or after asOfDate, and the urgency check can never trip. Whether that
// also under-fires in the real app on an ordinary day is an open question.
// The line below hand-models what the mechanism is designed to produce, so
// there's still a real example to look at even though it didn't come from
// Propose() today.
var carRegStartingEarmark = ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = carRegProposal.Outflow.FinanceId, Date = asOfDate, Amount = 180m }, carRegProposal.Plan);
manualEarmarks.Save(carRegStartingEarmark);
Console.WriteLine("Bill: DMV Registration — starting-earmark shape hand-modeled (see the FINDING comment above); everything else about the plan is real Propose() output.");

// A REPEATING bill whose first due date is soon enough that ProposePaced's
// own rate — a long-run average spread across every payday through the
// bill's FAR Until, not tuned to the first occurrence specifically — can't
// have accumulated enough by then. Tests whether a plan needing SEVERAL
// contributions to cover one occurrence, with only some landed by the due
// date, reads as a genuine PARTIAL shortfall (not $0, not the full amount)
// through IsFirstOccurrencePending/FirstOccurrenceShortfall.
//
// MaybeStartingEarmark can't catch this one either, for the same reason as
// DMV Registration above: its plan always starts AT asOfDate for this
// dataset, so the urgency check never fires regardless of how short the due
// date is. MaybeStartingEarmark only ever asks "has ANYTHING landed by the
// due date," never "has ENOUGH" — this scenario, and the PlanHealthState
// properties, answer that second question. See the printed report below
// for the real number, not this comment's guess.
var propertyTax = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "County Property Tax", DatePattern = Monthly(asOfDate.AddDays(40), interval: 6), Amount = -1200m, Priority = 8 });
var propertyTaxProposal = AllocationPlanProposer.Propose(propertyTax, financialPatterns.GetAll(), asOfDate);
financialPatterns.Save(propertyTaxProposal.Outflow, checking.Id);
earMarkPatterns.Save(propertyTaxProposal.Plan);
if (propertyTaxProposal.StartingEarmark is { } propertyTaxStartingEarmark)
{
    manualEarmarks.Save(propertyTaxStartingEarmark);
}
Console.WriteLine($"Bill: County Property Tax — real Propose() output, semi-annual, first due date {propertyTax.DatePattern.ActiveStart:yyyy-MM-dd}, " +
    $"paced at {-propertyTaxProposal.Plan.Amount:C}/payday; MaybeStartingEarmark fired: {propertyTaxProposal.StartingEarmark is not null} " +
    "(see FirstOccurrenceShortfall in the report below for the real coverage check)");

// Same real-Propose() shape as County Property Tax, but quarterly (a
// shorter cycle relative to the biweekly paycheck, so fewer paydays are
// "missing" by the due date) — meant to land closer to a HALF-covered gap
// rather than Property Tax's more dramatic one, so the warning's own
// scaling (a small gap reads as a small gap, not just "always huge") has a
// real example too.
var hoaAssessment = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "HOA Assessment", DatePattern = Monthly(asOfDate.AddDays(35), interval: 3), Amount = -400m, Priority = 7 });
var hoaProposal = AllocationPlanProposer.Propose(hoaAssessment, financialPatterns.GetAll(), asOfDate);
financialPatterns.Save(hoaProposal.Outflow, checking.Id);
earMarkPatterns.Save(hoaProposal.Plan);
if (hoaProposal.StartingEarmark is { } hoaStartingEarmark)
{
    manualEarmarks.Save(hoaStartingEarmark);
}
Console.WriteLine($"Bill: HOA Assessment — real Propose() output, quarterly, first due date {hoaAssessment.DatePattern.ActiveStart:yyyy-MM-dd}, " +
    $"paced at {-hoaProposal.Plan.Amount:C}/payday (see FirstOccurrenceShortfall in the report below)");

// A THIRD flavor of the same gap: a small dollar amount can still trigger a
// real first-occurrence shortfall when the CADENCE mismatch is severe
// enough — annual bill, biweekly paycheck, due date soon — proportionally
// even worse than Property Tax despite being a much smaller number, since
// at most one or two paydays can possibly land before it either way.
var membershipDues = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Warehouse Club Membership", DatePattern = Monthly(asOfDate.AddDays(22), interval: 12), Amount = -150m, Priority = 3, Mandatory = false });
var membershipProposal = AllocationPlanProposer.Propose(membershipDues, financialPatterns.GetAll(), asOfDate);
financialPatterns.Save(membershipProposal.Outflow, checking.Id);
earMarkPatterns.Save(membershipProposal.Plan);
if (membershipProposal.StartingEarmark is { } membershipStartingEarmark)
{
    manualEarmarks.Save(membershipStartingEarmark);
}
Console.WriteLine($"Bill: Warehouse Club Membership — real Propose() output, annual, first due date {membershipDues.DatePattern.ActiveStart:yyyy-MM-dd}, " +
    $"paced at {-membershipProposal.Plan.Amount:C}/payday (see FirstOccurrenceShortfall in the report below)");

// A genuine break-off, wired through the real factory — so the successor's
// StartingAllocation is authentic, not a guessed number. This is the
// "inherited from break-off" starting-point case. The predecessor is left
// open-ended (farUntil) on purpose: BreakOff's own PatternTruncation.EndOn
// is what actually imposes the 2026-06-30 end, the same as a real "Change
// starting on a date" action would.
var carLease1 = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Car Lease Payment", DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -420m, Priority = 6 });
var carLease1Plan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = carLease1.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -420m }, carLease1);

var carLeaseBreakOff = BreakOffFactory.BreakOff(new BreakOffRequest
{
    Predecessor = carLease1,
    PredecessorPlan = carLease1Plan,
    CutDate = new DateOnly(2026, 7, 1),
    SuccessorFinanceId = NextFinanceId(),
    SuccessorAmount = -450m,
    SuccessorSchedule = new RecurrenceRuleOptions { Frequency = RecurrenceFrequency.Monthly, DtStart = new DateOnly(2026, 7, 1), ByMonthDay = [1], Until = farUntil },
    // A flat, illustrative figure — a real break-off reads this off the LIVE
    // forecast at the cut date (the UI's job, not this factory's); this
    // script has no live forecast to read yet at this point, so this is a
    // plausible stand-in, not a derived one.
    CarriedOverJarBalance = 340m,
    AllPatterns = financialPatterns.GetAll(),
});

financialPatterns.Save(carLeaseBreakOff.Predecessor, checking.Id);
if (carLeaseBreakOff.PredecessorPlan is { } carLease1TruncatedPlan)
{
    earMarkPatterns.Save(carLease1TruncatedPlan);
}

financialPatterns.Save(carLeaseBreakOff.Successor, checking.Id);
if (carLeaseBreakOff.SuccessorPlan is { } carLease2Plan)
{
    earMarkPatterns.Save(carLease2Plan);
}

if (carLeaseBreakOff.SuccessorStartingEarmark is { } carLeaseStartingEarmark)
{
    manualEarmarks.Save(carLeaseStartingEarmark);
}

// Genuinely two things stacking, not one: $340 carried over from the old
// segment (StartingAllocation) PLUS a real $450 urgency front-load
// (AllocationPlanProposer.MaybeStartingEarmark, fired for real here — unlike
// DMV Registration above, where it doesn't) because the new segment's first
// payment is due the same day it starts. The Earmark form's Starting-point
// region sums both on purpose — $790 total is correct, not a bug.
Console.WriteLine($"Bill: Car Lease Payment — real break-off chain, successor StartingAllocation = {carLeaseBreakOff.SuccessorPlan?.StartingAllocation:C}" +
    (carLeaseBreakOff.SuccessorStartingEarmark is { } se ? $" + a real starting earmark {se.Amount:C} on {se.Date:yyyy-MM-dd}" : string.Empty));

Console.WriteLine($"Financial patterns so far: {financeId - 1} (bills, paycheck)");

// ---- One-time goals --------------------------------------------------

var tripToJapan = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Trip to Japan", DatePattern = OneTime(new DateOnly(2026, 12, 1), activeFrom: new DateOnly(2026, 6, 5)), Amount = -3200m, Priority = 5 });
financialPatterns.Save(tripToJapan, savings.Id);

var carRepair = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Emergency: Car repair", DatePattern = OneTime(asOfDate.AddDays(5), activeFrom: asOfDate.AddDays(-11)), Amount = -600m, Priority = 9 });
financialPatterns.Save(carRepair, checking.Id);

// Deliberately left without a savings plan below — the "goal exists, no plan
// yet" state.
var newLaptop = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "New Laptop", DatePattern = OneTime(new DateOnly(2026, 10, 15)), Amount = -1400m, Priority = 4 });
financialPatterns.Save(newLaptop, checking.Id);

// A user who DECLINED the proposed plan at creation time — a real,
// zero-contribution EarMarkPattern (AllocationPlanProposer.ProposeEmpty),
// not a hand-set $0 plan. Reads as short from the moment it exists — check
// the printed report below for which category it actually lands in.
// Doubles as the "$0, nothing to explain" starting-point control case.
var homeDownPayment = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Home Down Payment", DatePattern = OneTime(new DateOnly(2027, 4, 1)), Amount = -15000m, Priority = 5 });
var homeProposal = AllocationPlanProposer.ProposeEmpty(homeDownPayment, asOfDate);
financialPatterns.Save(homeProposal.Outflow, savings.Id);
earMarkPatterns.Save(homeProposal.Plan);

// A repeating, non-mandatory goal whose plan starts with a MANUAL, plainly
// user-declared "already saved" entry — no proposer, no automatic
// reasoning, added manually with no automatic reason recorded. This is the
// third starting-point case (the first two are the DMV Registration and Car
// Lease scenarios above).
var emergencyFundReserve = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Emergency Fund Reserve", Description = "Emergency fund top-up", DatePattern = Monthly(asOfDate, until: new DateOnly(2027, 8, 1)), Amount = -100m, Priority = 3, Mandatory = false });
financialPatterns.Save(emergencyFundReserve, checking.Id);
var emergencyPlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = emergencyFundReserve.FinanceId, DatePattern = Monthly(asOfDate, until: new DateOnly(2027, 8, 1)), Amount = -100m }, emergencyFundReserve);
earMarkPatterns.Save(emergencyPlan);

Console.WriteLine($"Financial patterns: {financeId - 1} total (bills, paycheck, one-time and repeating goals — including one real break-off chain and one declined plan)");

// ---- Transfer (recurring, Checking -> Savings) ---------------------------

var transferResult = TransferFactory.Create(new TransferRequest
{
    TransferId = transfers.NextId(),
    WithdrawalFinanceId = NextFinanceId(),
    DepositFinanceId = NextFinanceId(),
    FromAccountId = checking.Id,
    ToAccountId = savings.Id,
    FromAccountName = checking.Name,
    ToAccountName = savings.Name,
    Amount = 200m,
    DatePattern = Monthly(new DateOnly(2026, 1, 3)),
});
transfers.Save(transferResult);
Console.WriteLine("Transfer: Checking -> Savings, $200/month");

// ---- Savings plans (EarMarkPatterns) for the simple bills/goals above -----
// Left without a plan on purpose: Electric, Internet, Streaming, Gym,
// Employer Inc (income never gets one), New Laptop — a mix of "no plan
// needed" and "goal exists, no plan yet" is more realistic than everything
// being fully set up.

var rentPlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = rent.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -1800m }, rent);
earMarkPatterns.Save(rentPlan);

// The "Car insurance savings plan" numbers
// ($100/month) — so the real UI can be compared directly against them.
var carInsurancePlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = carInsurance.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 15)), Amount = -100m }, carInsurance);
earMarkPatterns.Save(carInsurancePlan);

// The "Trip to Japan" numbers ($320
// biweekly Fridays, Jun 5 - Dec 1) — this plan out-paces the $3,200 goal on
// its own (13 occurrences x $320 = $4,160), so it's deliberately the
// WillBeOverfunded/CurrentlyOverfunded exemplar, not a healthy one; see the
// printed report for which it actually lands as.
var tripPlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = tripToJapan.FinanceId, DatePattern = Biweekly(new DateOnly(2026, 6, 5), DayOfWeek.Friday, until: new DateOnly(2026, 12, 1)), Amount = -320m }, tripToJapan);
earMarkPatterns.Save(tripPlan);

// A second person funding the SAME goal — the original design forbids this
// outright (3.11.1.a1); the current app deliberately allows it as long as
// the two plans don't share a StartDate (the EarMarkPatterns table's real
// composite key now). The engine sums both into one jar, one PlanHealthState.
var tripPlanPartner = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = tripToJapan.FinanceId, DatePattern = Biweekly(new DateOnly(2026, 6, 19), DayOfWeek.Friday, until: new DateOnly(2026, 11, 20)), Amount = -100m }, tripToJapan);
earMarkPatterns.Save(tripPlanPartner);

// Deliberately underfunded ($50/week for 3 weeks = $150 against a $600 need,
// due in 5 days from asOfDate) — a real ONE-OFF shortfall/warning state
// (contrast with Mobile Carrier's chronic one above): a single catch-up
// earmark would fix this for good.
var carRepairPlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = carRepair.FinanceId, DatePattern = Weekly(asOfDate.AddDays(-11), asOfDate.AddDays(3)), Amount = -50m }, carRepair);
earMarkPatterns.Save(carRepairPlan);

Console.WriteLine("Savings plans: Rent, Car Insurance ($100/mo), Trip to Japan (+ a second concurrent earmark pattern), Emergency car repair (deliberately underfunded)");

// ---- Manual earmarks (one-off adjustments) --------------------------------

manualEarmarks.Save(ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = tripToJapan.FinanceId, Date = new DateOnly(2026, 7, 4), Amount = 200m }, tripPlan));
manualEarmarks.Save(ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = carInsurance.FinanceId, Date = new DateOnly(2026, 3, 1), Amount = 50m }, carInsurancePlan));

// The "plain user decision" starting-point case: no proposer involved, just
// a flat declaration dated at the plan's own Start.
manualEarmarks.Save(ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = emergencyFundReserve.FinanceId, Date = asOfDate, Amount = 500m }, emergencyPlan));

// A recent WITHDRAWAL, not a deposit — pulls today's jar below today's
// milestone. Tried this against Mobile Carrier first; it had no effect,
// since Mobile Carrier's release lands the same day each month as its one
// contribution, so the jar is back near $0 within a day of every accrual
// and per-day flooring absorbs a withdrawal that lands in that gap. Car
// Repair's plan contributes weekly toward a due date that's still 3 days
// out, so real balance actually sits in the jar to pull from — this is the
// only scenario in the set that reads short RIGHT NOW (AlreadyMissing
// outranks WillMiss in the ranking rule), layered on the whole-span
// shortfall it already has.
manualEarmarks.Save(ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = carRepair.FinanceId, Date = asOfDate.AddDays(-1), Amount = -40m }, carRepairPlan));

Console.WriteLine("Manual earmarks: +$200 on Trip to Japan (Jul 4), +$50 on Car Insurance (Mar 1), +$500 on Emergency Fund Top-up (day one, plain user decision), -$40 on Car Repair (recent withdrawal, forces AlreadyMissing)");

// ---- Editing-with-history manual-test scenarios --------------
// Two bills built specifically to exercise editing-with-history through the
// REAL UI, not just the automated tests: each already has real history
// (occurrences well before asOfDate) and its own savings plan(s), so editing
// a Critical field (amount/start_date/schedule) through the Expense form
// should bring up EditingHistoryConfirmationWindow for real. Both plans fund
// slightly AHEAD of their bill's own pace on purpose — a plan that exactly
// matches its bill's due amount gets drained back to near $0 every cycle,
// leaving nothing real to see get carried over or absorbed.

// The forced-consolidation case (multiple existing EarMarkPatterns sharing a
// finance_id): two concurrent earmark patterns. Edit the due date (e.g. the 1st ->
// the 15th) and save — the schedule itself changing makes ConsolidationNeeded
// true, so the popup should ANNOUNCE the combine rather than ask, and the
// two plans should become one under a new finance id once saved.
var storageUnit = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Storage Unit Rental", DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -50m, Priority = 4, Mandatory = false });
financialPatterns.Save(storageUnit, checking.Id);
var storageUnitPlanA = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = storageUnit.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -35m }, storageUnit);
earMarkPatterns.Save(storageUnitPlanA);
var storageUnitPlanB = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = storageUnit.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 2)), Amount = -25m }, storageUnit);
earMarkPatterns.Save(storageUnitPlanB);
Console.WriteLine("Bill: Storage Unit Rental — TWO concurrent savings plans, real history since Jan 2026. To see the forced consolidation: edit its due date (e.g. 1st -> 15th) and save.");

// A single-plan bill with real history, plus a manual top-up dated in the
// past. Editing a Critical field (amount or start date) breaks off a new
// segment from today — forward-only; the old retroactive "apply it everywhere"
// path was removed. Editing its AMOUNT is the plan-shape picker case: the
// successor plan can be shaped several ways (Recommended / keep the same
// schedule / keep the same amount), so the confirmation offers that choice, and
// the past top-up gives the break-off a real carried-over balance to reflect.
var cloudStorage = FinancialPattern.Create(new FinancialPatternOptions { FinanceId = NextFinanceId(), Source = "Cloud Storage Plan", DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -12m, Priority = 2, Mandatory = false });
financialPatterns.Save(cloudStorage, checking.Id);
var cloudStoragePlan = EarMarkPattern.Create(new EarMarkPatternOptions { FinanceId = cloudStorage.FinanceId, DatePattern = Monthly(new DateOnly(2026, 1, 1)), Amount = -20m }, cloudStorage);
earMarkPatterns.Save(cloudStoragePlan);
manualEarmarks.Save(ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = cloudStorage.FinanceId, Date = new DateOnly(2026, 2, 1), Amount = 15m }, cloudStoragePlan));
Console.WriteLine("Bill: Cloud Storage Plan — ONE savings plan, real history since Jan 2026, plus a manual +$15 top-up on Feb 1. To see the plan-shape picker: edit its amount and save — the break-off's successor plan can be shaped several ways.");

// ---- Forecast tab pre-fill -------------------------------------------------

currentBalance.Save(0m, asOfDate, horizonEndDate, 0m);
Console.WriteLine($"Forecast range pre-filled: {asOfDate:yyyy-MM-dd} through {horizonEndDate:yyyy-MM-dd}");

// ---- Verify: run a REAL forecast and report what actually landed ----------
// Mirrors MainWindow.BuildAccountInputs()/RefreshForecast() exactly, so this
// is the same computation the app itself will show — not a separate,
// possibly-diverging check.

var patternsByAccount = financialPatterns.GetAllByAccount();
var allEarmarkPatterns = earMarkPatterns.GetAll();
var allManualEarmarks = manualEarmarks.GetAll();

var accountInputs = accounts.GetAll().Select(account =>
{
    var patterns = patternsByAccount.GetValueOrDefault(account.Id) ?? [];
    var accountFinanceIds = patterns.Select(p => p.FinanceId).ToHashSet();
    return new AccountForecastInput
    {
        AccountId = account.Id,
        Name = account.Name,
        StartingBalance = account.Balance,
        IdealSafetyCushion = account.IdealSafetyCushion,
        FinancialPatterns = patterns,
        EarMarkPatterns = allEarmarkPatterns.Where(e => accountFinanceIds.Contains(e.FinanceId)).ToList(),
        ManualEarmarks = allManualEarmarks.Where(m => accountFinanceIds.Contains(m.FinanceId)).ToList(),
    };
}).ToList();

var forecast = TransactionLogBookFactory.CreateForecast(new ForecastOptions
{
    FinancialPatterns = [],
    EarMarkPatterns = [],
    StartingBalance = 0m,
    AsOfDate = asOfDate,
    HorizonEndDate = horizonEndDate,
    Accounts = accountInputs,
});

var patternsById = financialPatterns.GetAll().ToDictionary(p => p.FinanceId);
var plansByFinanceId = allEarmarkPatterns.GroupBy(e => e.FinanceId).ToDictionary(g => g.Key, g => g.ToList());

Console.WriteLine();
Console.WriteLine("==================== PLAN HEALTH REPORT (from a real forecast run) ====================");
foreach (var health in forecast.PlanHealthStates.OrderBy(h => h.MostImportantHealthState).ThenBy(h => h.FinanceId))
{
    var label = patternsById.GetValueOrDefault(health.FinanceId)?.Source ?? $"finance id {health.FinanceId}";
    var plans = plansByFinanceId.GetValueOrDefault(health.FinanceId) ?? [];
    var startingAllocation = plans.Sum(p => p.StartingAllocation);
    var startingNote = startingAllocation > 0m ? $" | StartingAllocation {startingAllocation:C}" : string.Empty;
    var concurrentNote = plans.Count > 1 ? $" | {plans.Count} concurrent plans" : string.Empty;
    var firstOccNote = health.IsFirstOccurrencePending
        ? $" | firstOccPending=true firstOccShort={health.FirstOccurrenceShortfall:C}"
        : string.Empty;

    Console.WriteLine(
        $"{health.MostImportantHealthState,-20} {label,-24} " +
        $"chronic={health.IsChronicShortfall,-5} warn={health.IsWorthWarningAbout,-5} " +
        $"todayShort={health.CurrentShortfallAmount,10:C} todayOver={health.CurrentOverfundedAmount,10:C} " +
        $"dueShort={health.Shortfall.ShortfallAmount,10:C} dueOver={health.Shortfall.OverfundedAmount,10:C}" +
        $"{startingNote}{concurrentNote}{firstOccNote}");
}

var seenCategories = forecast.PlanHealthStates.Select(h => h.MostImportantHealthState).Distinct().OrderBy(c => c).ToList();
var missingCategories = Enum.GetValues<PlanHealthCategory>().Except(seenCategories).ToList();
Console.WriteLine();
Console.WriteLine("Category coverage: " + string.Join(", ", seenCategories));
if (missingCategories.Count > 0)
{
    Console.WriteLine("NOT represented: " + string.Join(", ", missingCategories) + " — a scenario above needs adjusting if one of these matters right now.");
}

Console.WriteLine();
Console.WriteLine(
    $"Household: as-of free {forecast.Household.AsOfFree:C}" +
    (forecast.HasNegativeFreeBalance ? $" | first negative free balance: {forecast.FirstNegativeFreeBalanceDate:yyyy-MM-dd}" : " | never goes negative in this window"));

var cushionDippedDays = forecast.Household.Days.Where(d => d.AnyCushionDipped).ToList();
Console.WriteLine(cushionDippedDays.Count > 0
    ? $"Cushion dipped on {cushionDippedDays.Count} day(s) — first {cushionDippedDays[0].Date:yyyy-MM-dd} ({string.Join(", ", cushionDippedDays[0].CushionDippedAccounts)})"
    : "Cushion never dips below target in this window.");

Console.WriteLine();
Console.WriteLine("To see the editing-with-history popup on the Expense tab: Storage Unit Rental (multiple plans — edit the due date for the forced-consolidation notice) and Cloud Storage Plan (single plan — edit the amount for the plan-shape picker).");
Console.WriteLine();
Console.WriteLine("Done. Launch the app to see it populated.");
