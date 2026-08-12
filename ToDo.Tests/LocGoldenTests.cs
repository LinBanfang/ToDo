using System.Collections.Generic;
using System.IO;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Golden regression test for the localization migration: loc-golden.txt was captured
/// from the pre-migration ternary Loc (one member + one value per language per line,
/// escaped with <see cref="LocGolden.Encode"/>), so the RESX-backed Loc is guaranteed to
/// produce byte-identical strings — the no-drift spec for all 221 captured values.
/// HoursFromNow is newer than the capture, so it is pinned explicitly below.
/// </summary>
public sealed class LocGoldenTests
{
    [Fact]
    public void GoldenFile_MatchesCurrentLocValues()
    {
        var golden = new Dictionary<(string name, AppLanguage lang), string>();
        foreach (var line in File.ReadLines(LocGolden.OutputGoldenPath()))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            Assert.True(parts.Length == 3, $"malformed golden line: {line}");
            var lang = parts[1] == "zh" ? AppLanguage.Chinese : AppLanguage.English;
            golden[(parts[0], lang)] = parts[2];
        }

        var goldenNames = golden.Keys.Select(k => k.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.All(goldenNames, name =>
        {
            Assert.True(golden.ContainsKey((name, AppLanguage.Chinese)), $"golden missing zh for {name}");
            Assert.True(golden.ContainsKey((name, AppLanguage.English)), $"golden missing en for {name}");
        });

        // The golden covers every current member except HoursFromNow (added after capture).
        var expectedNames = LocGolden.AllMemberNames().Except(new[] { "HoursFromNow" }).ToArray();
        Assert.Equal(expectedNames, goldenNames);

        var failures = new List<string>();
        foreach (var ((name, lang), expected) in golden)
        {
            var actual = LocGolden.Evaluate(name, lang);
            Assert.True(actual != null, $"Loc.{name} no longer exists (golden member dropped?)");
            var encoded = LocGolden.Encode(actual);
            if (encoded != expected)
                failures.Add($"Loc.{name} ({lang}): expected <{expected}> got <{encoded}>");
        }
        Assert.True(failures.Count == 0, "Golden drift:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, 1, "1 小时后")]
    [InlineData(AppLanguage.Chinese, 3, "3 小时后")]
    [InlineData(AppLanguage.English, 1, "1 hour later")]
    [InlineData(AppLanguage.English, 3, "3 hours later")]
    public void HoursFromNow_Localized(AppLanguage lang, int n, string expected)
    {
        Loc.SetLanguage(lang);
        Assert.Equal(expected, Loc.HoursFromNow(n));
    }
}
