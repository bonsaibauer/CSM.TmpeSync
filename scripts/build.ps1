#!/bin/pwsh
param(
    [switch]$Update = $false,
    [switch]$Build = $false,
    [switch]$Install = $false,
    [switch]$Configure = $false,
    [switch]$CSM = $false,
    [string]$Configuration = "Release",
    [string]$GameDirectory = "",
    [string]$CitiesSkylinesDir = "",
    [string]$HarmonyDllDir = "",
    [string]$CsmApiDllPath = "",
    [string]$TmpeDir = "",
    [string]$SteamModsDir = "",
    [string]$ModDirectory = "Default",
    [string]$CsmModDirectory = "Default",
    [switch]$SkipCsmUpdate = $false,
    [switch]$SkipCsmBuild = $false,
    [switch]$SkipCsmInstall = $false
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\common.ps1"

$projectPath = Get-ProjectPath
if (-not (Test-Path $projectPath)) {
    throw "CSM.TmpeSync project not found under $projectPath."
}

if (-not ($Update -or $Build -or $Install -or $Configure)) {
    Write-Host "[CSM.TmpeSync] No action specified. Use -Update, -Build, -Install and/or -Configure." -ForegroundColor Yellow
    return
}

try {
    if (-not $CSM) {
        if ($SkipCsmUpdate -or $SkipCsmBuild -or $SkipCsmInstall) {
            Write-Host "[CSM.TmpeSync] Ignoring -SkipCsm* flags because -CSM is not specified." -ForegroundColor DarkYellow
        }
    }

    if ($Configure) {
        Configure-Interactive
        if (-not ($Update -or $Build -or $Install)) {
            return
        }
    }

    $modDirectoryParameter = if ($ModDirectory -eq "Default") { "" } else { $ModDirectory }
    $resolvedModDirectory = Get-ConfigValue -Key "ModDirectory" -ParameterValue $modDirectoryParameter
    if (-not [string]::IsNullOrWhiteSpace($resolvedModDirectory)) {
        $ModDirectory = $resolvedModDirectory
    }
    elseif ($ModDirectory -eq "Default") {
        $ModDirectory = Get-DefaultModDirectory -ModName "CSM.TmpeSync"
    }

    if ($Install -and $CSM) {
        $csmModDirectoryParameter = if ($CsmModDirectory -eq "Default") { "" } else { $CsmModDirectory }
        $resolvedCsmModDir = Get-ConfigValue -Key "CsmModDirectory" -ParameterValue $csmModDirectoryParameter
        if (-not [string]::IsNullOrWhiteSpace($resolvedCsmModDir)) {
            $CsmModDirectory = $resolvedCsmModDir
        }
        elseif ($CsmModDirectory -eq "Default") {
            $CsmModDirectory = Get-DefaultModDirectory -ModName "CSM"
        }
    }
    elseif (-not $CSM) {
        $CsmModDirectory = ""
    }

    if ($Update) {
        Invoke-TmpeSyncUpdate -CitiesSkylinesDir $CitiesSkylinesDir -GameDirectory $GameDirectory -HarmonyDllDir $HarmonyDllDir -TmpeDir $TmpeDir -SteamModsDir $SteamModsDir -IncludeCsm:$CSM -SkipCsmUpdate:$SkipCsmUpdate | Out-Null
    }

    if ($Build) {
        Invoke-TmpeSyncBuild -Configuration $Configuration -CitiesSkylinesDir $CitiesSkylinesDir -HarmonyDllDir $HarmonyDllDir -CsmApiDllPath $CsmApiDllPath -TmpeDir $TmpeDir -SteamModsDir $SteamModsDir -ModDirectory $ModDirectory -IncludeCsm:$CSM -SkipCsmBuild:$SkipCsmBuild
    }

    if ($Install) {
        Invoke-TmpeSyncInstall -Configuration $Configuration -ModDirectory $ModDirectory -IncludeCsm:$CSM -SkipCsmInstall:$SkipCsmInstall -CsmModDirectory $CsmModDirectory
    }
}
finally {
    Save-BuildConfig
}

Write-Host "[CSM.TmpeSync] Done." -ForegroundColor Green
