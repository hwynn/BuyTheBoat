using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

// AllFinancialPatterns() — built for FinancialPatternPickerWindow (a reusable
// "pick one of my Bills/Paychecks/Goals" popup), whose whole point is seeing
// every pattern the user has, not just the ones with occurrences inside a
// forecast's own AsOf..HorizonEnd window. Tests build TransactionLogBook/
// TransactionLogPage/AccountTransactionPage directly (all plain records) —
// no need to run a real forecast just to exercise this one method.
public class TransactionLogBookTests
{
    private static int _nextFinanceId = 2000;

    private static FinancialPattern OneOffPattern(string source, DateOnly date) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = source,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = date,
                Count = 1,
            }),
            Amount = -100m,
        });

    private static readonly BalanceSnapshot EmptySnapshot = new()
    {
        SnapshotDate = null,
        FullAmount = null,
        ExpectedAmount = 0m,
        ExpectedFreeAmount = 0m,
        FundJars = [],
        ActualTransactions = [],
        ExpectedTransactions = [],
        EarMarkEvents = [],
    };

    private static AccountTransactionPage AccountPage(string account, DateOnly start, DateOnly end, params FinancialPattern[] patterns) => new()
    {
        Account = account,
        StartDate = start,
        EndDate = end,
        FinancePatterns = patterns,
        EarmarkPatterns = [],
        InitialSnapshot = EmptySnapshot,
        BalanceRecord = [],
    };

    private static TransactionLogBook Book(params TransactionLogPage[] pages) => new()
    {
        PageLength = null,
        LogPages = pages,
    };

    private static TransactionLogBook SinglePageBook(params FinancialPattern[] patterns)
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        return Book(new TransactionLogPage
        {
            StartDate = start,
            EndDate = end,
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", start, end, patterns),
            },
        });
    }

    [Fact]
    public void ChainFinanceIds_groups_every_same_source_segment()
    {
        var electric1 = OneOffPattern("Electric", new DateOnly(2026, 1, 1));
        var electric2 = OneOffPattern("Electric", new DateOnly(2026, 6, 1)); // same Source = same chain
        var rent = OneOffPattern("Rent", new DateOnly(2026, 1, 1));
        var book = SinglePageBook(electric1, electric2, rent);

        var chain = book.ChainFinanceIds(electric1.FinanceId);

        chain.Count.ShouldBe(2);
        chain.ShouldContain(electric1.FinanceId);
        chain.ShouldContain(electric2.FinanceId);
        chain.ShouldNotContain(rent.FinanceId);
    }

    [Fact]
    public void ChainFinanceIds_returns_just_the_id_for_a_pattern_with_no_siblings()
    {
        var rent = OneOffPattern("Rent", new DateOnly(2026, 1, 1));
        var book = SinglePageBook(rent, OneOffPattern("Electric", new DateOnly(2026, 1, 1)));

        book.ChainFinanceIds(rent.FinanceId).ShouldBe([rent.FinanceId]);
    }

    [Fact]
    public void ChainFinanceIds_returns_just_the_id_when_it_has_no_pattern_here()
    {
        var book = SinglePageBook(OneOffPattern("Rent", new DateOnly(2026, 1, 1)));

        book.ChainFinanceIds(9999).ShouldBe([9999]);
    }

    [Fact]
    public void AllFinancialPatterns_returns_a_single_account_page_s_patterns()
    {
        var rent = OneOffPattern("Rent", new DateOnly(2026, 8, 1));
        var page = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), rent),
            },
        };

        Book(page).AllFinancialPatterns().ShouldBe([rent]);
    }

    [Fact]
    public void AllFinancialPatterns_aggregates_across_multiple_accounts_on_the_same_page()
    {
        var rent = OneOffPattern("Rent", new DateOnly(2026, 8, 1));
        var paycheck = OneOffPattern("Employer", new DateOnly(2026, 8, 15));
        var page = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), rent),
                ["Savings"] = AccountPage("Savings", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), paycheck),
            },
        };

        Book(page).AllFinancialPatterns().ShouldBe([rent, paycheck], ignoreOrder: true);
    }

    [Fact]
    public void AllFinancialPatterns_includes_a_pattern_whose_occurrence_is_entirely_outside_the_page_window()
    {
        // The whole point of this method: a goal due next year still has to
        // show up in a picker built off a 3-month forecast.
        var tripNextYear = OneOffPattern("Trip to Japan", new DateOnly(2027, 6, 1));
        var page = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 10, 31), // a 3-month window that never reaches 2027
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31), tripNextYear),
            },
        };

        Book(page).AllFinancialPatterns().ShouldBe([tripNextYear]);
    }

    [Fact]
    public void AllFinancialPatterns_dedupes_the_same_pattern_carried_across_multiple_pages()
    {
        // Not reachable via CreateForecast today (DIVERGENCE(page-length):
        // always exactly one page) — future-proofed for once page-to-page
        // runoff exists, so the picker never shows the same pattern twice.
        var rent = OneOffPattern("Rent", new DateOnly(2026, 8, 1));
        var pageOne = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), rent),
            },
        };
        var pageTwo = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), rent),
            },
        };

        Book(pageOne, pageTwo).AllFinancialPatterns().ShouldBe([rent]);
    }

    [Fact]
    public void AllFinancialPatterns_returns_empty_for_a_book_with_no_patterns()
    {
        var page = new TransactionLogPage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            AccountPages = new Dictionary<string, AccountTransactionPage>
            {
                ["Checking"] = AccountPage("Checking", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            },
        };

        Book(page).AllFinancialPatterns().ShouldBeEmpty();
    }
}
