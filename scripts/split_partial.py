#!/usr/bin/env python3
"""拆分大型 partial 类文件为按职责的多个文件（逐字搬迁，零行为变化）。

用法:
  python scripts/split_partial.py <源文件> <保留文件> <命名空间> <类名> \
      <新文件1> <起始行> <结束行> <用到的using> \
      [<新文件2> <起始行> <结束行> <用到的using> ...]

- 行号 1 起，区间含两端。
- 提取的行保持原缩进（partial 类体缩进 4 空格）；保留文件继承源文件的 BOM 与行尾。
- 每个新文件 = 显式指定的 using 行 + 命名空间 + class 声明 + 提取行 + 收尾（新文件用源文件行尾）。
- 源文件重写为保留文件（未提取行），被提取区间的位置留下一行注释指引。
"""
import sys
from pathlib import Path


def main():
    if len(sys.argv) < 9 or (len(sys.argv) - 5) % 4 != 0:
        print(__doc__)
        sys.exit(2)

    src_path, keep_path, ns, class_name = sys.argv[1:5]
    args = sys.argv[5:]
    ranges = []  # (target_file, start, end, usings_str)
    for i in range(0, len(args), 4):
        ranges.append((args[i], int(args[i + 1]), int(args[i + 2]), args[i + 3]))

    raw = Path(src_path).read_bytes()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    eol = "\r\n" if "\r\n" in text else "\n"
    lines = text.splitlines(keepends=True)
    total = len(lines)

    # Strip trailing EOL from the last line so the kept/partial files end cleanly.
    if lines and lines[-1] in ("\n", "\r\n"):
        lines[-1] = lines[-1].rstrip("\r\n")

    # Extract content per range.
    extracts = []
    for target, start, end, usings in ranges:
        content = "".join(lines[start - 1:end])
        extracts.append((target, start, end, usings, content))

    # Keep file = all lines not inside any extracted range, with a pointer comment
    # left at each extraction point so future readers know where the code went.
    marker_notes = {s: t for t, s, e, u, c in extracts}
    kept = []
    removed = set()
    for _, s, e, _, _ in extracts:
        removed.update(range(s - 1, e))
    for i, l in enumerate(lines):
        if i in removed:
            continue
        if i + 1 in marker_notes and lines[i].strip() != "":
            kept.append(f"    // (区域已拆出 → {Path(marker_notes[i + 1]).name})" + eol)
        kept.append(l)

    def write(path, text):
        # newline='' → no translation: the string's own EOLs are written verbatim,
        # so a CRLF source yields CRLF partials (Git won't see mixed endings).
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)

    keep_text = "".join(kept)
    if has_bom:
        keep_text = "﻿" + keep_text
    write(keep_path, keep_text)
    print(f"keep:  {keep_path}  ({total} -> {len(kept)} lines, eol={eol!r}, bom={has_bom})")

    # Write partial files. The usings arg is a single shell string with `|` separators.
    for target, start, end, usings, content in extracts:
        using_lines = [u.strip() for u in usings.split("|") if u.strip()]
        using_block = eol.join(u if u.endswith(";") else u + ";" for u in using_lines)
        header = using_block + eol * 2 + f"namespace {ns};" + eol * 2 + f"public partial class {class_name}" + eol + "{" + eol
        footer = "}" + eol
        write(target, header + content + footer)
        print(f"split: {target}  (lines {start}-{end}, {content.count(chr(10))} body lines)")

    print("done.")


if __name__ == "__main__":
    main()
