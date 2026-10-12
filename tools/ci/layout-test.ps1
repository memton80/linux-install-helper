<#
.SYNOPSIS
    Uses the pages like a person would (Tab key, lists, expanders) and checks that they never move sideways.
.DESCRIPTION
    The title of a page must keep its position and its width whatever is focused, selected or expanded: when it
    changes, the whole page jumps from left to right. The page must not scroll horizontally either.
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
}
"@
[LayoutInput]::SetProcessDPIAware() | Out-Null

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]::Descendants
$Any = [System.Windows.Automation.Condition]::TrueCondition
$Types = [System.Windows.Automation.ControlType]
$VkTab = 0x09
$VkEscape = 0x1B

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

function Measure-Page($run) {
    $title = Get-Title $run.Root $run.Title
    if (-not $title) { return $null }
    $rect = $title.Current.BoundingRectangle
    $scrollable = $false
    $percent = 0
    foreach ($viewer in Find-All $run.Root (New-Condition $UIA::ClassNameProperty "ScrollViewer")) {
        $box = $viewer.Current.BoundingRectangle
        if ($box.Left -le $rect.Left -and $box.Right -ge $rect.Left + 1 -and $box.Top -le $rect.Top -and $box.Bottom -ge $rect.Top + 1) {
            $pattern = $null
            if ($viewer.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pattern)) {
                $scrollable = $pattern.Current.HorizontallyScrollable
                $percent = [math]::Max($pattern.Current.HorizontalScrollPercent, 0)
            }
        }
    }

    [pscustomobject]@{ Left = [math]::Round($rect.Left); Width = [math]::Round($rect.Width); Scrollable = $scrollable; Percent = [math]::Round($percent) }
}

function Test-Step($run, [string] $what) {
    Start-Sleep -Milliseconds 600
    $now = Measure-Page $run
    if (-not $now) {
        Write-Host "  ?      $what (title not found)"
        return
    }

    $ref = $run.Reference
    $line = "{0,-70} x={1} width={2} hscroll={3} {4}%" -f $what, $now.Left, $now.Width, $now.Scrollable, $now.Percent
    if ([math]::Abs($now.Left - $ref.Left) -gt 1 -or [math]::Abs($now.Width - $ref.Width) -gt 1 -or $now.Percent -gt 0) {
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

function Start-App($page) {
    $info = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $Exe
    foreach ($argument in @("--page", $page, "--theme", "light", "--lang", "fr-FR")) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "The application exited during startup (code $($process.ExitCode))." }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    $process
}

function Test-Page([string] $page, [string] $title, [int] $width = 0, [int] $height = 0) {
    $name = if ($width) { "$page ${width}x$height" } else { $page }
    Write-Host "== $name"
    $process = Start-App $page
    try {
        $handle = $process.MainWindowHandle
        if ($width) { [LayoutInput]::MoveWindow($handle, 0, 0, $width, $height, $true) | Out-Null }
        [LayoutInput]::SetForegroundWindow($handle) | Out-Null
        $root = $UIA::FromHandle($handle)
        $run = @{ Name = $name; Root = $root; Title = $title; Reference = $null }

        # Wait for the page and for what it loads (catalog, this PC, drives).
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Get-Title $root $title) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
        Start-Sleep -Seconds 5
        $run.Reference = Measure-Page $run
        if (-not $run.Reference) { throw "'$title' was not displayed." }
        $window = $root.Current.BoundingRectangle
        Write-Host ("  window {0}x{1}, title x={2} width={3}" -f [math]::Round($window.Width), [math]::Round($window.Height), $run.Reference.Left, $run.Reference.Width)
        Write-Layout $run

        # The keyboard: every Tab brings the focused control into view.
        for ($i = 1; $i -le 45; $i++) {
            [LayoutInput]::Key($VkTab)
            $focused = try { $UIA::FocusedElement.Current } catch { $null }
            $label = if ($focused) { "$($focused.ControlType.ProgrammaticName -replace 'ControlType\.', '') '$(Get-Short $focused.Name)'" } else { "?" }
            Test-Step $run "Tab $i -> $label"
        }

        # The lists: open each one with the mouse and pick another entry.
        $combos = @(Find-All $root (New-Condition $UIA::ControlTypeProperty $Types::ComboBox))
        foreach ($combo in $combos) {
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

        # The expanders: open each one by clicking its header.
        $expanders = @(Find-All $root (New-Condition $UIA::ClassNameProperty "Expander"))
        foreach ($expander in $expanders | Select-Object -First 4) {
            $box = $expander.Current.BoundingRectangle
            if ($box.IsEmpty -or $expander.Current.IsOffscreen) { continue }
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
    @{ Page = "Readiness"; Title = "Préparer mon PC" },
    @{ Page = "Troubleshoot"; Title = "Dépannage" },
    @{ Page = "Guide"; Title = "Guide Linux" },
    @{ Page = "Software"; Title = "Mes logiciels sous Linux" },
    @{ Page = "Settings"; Title = "Paramètres" },
    @{ Page = "Advisor"; Title = "Quelle distribution choisir ?" },
    @{ Page = "Distros"; Title = "Choisissez une distribution Linux" },
    @{ Page = "LocalIso"; Title = "Utiliser une image ISO locale" },
    @{ Page = "Restore"; Title = "Restaurer une clé USB" }
)

foreach ($page in $pages) { Test-Page $page.Page $page.Title }

# A large window: the content is centered and must stay so.
Test-Page "Readiness" "Préparer mon PC" 1700 1000
Test-Page "Troubleshoot" "Dépannage" 1700 1000

if ($moves.Count -gt 0) {
    Write-Host "Pages that moved:"
    $moves | ForEach-Object { Write-Host "  $_" }
    if ($Strict) { throw "$($moves.Count) step(s) moved a page sideways." }
}
else {
    Write-Host "Layout test passed: no page moved."
}
