using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ToDo.Models;

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
        if (value is CloseMode mode)
            return mode == CloseMode.Complete
                ? new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10))
                : new SolidColorBrush(Color.FromRgb(0x79, 0x77, 0x75));
        return new SolidColorBrush(Colors.Gray);
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

            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return dt.ToString("MMM d, yyyy");
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

            if (dt.Date == today) return "Today";
            if (dt.Date == today.AddDays(1)) return "Tomorrow";
            if (dt.Date == today.AddDays(-1)) return "Yesterday";
            if (dt.Date < today) return $"Overdue {dt:MMM d}";
            return dt.ToString("MMM d");
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
                return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        }
        return new SolidColorBrush(Color.FromRgb(0x60, 0x5E, 0x5C));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
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
