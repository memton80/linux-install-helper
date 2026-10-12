<#
.SYNOPSIS
    Uses the pages like a person would (Tab key, lists, expanders) and checks that they never move sideways.
.DESCRIPTION
    The title of a page must keep its position and its width whatever is focused, selected or expanded: when it
    changes, the whole page jumps from left to right. The page must not scroll horizontally either, and its column
    must be centered. The pages are tested at the usual size of the window and maximized, where the column is
    narrower than the page.
.PARAMETER Exe
    Path of LinuxInstallHelper.exe.
.PARAMETER Strict
    Fail when a page moves (otherwise only report it).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [switch] $Strict
)

$ErrorActionPreference = "Stop"
$Exe = (Resolve-Path $Exe).Path

# The manifest asks for administrator rights; on the CI runner start the app without the UAC prompt.
$env:__COMPAT_LAYER = "RunAsInvoker"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LayoutInput {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
    public static void Key(byte vk) {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent;
        public int dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
    // Returns 0 when the screen now has this resolution.
    public static int SetResolution(int width, int height) {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, -1, ref mode)) return -100;
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmFields = 0x80000 | 0x100000;
        return ChangeDisplaySettings(ref mode, 0);
    }
}
"@
[LayoutInput]::SetProcessDPIAware() | Out-Null

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]::Descendants
$Any = [System.Windows.Automation.Condition]::TrueCondition
$Types = [System.Windows.Automation.ControlType]
$VkTab = 0x09
$VkEscape = 0x1B

# Layout tokens of Styles.xaml: the column of a page is at most ContentMaxWidth wide, centered, with PageMargin inside.
$ContentMaxWidth = 1000
$PageMargin = 36

function Find-All($root, $condition) { $root.FindAll($Scope, $condition) }

function New-Condition($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

# The page title: the text with this name the furthest to the right (the menu shows the same name on the left).
function Get-Title($root, [string] $title) {
    $best = $null
    foreach ($element in Find-All $root (New-Condition $UIA::NameProperty $title)) {
        if ($element.Current.ControlType -ne $Types::Text) { continue }
        if (-not $best -or $element.Current.BoundingRectangle.Left -gt $best.Current.BoundingRectangle.Left) { $best = $element }
    }
    $best
}

# The scroll viewer of the page, the one that holds its title.
function Get-PageScroll($run) {
    $title = Get-Title $run.Root $run.Title
    if (-not $title) { return $null }
    $rect = $title.Current.BoundingRectangle
    foreach ($viewer in Find-All $run.Root (New-Condition $UIA::ClassNameProperty "ScrollViewer")) {
        $box = $viewer.Current.BoundingRectangle
        if ($box.Left -le $rect.Left -and $box.Right -ge $rect.Left + 1 -and $box.Top -le $rect.Top -and $box.Bottom -ge $rect.Top + 1) { return $viewer }
    }
    $null
}

# Scrolls the page to its top, without touching the horizontal position.
function Show-Top($run) {
    if (-not $run.Scroll) { return }
    $pattern = $null
    if ($run.Scroll.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pattern) -and $pattern.Current.VerticallyScrollable) {
        $pattern.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
        Start-Sleep -Milliseconds 300
    }
}

# Scrolls the page until this element is shown.
function Show-Element($element) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $pattern)) {
        $pattern.ScrollIntoView()
        Start-Sleep -Milliseconds 400
    }
}

function Measure-Page($run) {
    Show-Top $run
    $title = Get-Title $run.Root $run.Title
    if (-not $title) { return $null }
    $rect = $title.Current.BoundingRectangle
    if ($rect.IsEmpty) { return $null }
    $scrollable = $false
    $percent = 0
    $pattern = $null
    $expected = [double]::NaN
    if ($run.Scroll) {
        if ($run.Scroll.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pattern)) {
            $scrollable = $pattern.Current.HorizontallyScrollable
            $percent = [math]::Max($pattern.Current.HorizontalScrollPercent, 0)
        }

        # Where the title is when the column is centered in the page.
        $viewer = $run.Scroll.Current.BoundingRectangle
        $expected = $viewer.Left + [math]::Max(0, ($viewer.Width - $ContentMaxWidth) / 2) + $PageMargin
    }

    [pscustomobject]@{ Left = [math]::Round($rect.Left); Width = [math]::Round($rect.Width); Scrollable = $scrollable; Percent = [math]::Round($percent); Expected = [math]::Round($expected) }
}

function Test-Step($run, [string] $what) {
    Start-Sleep -Milliseconds 400
    $now = Measure-Page $run
    if (-not $now) {
        Write-Host "  ?      $what (title not shown)"
        return
    }

    $ref = $run.Reference
    $line = "{0,-70} x={1} width={2} hscroll={3} {4}%" -f $what, $now.Left, $now.Width, $now.Scrollable, $now.Percent
    if ([math]::Abs($now.Left - $ref.Left) -gt 1 -or [math]::Abs($now.Width - $ref.Width) -gt 1 -or $now.Percent -gt 0 -or [math]::Abs($now.Left - $now.Expected) -gt 2) {
        Write-Host "  MOVED  $line"
        $script:moves += "$($run.Name): $what (x $($ref.Left) -> $($now.Left), width $($ref.Width) -> $($now.Width), hscroll $($now.Percent)%)"
    }
    else {
        Write-Host "  ok     $line"
    }
}

function Get-Short([string] $text) {
    $text = ($text -replace '\s+', ' ').Trim()
    if ($text.Length -gt 50) { $text.Substring(0, 50) + "…" } else { $text }
}

# What the page shows, to understand a move from the log.
function Write-Layout($run) {
    $title = Get-Title $run.Root $run.Title
    $left = $title.Current.BoundingRectangle.Left - 40
    $count = 0
    foreach ($element in Find-All $run.Root $Any) {
        $current = $element.Current
        $rect = $current.BoundingRectangle
        if ($rect.IsEmpty -or $current.IsOffscreen -or $rect.Left -lt $left) { continue }
        Write-Host ("    {0,-12} {1,-20} x={2,5} w={3,5} y={4,5} '{5}'" -f ($current.ControlType.ProgrammaticName -replace 'ControlType\.', ''), $current.ClassName, [math]::Round($rect.Left), [math]::Round($rect.Width), [math]::Round($rect.Top), (Get-Short $current.Name))
        if (++$count -ge 120) { break }
    }
}

# $guidance: "helper" or "alone" forces that mode for this run (without saving it); empty keeps the saved one.
# $advanced: turns the advanced mode on for this run (orange frame around the window), without saving it.
function Start-App($page, [string] $guidance, [bool] $advanced) {
    $info = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $Exe
    $arguments = @("--page", $page, "--theme", "light", "--lang", "fr-FR")
    if ($guidance) { $arguments += @("--guidance", $guidance) }
    if ($advanced) { $arguments += "--advanced" }
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "The application exited during startup (code $($process.ExitCode))." }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    $process
}

function Test-Page([hashtable] $spec, [string] $size) {
    $name = "$($spec.Page)$(if ($spec.Guidance) { " $($spec.Guidance)" })$(if ($spec.Advanced) { ' advanced' }) ($size)"
    Write-Host "== $name"
    $process = Start-App $spec.Page $spec.Guidance ([bool] $spec.Advanced)
    try {
        $handle = $process.MainWindowHandle
        if ($size -eq "maximized") {
            $window = $UIA::FromHandle($handle)
            $pattern = $null
            if ($window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref] $pattern)) {
                $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
            }
        }
        [LayoutInput]::SetForegroundWindow($handle) | Out-Null
        $root = $UIA::FromHandle($handle)
        $title = $spec.Title
        $run = @{ Name = $name; Root = $root; Title = $title; Reference = $null; Scroll = $null }

        # Wait for the page and for what it loads (catalog, this PC, drives).
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Get-Title $root $title) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
        Start-Sleep -Seconds 5
        $run.Scroll = Get-PageScroll $run
        $run.Reference = Measure-Page $run
        if (-not $run.Reference) { throw "'$title' was not displayed." }
        $window = $root.Current.BoundingRectangle
        Write-Host ("  window {0}x{1}, title x={2} width={3}, centered at x={4}" -f [math]::Round($window.Width), [math]::Round($window.Height), $run.Reference.Left, $run.Reference.Width, $run.Reference.Expected)
        if ([math]::Abs($run.Reference.Left - $run.Reference.Expected) -gt 2) {
            Write-Host "  MOVED  the page is not centered"
            $script:moves += "${name}: not centered (x $($run.Reference.Left), expected $($run.Reference.Expected))"
        }
        Write-Layout $run

        # The keyboard: every Tab brings the focused control into view.
        for ($i = 1; $i -le 30; $i++) {
            [LayoutInput]::Key($VkTab)
            $focused = try { $UIA::FocusedElement.Current } catch { $null }
            $label = if ($focused) { "$($focused.ControlType.ProgrammaticName -replace 'ControlType\.', '') '$(Get-Short $focused.Name)'" } else { "?" }
            Test-Step $run "Tab $i -> $label"
        }

        # The lists: open each one with the mouse and pick another entry.
        $combos = @(Find-All $root (New-Condition $UIA::ControlTypeProperty $Types::ComboBox))
        foreach ($combo in $combos) {
            Show-Element $combo
            $box = $combo.Current.BoundingRectangle
            if ($box.IsEmpty -or $combo.Current.IsOffscreen -or -not $combo.Current.IsEnabled) { continue }
            $label = Get-Short $combo.Current.Name
            [LayoutInput]::Click([int]($box.Left + $box.Width / 2), [int]($box.Top + $box.Height / 2))
            Start-Sleep -Milliseconds 800
            Test-Step $run "open list '$label'"
            $items = @(Find-All $combo (New-Condition $UIA::ControlTypeProperty $Types::ListItem))
            $choice = $items | Where-Object { -not $_.Current.IsOffscreen } | Select-Object -Last 1
            if ($choice) {
                $itemBox = $choice.Current.BoundingRectangle
                $itemLabel = Get-Short $choice.Current.Name
                [LayoutInput]::Click([int]($itemBox.Left + $itemBox.Width / 2), [int]($itemBox.Top + $itemBox.Height / 2))
                Start-Sleep -Milliseconds 1500
                Test-Step $run "pick '$itemLabel' in '$label'"
            }
            else {
                [LayoutInput]::Key($VkEscape)
                Test-Step $run "close list '$label' (no entry)"
            }
        }

        # The answers of the questionnaire: the first one of each question.
        if ($spec.Answers) {
            foreach ($group in @(Find-All $root (New-Condition $UIA::ClassNameProperty "Microsoft.UI.Xaml.Controls.RadioButtons"))) {
                $answer = $group.FindFirst($Scope, (New-Condition $UIA::ControlTypeProperty $Types::RadioButton))
                if (-not $answer) { continue }
                Show-Element $answer
                $box = $answer.Current.BoundingRectangle
                if ($box.IsEmpty) { continue }
                [LayoutInput]::Click([int]($box.Left + 10), [int]($box.Top + $box.Height / 2))
                Test-Step $run "answer '$(Get-Short $answer.Current.Name)'"
            }
        }

        # The buttons that only show something on the page.
        foreach ($label in @($spec.Buttons)) {
            if (-not $label) { continue }
            $button = $root.FindFirst($Scope, (New-Condition $UIA::NameProperty $label))
            if (-not $button) { Write-Host "  ?      button '$label' not found"; continue }
            Show-Element $button
            $box = $button.Current.BoundingRectangle
            [LayoutInput]::Click([int]($box.Left + $box.Width / 2), [int]($box.Top + $box.Height / 2))
            Start-Sleep -Seconds 2
            Test-Step $run "click '$label'"
        }

        # The expanders: open each one by clicking its header.
        $expanders = @(Find-All $root (New-Condition $UIA::ClassNameProperty "Microsoft.UI.Xaml.Controls.Expander"))
        foreach ($expander in $expanders) {
            Show-Element $expander
            $box = $expander.Current.BoundingRectangle
            if ($box.IsEmpty) { continue }
            $label = Get-Short $expander.Current.Name
            [LayoutInput]::Click([int]($box.Left + 60), [int]($box.Top + 20))
            Start-Sleep -Milliseconds 800
            Test-Step $run "expand '$label'"
        }

        Write-Layout $run
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

$moves = @()
$pages = @(
    @{ Page = "Readiness"; Title = "Préparer mon PC"; Buttons = @("Afficher les réseaux enregistrés", "Vérifier à nouveau") },
    @{ Page = "Troubleshoot"; Title = "Dépannage" },
    @{ Page = "Advisor"; Title = "Quelle distribution choisir ?"; Answers = $true; Buttons = @("Voir mon conseil") },
    @{ Page = "Guide"; Title = "Guide Linux" },
    @{ Page = "Software"; Title = "Mes logiciels sous Linux" },
    @{ Page = "Settings"; Title = "Paramètres" },
    @{ Page = "Distros"; Title = "Choisissez une distribution Linux" },
    @{ Page = "LocalIso"; Title = "Utiliser une image ISO locale" },
    @{ Page = "Restore"; Title = "Restaurer une clé USB" },
    @{ Page = "Welcome"; Title = "Bienvenue" },
    @{ Page = "Advisor"; Guidance = "alone"; Title = "Quelle distribution choisir ?"; Answers = $true; Buttons = @("Voir mon conseil"); Maximized = $true },
    @{ Page = "Distros"; Guidance = "alone"; Advanced = $true; Title = "Choisissez une distribution Linux" }
)

# The runner's screen is 1024x768: the window would not get its usual size, nor be larger than the content.
$resolution = [LayoutInput]::SetResolution(1920, 1080)
Write-Host "Screen resolution 1920x1080: $(if ($resolution -eq 0) { 'set' } else { "not set ($resolution)" })"

foreach ($page in $pages) { Test-Page $page "default size" }

# A large window: the content is centered and must stay so (the questionnaire also with its "on my own" explanations).
foreach ($page in @($pages | Select-Object -First 3) + @($pages | Where-Object { $_.Maximized })) { Test-Page $page "maximized" }

if ($moves.Count -gt 0) {
    Write-Host "Pages that moved:"
    $moves | ForEach-Object { Write-Host "  $_" }
    if ($Strict) { throw "$($moves.Count) step(s) moved a page sideways." }
}
else {
    Write-Host "Layout test passed: no page moved."
}
