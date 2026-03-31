#!/bin/pwsh

function Invoke-InstallMod {
    param(
        [hashtable]$Profile,
        [string]$Configuration,
        [string]$OverrideModDirectory
    )

    $repoRoot = if (Get-Variable -Name RepoRoot -Scope Script -ErrorAction SilentlyContinue) { $script:RepoRoot } else { Split-Path $PSScriptRoot -Parent }
    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        throw "Repository root could not be resolved."
    }

    $targetDirectory = if (-not [string]::IsNullOrWhiteSpace($OverrideModDirectory)) { $OverrideModDirectory } elseif ($Profile.ContainsKey('ModDirectory')) { [string]$Profile.ModDirectory } else { '' }

    if ([string]::IsNullOrWhiteSpace($targetDirectory)) {
        throw "No mod installation directory configured. Run build.ps1 -Configure or pass -ModDirectory."
    }

    $targetDirectory = Resolve-ModDirectoryForConfiguration -BaseDirectory $targetDirectory -Configuration $Configuration

    $outputDir = Get-OutputDirectory -Configuration $Configuration
    Ensure-DirectoryExists -Path $outputDir -Description "Build output"

    $assemblies = Get-ChildItem -LiteralPath $outputDir -Filter 'CSM.TmpeSync*.dll' -ErrorAction Stop
    if (-not $assemblies) {
        throw "No CSM.TmpeSync assemblies found in $outputDir. Build the project first."
    }

    $expectedAssemblies = Get-ModAssemblyNames
    if ($expectedAssemblies -and $expectedAssemblies.Count -gt 0) {
        $presentAssemblyNames = $assemblies | ForEach-Object { $_.Name }
        $missingAssemblies = @()

        foreach ($expected in $expectedAssemblies) {
            if ($presentAssemblyNames -notcontains $expected) {
                $candidate = Join-Path $outputDir $expected
                if (-not (Test-Path $candidate)) {
                    $missingAssemblies += $expected
                }
            }
        }

        if ($missingAssemblies.Count -gt 0) {
            throw "Missing expected assemblies in ${outputDir}: $($missingAssemblies -join ', '). Ensure the build completed successfully."
        }

        $sortedAssemblies = $presentAssemblyNames | Sort-Object -Unique
        Write-Host "[CSM.TmpeSync] Installing assemblies: $($sortedAssemblies -join ', ')" -ForegroundColor Cyan
    }

    Write-Host "[CSM.TmpeSync] Installing build to $targetDirectory" -ForegroundColor Cyan
    Copy-DirectoryContents -Source $outputDir -Destination $targetDirectory -ExcludeExtensions '.pdb'

    $outputRoot = Join-Path $repoRoot "output"
    if (-not (Test-Path $outputRoot)) {
        New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    }

    $mirrorDestination = Join-Path $outputRoot (Split-Path $targetDirectory -Leaf)
    Write-Host "[CSM.TmpeSync] Mirroring build to $mirrorDestination" -ForegroundColor Cyan
    Copy-DirectoryContents -Source $outputDir -Destination $mirrorDestination -ExcludeExtensions '.pdb'

    Write-Host "[CSM.TmpeSync] Installation complete." -ForegroundColor Green
}
