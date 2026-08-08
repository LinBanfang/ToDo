# capture-screenshots.ps1
# Regenerate the README feature screenshots (work-list / my-day / sticky-note / settings).
#
# What it does:
#   1. Builds ToDo + ToDo.Demo (skip with -SkipBuild).
#   2. Seeds a throwaway demo DB from ToDo.Demo (temporary file, never your real one).
#   3. Launches the app pointed at that DB via a temporary settings.json.
#   4. Drives the UI with UIAutomation: clicks sidebar 工作 / 我的一天, opens the
#      sticky note via the footer button (captures the separate sticky window, then
#      clicks its back-to-main button), opens the settings page — one shot each.
#   5. Captures with DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) so the
#      Windows window shadow (left/right/bottom ~7px, top 0) is NOT included and
#      the borders stay symmetric — see docs/screenshots.md.
#   6. Self-checks every screenshot: the outer 12px strips must contain no dark
#      shadow pixels, and the four edges must share one color (the small sticky
#      note uses a 3px strip — content-dense, a real edge shadow would still show).
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

# Capture the WINDOW VISIBLE area via DWMWA_EXTENDED_FRAME_BOUNDS (attr 9).
# GetWindowRect would include the DWM shadow (bottom ~7px, sides ~7px, top 0),
# which makes README screenshots look uneven.
function Save-WindowShot($hwnd, [string]$path) {
    $r = New-Object NativeCap+RECT
    [NativeCap]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf([type][NativeCap+RECT])) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $log.AppendLine("  $([System.IO.Path]::GetFileName($path)) captured ($w x $h)") | Out-Null
    return $path
}

# Self-check: outer strips of each side must be free of dark shadow pixels and the
# edge colors must match, so the four borders render symmetrically.
# $strip: how many outer pixels to scan for shadow (12 = full DWM shadow ~7px; the
# small sticky note is content-dense, so 3px still catches a real shadow at the edge).
function Assert-SymmetricEdges([string]$path, [int]$strip = 12) {
    $bmp = New-Object System.Drawing.Bitmap($path)
    $w = $bmp.Width; $h = $bmp.Height
    $midX = [int]($w / 2); $midY = [int]($h / 2)
    $dark = 0
    foreach ($x in 0..($strip-1)) { $c = $bmp.GetPixel($x, $midY);    if ($c.R -lt 40) { $dark++ } }
    foreach ($x in ($w-$strip)..($w-1)) { $c = $bmp.GetPixel($x, $midY);    if ($c.R -lt 40) { $dark++ } }
    foreach ($y in 0..($strip-1)) { $c = $bmp.GetPixel($midX, $y);    if ($c.R -lt 40) { $dark++ } }
    foreach ($y in ($h-$strip)..($h-1)) { $c = $bmp.GetPixel($midX, $y);    if ($c.R -lt 40) { $dark++ } }
    $hex = @{}
    foreach ($key in @('left','right','top','bottom')) {
        switch ($key) {
            'left'   { $c = $bmp.GetPixel(0, $midY) }
            'right'  { $c = $bmp.GetPixel($w-1, $midY) }
            'top'    { $c = $bmp.GetPixel($midX, 0) }
            'bottom' { $c = $bmp.GetPixel($midX, $h-1) }
        }
        $hex[$key] = ('{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B)
    }
    $bmp.Dispose()
    $ok = ($dark -eq 0) -and (($hex.Values | Select-Object -Unique).Count -eq 1)
    $log.AppendLine("  edge check: dark=$dark colors=$($hex.left)/$($hex.right)/$($hex.top)/$($hex.bottom) -> $(if($ok){'PASS'}else{'FAIL'})") | Out-Null
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
    Get-Process -Name 'ToDo' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 900
    Remove-Item $DbPath -Force -ErrorAction SilentlyContinue
    Remove-Item $journalPath -Force -ErrorAction SilentlyContinue
    $null = & dotnet $demoDll $DbPath
    if ($LASTEXITCODE -ne 0) { throw "todo-demo failed: $LASTEXITCODE" }
    $log.AppendLine("demo DB seeded: $DbPath") | Out-Null

    # 3) temporary settings pointing at the demo DB (backup the real file first)
    Copy-Item $settingsPath $settingsBak -Force
    $demoSettings = @{
        SchemaVersion = 5; DbPath = $DbPath; Theme = $Theme; SidebarWidth = 280
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
    Save-WindowShot $hwnd $p | Out-Null
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
    Save-WindowShot $shwnd $p | Out-Null
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
    Save-WindowShot $hwnd $p | Out-Null
    Assert-SymmetricEdges $p

    $openCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'OpenSettings')
    $openBtn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $openCond)
    if (-not $openBtn) { throw 'OpenSettings button not found' }
    $openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $p = Join-Path $OutputDir 'settings.png'
    Save-WindowShot $hwnd $p | Out-Null
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
