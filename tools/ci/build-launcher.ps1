<#
.SYNOPSIS
    Builds the single .exe of a release: zips the published application and embeds it in the launcher.
.PARAMETER Publish
    Folder produced by "dotnet publish" for the application.
.PARAMETER Rid
    Runtime identifier (win-x64 or win-arm64).
.PARAMETER Output
    Path of the .exe to create.
.PARAMETER Version
    Version of the launcher (the application's own version is already in the published folder).
#>
param(
    [Parameter(Mandatory)] [string] $Publish,
    [Parameter(Mandatory)] [string] $Rid,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot)
$work = Join-Path ([System.IO.Path]::GetTempPath()) "lih-launcher-$Rid"
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null

$zip = Join-Path $work "app.zip"
Compress-Archive -Path (Join-Path (Resolve-Path $Publish) "*") -DestinationPath $zip -CompressionLevel Optimal
$id = (Get-FileHash $zip -Algorithm SHA256).Hash.Substring(0, 16).ToLowerInvariant()
if ($Version) { $id = "$Version-$id" }
"Application zip: $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB, id $id"

$arguments = @(
    "publish", (Join-Path $root "src/LinuxInstallHelper.Launcher/LinuxInstallHelper.Launcher.csproj"),
    "-c", "Release", "-r", $Rid, "-o", (Join-Path $work "out"),
    "-p:AppPayload=$zip", "-p:AppPayloadId=$id"
)
if ($Version) { $arguments += "-p:Version=$Version" }
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "The launcher could not be built." }

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
Copy-Item (Join-Path $work "out/LinuxInstallHelper.Launcher.exe") $Output -Force
"$(Split-Path $Output -Leaf): $([math]::Round((Get-Item $Output).Length / 1MB, 1)) MB, SHA-256 $((Get-FileHash $Output -Algorithm SHA256).Hash)"
