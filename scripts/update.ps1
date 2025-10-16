#!/bin/pwsh
param(
    [switch]$CSM = $false,
    [switch]$Configure = $false,
    [string]$GameDirectory = "",
    [string]$CitiesSkylinesDir = "",
    [string]$HarmonyDllDir = "",
    [string]$TmpeDir = "",
    [string]$SteamModsDir = "",
    [switch]$SkipCsmUpdate = $false
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\common.ps1"

try {
    if ($Configure) {
        Configure-Interactive
    }

    Invoke-TmpeSyncUpdate -CitiesSkylinesDir $CitiesSkylinesDir -GameDirectory $GameDirectory -HarmonyDllDir $HarmonyDllDir -TmpeDir $TmpeDir -SteamModsDir $SteamModsDir -IncludeCsm:$CSM -SkipCsmUpdate:$SkipCsmUpdate | Out-Null
}
finally {
    Save-BuildConfig
}

Write-Host "[CSM.TmpeSync] Update completed." -ForegroundColor Green
