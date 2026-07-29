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
        DatePicker.SelectedDate = dt.Date;

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Save();
            if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save()
    {
        if (DatePicker.SelectedDate == null) return;

        var dt = DatePicker.SelectedDate.Value.Date;
        ResultTimestamp = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Saved = true;
        DialogResult = true;
        Close();
    }
}
