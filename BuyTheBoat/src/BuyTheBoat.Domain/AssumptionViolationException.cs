namespace BuyTheBoat.Domain;

/// <summary>
/// A check enforcing one of the documented assumptions (planning/03-assumptions-glossary.md) failed.
/// Derives from ArgumentException because most checks reject bad input to a Create or factory call.
/// </summary>
public abstract class AssumptionViolationException(string assumptionId, string message, string? paramName)
    : ArgumentException(message, paramName)
{
    // The assumption's ID exactly as the docs write it, e.g. "3.11.2.a2".
    public string AssumptionId { get; } = assumptionId;
}

/// <summary>3.1.a1: account cannot be None</summary>
public sealed class AccountNameMissingException(string message, string? paramName)
    : AssumptionViolationException("3.1.a1", message, paramName);

/// <summary>4.2.a1: source cannot be None</summary>
public sealed class FinancialPatternSourceMissingException(string message, string? paramName)
    : AssumptionViolationException("4.2.a1", message, paramName);

/// <summary>3.11.2.a2: an earmark pattern's date_pattern cannot extend beyond or before the date_pattern of it's associated finance_pattern.</summary>
public sealed class EarMarkPatternOutsideGoalSpanException(string message, string? paramName)
    : AssumptionViolationException("3.11.2.a2", message, paramName);

/// <summary>
/// 3.10.a3: if an EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction exists in our page with a finance_id of x and our page has no finance pattern with a finance_id of x, that EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction shouldn't exist
/// Checked when an EarMarkPattern is created against a goal whose finance id differs from its own.
/// </summary>
public sealed class EarMarkPatternGoalMismatchException(string message, string? paramName)
    : AssumptionViolationException("3.10.a3", message, paramName);

/// <summary>
/// 3.13.8.a1: an earmark, even an isolated earmark, cannot exist on a page that does not have an earmark pattern for it, (unless the earmark's finance_id is None)
/// Checked when a manual earmark is created against an earmark pattern whose finance id differs from its own.
/// </summary>
public sealed class ManualEarmarkPatternMismatchException(string message, string? paramName)
    : AssumptionViolationException("3.13.8.a1", message, paramName);

/// <summary>3.13.8.a2: an earmark cannot exist outside the date_pattern of its earmark pattern (unless the earmark's finance_id is None)</summary>
public sealed class ManualEarmarkOutsidePatternSpanException(string message, string? paramName)
    : AssumptionViolationException("3.13.8.a2", message, paramName);

/// <summary>
/// 3.13c.8.a5: implicit earmarks on balancesnapshot c have been cleared. Any isolated earmark with an expected_amount of 0 has been deleted.
/// Checked when a manual earmark is created with an amount of zero.
/// </summary>
public sealed class ZeroIsolatedEarmarkException(string message, string? paramName)
    : AssumptionViolationException("3.13c.8.a5", message, paramName);

/// <summary>
/// 3.10.a2: self.finance_patterns cannot contain any duplicate ids
/// Checked when a break-off's successor is given its predecessor's own finance id.
/// </summary>
public sealed class DuplicateFinanceIdException(string message, string? paramName)
    : AssumptionViolationException("3.10.a2", message, paramName);

/// <summary>
/// 3.13.5.a1: if the day exists within EarMarkPattern A's date_patern(any day between start and end), a fund jar with A's finance_id must exist on that day.
/// Checked on every snapshot day of a forecast; the forecast builds jars from the same plans it checks, so this only fires if that link breaks.
/// </summary>
public sealed class FundJarMissingException(string message, string? paramName)
    : AssumptionViolationException("3.13.5.a1", message, paramName);
