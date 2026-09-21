using Shouldly;

namespace MyMoneyForecast.App.Tests;

// The selected-day group header's shortfall narrative — the three-rung ladder's
// wording (planning/22). The donor-detection that feeds these lives in
// MainWindow.ShowDayDetail (WPF, not unit-tested); this pins the strings each
// resulting state produces, the way PlanHealthMessagesTests pins the forms'.
public class AccountGroupKeyTests
{
    [Fact]
    public void A_healthy_account_shows_its_balance_but_no_lever_or_rung_line()
    {
        var key = new AccountGroupKey(AccountId: 1, Name: "Bill Pool", Balance: 2987m, Shortfall: 0m);

        key.BalanceText.ShouldBe("$2,987");                          // how much money is in the account
        key.IsShort.ShouldBeFalse();
        key.BalanceNegative.ShouldBeFalse();
        key.ShowCoverButton.ShouldBeFalse();
        key.RungText.ShouldBeEmpty();
    }

    [Fact]
    public void Rung2_with_one_donor_shows_balance_names_the_account_and_offers_the_lever()
    {
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: 0m, Shortfall: 2000m,
            CanCoverElsewhere: true, DonorName: "Bill Pool");

        key.BalanceText.ShouldBe("$0");                              // the account holds $0...
        key.IsShort.ShouldBeTrue();
        key.ShowCoverButton.ShouldBeTrue();                          // a transfer can fix it
        key.ShortText.ShouldBe("Short $2,000");                      // ...yet is short, shown to the right
        key.MoveInText.ShouldBe("Move $2,000 in →");                 // direction-explicit lever
        key.RungText.ShouldBe("In another account — the money's in Bill Pool.");
    }

    [Fact]
    public void Rung2_with_no_single_donor_stays_generic_but_still_offers_the_lever()
    {
        // Coverable in aggregate, but no one account holds the whole gap — the line
        // can't name a donor, so it stays generic; the lever still applies.
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: 0m, Shortfall: 2000m,
            CanCoverElsewhere: true, DonorName: null);

        key.ShowCoverButton.ShouldBeTrue();
        key.RungText.ShouldBe("In another account — the money's in your other accounts.");
    }

    [Fact]
    public void Rung3_says_none_can_cover_it_and_hides_the_lever()
    {
        // Genuinely short household-wide: offering "move money in" would be a lie.
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: 0m, Shortfall: 2000m,
            CanCoverElsewhere: false, DonorName: null);

        key.IsShort.ShouldBeTrue();
        key.ShowCoverButton.ShouldBeFalse();
        key.RungText.ShouldBe("No other account can cover it.");
    }

    // AccessibleSummary rolls the header's separate labels into one line, because a
    // screen reader can't reach them individually inside the WPF GroupItem header.
    // These pin what each state announces.

    [Fact]
    public void AccessibleSummary_for_a_healthy_account_is_just_name_and_balance()
    {
        var key = new AccountGroupKey(AccountId: 1, Name: "Bill Pool", Balance: 2987m, Shortfall: 0m);

        key.AccessibleSummary.ShouldBe("Bill Pool, balance $2,987");
    }

    [Fact]
    public void AccessibleSummary_for_a_short_account_adds_the_shortfall_and_rung()
    {
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: 0m, Shortfall: 2000m,
            CanCoverElsewhere: true, DonorName: "Bill Pool");

        key.AccessibleSummary.ShouldBe(
            "primary, balance $0, Short $2,000. In another account — the money's in Bill Pool.");
    }

    [Fact]
    public void AccessibleSummary_for_a_thin_but_not_short_account_adds_the_low_funds_warning()
    {
        var key = new AccountGroupKey(AccountId: 1, Name: "primary", Balance: 50m, Shortfall: 0m, IsThin: true);

        key.AccessibleSummary.ShouldBe("primary, balance $50, funds running low");
    }

    [Fact]
    public void AccessibleSummary_can_carry_both_the_shortfall_and_low_funds_warnings()
    {
        // Short and thin are independent flags that can both hold; the summary
        // announces both, in the header's own left-to-right order.
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: 0m, Shortfall: 2000m,
            CanCoverElsewhere: false, DonorName: null, IsThin: true);

        key.AccessibleSummary.ShouldBe(
            "primary, balance $0, Short $2,000, funds running low. No other account can cover it.");
    }

    [Fact]
    public void An_overdrawn_account_reports_a_negative_balance()
    {
        // Balance itself below zero (spending pushed it under) — flagged so the
        // header can render it red, the way free is elsewhere. The figure uses the
        // app's standard "C0" formatting, so a negative reads in accounting parens.
        var key = new AccountGroupKey(
            AccountId: 1, Name: "primary", Balance: -358.97m, Shortfall: 358.97m,
            CanCoverElsewhere: true, DonorName: "Bill Pool");

        key.BalanceNegative.ShouldBeTrue();
        key.BalanceText.ShouldBe("($359)");
    }
}
