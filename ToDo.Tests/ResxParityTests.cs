using System.Collections;
using System.Globalization;
using System.Resources;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// RESX health checks: Strings.resx (zh neutral) and Strings.en.resx (satellite) must
/// share an identical key set and carry no blank value, so a future language addition
/// (add Strings.xx.resx + an AppLanguage value + a SetLanguage mapping) fails loudly
/// here if any key is forgotten. Loading via the same ResourceManager the app uses
/// also proves the manifest name "ToDo.Resources.Strings" resolves in the tests.
/// </summary>
public sealed class ResxParityTests
{
    private static readonly ResourceManager Res = new("ToDo.Resources.Strings", typeof(Loc).Assembly);

    [Fact]
    public void ZhAndEn_HaveIdenticalKeysAndNoBlankValues()
    {
        var zh = Res.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, true);
        var en = Res.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);
        Assert.NotNull(zh);
        Assert.NotNull(en);

        var zhKeys = zh.Cast<DictionaryEntry>().Select(e => (string)e.Key!).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var enKeys = en.Cast<DictionaryEntry>().Select(e => (string)e.Key!).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(zhKeys, enKeys);
        Assert.True(zhKeys.Length > 0);

        foreach (var key in zhKeys)
        {
            Assert.False(string.IsNullOrEmpty(zh.GetString(key)), $"zh:{key} is blank");
            Assert.False(string.IsNullOrEmpty(en.GetString(key)), $"en:{key} is blank");
        }
    }
}
