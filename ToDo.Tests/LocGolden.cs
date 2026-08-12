using System.IO;
using System.Reflection;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.Tests;

/// <summary>
/// Shared evaluator for the localization golden spec: reflects over every public
/// static string-returning member of <see cref="Loc"/> and evaluates it with the
/// fixed argument table below. Used by the one-time capture test (which wrote
/// loc-golden.txt from the pre-migration ternary implementation) and by the
/// permanent <see cref="LocGoldenTests"/> (which re-checks the RESX-backed Loc
/// against that file). The golden file is the durable spec of all 218 values.
/// </summary>
internal static class LocGolden
{
    /// <summary>Fixed date used for the date-format methods.</summary>
    public static readonly DateTime Now = new(2024, 3, 5, 9, 30, 0);

    /// <summary>Every public static string-returning property and method name on Loc.</summary>
    public static string[] AllMemberNames()
    {
        var props = typeof(Loc).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name);
        var methods = typeof(Loc).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && m.GetParameters().Length > 0)
            .Select(m => m.Name);
        return props.Concat(methods).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Evaluate one Loc member under the given language (null when not found).</summary>
    public static string? Evaluate(string name, AppLanguage lang)
    {
        Loc.SetLanguage(lang);
        var prop = typeof(Loc).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (prop != null && prop.PropertyType == typeof(string))
            return (string?)prop.GetValue(null);
        var method = typeof(Loc).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        if (method != null)
            return (string?)method.Invoke(null, ArgsFor(name));
        return null;
    }

    /// <summary>Source-tree path the one-time capture test writes to.</summary>
    public static string SourceGoldenPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "loc-golden.txt"));

    /// <summary>Output-tree path the permanent golden test reads (CopyToOutputDirectory).</summary>
    public static string OutputGoldenPath() =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "loc-golden.txt");

    public static string Encode(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    public static string Decode(string value) =>
        value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");

    private static object[] ArgsFor(string name) => name switch
    {
        "ConfirmDeleteListGroupMsg" => new object[] { "A组" },
        "AttachmentOpenFailed" => new object[] { "f.txt" },
        "AttachmentTooLarge" => new object[] { 8 },
        "ConfirmDeleteMsg" => new object[] { "收件箱" },
        "TagNameExists" => new object[] { "工作" },
        "ConfirmDeleteGroupMsg" => new object[] { "组A" },
        "UndoCompleteMsg" => new object[] { "任务X" },
        "UndoDeleteMsg" => new object[] { "任务X" },
        "MinutesAgo" => new object[] { 5 },
        "HoursAgo" => new object[] { 3 },
        "DaysAgo" => new object[] { 2 },
        "UpdateUpToDate" => new object[] { "1.3.0" },
        "UpdateCheckFailed" => new object[] { "网络错误" },
        "BackupSaved" => new object[] { @"C:\backup\app.db" },
        "ImageTooLarge" => new object[] { 12 },
        "RelativeDate" => new object[] { Now },
        "ShortDate" => new object[] { Now },
        "ReminderTime" => new object[] { Now },
        "ReminderDateOnly" => new object[] { Now },
        "ReminderTimeOnly" => new object[] { Now },
        "RecurrenceName" => new object[] { RecurrenceFrequency.Daily },
        "TitleTextRecommend" => new object[] { true },
        "HoursFromNow" => new object[] { 1 },
        _ => throw new ArgumentException($"No argument table for Loc.{name}", nameof(name)),
    };
}
