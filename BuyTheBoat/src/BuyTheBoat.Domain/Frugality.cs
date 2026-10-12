namespace BuyTheBoat.Domain;

/// <summary>
/// How hard a "what could we afford" estimate looks for money: the one caution setting AccountTransactionPage's available-funds methods share, ordered from least to most money counted.
/// Each tier is named for its spending attitude, and its summary says exactly what it counts.
/// </summary>
public enum Frugality
{
    /// <summary>Free funds only (the day's ExpectedFreeAmount); nothing already set aside is touched.</summary>
    Relaxed,

    /// <summary>Free funds plus the safety cushion.</summary>
    Considerate,

    /// <summary>Free funds plus jars for non-mandatory expenses; needs a target priority (see AvailableFundsFor).</summary>
    Thrifty,

    /// <summary>Free funds, non-mandatory jars, and jars for expenses with a lower priority than the target's; needs a target priority too.</summary>
    Miserly,
}
