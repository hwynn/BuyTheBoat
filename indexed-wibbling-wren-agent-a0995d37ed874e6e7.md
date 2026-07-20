# Forecast tab: replace TimelineGrid with month-calendar overview (design)

Full design returned in the agent's final message. Summary:

- Outer `ListBox x:Name="TimelineCalendar"` of `MonthRow` items, VirtualizingStackPanel
  (Recycling, ScrollUnit=Pixel, CanContentScroll=True), non-selectable month containers
  (retemplated ListBoxItem, Focusable=False) — chosen over raw ItemsControl to get the
  ScrollViewer + ScrollIntoView for free.
- Inner per-month ItemsControl with UniformGrid Columns=7; leading blank padding cells
  (DayCellRow with Date=null) for the first weekday.
- Day cell = restyled Button (InvokePattern), Click -> OnDayCellClick in MainWindow.xaml.cs;
  manual global selection via code-behind field + DayCellRow.IsSelected (INPC, the one
  mutable prop) + DataTrigger. No-snapshot cells IsEnabled=False.
- New rows in PatternRows.cs: MonthRow { Title, MonthAutomationId, Cells },
  DayCellRow { Date?, DayNumber, HasSnapshot, Entry/Snapshot, FreeText, FreeNegative,
  AllocatedText, HasIncome/HasExpense/HasAllocation, IsToday, NeedsAttention,
  AutomationId, AutomationName, IsSelected (INPC) }.
- Months built in RefreshForecast from GetTimeline() dictionary keyed by date over the
  full AsOfDate..HorizonEndDate range; initial snapshot already surfaces as AsOfDate entry.
- Pre-select as-of cell (replaces SelectedIndex=0); scroll-to-today via
  TimelineCalendar.ScrollIntoView(month of DateTime.Today if in range, else first month),
  dispatched at Loaded priority.
- OnTimelineSelectionChanged -> ShowDayDetail(DayCellRow) reusing JarDetailRow/DayEventRow.
- AutomationIds: Day_yyyy-MM-dd on cell buttons, Month_yyyy-MM on ListBoxItem containers;
  drive scrolls (ScrollPattern) until target realized.
- Keep TimelineRow class (ForecastSpreadsheetExporter uses its internal statics; JarDetailRow
  and DayEventRow use TimelineRow.JarLabel); only the ctor/instance usage in MainWindow goes.
