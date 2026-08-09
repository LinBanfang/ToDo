using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is Visibility v) && v == Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is false;
}

/// <summary>Inverse bool mapped to Visibility (true → Collapsed), for hiding a label
/// while an edit box takes over.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value == null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
            return new SolidColorBrush(ColorParser.ParseColor(hex));
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToColorWithAlphaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            var c = ColorParser.ParseColor(hex);
            c.A = 40;
            return new SolidColorBrush(c);
        }
        return new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CloseModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CloseMode mode)
            return mode == CloseMode.Complete ? "" : "";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CloseModeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Use the theme's semantic brushes so the colors work on both themes
        if (value is CloseMode mode)
            return mode == CloseMode.Complete
                ? (Brush)System.Windows.Application.Current.FindResource("AccentGreenBrush")
                : (Brush)System.Windows.Application.Current.FindResource("TextDisabledBrush");
        return (Brush)System.Windows.Application.Current.FindResource("TextDisabledBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TimestampToRelativeStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ts)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            var now = DateTime.Now;
            var diff = now - dt;

            if (diff.TotalMinutes < 1) return Loc.JustNow;
            if (diff.TotalMinutes < 60) return Loc.MinutesAgo((int)diff.TotalMinutes);
            if (diff.TotalHours < 24) return Loc.HoursAgo((int)diff.TotalHours);
            if (diff.TotalDays < 7) return Loc.DaysAgo((int)diff.TotalDays);
            return Loc.RelativeDate(dt);
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DueDateToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ts)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            var today = DateTime.Today;

            if (dt.Date == today) return Loc.Today;
            if (dt.Date == today.AddDays(1)) return Loc.Tomorrow;
            if (dt.Date == today.AddDays(-1)) return Loc.Yesterday;
            return Loc.ShortDate(dt);
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DueDateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ts)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            if (dt.Date < DateTime.Today)
                return (Brush)System.Windows.Application.Current.FindResource("AccentRedBrush");
        }
        return (Brush)System.Windows.Application.Current.FindResource("TaskMetaBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Visibility for a task-row meta item: the item must be present AND its display
/// toggle (bound second) must be on. Presence is inferred from the value type —
/// IList count &gt; 0, int &gt; 0, long = non-null timestamp, non-empty string. A "future"
/// converter parameter additionally requires a long timestamp to still be in the future
/// (reminders are hidden once their time has passed).</summary>
public class ItemAndSettingVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not true) return Visibility.Collapsed;
        bool present = values[0] switch
        {
            IList list => list.Count > 0,
            int n => n > 0,
            long ts => parameter as string == "future"
                ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime > DateTime.Now
                : true,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => false,
        };
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Reminder timestamp → list text. A reminder whose time has passed is treated as
/// already reminded and hidden; today shows just the time (HH:mm); any other day shows just
/// the date.</summary>
public class ReminderToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ts)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            if (dt <= DateTime.Now) return "";   // already reminded — don't show
            return dt.Date == DateTime.Today ? Loc.ReminderTimeOnly(dt) : Loc.ReminderDateOnly(dt);
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Visibility for the "·" separators between a task row's meta items.
/// MultiBinding values: [TagIds, Steps.Count, DueDate, Reminder, Note,
/// ShowTaskTags, ShowTaskSteps, ShowTaskDue, ShowTaskReminder, ShowTaskNote]; the parameter
/// picks the separator: "1" after tags or the My Day sun, "2" after steps, "3" after the
/// due date, "4" after the reminder. Separator "1" additionally binds IsMyDay (index 10) so
/// the sun counts as a leading item when tags are hidden. A separator is visible when its own
/// item is visible (present AND its toggle is on) and at least one later item is visible, so
/// a missing or hidden middle item never leaves a dangling "·". A past reminder counts as
/// absent (it is hidden once reminded).</summary>
public class MetaSeparatorVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 10) return Visibility.Collapsed;

        bool tags = (values[0] is IList list && list.Count > 0) && values[5] is true;
        bool sun = values.Length > 10 && values[10] is true;   // My Day sun (separator 1 only)
        bool steps = (values[1] is int n && n > 0) && values[6] is true;
        bool due = values[2] != null && values[7] is true;
        bool rem = values[3] != null && values[8] is true && IsFutureReminder(values[3]);
        bool note = (values[4] is string s && !string.IsNullOrWhiteSpace(s)) && values[9] is true;

        bool visible = (parameter as string) switch
        {
            "1" => (tags || sun) && (steps || due || rem || note),
            "2" => steps && (due || rem || note),
            "3" => due && (rem || note),
            "4" => rem && note,
            _ => false,
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsFutureReminder(object? value)
        => value is long ts && DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime > DateTime.Now;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.Equals(parameter) == true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Resolve ListId to list name during search: MultiBinding({ ListId }, { AllLists }) → string</summary>
public class ListIdToNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return "";
        var listId = values[0] as string ?? "";
        var allLists = (values[1] as IList)?.Cast<TaskList>() ?? Enumerable.Empty<TaskList>();
        return allLists.FirstOrDefault(l => l.Id == listId)?.DisplayName ?? "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Resolve tag IDs to Tag objects: MultiBinding({ TagIds }, { AllTags }) → List<Tag></summary>
public class TagIdsToTagsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return new List<Tag>();
        var tagIds = values[0] is IList list0
            ? list0.Cast<string>().ToList()
            : new List<string>();
        var allTags = values[1] is IList list1
            ? list1.Cast<Tag>().ToList()
            : new List<Tag>();
        return allTags.Where(t => tagIds.Contains(t.Id)).ToList();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Display any object in combo: string→itself, TaskGroup→Name, else→ToString</summary>
public class ComboDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s) return s;
        if (value is TaskGroup g) return g.Name;
        return value?.ToString() ?? "";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Parse hex color string like "#RRGGBB" or "#AARRGGBB"</summary>
internal static class ColorParser
{
    internal static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        if (hex.Length == 8)
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        return Colors.Gray;
    }
}
