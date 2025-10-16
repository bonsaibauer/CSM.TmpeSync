#!/bin/pwsh
param(
    [switch]$Update = $false,
    [switch]$Build = $false,
    [switch]$Install = $false,
    [string]$Configuration = "Release",
    [string]$GameDirectory = "",
    [string]$ModDirectory = "Default",
    [string]$CsmModDirectory = "Default",
    [string]$CitiesSkylinesDir = "",
    [string]$HarmonyDllDir = "",
    [string]$CsmApiDllPath = "",
    [string]$TmpeDir = "",
    [string]$SteamModsDir = "",
    [switch]$SkipCsmUpdate = $false,
    [switch]$SkipCsmBuild = $false,
    [switch]$SkipCsmInstall = $false
)

$ErrorActionPreference = "Stop"

$HostIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$HostIsLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
$HostIsMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot = Split-Path -Parent $ScriptDir
$CsmScriptsDir = Join-Path $RepoRoot "submodules\CSM\scripts"
$CsmBuildScript = Join-Path $CsmScriptsDir "build.ps1"
$ProjectPath = Join-Path $RepoRoot "src\CSM.TmpeSync\CSM.TmpeSync.csproj"
$OutputDir = Join-Path $RepoRoot ("src\CSM.TmpeSync\bin\{0}\net35" -f $Configuration)

if (-not (Test-Path $ProjectPath)) {
    throw "CSM.TmpeSync project not found under $ProjectPath."
}

if (-not ($Update -or $Build -or $Install)) {
    Write-Host "[CSM.TmpeSync] No action specified. Use -Update, -Build and/or -Install." -ForegroundColor Yellow
    return
}

function Get-DefaultModDirectory {
    param([string]$ModName)

    if ($HostIsLinux) {
        return "~/.local/share/Colossal Order/Cities_Skylines/Addons/Mods/$ModName"
    }

    if ($HostIsMacOS) {
        return "~/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/$ModName"
    }

    return "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\$ModName"
}

if ($ModDirectory -eq "Default") {
    $ModDirectory = Get-DefaultModDirectory -ModName "CSM.TmpeSync"
}

if ($CsmModDirectory -eq "Default") {
    $CsmModDirectory = Get-DefaultModDirectory -ModName "CSM"
}

$script:PwshPath = $null
function Resolve-PwshPath {
    if ($script:PwshPath) {
        return $script:PwshPath
    }

    $candidate = Get-Command "pwsh" -ErrorAction SilentlyContinue
    if (-not $candidate -and $HostIsWindows) {
        $candidate = Get-Command "pwsh.exe" -ErrorAction SilentlyContinue
    }

    if (-not $candidate) {
        throw "Unable to locate pwsh executable required for invoking the CSM build script."
    }

    $script:PwshPath = $candidate.Source
    return $script:PwshPath
}

function Invoke-CsmScript {
    param([string[]]$Arguments)

    if (-not (Test-Path $CsmBuildScript)) {
        throw "CSM build script is missing at $CsmBuildScript. Make sure the submodule is initialised."
    }

    $pwshExecutable = Resolve-PwshPath
    $pwshArguments = @("-NoLogo", "-NoProfile", "-WorkingDirectory", $CsmScriptsDir, "-File", "build.ps1") + $Arguments

    Write-Host "[CSM.TmpeSync] -> CSM build.ps1 $($Arguments -join ' ')" -ForegroundColor DarkCyan
    & $pwshExecutable @($pwshArguments)

    if ($LASTEXITCODE -ne 0) {
        throw "CSM build script failed with exit code $LASTEXITCODE."
    }
}

function Build-PropertyArgument {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $escaped = $Value.Replace('"', '\"')
    return "-p:$Name=`"$escaped`""
}

if ($Update) {
    if ($SkipCsmUpdate) {
        Write-Host "[CSM.TmpeSync] Skipping CSM update step (SkipCsmUpdate)." -ForegroundColor DarkYellow
    }
    else {
        $arguments = @("-Update")
        if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
            $arguments += @("-GameDirectory", $GameDirectory)
        }

        Invoke-CsmScript -Arguments $arguments
    }
}

if ($Build) {
    if ($SkipCsmBuild) {
        Write-Host "[CSM.TmpeSync] Skipping CSM build step (SkipCsmBuild)." -ForegroundColor DarkYellow
    }
    else {
        $csmOutputDir = "..\src\csm\bin\$Configuration"
        $arguments = @("-Build", "-OutputDirectory", $csmOutputDir)
        Invoke-CsmScript -Arguments $arguments
    }

    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        throw "dotnet CLI not found. Install the .NET SDK to build CSM.TmpeSync."
    }

    $dotnetArguments = @("build", $ProjectPath, "-c", $Configuration, "/restore", "--nologo")

    foreach ($property in @(
        Build-PropertyArgument -Name "CitiesSkylinesDir" -Value $CitiesSkylinesDir
        Build-PropertyArgument -Name "HarmonyDllDir" -Value $HarmonyDllDir
        Build-PropertyArgument -Name "CsmApiDllPath" -Value $CsmApiDllPath
        Build-PropertyArgument -Name "TmpeDir" -Value $TmpeDir
        Build-PropertyArgument -Name "SteamModsDir" -Value $SteamModsDir
        Build-PropertyArgument -Name "ModDirectory" -Value $ModDirectory
        Build-PropertyArgument -Name "ModsOutDir" -Value $ModDirectory
    )) {
        if ($property) {
            $dotnetArguments += $property
        }
    }

    Write-Host "[CSM.TmpeSync] Building add-on (Configuration=$Configuration)." -ForegroundColor Cyan
    & dotnet @($dotnetArguments)

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

if ($Install) {
    if ($SkipCsmInstall) {
        Write-Host "[CSM.TmpeSync] Skipping CSM install step (SkipCsmInstall)." -ForegroundColor DarkYellow
    }
    else {
        $arguments = @("-Install", "-ModDirectory", $CsmModDirectory)
        Invoke-CsmScript -Arguments $arguments
    }

    if (-not (Test-Path $OutputDir)) {
        throw "Build output directory $OutputDir not found. Run with -Build first."
    }

    $assemblies = Get-ChildItem -Path $OutputDir -Filter "CSM.TmpeSync*.dll" -ErrorAction Stop
    if (-not $assemblies) {
        throw "No CSM.TmpeSync assemblies were produced under $OutputDir."
    }

    Write-Host "[CSM.TmpeSync] Installing add-on to $ModDirectory." -ForegroundColor Cyan
    Remove-Item -Path $ModDirectory -Recurse -ErrorAction Ignore
    New-Item -ItemType Directory -Path $ModDirectory -Force | Out-Null

    foreach ($assembly in $assemblies) {
        Copy-Item -Path $assembly.FullName -Destination $ModDirectory -Force
    }

    $pdbFiles = Get-ChildItem -Path $OutputDir -Filter "CSM.TmpeSync*.pdb" -ErrorAction SilentlyContinue
    foreach ($pdb in $pdbFiles) {
        Copy-Item -Path $pdb.FullName -Destination $ModDirectory -Force
    }

    Write-Host "[CSM.TmpeSync] Install completed." -ForegroundColor Green
}

Write-Host "[CSM.TmpeSync] Done." -ForegroundColor Green
