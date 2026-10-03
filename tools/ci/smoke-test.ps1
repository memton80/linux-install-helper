<#
.SYNOPSIS
    Starts the published application on several pages, checks that it does not crash and takes screenshots.
.PARAMETER Exe
    Path of LinuxInstallHelper.exe.
.PARAMETER Output
    Folder for the screenshots.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Output = "screenshots"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Add-Type -AssemblyName System.Drawing
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

$runs = @(
    @{ Page = "Distros";  Theme = "light"; Lang = "fr-FR" },
    @{ Page = "Distros";  Theme = "dark";  Lang = "en-US" },
    @{ Page = "LocalIso"; Theme = "light"; Lang = "fr-FR" },
    @{ Page = "Restore";  Theme = "light"; Lang = "fr-FR" },
    @{ Page = "Settings"; Theme = "dark";  Lang = "fr-FR" },
    @{ Page = "About";    Theme = "light"; Lang = "en-US" }
)

foreach ($run in $runs) {
    $name = "$($run.Page)-$($run.Theme)-$($run.Lang)".ToLowerInvariant()
    Write-Host "Starting $name"
    $process = Start-Process -FilePath $Exe -ArgumentList "--page", $run.Page, "--theme", $run.Theme, "--lang", $run.Lang -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "The application exited during startup (code $($process.ExitCode))." }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)

    # Let the catalog load and the page settle.
    Start-Sleep -Seconds 8
    $process.Refresh()
    if ($process.HasExited) { throw "The application crashed on $name (code $($process.ExitCode))." }

    Save-WindowScreenshot $process (Join-Path $Output "$name.png")
    Stop-Process -Id $process.Id -Force
    Start-Sleep -Seconds 2
}

Write-Host "Smoke test passed: the application started on every page."
