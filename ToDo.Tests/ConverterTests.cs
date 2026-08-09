using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>Pure value-converter and color-parsing logic (brush converters that need
/// Application.Current resources are intentionally not covered here).</summary>
public sealed class ConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void BoolToVisibility_MapsTrueToVisible()
    {
        IValueConverter c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(true, typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(false, typeof(Visibility), null, Inv));
    }

    [Fact]
    public void InverseBool_FlipsValue()
    {
        IValueConverter c = new InverseBoolConverter();
        Assert.True((bool)c.Convert(false, typeof(bool), null, Inv));
        Assert.False((bool)c.Convert(true, typeof(bool), null, Inv));
    }

    [Fact]
    public void NullToVisibility_ShowsOnlyNonNull()
    {
        IValueConverter c = new NullToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert("x", typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(null, typeof(Visibility), null, Inv));
    }

    [Fact]
    public void CountToVisibility_ShowsOnlyPositive()
    {
        IValueConverter c = new CountToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(3, typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(0, typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(-1, typeof(Visibility), null, Inv));
    }

    [Fact]
    public void Equality_ComparesToParameter()
    {
        IValueConverter c = new EqualityConverter();
        Assert.True((bool)c.Convert("a", typeof(bool), "a", Inv));
        Assert.False((bool)c.Convert("a", typeof(bool), "b", Inv));
        Assert.False((bool)c.Convert(null, typeof(bool), "b", Inv));
    }

    [Fact]
    public void CloseModeToIcon_CompleteVsCancel()
    {
        IValueConverter c = new CloseModeToIconConverter();
        Assert.Equal("", c.Convert(CloseMode.Complete, typeof(string), null, Inv));
        Assert.Equal("", c.Convert(CloseMode.Cancel, typeof(string), null, Inv));
    }

    [Fact]
    public void StringToColorBrush_ParsesHex()
    {
        IValueConverter c = new StringToColorBrushConverter();
        var brush = (SolidColorBrush)c.Convert("#0078D4", typeof(Brush), null, Inv);
        Assert.Equal(Color.FromRgb(0x00, 0x78, 0xD4), brush.Color);
    }

    [Fact]
    public void StringToColorBrush_InvalidHex_FallsBackToGray()
    {
        IValueConverter c = new StringToColorBrushConverter();
        var brush = (SolidColorBrush)c.Convert("", typeof(Brush), null, Inv);
        Assert.Equal(Colors.Gray, brush.Color);
    }

    [Fact]
    public void ColorParser_HandlesRgbArgbAndInvalid()
    {
        Assert.Equal(Color.FromRgb(0x00, 0x78, 0xD4), ColorParser.ParseColor("#0078D4"));
        Assert.Equal(Color.FromArgb(0x80, 0x00, 0x78, 0xD4), ColorParser.ParseColor("#800078D4"));
        Assert.Equal(Colors.Gray, ColorParser.ParseColor("nope"));
    }

    [Fact]
    public void DueDateToString_TodayTomorrowYesterday()
    {
        IValueConverter c = new DueDateToStringConverter();
        var today = DateTime.Today;
        long Ts(int days) => new DateTimeOffset(today.AddDays(days)).ToUnixTimeMilliseconds();

        Assert.Equal(Loc.Today, c.Convert(Ts(0), typeof(string), null, Inv));
        Assert.Equal(Loc.Tomorrow, c.Convert(Ts(1), typeof(string), null, Inv));
        Assert.Equal(Loc.Yesterday, c.Convert(Ts(-1), typeof(string), null, Inv));
    }

    [Fact]
    public void DueDateToString_PastDateShowsShortDate()
    {
        IValueConverter c = new DueDateToStringConverter();
        var dt = DateTime.Today.AddDays(-3);
        long ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Assert.Equal(Loc.ShortDate(dt), c.Convert(ts, typeof(string), null, Inv));
    }

    [Fact]
    public void TimestampToRelative_RecentIsJustNow()
    {
        IValueConverter c = new TimestampToRelativeStringConverter();
        var ts = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds();
        Assert.Equal(Loc.JustNow, c.Convert(ts, typeof(string), null, Inv));
    }

    [Fact]
    public void ComboDisplay_UsesGroupName()
    {
        IValueConverter c = new ComboDisplayConverter();
        Assert.Equal("My list", c.Convert("My list", typeof(string), null, Inv));
        Assert.Equal("Groceries", c.Convert(new TaskGroup { Name = "Groceries" }, typeof(string), null, Inv));
    }

    [Fact]
    public void ListIdToName_ResolvesCustomList()
    {
        IMultiValueConverter c = new ListIdToNameConverter();
        var list = new TaskList { Id = "l1", Name = "Work", Type = ListType.Custom };
        var result = c.Convert(new object[] { "l1", new[] { list } }, typeof(string), null, Inv);
        Assert.Equal("Work", result);
    }

    [Fact]
    public void TagIdsToTags_FiltersByIds()
    {
        IMultiValueConverter c = new TagIdsToTagsConverter();
        var tags = new[]
        {
            new Tag { Id = "t1", Name = "A" },
            new Tag { Id = "t2", Name = "B" },
            new Tag { Id = "t3", Name = "C" },
        };
        var result = (List<Tag>)c.Convert(new object[] { new[] { "t1", "t3" }, tags }, typeof(List<Tag>), null, Inv);
        Assert.Equal(new[] { "t1", "t3" }, result.ConvertAll(t => t.Id));
    }

    [Fact]
    public void ReminderToString_FutureTodayShowsTimeOnly()
    {
        IValueConverter c = new ReminderToStringConverter();
        // A reminder today but in the past is hidden (already reminded), so pick a future
        // time; near midnight no future-today time exists, so skip the assert.
        var dt = DateTime.Now.AddMinutes(30);
        if (dt.Date != DateTime.Today) return;
        long ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Assert.Equal(Loc.ReminderTimeOnly(dt), c.Convert(ts, typeof(string), null, Inv));
    }

    [Fact]
    public void ReminderToString_OtherDayShowsDateOnly()
    {
        IValueConverter c = new ReminderToStringConverter();
        var later = DateTime.Today.AddDays(1).AddHours(9).AddMinutes(5);
        long ts = new DateTimeOffset(later).ToUnixTimeMilliseconds();
        Assert.Equal(Loc.ReminderDateOnly(later), c.Convert(ts, typeof(string), null, Inv));
    }

    [Fact]
    public void ReminderToString_PastShowsEmpty()
    {
        IValueConverter c = new ReminderToStringConverter();
        var dt = DateTime.Now.AddMinutes(-5);
        long ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        Assert.Equal("", c.Convert(ts, typeof(string), null, Inv));
    }

    [Fact]
    public void ReminderToString_InvalidReturnsEmpty()
    {
        IValueConverter c = new ReminderToStringConverter();
        Assert.Equal("", c.Convert(null, typeof(string), null, Inv));
    }

    [Fact]
    public void ItemAndSetting_PresenceAndToggleMustBothHold()
    {
        IMultiValueConverter c = new ItemAndSettingVisibilityConverter();
        object[] With(object? present, bool setting) => new object[] { present!, setting };

        // Tags: list count > 0 AND toggle on
        Assert.Equal(Visibility.Visible, c.Convert(With(new[] { "t1" }, true), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(new[] { "t1" }, false), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), true), typeof(Visibility), null, Inv));

        // Steps: int count > 0 AND toggle on
        Assert.Equal(Visibility.Visible, c.Convert(With(3, true), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(0, true), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(3, false), typeof(Visibility), null, Inv));

        // Due / reminder: non-null timestamp AND toggle on
        Assert.Equal(Visibility.Visible, c.Convert(With(123L, true), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(null, true), typeof(Visibility), null, Inv));

        // Note: non-empty string AND toggle on
        Assert.Equal(Visibility.Visible, c.Convert(With("note", true), typeof(Visibility), null, Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With("  ", true), typeof(Visibility), null, Inv));
    }

    [Fact]
    public void ItemAndSetting_ReminderFutureParam_HidesPastTimestamp()
    {
        IMultiValueConverter c = new ItemAndSettingVisibilityConverter();
        long past = new DateTimeOffset(DateTime.Now.AddMinutes(-5)).ToUnixTimeMilliseconds();
        long future = new DateTimeOffset(DateTime.Now.AddMinutes(30)).ToUnixTimeMilliseconds();
        object[] With(long ts, bool setting) => new object[] { ts, setting };

        // With the "future" parameter a past reminder is treated as absent
        Assert.Equal(Visibility.Collapsed, c.Convert(With(past, true), typeof(Visibility), "future", Inv));
        Assert.Equal(Visibility.Visible, c.Convert(With(future, true), typeof(Visibility), "future", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(future, false), typeof(Visibility), "future", Inv));
        // Without it, any non-null timestamp counts as present (due dates, etc.)
        Assert.Equal(Visibility.Visible, c.Convert(With(past, true), typeof(Visibility), null, Inv));
    }

    [Fact]
    public void MetaSeparator_AfterTags_VisibleOnlyWhenLaterItemPresent()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        object[] With(string[] tags, int steps, long? due, long? rem, string? note,
                      bool showTags = true, bool showSteps = true, bool showDue = true,
                      bool showRem = true, bool showNote = true) =>
            new object[] { tags, steps, due!, rem!, note!, showTags, showSteps, showDue, showRem, showNote };

        Assert.Equal(Visibility.Visible, c.Convert(With(new[] { "t1" }, 0, 123, null, null), typeof(Visibility), "1", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(new[] { "t1" }, 0, null, null, null), typeof(Visibility), "1", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), 2, 123, null, null), typeof(Visibility), "1", Inv));
    }

    [Fact]
    public void MetaSeparator_WithMyDaySun_CountsAsLeadingItem()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        // Separator "1" binds IsMyDay last (index 10): the sun counts as a leading item.
        object[] With(bool sun, string[] tags, int steps, long? due) =>
            new object[] { tags, steps, due!, null!, null, true, true, true, true, true, sun };

        // Sun visible, tags hidden → the "·" before steps still shows
        Assert.Equal(Visibility.Visible, c.Convert(With(true, Array.Empty<string>(), 2, null), typeof(Visibility), "1", Inv));
        // Neither tags nor sun → no leading item, no "·" even with steps after
        Assert.Equal(Visibility.Collapsed, c.Convert(With(false, Array.Empty<string>(), 2, null), typeof(Visibility), "1", Inv));
        // Sun visible but nothing after it → no dangling "·"
        Assert.Equal(Visibility.Collapsed, c.Convert(With(true, Array.Empty<string>(), 0, null), typeof(Visibility), "1", Inv));
    }

    [Fact]
    public void MetaSeparator_AfterSteps_NeedsLaterItem()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        object[] With(string[] tags, int steps, long? due, long? rem, string? note,
                      bool showTags = true, bool showSteps = true, bool showDue = true,
                      bool showRem = true, bool showNote = true) =>
            new object[] { tags, steps, due!, rem!, note!, showTags, showSteps, showDue, showRem, showNote };

        Assert.Equal(Visibility.Visible, c.Convert(With(Array.Empty<string>(), 2, 123, null, null), typeof(Visibility), "2", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), 2, null, null, null), typeof(Visibility), "2", Inv));
    }

    [Fact]
    public void MetaSeparator_AfterDue_NeedsLaterItem()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        // The reminder timestamp must be in the future — a past one is hidden
        long future = new DateTimeOffset(DateTime.Now.AddMinutes(30)).ToUnixTimeMilliseconds();
        object[] With(string[] tags, int steps, long? due, long? rem, string? note,
                      bool showTags = true, bool showSteps = true, bool showDue = true,
                      bool showRem = true, bool showNote = true) =>
            new object[] { tags, steps, due!, rem!, note!, showTags, showSteps, showDue, showRem, showNote };

        Assert.Equal(Visibility.Visible, c.Convert(With(Array.Empty<string>(), 0, 123, future, null), typeof(Visibility), "3", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), 0, 123, null, null), typeof(Visibility), "3", Inv));
    }

    [Fact]
    public void MetaSeparator_AfterReminder_NeedsNote()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        // The reminder timestamp must be in the future — a past one is hidden
        long future = new DateTimeOffset(DateTime.Now.AddMinutes(30)).ToUnixTimeMilliseconds();
        object[] With(string[] tags, int steps, long? due, long? rem, string? note,
                      bool showTags = true, bool showSteps = true, bool showDue = true,
                      bool showRem = true, bool showNote = true) =>
            new object[] { tags, steps, due!, rem!, note!, showTags, showSteps, showDue, showRem, showNote };

        Assert.Equal(Visibility.Visible, c.Convert(With(Array.Empty<string>(), 0, null, future, "hi"), typeof(Visibility), "4", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), 0, null, future, null), typeof(Visibility), "4", Inv));
        Assert.Equal(Visibility.Collapsed, c.Convert(With(Array.Empty<string>(), 0, null, future, "   "), typeof(Visibility), "4", Inv));
    }

    [Fact]
    public void MetaSeparator_PastReminderCountsAsAbsent()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        long past = new DateTimeOffset(DateTime.Now.AddMinutes(-5)).ToUnixTimeMilliseconds();
        long future = new DateTimeOffset(DateTime.Now.AddMinutes(30)).ToUnixTimeMilliseconds();
        object[] With(long? rem, string? note = null) =>
            new object[] { Array.Empty<string>(), 0, 123L, rem!, note!, true, true, true, true, true };

        // Separator after the due date needs a later visible item; a past reminder doesn't count
        Assert.Equal(Visibility.Collapsed, c.Convert(With(past), typeof(Visibility), "3", Inv));
        Assert.Equal(Visibility.Visible, c.Convert(With(future), typeof(Visibility), "3", Inv));
        // Separator after the reminder needs the note; a past reminder hides the "·" too
        Assert.Equal(Visibility.Collapsed, c.Convert(With(past, "hi"), typeof(Visibility), "4", Inv));
        Assert.Equal(Visibility.Visible, c.Convert(With(future, "hi"), typeof(Visibility), "4", Inv));
    }

    [Fact]
    public void MetaSeparator_HiddenItemNeverLeavesDanglingSeparator()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        object[] With(string[] tags, int steps, long? due, long? rem, string? note,
                      bool showTags = true, bool showSteps = true, bool showDue = true,
                      bool showRem = true, bool showNote = true) =>
            new object[] { tags, steps, due!, rem!, note!, showTags, showSteps, showDue, showRem, showNote };

        // Tags + due date, but the due-date toggle is off → no separator after tags
        Assert.Equal(Visibility.Collapsed, c.Convert(
            With(new[] { "t1" }, 0, 123, null, null, showDue: false), typeof(Visibility), "1", Inv));
        // Steps + due date, due hidden → the separator after steps is gone too
        Assert.Equal(Visibility.Collapsed, c.Convert(
            With(Array.Empty<string>(), 2, 123, null, null, showDue: false), typeof(Visibility), "2", Inv));
        // Tags + note, tags hidden → nothing to separate after tags
        Assert.Equal(Visibility.Collapsed, c.Convert(
            With(new[] { "t1" }, 0, null, null, "hi", showTags: false), typeof(Visibility), "1", Inv));
    }

    [Fact]
    public void MetaSeparator_ShortInput_Collapsed()
    {
        IMultiValueConverter c = new MetaSeparatorVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(new object[] { null! }, typeof(Visibility), "1", Inv));
    }
}
