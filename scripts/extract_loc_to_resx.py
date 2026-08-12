#!/usr/bin/env python3
"""Extract Loc's string values into RESX resource files (P2-9 migration).

Reads the old ternary-based LocalizationService.cs, pulls every simple
`public static string X => Language == Chinese ? "zh" : "en";` property verbatim,
and writes Strings.resx (zh, neutral) + Strings.en.resx (en satellite). The
parameterized methods are transcribed by hand in MANUAL below (their values carry
{0} format placeholders). Any `public static string` declaration the regex misses
is printed so nothing silently slips through.

Run:  python scripts/extract_loc_to_resx.py
Verify after: dotnet build ToDo.Core && the LocGolden tests.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "ToDo.Core", "Services", "LocalizationService.cs")
OUT = os.path.join(ROOT, "ToDo.Core", "Resources")

# Hand-transcribed parameterized members: name -> (zh template, en template).
MANUAL = {
    # 15 format templates (single {0} placeholder)
    "ConfirmDeleteListGroupMsg": ('确定删除分组 "{0}" 吗？组内列表将变为未分组。', 'Delete group "{0}"? Lists will become ungrouped.'),
    "AttachmentOpenFailed": ('无法打开附件 "{0}"', 'Couldn\'t open attachment "{0}"'),
    "AttachmentTooLarge": ('单个附件不能超过 {0} MB', 'Attachment too large (max {0} MB)'),
    "ConfirmDeleteMsg": ('确定删除 "{0}" 吗？', 'Delete "{0}"?'),
    "TagNameExists": ('标签名 "{0}" 已存在', 'A tag named "{0}" already exists'),
    "ConfirmDeleteGroupMsg": ('确定删除分组 "{0}" 吗？任务将变为未分组。', 'Delete group "{0}"? Tasks will become ungrouped.'),
    "UndoCompleteMsg": ('已完成「{0}」', 'Completed "{0}"'),
    "UndoDeleteMsg": ('已删除「{0}」', 'Deleted "{0}"'),
    "MinutesAgo": ('{0} 分钟前', '{0}m ago'),
    "HoursAgo": ('{0} 小时前', '{0}h ago'),
    "DaysAgo": ('{0} 天前', '{0}d ago'),
    "UpdateUpToDate": ('已是最新版本（最新版本 {0}）', 'You\'re up to date (latest version {0})'),
    "UpdateCheckFailed": ('检查更新失败：{0}', 'Update check failed: {0}'),
    "BackupSaved": ('备份已导出到：{0}', 'Backup exported to: {0}'),
    "ImageTooLarge": ('图片不能超过 {0} MB', 'Image too large (max {0} MB)'),
    # 3 date templates (formatted with InvariantCulture). Each is a SINGLE custom
    # pattern inside the placeholder — `{0:M}月{0:d}日` would NOT work, because a
    # one-char `{0:M}`/`{0:d}` is a *standard* specifier (full month name / short
    # date), not numeric month/day. Only a multi-char pattern like `{0:M月d日}` is
    # a custom format, where M/d are unpadded numeric month/day and the CJK chars
    # are literals.
    "RelativeDateFormat": ('{0:yyyy年M月d日}', '{0:MMM d, yyyy}'),
    "ShortDateFormat": ('{0:M月d日}', '{0:MMM d}'),
    "ReminderTimeFormat": ('{0:M月d日 HH:mm}', '{0:MMM d, HH:mm}'),
    # 2 new keys for the detail-pane snooze menu
    "HourLater": ('{0} 小时后', '{0} hour later'),
    "HoursLater": ('{0} 小时后', '{0} hours later'),
}

# Resx values keep LF; escape the characters that are special in XML text.
def xml_escape(v: str) -> str:
    return v.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('\n', '&#xA;')

# Turn C# string-literal escapes back into real characters (sentinel preserves
# literal backslashes from being treated as escape introducers).
def unescape(v: str) -> str:
    v = v.replace('\\\\', '\x00')
    v = v.replace('\\"', '"').replace('\\n', '\n').replace('\\r', '\r').replace('\\t', '\t')
    return v.replace('\x00', '\\')

RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""

RESX_FOOTER = "</root>\n"

def write_resx(path: str, values: dict):
    lines = [RESX_HEADER]
    for key in sorted(values):
        lines.append(f'  <data name="{key}" xml:space="preserve">')
        lines.append(f'    <value>{xml_escape(values[key])}</value>')
        lines.append('  </data>')
    lines.append(RESX_FOOTER)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"wrote {path}: {len(values)} keys")

def main() -> int:
    with open(SRC, encoding="utf-8") as f:
        source = f.read()

    # Simple properties only: a name, then `=>`, then the inline ternary. Methods
    # carry a parameter list so `\w+\s*=>` fails for them (correctly skipped).
    # `\s*` (not literal spaces) is required: multi-line properties put the `?` and
    # `:` ternary arms on their own indented lines.
    pattern = re.compile(
        r'public static string (\w+)\s*=>\s*Language == AppLanguage\.Chinese\s*\?\s*"([^"]*)"\s*:\s*"([^"]*)"\s*;',
        re.DOTALL)
    zh = {k: unescape(a) for k, a, _ in pattern.findall(source)}
    en = {k: unescape(b) for k, _, b in pattern.findall(source)}

    # Cross-check: the regex must have caught every simple property. The expected
    # set is all `public static string` members minus the parameterized methods
    # (they carry a `(` in the signature and are transcribed in MANUAL instead).
    member_names = re.findall(r'public static string (\w+)', source)
    method_names = set(re.findall(r'public static string (\w+)\(', source))
    expected_simple = sorted(n for n in member_names if n not in method_names)
    if set(zh) != set(expected_simple):
        print(f"ERROR: regex extracted {len(zh)} simple props but expected {len(expected_simple)}")
        print("  missing:", sorted(set(expected_simple) - set(zh)))
        print("  extra:  ", sorted(set(zh) - set(expected_simple)))
        return 1

    # Merge manual keys; every value must be present in both languages.
    for name, (z, e) in MANUAL.items():
        if name in zh or name in en:
            print(f"ERROR: manual key {name} collides with a regex-extracted key")
            return 1
        zh[name] = z
        en[name] = e

    missing = [k for k in zh if k not in en or not zh[k] or not en[k]]
    if missing:
        print(f"ERROR: incomplete keys: {missing}")
        return 1

    os.makedirs(OUT, exist_ok=True)
    write_resx(os.path.join(OUT, "Strings.resx"), zh)
    write_resx(os.path.join(OUT, "Strings.en.resx"), en)
    print(f"total keys: {len(zh)}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
