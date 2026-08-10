using System;
using System.Reflection;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Localization: language-switch semantics (LanguageChanged fires only on a real change),
/// and a reflection sweep that every string property resolves to non-empty text in BOTH
/// languages. The property getters individually are trivial ternaries, but the sweep turns
/// them into a real check — a missing or blank translation fails here — and pins the
/// coverage without one assertion per string.
/// </summary>
public sealed class LocTests : IDisposable
{
    private readonly AppLanguage _original;

    public LocTests() => _original = Loc.Language;

    public void Dispose() => Loc.SetLanguage(_original);

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void EveryStringProperty_ResolvesToNonEmptyText(AppLanguage lang)
    {
        Loc.SetLanguage(lang);
        foreach (var prop in typeof(Loc).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (prop.PropertyType != typeof(string)) continue;
            var value = Assert.IsType<string>(prop.GetValue(null));
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"Loc.{prop.Name} is blank in {(lang == AppLanguage.Chinese ? "Chinese" : "English")}");
        }
    }

    [Fact]
    public void DefaultLanguage_IsChinese()
    {
        Assert.Equal(AppLanguage.Chinese, Loc.Language);
    }

    [Fact]
    public void SetLanguage_SameValue_DoesNotRaiseChanged()
    {
        Loc.SetLanguage(AppLanguage.Chinese);
        var raised = 0;
        Loc.LanguageChanged += () => raised++;
        Loc.SetLanguage(AppLanguage.Chinese);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void SetLanguage_NewValue_RaisesOnce_AndUpdatesLanguage()
    {
        Loc.SetLanguage(AppLanguage.Chinese);
        var raised = 0;
        Loc.LanguageChanged += () => raised++;
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal(1, raised);
        Assert.Equal(AppLanguage.English, Loc.Language);
    }

    [Fact]
    public void Toggle_FlipsLanguageEachCall()
    {
        Loc.SetLanguage(AppLanguage.Chinese);
        Loc.Toggle();
        Assert.Equal(AppLanguage.English, Loc.Language);
        Loc.Toggle();
        Assert.Equal(AppLanguage.Chinese, Loc.Language);
    }

    [Fact]
    public void ReminderTimeOnly_UsesInvariantHHmm()
    {
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal("09:30", Loc.ReminderTimeOnly(new DateTime(2024, 3, 5, 9, 30, 0)));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, 5, "5 分钟前")]
    [InlineData(AppLanguage.English, 5, "5m ago")]
    public void MinutesAgo_Localized(AppLanguage lang, int n, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.MinutesAgo(n));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, 3, "3 小时前")]
    [InlineData(AppLanguage.English, 3, "3h ago")]
    public void HoursAgo_Localized(AppLanguage lang, int n, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.HoursAgo(n));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, 2, "2 天前")]
    [InlineData(AppLanguage.English, 2, "2d ago")]
    public void DaysAgo_Localized(AppLanguage lang, int n, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.DaysAgo(n));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "确定删除 \"收件箱\" 吗？")]
    [InlineData(AppLanguage.English, "Delete \"收件箱\"?")]
    public void ConfirmDeleteMsg_InterpolatesName(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.ConfirmDeleteMsg("收件箱"));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "标签名 \"工作\" 已存在")]
    [InlineData(AppLanguage.English, "A tag named \"工作\" already exists")]
    public void TagNameExists_InterpolatesName(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.TagNameExists("工作"));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "单个附件不能超过 8 MB")]
    [InlineData(AppLanguage.English, "Attachment too large (max 8 MB)")]
    public void AttachmentTooLarge_InterpolatesSize(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.AttachmentTooLarge(8));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "已是最新版本（最新版本 1.3.0）")]
    [InlineData(AppLanguage.English, "You're up to date (latest version 1.3.0)")]
    public void UpdateUpToDate_InterpolatesVersion(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.UpdateUpToDate("1.3.0"));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "检查更新失败：网络错误")]
    [InlineData(AppLanguage.English, "Update check failed: 网络错误")]
    public void UpdateCheckFailed_InterpolatesDetail(AppLanguage lang, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.UpdateCheckFailed("网络错误"));
    }
}
