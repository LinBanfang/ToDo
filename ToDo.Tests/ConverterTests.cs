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
}
