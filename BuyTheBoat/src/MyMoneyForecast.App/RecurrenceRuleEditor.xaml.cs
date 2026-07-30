using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A self-contained "build me a recurrence rule" form + live calendar preview,
// meant to be embedded wherever something needs a date_pattern — pattern
// creation popups, eventually. Exposes the computed RecurrenceRule (or null,
// while the form is in an invalid state) rather than owning a window itself.
public partial class RecurrenceRuleEditor : UserControl
{
    // Guards against recalculating on every individual control's default-value
    // assignment while the control is still being constructed.
    private bool _initialized;

    // planning/15 item D: when a host form owns the "when does this stop?"
    // question (the bill form), the built-in Ends controls are hidden and the
    // end date is supplied from outside via SetHostEndDate instead of the radios.
    private bool _hostControlsEnd;
    private DateOnly? _hostUntil;

    public RecurrenceRuleEditor()
    {
        InitializeComponent();

        StartDatePicker.SelectedDate = DateTime.Today;
        UntilDatePicker.SelectedDate = DateTime.Today.AddYears(3);

        _initialized = true;
        Recalculate();
    }

    public RecurrenceRule? Result { get; private set; }

    public event EventHandler? ResultChanged;

    // For edit mode — pre-fills the form from an existing rule instead of the
    // today/+6-months defaults.
    public void LoadFrom(RecurrenceRule rule)
    {
        _initialized = false;

        foreach (var item in FrequencyComboBox.Items.OfType<ComboBoxItem>())
        {
            if ((string)item.Tag == rule.Frequency.ToString())
            {
                FrequencyComboBox.SelectedItem = item;
                break;
            }
        }

        IntervalTextBox.Text = rule.Interval.ToString();

        foreach (var checkBox in ByDayPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = rule.ByDay.Contains(Enum.Parse<DayOfWeek>((string)checkBox.Tag));
        }

        ByMonthDayTextBox.Text = string.Join(", ", rule.ByMonthDay);
        StartDatePicker.SelectedDate = rule.Start.ToDateTime(TimeOnly.MinValue);
        EndsOnDateRadio.IsChecked = true;
        UntilDatePicker.SelectedDate = rule.Until.ToDateTime(TimeOnly.MinValue);

        _initialized = true;
        Recalculate();
    }

    /// <summary>[UI] Hides the built-in "Ends" controls so the host form supplies the end date itself — used by the bill form's "when does this stop?" question.</summary>
    public void LetHostControlEndDate()
    {
        _hostControlsEnd = true;
        foreach (var element in new UIElement[]
                 { EndsHeader, EndsOnDateRadio, UntilDatePicker, EndsAfterCountRadio, CountTextBox, ResolvedUntilText })
        {
            element.Visibility = Visibility.Collapsed;
        }

        Recalculate();
    }

    /// <summary>[UI] Sets the end date the host chose through its own stop question, refreshing the preview. Null leaves the rule incomplete until one is picked.</summary>
    public void SetHostEndDate(DateOnly? until)
    {
        // No-op when unchanged: the host recomputes the end in response to
        // ResultChanged, so without this guard SetHostEndDate → Recalculate →
        // ResultChanged → SetHostEndDate would loop.
        if (_hostUntil == until)
        {
            return;
        }

        _hostUntil = until;
        Recalculate();
    }

    /// <summary>[CALC] The recurrence entered so far, minus its end date — what a loan payoff date is computed from. Throws a friendly error if the schedule fields aren't valid yet.</summary>
    public (RecurrenceFrequency Frequency, DateOnly Start, int Interval, IReadOnlyList<DayOfWeek> ByDay, IReadOnlyList<int> ByMonthDay) ReadScheduleParts()
    {
        var (frequency, start, interval, byDay, byMonthDay) = ReadScheduleFields();
        return (frequency, start, interval, byDay, byMonthDay);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Recalculate();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Recalculate();

    private void OnCheckChanged(object sender, RoutedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        if (!_initialized)
        {
            return;
        }

        UpdateFormVisibility();
        ErrorText.Text = string.Empty;

        try
        {
            var options = ReadOptionsFromForm();
            var rule = RecurrenceRule.Create(options);

            RruleStringTextBox.Text = rule.ToRruleString();
            ResolvedUntilText.Text = EndsAfterCountRadio.IsChecked == true
                ? $"Effective until: {rule.Until:D}"
                : string.Empty;

            var occurrences = rule.GetOccurrences();

            PreviewCalendar.SelectedDates.Clear();
            foreach (var date in occurrences)
            {
                PreviewCalendar.SelectedDates.Add(date.ToDateTime(TimeOnly.MinValue));
            }
            PreviewCalendar.DisplayDate = rule.Start.ToDateTime(TimeOnly.MinValue);

            OccurrencesListBox.ItemsSource = occurrences
                .Select(date => date.ToString("dddd, MMMM d, yyyy"))
                .ToList();

            Result = rule;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            PreviewCalendar.SelectedDates.Clear();
            OccurrencesListBox.ItemsSource = null;
            RruleStringTextBox.Text = string.Empty;
            ResolvedUntilText.Text = string.Empty;
            Result = null;
        }

        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    // Frequency drives which fields are even meaningful: BYDAY only makes sense
    // for Weekly, BYMONTHDAY only for Monthly/Yearly.
    private void UpdateFormVisibility()
    {
        var frequency = SelectedFrequency();

        IntervalUnitLabel.Text = frequency switch
        {
            RecurrenceFrequency.Daily => "day(s)",
            RecurrenceFrequency.Weekly => "week(s)",
            RecurrenceFrequency.Monthly => "month(s)",
            RecurrenceFrequency.Yearly => "year(s)",
            _ => string.Empty,
        };

        var showByDay = frequency == RecurrenceFrequency.Weekly;
        ByDayLabel.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;
        ByDayPanel.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;
        ByDayHint.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;

        var showByMonthDay = frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly;
        ByMonthDayLabel.Visibility = showByMonthDay ? Visibility.Visible : Visibility.Collapsed;
        ByMonthDayTextBox.Visibility = showByMonthDay ? Visibility.Visible : Visibility.Collapsed;

        UntilDatePicker.IsEnabled = EndsOnDateRadio.IsChecked == true;
        CountTextBox.IsEnabled = EndsAfterCountRadio.IsChecked == true;
    }

    private RecurrenceFrequency SelectedFrequency()
    {
        var tag = (string)((ComboBoxItem)FrequencyComboBox.SelectedItem).Tag;
        return Enum.Parse<RecurrenceFrequency>(tag);
    }

    /// <summary>[CALC] Reads just the repeat-schedule fields the user has entered (frequency, start, interval, days), without an end date — the shared half of the form ReadOptionsFromForm and the public ReadScheduleParts both build on.</summary>
    private (RecurrenceFrequency Frequency, DateOnly Start, int Interval, List<DayOfWeek> ByDay, List<int> ByMonthDay) ReadScheduleFields()
    {
        if (StartDatePicker.SelectedDate is not { } start)
        {
            throw new InvalidOperationException("Pick a start date.");
        }

        var interval = int.TryParse(IntervalTextBox.Text, out var parsedInterval) && parsedInterval > 0
            ? parsedInterval
            : throw new InvalidOperationException("\"Every\" must be a whole number of 1 or more.");

        var byDay = ByDayPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => Enum.Parse<DayOfWeek>((string)checkBox.Tag))
            .ToList();

        var byMonthDay = ParseByMonthDay(ByMonthDayTextBox.Text);

        return (SelectedFrequency(), DateOnly.FromDateTime(start), interval, byDay, byMonthDay);
    }

    private RecurrenceRuleOptions ReadOptionsFromForm()
    {
        var (frequency, start, interval, byDay, byMonthDay) = ReadScheduleFields();

        DateOnly? until = null;
        int? count = null;
        if (_hostControlsEnd)
        {
            until = _hostUntil ?? throw new InvalidOperationException("Choose when this stops.");
        }
        else if (EndsOnDateRadio.IsChecked == true)
        {
            until = UntilDatePicker.SelectedDate is { } untilDate
                ? DateOnly.FromDateTime(untilDate)
                : throw new InvalidOperationException("Pick an end date.");
        }
        else
        {
            count = int.TryParse(CountTextBox.Text, out var parsedCount) && parsedCount > 0
                ? parsedCount
                : throw new InvalidOperationException("Enter a whole number of 1 or more occurrences.");
        }

        return new RecurrenceRuleOptions
        {
            Frequency = frequency,
            Start = start,
            Interval = interval,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            Until = until,
            Count = count,
        };
    }

    private static List<int> ParseByMonthDay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var days = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var day) || day is < 1 or > 31)
            {
                throw new InvalidOperationException($"\"{part}\" isn't a valid day of the month (1-31).");
            }

            days.Add(day);
        }

        return days;
    }
}
