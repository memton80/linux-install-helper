<#
.SYNOPSIS
    Attaches two small virtual disks for the raw write tests and gives their numbers to the next steps:
    LIH_TEST_BLANK_DISK has no partition table (like a drive whose layout was deleted) and
    LIH_TEST_MOUNTED_DISK has a FAT32 volume with a drive letter (like a drive used as is).
.PARAMETER Folder
    Folder for the .vhdx files.
#>
param(
    [string] $Folder = (Join-Path ([System.IO.Path]::GetTempPath()) "lih-vdisks")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Folder | Out-Null
$blank = Join-Path $Folder "blank.vhdx"
$mounted = Join-Path $Folder "mounted.vhdx"
$script = Join-Path $Folder "diskpart.txt"

@"
create vdisk file="$blank" maximum=256 type=expandable
attach vdisk
create vdisk file="$mounted" maximum=256 type=expandable
attach vdisk
"@ | Set-Content -Path $script -Encoding ascii

diskpart /s $script
if ($LASTEXITCODE -ne 0) { throw "diskpart failed with code $LASTEXITCODE." }

function Get-AttachedDisk([string] $path) {
    $disk = Get-DiskImage -ImagePath $path | Get-Disk
    if ($null -eq $disk) { throw "The disk of $path was not found." }
    if ($disk.IsOffline) { Set-Disk -Number $disk.Number -IsOffline $false }
    if ($disk.IsReadOnly) { Set-Disk -Number $disk.Number -IsReadOnly $false }
    return $disk
}

$blankDisk = Get-AttachedDisk $blank
$mountedDisk = Get-AttachedDisk $mounted
Initialize-Disk -Number $mountedDisk.Number -PartitionStyle MBR
New-Partition -DiskNumber $mountedDisk.Number -UseMaximumSize -AssignDriveLetter |
    Format-Volume -FileSystem FAT32 -NewFileSystemLabel LIHTEST -Confirm:$false | Out-Null

Get-Disk | Format-Table Number, FriendlyName, BusType, PartitionStyle, Size, IsOffline, IsReadOnly -AutoSize | Out-String | Write-Host
Get-Partition -DiskNumber $mountedDisk.Number | Format-Table PartitionNumber, DriveLetter, Offset, Size -AutoSize | Out-String | Write-Host

"LIH_TEST_BLANK_DISK=$($blankDisk.Number)" | Add-Content -Path $env:GITHUB_ENV
"LIH_TEST_MOUNTED_DISK=$($mountedDisk.Number)" | Add-Content -Path $env:GITHUB_ENV
Write-Host "Blank disk: $($blankDisk.Number), disk with a mounted volume: $($mountedDisk.Number)"
