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

# --- Build step ---
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
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) { exit $buildExit }

# --- Determine repo root robustly ---
$scriptPath = $PSCommandPath
if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrEmpty($scriptPath)) { throw "Cannot resolve script path. Run with: pwsh -File .\scripts\update.ps1" }
$scriptDir = Split-Path -Path $scriptPath -Parent
$repoRoot  = Split-Path -Path $scriptDir -Parent

# --- Run manage_subtrees.py ---
$pythonCandidates = @('python', 'py')
$pyExit = $null
$subtreeScript = Join-Path $repoRoot 'scripts/manage_subtrees.py'
if (-not (Test-Path $subtreeScript)) {
    throw "scripts/manage_subtrees.py not found at repo root: $subtreeScript"
}

foreach ($pc in $pythonCandidates) {
    try {
        & $pc $subtreeScript
        $pyExit = $LASTEXITCODE
        if ($pyExit -eq 0) { break }
    } catch {
        # try next candidate
    }
}
if (($pyExit -ne 0) -and ($pyExit -ne $null)) {
    Write-Error "manage_subtrees.py failed with exit code $pyExit"
    exit $pyExit
}

# --- Interactive update of NewVersion based on LatestCsmTmpeSyncReleaseTag ---
$modMetadataPath = Join-Path $repoRoot 'src/CSM.TmpeSync/Mod/ModMetadata.cs'

# wait up to 5s if file was just created by the Python script
$maxWaitMs = 5000
$stepMs = 200
$waited = 0
while (-not (Test-Path $modMetadataPath) -and ($waited -lt $maxWaitMs)) {
    Start-Sleep -Milliseconds $stepMs
    $waited += $stepMs
}

if (-not (Test-Path $modMetadataPath)) {
    throw "File not found: $modMetadataPath"
}

# Read file
$content = Get-Content -Path $modMetadataPath -Raw -Encoding UTF8

# Extract current LatestCsmTmpeSyncReleaseTag
$tagMatch = [regex]::Match($content, 'internal\s+const\s+string\s+LatestCsmTmpeSyncReleaseTag\s*=\s*"([^"]+)"\s*;')
$currentTag = if ($tagMatch.Success) { $tagMatch.Groups[1].Value } else { "<unknown>" }

Write-Host "LatestCsmTmpeSyncReleaseTag: $currentTag"
$change = Read-Host "Do you want to change the constant 'NewVersion'? (yes/no)"

if ($change -match '^(?i)y(es)?$') {
    $newVersion = Read-Host "Enter new string for 'NewVersion' (leave empty to keep current)"
    if (-not [string]::IsNullOrWhiteSpace($newVersion)) {
        # Replace NewVersion
        $newContent = [regex]::Replace(
            $content,
            '(?m)^\s*internal\s+const\s+string\s+NewVersion\s*=\s*"[^"]*"\s*;',
            ('        internal const string NewVersion = "' + ($newVersion -replace '"','\"') + '";')
        )

        if ($newContent -ne $content) {
            Set-Content -Path $modMetadataPath -Value $newContent -Encoding UTF8
            Write-Host "NewVersion updated to: $newVersion"
        } else {
            Write-Host "No match found for NewVersion. File unchanged."
        }
    } else {
        Write-Host "Empty input. NewVersion remains unchanged."
    }
} else {
    Write-Host "No change requested for NewVersion."
}

exit 0
