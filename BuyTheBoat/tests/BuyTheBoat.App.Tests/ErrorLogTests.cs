using System.IO;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// RecordAndDescribe is the backstop every save/confirm catch site now uses: it
// keeps deliberate, user-facing validation visible but never lets an unexpected
// internal fault (a domain invariant that was supposed to be unreachable, an I/O
// error) reach the user as raw developer text. It always logs first (not asserted
// here — Record writes to a file and never throws).
public class ErrorLogTests
{
    [Fact]
    public void Shows_the_apps_own_validation_message_verbatim()
    {
        ErrorLog.RecordAndDescribe("x", new InvalidOperationException("Pick a start date."))
            .ShouldBe("Pick a start date.");
    }

    [Fact]
    public void Shows_a_domain_input_validation_message_verbatim()
    {
        // e.g. FinancialPattern.Create's "Source cannot be empty." — actionable, keep it.
        ErrorLog.RecordAndDescribe("x", new ArgumentException("Source cannot be empty."))
            .ShouldBe("Source cannot be empty.");
    }

    [Fact]
    public void Hides_an_unexpected_fault_behind_a_safe_logged_line()
    {
        var line = ErrorLog.RecordAndDescribe("x", new InvalidDataException("cut date must be after the pattern's own start #internal"));

        line.ShouldNotContain("cut date");   // no raw developer text
        line.ShouldNotContain("#internal");
        line.ShouldContain("logged");        // tells the user it's recorded, not lost
    }
}
