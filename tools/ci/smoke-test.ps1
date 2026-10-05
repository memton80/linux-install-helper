<#
.SYNOPSIS
    Starts the published application on several pages, checks that it does not crash and takes screenshots.
.PARAMETER Exe
    Path of LinuxInstallHelper.exe.
.PARAMETER Output
    Folder for the screenshots.
.PARAMETER Launcher
    $Exe is the single .exe of a release: it must extract the application and start it (checked on one page).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Output = "screenshots",
    [switch] $Launcher
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Exe = (Resolve-Path $Exe).Path

# The manifest asks for administrator rights; on the CI runner start the app without the UAC prompt.
$env:__COMPAT_LAYER = "RunAsInvoker"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Win32]::SetProcessDPIAware() | Out-Null

function Save-WindowScreenshot([System.Diagnostics.Process] $process, [string] $path) {
    $process.Refresh()
    $handle = $process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw "The application has no main window." }
    [Win32]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 800
    $rect = New-Object Win32+RECT
    [Win32]::GetWindowRect($handle, [ref] $rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

# Waits until an element whose accessible name contains $text is shown in the window.
function Wait-ForText([System.Diagnostics.Process] $process, [string] $text, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    do {
        $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $elements) {
            if ($element.Current.Name -like "*$text*") { return }
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "'$text' was not displayed within $seconds seconds."
}

$runs = @(
    @{ Page = "Advisor";  Theme = "light"; Lang = "fr-FR"; Expect = "Avez-vous déjà utilisé Linux" },
    @{ Page = "Distros";  Theme = "light"; Lang = "fr-FR"; Expect = "Ubuntu" },
    @{ Page = "Distros";  Theme = "dark";  Lang = "en-US"; Expect = "Linux Mint" },
    @{ Page = "DistroDetails"; Theme = "light"; Lang = "fr-FR"; Expect = "Site officiel" },
    @{ Page = "LocalIso"; Theme = "light"; Lang = "fr-FR" },
    @{ Page = "Restore";  Theme = "light"; Lang = "fr-FR" },
    @{ Page = "Guide";    Theme = "dark";  Lang = "fr-FR"; Expect = "Avant de quitter Windows" },
    @{ Page = "Settings"; Theme = "dark";  Lang = "fr-FR"; Expect = "Paramètres" },
    @{ Page = "About";    Theme = "light"; Lang = "en-US"; Expect = "Open source components" }
)

if ($Launcher) { $runs = @($runs[0]) }

foreach ($run in $runs) {
    $name = "$(if ($Launcher) { 'launcher-' })$($run.Page)-$($run.Theme)-$($run.Lang)".ToLowerInvariant()
    Write-Host "Starting $name"
    $info = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $Exe
    foreach ($argument in @("--page", $run.Page, "--theme", $run.Theme, "--lang", $run.Lang)) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    $started = $process

    if ($Launcher) {
        # The launcher extracts the application to %LOCALAPPDATA%\LinuxInstallHelper\app and starts it.
        $extracted = Join-Path $env:LOCALAPPDATA "LinuxInstallHelper\app"
        $deadline = (Get-Date).AddSeconds(120)
        $app = $null
        do {
            Start-Sleep -Seconds 1
            $app = Get-Process -Name LinuxInstallHelper -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -like "$extracted\*" } | Select-Object -First 1
            if (-not $app -and $process.HasExited) { throw "The launcher exited without starting the application (code $($process.ExitCode))." }
        } while (-not $app -and (Get-Date) -lt $deadline)
        if (-not $app) { throw "The launcher did not start the application within 120 seconds." }
        Write-Host "The launcher started $($app.Path)"
        $process = $app
    }

    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "The application exited during startup (code $($process.ExitCode))." }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)

    # The page must show its content (the catalog for the distributions page).
    if ($run.Expect) { Wait-ForText $process $run.Expect }
    Start-Sleep -Seconds 4
    $process.Refresh()
    if ($process.HasExited) { throw "The application crashed on $name (code $($process.ExitCode))." }

    Save-WindowScreenshot $process (Join-Path $Output "$name.png")
    Stop-Process -Id $process.Id -Force
    if ($started.Id -ne $process.Id) { Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}

Write-Host "Smoke test passed: the application started on every page."
