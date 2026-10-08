using System.Globalization;
using System.Text.RegularExpressions;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

// What's wrong with one spreadsheet cell, shown on hover over its red text, plus the closest valid value when one can be guessed.
public sealed record CellProblem(string Message, string? Suggestion = null)
{
    /// <summary>[CALC] The hover text for a red cell: the problem, then the suggested fix when there is one.</summary>
    public string Tip => Suggestion is null ? Message : $"{Message} Did you mean {Suggestion}?";
}

// How a cell is edited once you click into it.
public enum CellEditorKind
{
    TextBox,
    PickList,
    DatePicker,
}

// One column of the Bills & Paychecks › Spreadsheet subtab.
// A cell may hold any text at all; the column decides what counts as valid, how to tidy exact matches, and what to suggest for the rest.
public interface ISpreadsheetColumn
{
    // The DraftRow property this column's cells are stored in.
    string Key { get; }
    string Header { get; }
    string? HeaderTip { get; }
    double Width { get; }
    CellEditorKind Editor { get; }

    // The dropdown's entries for a PickList column; empty for the others.
    IReadOnlyList<string> Choices { get; }

    /// <summary>[CALC] Rewrites text that clearly means one valid value into that value's standard spelling ("monthly" → "Monthly"); anything else comes back unchanged. Run when the user leaves the cell.</summary>
    string Tidy(string text);

    /// <summary>[CALC] Null when the cell's text is acceptable, otherwise what's wrong with it. otherCell reads another cell in the same row by its column Key.</summary>
    CellProblem? Check(string text, Func<string, string> otherCell);
}

// Parsing shared by the column checks and by opening a draft in the Form, so the two can never disagree about what a cell means.
public static class CellParsing
{
    public static readonly string[] Frequencies = ["Daily", "Weekly", "Monthly", "Yearly"];

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "M/d/yy", "MMM d yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMMM d, yyyy"];

    /// <summary>[CALC] Reads an amount like "-62", "$1,200.50", "(140)", or "−15.49" (typographic minus). Null when it isn't one.</summary>
    public static decimal? ParseMoney(string text)
    {
        var cleaned = text.Trim().Replace('−', '-').Replace("$", "").Replace(",", "").Replace(" ", "");
        var negative = false;
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            negative = true;
            cleaned = cleaned[1..^1];
        }

        if (!decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return negative ? -amount : amount;
    }

    /// <summary>[CALC] Reads a whole number of 1 or more. Null when it isn't one.</summary>
    public static int? ParsePositiveWholeNumber(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 ? number : null;

    /// <summary>[CALC] Reads a date in one of the common spellings (2026-01-15, 1/15/2026, Jan 15 2026). Null when it isn't one.</summary>
    public static DateOnly? ParseDate(string text)
    {
        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return DateOnly.FromDateTime(exact);
        }

        return DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out var loose)
            ? DateOnly.FromDateTime(loose)
            : null;
    }

    /// <summary>[CALC] Reads a frequency spelled any way that matches one of the four exactly, ignoring case. Null otherwise.</summary>
    public static RecurrenceFrequency? ParseFrequency(string text) =>
        Enum.TryParse<RecurrenceFrequency>(text.Trim(), ignoreCase: true, out var frequency) && Frequencies.Contains(frequency.ToString())
            ? frequency
            : null;

    /// <summary>[CALC] Reads a comma-separated list of whole numbers, each between min and max. Null when any piece isn't one; empty for blank text.</summary>
    public static IReadOnlyList<int>? ParseNumberList(string text, int min, int max)
    {
        var numbers = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
            {
                return null;
            }

            if (!numbers.Contains(number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    /// <summary>[CALC] Turns the Days of week column's 1–7 numbers into weekdays. RRULE weeks start on Monday, so 1 = Monday and 7 = Sunday.</summary>
    public static DayOfWeek ToDayOfWeek(int number) => (DayOfWeek)(number % 7);

    /// <summary>[CALC] The Days of week column's number for a weekday: Monday = 1 … Sunday = 7.</summary>
    public static int FromDayOfWeek(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    /// <summary>[CALC] Every run of digits in the text, in order — the raw material most suggestions are built from ("15th" → 15).</summary>
    public static IEnumerable<int> NumbersIn(string text) =>
        Regex.Matches(text, @"\d+").Select(match => int.TryParse(match.Value, out var number) ? number : -1).Where(number => number >= 0);

    /// <summary>[CALC] Lowercases and squeezes out spaces, dashes and underscores, so "Bi-Weekly" and "biweekly" compare equal.</summary>
    public static string Squeeze(string text) =>
        new string(text.Where(ch => !char.IsWhiteSpace(ch) && ch is not '-' and not '_').ToArray()).ToLowerInvariant();

    /// <summary>[CALC] How many single-letter edits turn one word into the other — catches typos like "monthy" for a suggestion.</summary>
    public static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}

// Source, Description and Notes: anything goes.
public sealed class TextColumn(string key, string header, double width, string? headerTip = null) : ISpreadsheetColumn
{
    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.TextBox;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Free text is never rewritten.</summary>
    public string Tidy(string text) => text;

    /// <summary>[CALC] Free text is always acceptable.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell) => null;
}

// Amount: a signed number, negative for bills and positive for paychecks.
public sealed class MoneyColumn(string key, string header, double width, string? headerTip = null) : ISpreadsheetColumn
{
    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.TextBox;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Amounts are left as typed — "$62.00" is already clear.</summary>
    public string Tidy(string text) => text;

    /// <summary>[CALC] Accepts blank or any amount ParseMoney can read; otherwise suggests the first number in the text, keeping a minus sign that came before it.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text) || CellParsing.ParseMoney(text) is not null)
        {
            return null;
        }

        var match = Regex.Match(text.Replace('−', '-'), @"(-?)\s*\$?\s*(\d[\d,]*(?:\.\d+)?)");
        var suggestion = match.Success && CellParsing.ParseMoney(match.Groups[2].Value) is { } magnitude
            ? (match.Groups[1].Value == "-" ? -magnitude : magnitude).ToString(CultureInfo.InvariantCulture)
            : null;
        return new CellProblem("Amount must be a number, like -62.00 for a bill or 2150 for a paycheck.", suggestion);
    }
}

// Priority: a whole number of 1 or more.
public sealed class WholeNumberColumn(string key, string header, double width, string? headerTip = null) : ISpreadsheetColumn
{
    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.TextBox;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Drops leading zeros and spaces from a valid number ("05" → "5"); anything else is unchanged.</summary>
    public string Tidy(string text) => CellParsing.ParsePositiveWholeNumber(text) is { } number ? number.ToString(CultureInfo.InvariantCulture) : text;

    /// <summary>[CALC] Accepts blank or a whole number of 1 or more; suggests the first such number in the text.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text) || CellParsing.ParsePositiveWholeNumber(text) is not null)
        {
            return null;
        }

        var suggestion = CellParsing.NumbersIn(text).FirstOrDefault(number => number >= 1);
        return new CellProblem($"{Header} must be a whole number, 1 or more.", suggestion >= 1 ? suggestion.ToString(CultureInfo.InvariantCulture) : null);
    }
}

// Account, Frequency and Every: a text box with a dropdown. Any text can be typed and is kept;
// exact matches (ignoring case and spacing) are tidied to the choice, and anything else is red with the closest choice suggested.
public sealed class PickListColumn : ISpreadsheetColumn
{
    private readonly Func<IReadOnlyList<string>> _choices;
    private readonly string _whatItIs;
    private readonly Func<string, string?>? _acceptOther;
    private readonly Func<string, string?>? _suggestOther;

    /// <param name="choices">The dropdown entries; read fresh each time, so Account can follow the live account list.</param>
    /// <param name="whatItIs">How the problem message names a valid value, e.g. "a frequency".</param>
    /// <param name="acceptOther">Optional: also accepts text outside the list, returning its tidied spelling (Every accepts any whole number, not just the listed 1–12), or null.</param>
    /// <param name="suggestOther">Optional: a column-specific suggestion tried before the generic closest-choice match.</param>
    public PickListColumn(
        string key, string header, double width, Func<IReadOnlyList<string>> choices, string whatItIs,
        string? headerTip = null, Func<string, string?>? acceptOther = null, Func<string, string?>? suggestOther = null)
    {
        Key = key;
        Header = header;
        Width = width;
        HeaderTip = headerTip;
        _choices = choices;
        _whatItIs = whatItIs;
        _acceptOther = acceptOther;
        _suggestOther = suggestOther;
    }

    public string Key { get; }
    public string Header { get; }
    public string? HeaderTip { get; }
    public double Width { get; }
    public CellEditorKind Editor => CellEditorKind.PickList;
    public IReadOnlyList<string> Choices => _choices();

    /// <summary>[CALC] Swaps text matching a choice exactly (ignoring case, spaces and dashes) for that choice's spelling; anything else is unchanged.</summary>
    public string Tidy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var squeezed = CellParsing.Squeeze(text);
        return Choices.FirstOrDefault(choice => CellParsing.Squeeze(choice) == squeezed)
            ?? _acceptOther?.Invoke(text)
            ?? text;
    }

    /// <summary>[CALC] Accepts blank, a choice, or what acceptOther allows; otherwise names the problem and suggests the closest choice.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text) || Choices.Contains(text) || _acceptOther?.Invoke(text) == text)
        {
            return null;
        }

        return new CellProblem($"\"{text.Trim()}\" isn't {_whatItIs}.", _suggestOther?.Invoke(text) ?? ClosestChoice(text));
    }

    /// <summary>[CALC] The choice the text most likely meant: one it starts, contains, or is contained in, else one within a couple of typos. Null when nothing is close.</summary>
    private string? ClosestChoice(string text)
    {
        var squeezed = CellParsing.Squeeze(text);
        if (squeezed.Length == 0)
        {
            return null;
        }

        var choices = Choices.Select(choice => (Choice: choice, Squeezed: CellParsing.Squeeze(choice))).ToList();

        // Longest containing match first, so "biweekly" lands on "Weekly" rather than something shorter that also fits.
        var overlapping = choices
            .Where(pair => pair.Squeezed.StartsWith(squeezed) || squeezed.Contains(pair.Squeezed) || pair.Squeezed.Contains(squeezed))
            .OrderByDescending(pair => pair.Squeezed.Length)
            .Select(pair => pair.Choice)
            .FirstOrDefault();
        if (overlapping is not null)
        {
            return overlapping;
        }

        // An abbreviation: same first letter, the rest in order ("chk" → Checking, "yr" → Yearly).
        var abbreviated = choices
            .FirstOrDefault(pair => pair.Squeezed.Length > 0 && pair.Squeezed[0] == squeezed[0] && IsInOrderWithin(squeezed, pair.Squeezed))
            .Choice;
        if (abbreviated is not null)
        {
            return abbreviated;
        }

        var allowedTypos = Math.Max(2, squeezed.Length / 3);
        return choices
            .Select(pair => (pair.Choice, Distance: CellParsing.EditDistance(squeezed, pair.Squeezed)))
            .Where(pair => pair.Distance <= allowedTypos)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Choice)
            .FirstOrDefault();
    }

    /// <summary>[CALC] Whether every letter of the short text appears in the long one, in the same order.</summary>
    private static bool IsInOrderWithin(string shortText, string longText)
    {
        var position = 0;
        foreach (var ch in longText)
        {
            if (position < shortText.Length && shortText[position] == ch)
            {
                position++;
            }
        }

        return position == shortText.Length;
    }
}

// Starts and Ends: a date, with a calendar dropdown to pick one.
public sealed class DateColumn(string key, string header, double width, string? headerTip = null, string? mustNotPrecedeKey = null) : ISpreadsheetColumn
{
    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.DatePicker;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Rewrites any readable date as yyyy-MM-dd ("1/15/2026" → "2026-01-15"); anything else is unchanged.</summary>
    public string Tidy(string text) =>
        CellParsing.ParseDate(text) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : text;

    /// <summary>[CALC] Accepts blank or a readable date. Ends is also red when it falls before a valid Starts in the same row.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (CellParsing.ParseDate(text) is not { } date)
        {
            return new CellProblem("Not a date. Use a date like 2026-01-15, or pick one from the calendar.");
        }

        if (mustNotPrecedeKey is not null && CellParsing.ParseDate(otherCell(mustNotPrecedeKey)) is { } earliest && date < earliest)
        {
            return new CellProblem($"{Header} is before {mustNotPrecedeKey}.");
        }

        return null;
    }
}

// Days of week: 1 (Monday) to 7 (Sunday), comma-separated. Only used when Frequency is Weekly.
public sealed class DaysOfWeekColumn(string key, string header, double width, string? headerTip = null) : ISpreadsheetColumn
{
    private static readonly (string Prefix, int Number)[] DayNames =
        [("mo", 1), ("tu", 2), ("we", 3), ("th", 4), ("fr", 5), ("sa", 6), ("su", 7)];

    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.TextBox;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Rewrites a valid list in the standard "1, 5" spelling; anything else is unchanged.</summary>
    public string Tidy(string text) =>
        CellParsing.ParseNumberList(text, 1, 7) is { Count: > 0 } days ? string.Join(", ", days) : text;

    /// <summary>[CALC] Accepts blank or numbers 1–7, and only while Frequency is Weekly. Weekday names ("Mon, Fri") are suggested as their numbers.</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (CellParsing.ParseNumberList(text, 1, 7) is null)
        {
            return new CellProblem("Days of week are numbers from 1 (Monday) to 7 (Sunday), separated by commas.", SuggestFromNames(text));
        }

        return CellParsing.ParseFrequency(otherCell("Frequency")) == RecurrenceFrequency.Weekly
            ? null
            : new CellProblem("Only used when Frequency is Weekly.");
    }

    /// <summary>[CALC] Turns weekday names in the text into their numbers ("Mon & Fri" → "1, 5"). Null when none are found.</summary>
    private static string? SuggestFromNames(string text)
    {
        var numbers = Regex.Matches(text.ToLowerInvariant(), "[a-z]+")
            .Select(word => DayNames.FirstOrDefault(day => word.Value.StartsWith(day.Prefix)).Number)
            .Where(number => number > 0)
            .Distinct()
            .ToList();
        return numbers.Count > 0 ? string.Join(", ", numbers) : null;
    }
}

// Days of month: 1–31, comma-separated. Used when Frequency is Monthly or Yearly, matching where the Form shows its own day-of-month box.
public sealed class DaysOfMonthColumn(string key, string header, double width, string? headerTip = null) : ISpreadsheetColumn
{
    public string Key => key;
    public string Header => header;
    public string? HeaderTip => headerTip;
    public double Width => width;
    public CellEditorKind Editor => CellEditorKind.TextBox;
    public IReadOnlyList<string> Choices => [];

    /// <summary>[CALC] Rewrites a valid list in the standard "1, 15" spelling; anything else is unchanged.</summary>
    public string Tidy(string text) =>
        CellParsing.ParseNumberList(text, 1, 31) is { Count: > 0 } days ? string.Join(", ", days) : text;

    /// <summary>[CALC] Accepts blank or numbers 1–31, and only while Frequency is Monthly or Yearly. Suggests the numbers found in the text ("15th" → "15").</summary>
    public CellProblem? Check(string text, Func<string, string> otherCell)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (CellParsing.ParseNumberList(text, 1, 31) is null)
        {
            var found = CellParsing.NumbersIn(text).Where(day => day is >= 1 and <= 31).Distinct().ToList();
            return new CellProblem("Days of month are numbers from 1 to 31, separated by commas.", found.Count > 0 ? string.Join(", ", found) : null);
        }

        return CellParsing.ParseFrequency(otherCell("Frequency")) is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly
            ? null
            : new CellProblem("Only used when Frequency is Monthly or Yearly.");
    }
}

// The Spreadsheet subtab's columns, left to right. Notes stays last so its long text never pushes the other columns off screen.
public static class SpreadsheetColumns
{
    /// <summary>[CALC] Builds the column list. accountNames is read fresh every time the Account column checks a cell.</summary>
    public static IReadOnlyList<ISpreadsheetColumn> Create(Func<IReadOnlyList<string>> accountNames) =>
    [
        new TextColumn(nameof(Persistence.DraftRow.Source), "Source", 130, "The name on your bank statement."),
        new TextColumn(nameof(Persistence.DraftRow.Description), "Description", 150),
        new PickListColumn(nameof(Persistence.DraftRow.Account), "Account", 110, accountNames, "one of your accounts"),
        new MoneyColumn(nameof(Persistence.DraftRow.Amount), "Amount (− for bills)", 120,
            "Bills are negative amounts, like -62.00. Paychecks are positive, like 2150."),
        new WholeNumberColumn(nameof(Persistence.DraftRow.Priority), "Priority", 60, "1 = lowest, higher = higher priority."),
        new PickListColumn(nameof(Persistence.DraftRow.Frequency), "Frequency", 95, () => CellParsing.Frequencies, "a frequency",
            suggestOther: SuggestFrequency),
        new PickListColumn(nameof(Persistence.DraftRow.Every), "Every", 60,
            () => Enumerable.Range(1, 12).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(), "a whole number, 1 or more",
            "How many days, weeks, months or years between each one. 2 with Weekly means every other week.",
            acceptOther: text => CellParsing.ParsePositiveWholeNumber(text)?.ToString(CultureInfo.InvariantCulture),
            suggestOther: SuggestEvery),
        new DaysOfWeekColumn(nameof(Persistence.DraftRow.DaysOfWeek), "Days of week", 95,
            "1 = Monday … 7 = Sunday (RRULE weeks start on Monday). Separate several with commas. Only used when Frequency is Weekly."),
        new DaysOfMonthColumn(nameof(Persistence.DraftRow.DaysOfMonth), "Days of month", 100,
            "1–31. Separate several with commas. Only used when Frequency is Monthly or Yearly."),
        new DateColumn(nameof(Persistence.DraftRow.Starts), "Starts", 100),
        new DateColumn(nameof(Persistence.DraftRow.Ends), "Ends", 100, mustNotPrecedeKey: nameof(Persistence.DraftRow.Starts)),
        new TextColumn(nameof(Persistence.DraftRow.Notes), "Notes", 300),
    ];

    /// <summary>[CALC] Frequency words the generic closest-match misses: "annual" → Yearly, "fortnightly" → Weekly.</summary>
    private static string? SuggestFrequency(string text)
    {
        var squeezed = CellParsing.Squeeze(text);
        if (squeezed.Contains("annual") || squeezed.Contains("year"))
        {
            return "Yearly";
        }

        return squeezed.Contains("fortnight") ? "Weekly" : null;
    }

    /// <summary>[CALC] The first whole number in the text ("2 weeks" → 2), or 2 for "other"/"bi" ("every other", "biweekly").</summary>
    private static string? SuggestEvery(string text)
    {
        if (CellParsing.NumbersIn(text).FirstOrDefault(number => number >= 1) is var number and >= 1)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var squeezed = CellParsing.Squeeze(text);
        return squeezed.Contains("other") || squeezed.StartsWith("bi") ? "2" : null;
    }
}
