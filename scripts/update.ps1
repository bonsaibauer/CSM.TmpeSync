#!/bin/pwsh
param(
    [switch]$Configure = $false,
    [string]$Profile = "",
    [string]$GameDirectory = "",
    [string]$SteamModsDir = "",
    [string]$HarmonySourceDir = "",
    [string]$CsmSourceDir = "",
    [string]$TmpeSourceDir = "",
    [string]$HarmonyDllDir = "",
    [string]$CsmApiDllPath = "",
    [string]$TmpeDir = "",
    [string]$ModDirectory = "",
    [string]$ModRootDirectory = ""
)

$ErrorActionPreference = "Stop"

$buildScript = Join-Path $PSScriptRoot 'build.ps1'
if (-not (Test-Path $buildScript)) {
    throw "build.ps1 not found next to update.ps1"
}

$arguments = @('-Update')
if ($Configure) { $arguments += '-Configure' }
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $arguments += @('-Profile', $Profile) }
if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) { $arguments += @('-GameDirectory', $GameDirectory) }
if (-not [string]::IsNullOrWhiteSpace($SteamModsDir)) { $arguments += @('-SteamModsDir', $SteamModsDir) }
if (-not [string]::IsNullOrWhiteSpace($HarmonySourceDir)) { $arguments += @('-HarmonySourceDir', $HarmonySourceDir) }
if (-not [string]::IsNullOrWhiteSpace($CsmSourceDir)) { $arguments += @('-CsmSourceDir', $CsmSourceDir) }
if (-not [string]::IsNullOrWhiteSpace($TmpeSourceDir)) { $arguments += @('-TmpeSourceDir', $TmpeSourceDir) }
if (-not [string]::IsNullOrWhiteSpace($HarmonyDllDir)) { $arguments += @('-HarmonyDllDir', $HarmonyDllDir) }
if (-not [string]::IsNullOrWhiteSpace($CsmApiDllPath)) { $arguments += @('-CsmApiDllPath', $CsmApiDllPath) }
if (-not [string]::IsNullOrWhiteSpace($TmpeDir)) { $arguments += @('-TmpeDir', $TmpeDir) }
if (-not [string]::IsNullOrWhiteSpace($ModDirectory)) { $arguments += @('-ModDirectory', $ModDirectory) }
if (-not [string]::IsNullOrWhiteSpace($ModRootDirectory)) { $arguments += @('-ModRootDirectory', $ModRootDirectory) }

& $buildScript @arguments
exit $LASTEXITCODE
