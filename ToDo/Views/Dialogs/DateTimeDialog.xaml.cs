using System.Windows;

namespace ToDo.Views.Dialogs;

public partial class DateTimeDialog : Window
{
    public long ResultTimestamp { get; private set; }
    public bool Saved { get; private set; }

    public DateTimeDialog(long initialTimestamp)
    {
        InitializeComponent();

        var dt = DateTimeOffset.FromUnixTimeMilliseconds(initialTimestamp).LocalDateTime;

        YearCombo.ItemsSource = Enumerable.Range(dt.Year - 6, 13).ToList();
        YearCombo.SelectedItem = dt.Year;
        MonthCombo.ItemsSource = Enumerable.Range(1, 12).ToList();
        MonthCombo.SelectedItem = dt.Month;
        PopulateDays(dt.Year, dt.Month, dt.Day);

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
        if (YearCombo.SelectedItem is not int y || MonthCombo.SelectedItem is not int m
            || DayCombo.SelectedItem is not int d) return;

        var dt = new DateTime(y, m, d);
        ResultTimestamp = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Saved = true;
        DialogResult = true;
        Close();
    }
}
