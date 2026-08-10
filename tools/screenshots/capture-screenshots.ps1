# capture-screenshots.ps1
# Regenerate the README feature screenshots (work-list / my-day / sticky-note /
# list-theme / settings).
#
# What it does:
#   1. Builds ToDo + ToDo.Demo (skip with -SkipBuild).
#   2. Seeds a throwaway demo DB from ToDo.Demo (temporary file, never your real one).
#   3. Launches the app pointed at that DB via a temporary settings.json.
#   4. Drives the UI with UIAutomation: clicks sidebar 工作 / 我的一天 / 学习 (the
#      themed list), opens the sticky note via the footer button (captures the
#      separate sticky window, then clicks its back-to-main button), opens the
#      settings page and navigates to the 行为 section (shows the task-row display
#      toggles) — one shot each.
#   5. Captures with DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) so the
#      Windows window shadow (left/right/bottom ~7px, top 0) is NOT included and
#      the borders stay symmetric — see docs/screenshots.md.
#   6. Composites each shot on a transparent canvas (Margin px, default 12) with a
#      synthetic soft drop shadow drawn behind the rounded window, so it reads as the
#      app floating on whatever background hosts the PNG (GitHub light/dark both work).
#   7. Self-checks every screenshot: each edge's outermost pixels must be fully
#      transparent (the synthetic shadow must not overflow the margin) and the
#      shadow's reach into the strip must be symmetric L/R and T/B (±2px AA
#      tolerance) and non-zero. The small sticky note uses a 3px strip — content-
#      dense, an overflow would still show.
#
# Your real %LOCALAPPDATA%\ToDo\settings.json is backed up first and restored in
# a finally block; your real DB is never opened. Safe to run at any time.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1 -Theme Dark -OutputDir C:\tmp\shots
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1 -SkipBuild

param(
    [string]$Theme = 'Light',                 # Light | Dark — screenshot theme
    [string]$Configuration = 'Debug',         # Debug | Release
    [string]$OutputDir,                       # default: <repo>\screenshots
    [string]$DbPath,                          # default: temp DB, deleted afterwards
    [int]$Margin = 12,                        # px of breathing room around the window (0 = window fills the PNG edge-to-edge)
    [switch]$SkipBuild,
    [switch]$KeepApp                          # leave the app running afterwards (debugging)
)

$ErrorActionPreference = 'Stop'

# ---- paths (repo-relative so the script works from any checkout) -------------------
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$exe      = Join-Path $repoRoot "ToDo\bin\$Configuration\net9.0-windows\ToDo.exe"
$demoDll  = Join-Path $repoRoot "ToDo.Demo\bin\$Configuration\net9.0\todo-demo.dll"
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'screenshots' }
if (-not $DbPath)    { $DbPath = Join-Path $env:TEMP "todo-screenshot-$Theme.db" }
$journalPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($DbPath),
    [System.IO.Path]::GetFileNameWithoutExtension($DbPath) + "-log.db")

$settingsPath = Join-Path $env:LOCALAPPDATA 'ToDo\settings.json'
$settingsBak  = Join-Path $env:TEMP "todo-screenshot-settings-backup.json"
$logPath      = Join-Path $env:TEMP 'todo-screenshots.log'
$log = New-Object System.Text.StringBuilder
$proc = $null
$ok = $false
$radius = 8   # corner radius (px) applied to the composited window and its shadow

# ---- native helpers (UIA + DWM extended frame bounds) -----------------------------
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeCap
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public static string DumpWindows(uint pid)
    {
        var sb = new System.Text.StringBuilder();
        EnumWindows((h, l) =>
        {
            uint wpid;
            GetWindowThreadProcessId(h, out wpid);
            if (wpid == pid)
            {
                var t = new System.Text.StringBuilder(256);
                GetWindowText(h, t, 256);
                sb.AppendLine(h.ToString("X") + " visible=" + IsWindowVisible(h) + " title=" + t.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }
    // FindWindow(IntPtr.Zero, title) is awkward from PowerShell ($null marshals to
    // "" and no class name matches), so find by title via EnumWindows instead.
    public static IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            if (found != IntPtr.Zero) return false;
            if (!IsWindowVisible(h)) return true;
            var t = new System.Text.StringBuilder(256);
            GetWindowText(h, t, 256);
            if (t.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@

function Click-Center($el) {
    $r = $el.Current.BoundingRectangle
    if ($r.IsEmpty) { return $false }
    $x = [int]($r.Left + $r.Width / 2); $y = [int]($r.Top + $r.Height / 2)
    [NativeCap]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 80
    [NativeCap]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero) | Out-Null
    [NativeCap]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) | Out-Null
    return $true
}

# The sidebar is the leftmost column, so pick the name match with the smallest X.
function Find-SidebarText($root, [string]$name) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $els = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    $best = $null
    foreach ($e in $els) {
        $r = $e.Current.BoundingRectangle
        if ($r.IsEmpty) { continue }
        if (-not $best -or $r.Left -lt $best.Current.BoundingRectangle.Left) { $best = $e }
    }
    if ($best) {
        $br = $best.Current.BoundingRectangle
        $log.AppendLine("    '$name' -> ($([int]$br.Left),$([int]$br.Top)) $([int]$br.Width)x$([int]$br.Height)") | Out-Null
    } else {
        $log.AppendLine("    ERROR: '$name' not found in the automation tree") | Out-Null
    }
    return $best
}

# Rounded-rectangle GraphicsPath (float coords, works at any scale).
function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = 2 * $r
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draw a soft drop shadow under a w×h rectangle at (x,y) on $g. The Win11 DWM shadow
# is too faint to read on a light README canvas (and borderless windows like the
# sticky note barely get one), so we render our own: a stack of concentric rounded
# rects whose alphas are the differences of a linear falloff profile, so at any offset
# from the window edge the summed alpha is exactly alpha*(1 - t/blur) — the blur
# parameter IS the visible shadow width. (The earlier trick of scaling a solid core up
# with bicubic interpolation made the width fixed by the kernel, not by $blur.)
# $blur: visible shadow width (px) beyond each edge. $alpha: peak alpha at the edge.
# The outermost rings carry near-zero alpha, so edges fade cleanly into the canvas.
function Add-SoftShadow([System.Drawing.Graphics]$g, [int]$x, [int]$y, [int]$w, [int]$h) {
    $blur = 12; $alpha = 45; $N = 24
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    # Outermost -> innermost: ring at expansion $tIn*$blur carries the profile drop
    # across its span, so concentric overlap adds up to the target linear falloff.
    for ($i = $N - 1; $i -ge 1; $i--) {
        $tIn = $i / $N; $tOut = ($i + 1) / $N
        $a = [int]($alpha * (1 - $tIn)) - [int]($alpha * (1 - $tOut))
        if ($a -le 0) { continue }
        # cap expansion just short of the canvas margin so the outermost AA pixels
        # stay clear of the PNG edge (blur=12 on a 12px margin otherwise touches it)
        $e = $tIn * ($blur - 2)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($a, 0, 0, 0))
        # Radius grows with expansion so the silhouette keeps the window's corner.
        $path = New-RoundedRectPath ([single]($x - $e)) ([single]($y - $e)) ([single]($w + 2*$e)) ([single]($h + 2*$e)) ([single]($radius + $e))
        $g.FillPath($brush, $path)
        $path.Dispose(); $brush.Dispose()
    }
    # Innermost core: window footprint, carries the profile drop from t=0.
    $aCore = [int]($alpha * (1 - 0)) - [int]($alpha * (1 - 1.0/$N))
    $brush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($aCore, 0, 0, 0))
    $core = New-RoundedRectPath ([single]$x) ([single]$y) ([single]$w) ([single]$h) ([single]$radius)
    $g.FillPath($brush2, $core)
    $core.Dispose(); $brush2.Dispose()
}

# Capture the WINDOW onto a canvas.
# With $margin = 0: the visible area only, via DWMWA_EXTENDED_FRAME_BOUNDS
# (attr 9) — no DWM shadow, window runs edge-to-edge of the PNG.
# With $margin > 0 (default 12): draw the window centered on a $margin-wide canvas
# filled with the theme's window background, with our own soft drop shadow behind it —
# the shot reads like the real window floating on a matching canvas (README-friendly).
function Save-WindowShot($hwnd, [string]$path, [int]$margin = 0) {
    $r = New-Object NativeCap+RECT
    [NativeCap]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf([type][NativeCap+RECT])) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($margin -gt 0) {
        # Transparent canvas: the window + soft shadow float on whatever the host page
        # paints behind the PNG (GitHub light/dark both look natural). A solid canvas
        # color could never match the window's multi-color edges (F3F2F1 title bar,
        # FAF9F8 sidebar, FFFFFF content), so transparent avoids any visible frame.
        $windowShot = New-Object System.Drawing.Bitmap($w, $h)
        $wg = [System.Drawing.Graphics]::FromImage($windowShot)
        $wg.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
        $wg.Dispose()
        $cw = $w + 2 * $margin; $ch = $h + 2 * $margin
        $bmp = New-Object System.Drawing.Bitmap($cw, $ch)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        Add-SoftShadow $g $margin $margin $w $h
        # The capture is square (DoNotRound) so no desktop shows through; round the
        # corners over the canvas here to match the real Win11 window shape.
        $clip = New-RoundedRectPath $margin $margin $w $h $radius
        $g.SetClip($clip)
        $g.DrawImage($windowShot, $margin, $margin)
        $g.ResetClip()
        $clip.Dispose()
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $windowShot.Dispose(); $g.Dispose(); $bmp.Dispose()
        $log.AppendLine("  $([System.IO.Path]::GetFileName($path)) captured ($cw x $ch, +${margin}px margin, soft shadow)") | Out-Null
    } else {
        $bmp = New-Object System.Drawing.Bitmap($w, $h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        $log.AppendLine("  $([System.IO.Path]::GetFileName($path)) captured ($w x $h)") | Out-Null
    }
    return $path
}

# Self-check for the composited transparent-canvas shots. The synthetic shadow is
# drawn black-with-alpha (straight alpha, so stored RGB ~0), which means a "no dark
# pixels" strip check is meaningless on a transparent canvas — instead we assert the
# shadow's geometry: it is actually present, it stays inside the margin (all four
# edges fully transparent), and its reach into the strip is symmetric L/R and T/B.
# $strip: how many outer pixels to scan (12 for the big windows; the small sticky
# note is content-dense, so 3 is enough to catch an overflow at the edge).
function Assert-SymmetricEdges([string]$path, [int]$strip = 12, [switch]$SkipEdgeCheck) {
    if ($SkipEdgeCheck) {
        # Themed list shot: the background image fills the content column out to the
        # window's right edge, so right != left edge color and the image may legitimately
        # contain dark pixels in the strip. The DWM shadow is already excluded by
        # DWMWA_EXTENDED_FRAME_BOUNDS, so nothing reliable to assert here — skip.
        $log.AppendLine("  edge check: skipped (themed background fills the right edge)") | Out-Null
        return
    }
    $bmp = New-Object System.Drawing.Bitmap($path)
    $w = $bmp.Width; $h = $bmp.Height
    $midX = [int]($w / 2); $midY = [int]($h / 2)
    $reach = @{}
    foreach ($key in @('left','right','top','bottom')) {
        $n = 0
        switch ($key) {
            'left'   { for ($x = 0; $x -lt $strip; $x++)    { if ($bmp.GetPixel($x, $midY).A  -gt 0) { $n++ } } }
            'right'  { for ($x = $w-$strip; $x -lt $w; $x++){ if ($bmp.GetPixel($x, $midY).A  -gt 0) { $n++ } } }
            'top'    { for ($y = 0; $y -lt $strip; $y++)    { if ($bmp.GetPixel($midX, $y).A -gt 0) { $n++ } } }
            'bottom' { for ($y = $h-$strip; $y -lt $h; $y++){ if ($bmp.GetPixel($midX, $y).A -gt 0) { $n++ } } }
        }
        $reach[$key] = $n
    }
    $hex = @{}
    foreach ($key in @('left','right','top','bottom')) {
        switch ($key) {
            'left'   { $c = $bmp.GetPixel(0, $midY) }
            'right'  { $c = $bmp.GetPixel($w-1, $midY) }
            'top'    { $c = $bmp.GetPixel($midX, 0) }
            'bottom' { $c = $bmp.GetPixel($midX, $h-1) }
        }
        $hex[$key] = ('{0:X2}{1:X2}{2:X2}{3:X2}' -f $c.A, $c.R, $c.G, $c.B)
    }
    $bmp.Dispose()
    # Reach may differ by a pixel or two where the faintest ring's AA lands on a
    # half-pixel boundary — tolerate 2px. A real regression (e.g. the DWM shadow,
    # ~7px L/R/B vs 0 top) still fails hard.
    $edgeClear = ($hex.Values | Where-Object { $_ -eq '00000000' }).Count
    $reachTotal = ($reach.Values | Measure-Object -Sum).Sum
    $ok = ($edgeClear -eq 4) -and ([Math]::Abs($reach.left - $reach.right) -le 2) -and ([Math]::Abs($reach.top - $reach.bottom) -le 2) -and ($reachTotal -gt 0)
    $log.AppendLine("  edge check: reach L/R/T/B=$($reach.left)/$($reach.right)/$($reach.top)/$($reach.bottom) clear=$edgeClear/4 rgba=$($hex.left)/$($hex.right)/$($hex.top)/$($hex.bottom) -> $(if($ok){'PASS'}else{'FAIL'})") | Out-Null
    if (-not $ok) { throw "edge symmetry check failed for $([System.IO.Path]::GetFileName($path))" }
}

try {
    # 1) build
    if (-not $SkipBuild) {
        $log.AppendLine("building solution ($Configuration)...") | Out-Null
        $null = & dotnet build (Join-Path $repoRoot 'ToDo.slnx') -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }
    }
    if (-not (Test-Path $exe)) { throw "ToDo.exe not found at $exe — build first or pass -Configuration" }

    # 2) fresh demo DB
    # Kill only THIS repo's ToDo.exe (Path match). Get-Process -Name 'ToDo' also
    # matches the Microsoft Store's "Todo.exe" — don't close the user's other app.
    Get-Process -Name 'ToDo' -ErrorAction SilentlyContinue |
        Where-Object { try { $_.Path -eq $exe } catch { $false } } | Stop-Process -Force
    Start-Sleep -Milliseconds 900
    Remove-Item $DbPath -Force -ErrorAction SilentlyContinue
    Remove-Item $journalPath -Force -ErrorAction SilentlyContinue
    $null = & dotnet $demoDll $DbPath
    if ($LASTEXITCODE -ne 0) { throw "todo-demo failed: $LASTEXITCODE" }
    $log.AppendLine("demo DB seeded: $DbPath") | Out-Null

    # 3) temporary settings pointing at the demo DB (backup the real file first)
    Copy-Item $settingsPath $settingsBak -Force
    $demoSettings = @{
        SchemaVersion = 6; DbPath = $DbPath; Theme = $Theme; SidebarWidth = 280
        Language = 'Chinese'; CheckForUpdatesOnStartup = $false; ReminderNotifications = $false
        ReminderSound = $false; ReminderSoundPath = ''; SyncEnabled = $false; SyncServerUrl = ''; SyncKey = ''
        DeviceId = ''; LastSyncServerSeq = 0; LastSyncTime = 0; UpdateSources = @(); PendingRestorePath = $null
    }
    $demoSettings | ConvertTo-Json -Depth 4 | Set-Content $settingsPath -Encoding utf8

    # 4) launch
    $proc = Start-Process -FilePath $exe -PassThru
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { $hwnd = $proc.MainWindowHandle; break }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw 'no window appeared' }
    Start-Sleep -Seconds 2
    [NativeCap]::ShowWindow($hwnd, 9) | Out-Null
    [NativeCap]::SetForegroundWindow($hwnd) | Out-Null
    # Square corners (DWMWA_WINDOW_CORNER_PREFERENCE = 1, DoNotRound) so the four
    # corners fill with app colors — Win11 rounded corners would otherwise show the
    # desktop through the transparent corners of the capture.
    $cp = 1
    [NativeCap]::DwmSetWindowAttribute($hwnd, 33, [ref]$cp, 4) | Out-Null
    Start-Sleep -Milliseconds 600
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    # 5) the three shots
    $work = Find-SidebarText $root '工作'
    if (-not $work) { throw 'sidebar "工作" not found' }
    Click-Center $work | Out-Null
    Start-Sleep -Seconds 2
    $p = Join-Path $OutputDir 'work-list.png'
    Save-WindowShot $hwnd $p $Margin | Out-Null
    Assert-SymmetricEdges $p

    # Sticky note: the footer button opens a separate always-on-top window titled
    # Loc.StickyNote ("迷你便笺") and hides the main window. ActiveList is still 工作
    # here, so the sticky shows 工作's tagged tasks (the tag pills). Capture it, then
    # click its back-to-main button to restore the main window.
    $stickyCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StickyNote')
    $stickyBtn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $stickyCond)
    if (-not $stickyBtn) { throw 'StickyNote footer button not found' }
    $stickyBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $shwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 20; $i++) {
        $shwnd = [NativeCap]::FindWindowByTitle('迷你便笺')
        if ($shwnd -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 300
    }
    if ($shwnd -eq [IntPtr]::Zero) {
        $log.AppendLine("    process windows after sticky click:") | Out-Null
        $log.AppendLine(([NativeCap]::DumpWindows([uint32]$proc.Id))) | Out-Null
        throw 'sticky note window not found'
    }
    $cp = 1
    [NativeCap]::DwmSetWindowAttribute($shwnd, 33, [ref]$cp, 4) | Out-Null
    Start-Sleep -Milliseconds 600
    $p = Join-Path $OutputDir 'sticky-note.png'
    Save-WindowShot $shwnd $p $Margin | Out-Null
    Assert-SymmetricEdges $p 3   # 3px: the sticky is small & content-dense; a real edge shadow would still be caught
    # Restore the main window via the sticky's back-to-main button (AutomationId
    # StickyBackToMain), then keep capturing the remaining shots.
    $stickyRoot = [System.Windows.Automation.AutomationElement]::FromHandle($shwnd)
    $backCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StickyBackToMain')
    $backBtn = $stickyRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $backCond)
    if (-not $backBtn) { throw 'StickyBackToMain button not found' }
    $backBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2

    $myday = Find-SidebarText $root '我的一天'
    if (-not $myday) { throw 'sidebar "我的一天" not found' }
    Click-Center $myday | Out-Null
    Start-Sleep -Seconds 2
    $p = Join-Path $OutputDir 'my-day.png'
    Save-WindowShot $hwnd $p $Margin | Out-Null
    Assert-SymmetricEdges $p

    # Themed list shot: 学习 has the demo background image (seeded by ToDo.Demo from
    # Assets/demo-theme-bg.jpg). The theme fills the content column to the window's right
    # edge, so the uniform-edge invariant doesn't apply — skip that self-check here.
    $study = Find-SidebarText $root '学习'
    if (-not $study) { throw 'sidebar "学习" not found' }
    Click-Center $study | Out-Null
    Start-Sleep -Seconds 2
    $p = Join-Path $OutputDir 'list-theme.png'
    Save-WindowShot $hwnd $p $Margin | Out-Null
    Assert-SymmetricEdges $p -SkipEdgeCheck

    $openCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'OpenSettings')
    $openBtn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $openCond)
    if (-not $openBtn) { throw 'OpenSettings button not found' }
    $openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    # The settings page opens on the 常规 section; click the 行为 nav item so the
    # new "任务列表显示" row toggles are what settings.png showcases.
    $behavior = Find-SidebarText $root '行为'
    if (-not $behavior) { throw 'settings nav "行为" not found' }
    Click-Center $behavior | Out-Null
    Start-Sleep -Seconds 1
    $p = Join-Path $OutputDir 'settings.png'
    Save-WindowShot $hwnd $p $Margin | Out-Null
    Assert-SymmetricEdges $p

    $log.AppendLine("done. screenshots written to $OutputDir (theme: $Theme)") | Out-Null
    $ok = $true
}
catch {
    $log.AppendLine("ERROR: $($_.Exception.Message)") | Out-Null
    Write-Host "ERROR: $($_.Exception.Message)"
}
finally {
    [System.IO.File]::WriteAllText($logPath, $log.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    if ($proc -and -not $KeepApp) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 300
    if (Test-Path $settingsBak) { Copy-Item $settingsBak $settingsPath -Force }
    Write-Host "log: $logPath"
}
if ($ok) { Write-Host 'SCREENSHOTS OK'; exit 0 } else { Write-Host 'SCREENSHOTS FAILED'; exit 1 }
