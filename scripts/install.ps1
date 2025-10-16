#!/bin/pwsh
param([string]$ModDirectory = "Default")

$ErrorActionPreference = "Stop"

$HostIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$HostIsLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
$HostIsMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

function Get-DefaultModDirectory {
    if ($HostIsLinux) {
        return "~/.local/share/Colossal Order/Cities_Skylines/Addons/Mods/CSM.TmpeSync"
    }

    if ($HostIsMacOS) {
        return "~/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/CSM.TmpeSync"
    }

    return "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync"
}

if ($ModDirectory -eq "Default") {
    $ModDirectory = Get-DefaultModDirectory
}

$assemblies = Get-ChildItem -Path "." -Filter "CSM.TmpeSync*.dll" -ErrorAction Stop
if (-not $assemblies) {
    throw "No CSM.TmpeSync assemblies were found next to install.ps1."
}

Write-Host "[CSM.TmpeSync] Installing release build into $ModDirectory" -ForegroundColor Cyan
Remove-Item -Path $ModDirectory -Recurse -ErrorAction Ignore
New-Item -ItemType Directory -Path $ModDirectory -Force | Out-Null

foreach ($assembly in $assemblies) {
    Copy-Item -Path $assembly.FullName -Destination $ModDirectory -Force
}

$pdbFiles = Get-ChildItem -Path "." -Filter "CSM.TmpeSync*.pdb" -ErrorAction SilentlyContinue
foreach ($pdb in $pdbFiles) {
    Copy-Item -Path $pdb.FullName -Destination $ModDirectory -Force
}

Write-Host "[CSM.TmpeSync] Installation complete. Enable the mod inside Cities: Skylines." -ForegroundColor Green
