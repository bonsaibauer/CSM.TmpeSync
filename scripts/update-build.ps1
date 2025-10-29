#!/bin/pwsh

function Update-Dependencies {
    param([hashtable]$Profile)

    $gameDir = [string]$Profile.GameDirectory
    Ensure-DirectoryExists -Path $gameDir -Description "Cities: Skylines game directory"

    $managedSource = Join-Path (Join-Path $gameDir 'Cities_Data') 'Managed'
    Ensure-DirectoryExists -Path $managedSource -Description "Cities: Skylines managed assemblies"

    foreach ($file in @('ICities.dll', 'ColossalManaged.dll', 'UnityEngine.dll', 'UnityEngine.UI.dll', 'Assembly-CSharp.dll')) {
        $sourcePath = Join-Path $managedSource $file
        if (-not (Test-Path $sourcePath)) {
            throw "Missing required Cities: Skylines assembly: $sourcePath"
        }
    }

    $harmonySource = if ($Profile.ContainsKey('HarmonySourceDir')) { [string]$Profile.HarmonySourceDir } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($harmonySource)) {
        Copy-DirectoryContents -Source $harmonySource -Destination (Join-Path $script:LibRoot 'Harmony')
    }

    $csmSource = if ($Profile.ContainsKey('CsmSourceDir')) { [string]$Profile.CsmSourceDir } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($csmSource)) {
        Copy-DirectoryContents -Source $csmSource -Destination (Join-Path $script:LibRoot 'CSM')
    }

    $tmpeSource = if ($Profile.ContainsKey('TmpeSourceDir')) { [string]$Profile.TmpeSourceDir } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($tmpeSource)) {
        Copy-DirectoryContents -Source $tmpeSource -Destination (Join-Path $script:LibRoot 'TMPE')
    }

    $csmLibDir = Join-Path $script:LibRoot 'CSM'
    $csmApiPath = Join-Path $csmLibDir 'CSM.API.dll'
    if (-not (Test-Path $csmApiPath)) {
        $fallbackManaged = Join-Path (Join-Path $csmLibDir 'Cities_Data') 'Managed'
        $fallbackPath = Join-Path $fallbackManaged 'CSM.API.dll'
        if (Test-Path $fallbackPath) {
            Copy-Item -LiteralPath $fallbackPath -Destination $csmApiPath -Force
        }
    }

    if (-not (Test-Path $csmApiPath)) {
        throw "CSM.API.dll not found after dependency sync: $csmApiPath"
    }

    Set-ProfileValue -Profile $Profile -Key 'CsmApiDllPath' -Value $csmApiPath
    Set-ProfileValue -Profile $Profile -Key 'HarmonyDllDir' -Value (Join-Path $script:LibRoot 'Harmony')
    Set-ProfileValue -Profile $Profile -Key 'TmpeDir' -Value (Join-Path $script:LibRoot 'TMPE')
    Set-ProfileValue -Profile $Profile -Key 'CsmLibDir' -Value $csmLibDir

    Write-Host "[CSM.TmpeSync] Dependencies copied into lib/." -ForegroundColor Cyan
}
