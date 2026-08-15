using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class FundJarTests
{
    private static FundJar Jar(decimal expected, decimal? milestone) => new()
    {
        FinanceId = 1,
        CurrentAmount = null,
        ExpectedAmount = expected,
        MilestoneAmount = milestone,
    };

    [Fact]
    public void HasGlut_is_true_when_the_jar_holds_more_than_its_milestone_calls_for()
    {
        Jar(expected: 150m, milestone: 100m).HasGlut.ShouldBeTrue();
    }

    [Fact]
    public void HasGlut_is_true_at_the_exact_boundary()
    {
        // On-pace, not ahead — still counts: skipping the next contribution
        // wouldn't put it behind either.
        Jar(expected: 100m, milestone: 100m).HasGlut.ShouldBeTrue();
    }

    [Fact]
    public void HasGlut_is_false_when_the_jar_is_behind_its_milestone()
    {
        Jar(expected: 50m, milestone: 100m).HasGlut.ShouldBeFalse();
    }

    [Fact]
    public void HasGlut_is_false_when_no_earmark_pattern_drives_the_jar()
    {
        // The safety cushion, and auto-reserved bills — no schedule means no
        // milestone to be ahead of, regardless of how much is banked.
        Jar(expected: 500m, milestone: null).HasGlut.ShouldBeFalse();
    }

    [Fact]
    public void GlutSurplus_is_the_amount_ahead_of_the_milestone()
    {
        Jar(expected: 150m, milestone: 100m).GlutSurplus.ShouldBe(50m);
    }

    [Fact]
    public void GlutSurplus_is_zero_at_the_exact_boundary_even_though_HasGlut_is_true()
    {
        var jar = Jar(expected: 100m, milestone: 100m);

        jar.HasGlut.ShouldBeTrue();
        jar.GlutSurplus.ShouldBe(0m); // nothing extra to carry forward — same boundary, no contradiction
    }

    [Fact]
    public void GlutSurplus_is_zero_when_behind_the_milestone()
    {
        Jar(expected: 50m, milestone: 100m).GlutSurplus.ShouldBe(0m);
    }

    [Fact]
    public void GlutSurplus_is_zero_when_no_earmark_pattern_drives_the_jar()
    {
        Jar(expected: 500m, milestone: null).GlutSurplus.ShouldBe(0m);
    }
}
