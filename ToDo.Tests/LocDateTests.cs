using System;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>Localization date/time formatting — pure strings, easy to pin down.</summary>
public sealed class LocDateTests : IDisposable
{
    private readonly AppLanguage _original;

    public LocDateTests() => _original = Loc.Language;

    public void Dispose() => Loc.SetLanguage(_original);

    [Theory]
    [InlineData(AppLanguage.Chinese, "2024年3月5日")]
    [InlineData(AppLanguage.English, "Mar 5, 2024")]
    public void RelativeDate_FormatsPerLanguage(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.RelativeDate(new DateTime(2024, 3, 5)));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "3月5日")]
    [InlineData(AppLanguage.English, "Mar 5")]
    public void ShortDate_FormatsPerLanguage(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.ShortDate(new DateTime(2024, 3, 5)));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "3月5日 09:30")]
    [InlineData(AppLanguage.English, "Mar 5, 09:30")]
    public void ReminderTime_FormatsPerLanguage(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.ReminderTime(new DateTime(2024, 3, 5, 9, 30, 0)));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "3月5日")]
    [InlineData(AppLanguage.English, "Mar 5")]
    public void ReminderDateOnly_FormatsPerLanguage(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.ReminderDateOnly(new DateTime(2024, 3, 5)));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "刚刚")]
    [InlineData(AppLanguage.English, "just now")]
    public void JustNow_Localized(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.JustNow);
    }
}
