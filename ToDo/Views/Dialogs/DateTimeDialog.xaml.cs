using System.Windows;
using System.Windows.Controls;
using ToDo.Services;

namespace ToDo.Views.Dialogs;

public partial class DateTimeDialog : Window
{
    public long ResultTimestamp { get; private set; }
    public bool Saved { get; private set; }

    /// <summary>
    /// <paramref name="includeTime"/> shows a time-of-day row (hour : minute) and keeps
    /// it in the result — used for reminders. Due dates stay date-only (time collapses to
    /// midnight), which is their intended semantics.
    /// </summary>
    public DateTimeDialog(long initialTimestamp, bool includeTime = false)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);

        var dt = DateTimeOffset.FromUnixTimeMilliseconds(initialTimestamp).LocalDateTime;

        YearCombo.ItemsSource = Enumerable.Range(dt.Year - 6, 13).ToList();
        YearCombo.SelectedItem = dt.Year;
        MonthCombo.ItemsSource = Enumerable.Range(1, 12).ToList();
        MonthCombo.SelectedItem = dt.Month;
        PopulateDays(dt.Year, dt.Month, dt.Day);

        if (includeTime)
        {
            // Specific time of day, free-typed (digits only, validated on save) so any
            // minute 0-59 is reachable — not just 5-minute steps.
            HourBox.Text = dt.Hour.ToString("00");
            MinuteBox.Text = dt.Minute.ToString("00");
            Height = 260;
        }
        else
        {
            TimeRow.Visibility = Visibility.Collapsed;
        }

        YearCombo.SelectionChanged += (_, _) => RefreshDays();
        MonthCombo.SelectionChanged += (_, _) => RefreshDays();

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Save();
            if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void RefreshDays()
    {
        if (YearCombo.SelectedItem is int y && MonthCombo.SelectedItem is int m)
        {
            var prev = DayCombo.SelectedItem is int d ? d : 1;
            var max = DateTime.DaysInMonth(y, m);
            DayCombo.ItemsSource = Enumerable.Range(1, max).ToList();
            DayCombo.SelectedItem = Math.Clamp(prev, 1, max);
        }
    }

    private void PopulateDays(int year, int month, int day)
    {
        var max = DateTime.DaysInMonth(year, month);
        DayCombo.ItemsSource = Enumerable.Range(1, max).ToList();
        DayCombo.SelectedItem = Math.Clamp(day, 1, max);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save()
    {
        TimeInvalidHint.Visibility = Visibility.Collapsed;
        if (YearCombo.SelectedItem is not int y || MonthCombo.SelectedItem is not int m
            || DayCombo.SelectedItem is not int d) return;

        var dt = new DateTime(y, m, d);
        if (TimeRow.Visibility == Visibility.Visible)
        {
            if (!int.TryParse(HourBox.Text, out var h) || h < 0 || h > 23)
            {
                ShowTimeError(HourBox);
                return;
            }
            if (!int.TryParse(MinuteBox.Text, out var mi) || mi < 0 || mi > 59)
            {
                ShowTimeError(MinuteBox);
                return;
            }
            dt = dt.AddHours(h).AddMinutes(mi);
        }
        ResultTimestamp = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Saved = true;
        DialogResult = true;
        Close();
    }

    /// <summary>Digit-only input (with the existing selection replaced), max 2 chars;
    /// the 0-23 / 0-59 range is enforced in <see cref="Save"/> so a partially typed
    /// value like "9" stays valid until the user finishes.</summary>
    private void TimePart_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        var next = box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, e.Text);
        e.Handled = e.Text.Any(ch => !char.IsDigit(ch)) || next.Length > 2;
    }

    private void ShowTimeError(TextBox box)
    {
        TimeInvalidHint.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();
    }
}
