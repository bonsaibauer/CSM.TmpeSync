#!/bin/pwsh
param(
    [switch]$Update = $false,
    [switch]$Build = $false,
    [switch]$Install = $false,
    [switch]$Configure = $false,
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
$LibRoot = Join-Path $RepoRoot "lib"

$ConfigFile = Join-Path $ScriptDir "build-settings.json"
$BuildConfig = @{}
$ConfigUpdated = $false

function Load-BuildConfig {
    if (-not (Test-Path $ConfigFile)) {
        return
    }

    try {
        $json = Get-Content -LiteralPath $ConfigFile -Raw -ErrorAction Stop
        if (-not [string]::IsNullOrWhiteSpace($json)) {
            $data = $json | ConvertFrom-Json -AsHashtable
            if ($null -ne $data) {
                $script:BuildConfig = $data
            }
        }
    }
    catch {
        Write-Warning "[CSM.TmpeSync] Unable to parse existing build configuration. A new configuration will be created."
    }
}

function Save-BuildConfig {
    if (-not $script:ConfigUpdated) {
        return
    }

    $directory = Split-Path -Parent $ConfigFile
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $script:BuildConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ConfigFile -Encoding UTF8
    $script:ConfigUpdated = $false
}

Load-BuildConfig

if (-not (Test-Path $ProjectPath)) {
    throw "CSM.TmpeSync project not found under $ProjectPath."
}

if (-not ($Update -or $Build -or $Install -or $Configure)) {
    Write-Host "[CSM.TmpeSync] No action specified. Use -Update, -Build, -Install and/or -Configure." -ForegroundColor Yellow
    return
}

function Get-ConfigValue {
    param(
        [string]$Key,
        [string]$ParameterValue
    )

    if (-not [string]::IsNullOrWhiteSpace($ParameterValue)) {
        $script:BuildConfig[$Key] = $ParameterValue
        $script:ConfigUpdated = $true
        return $ParameterValue
    }

    if ($script:BuildConfig.ContainsKey($Key)) {
        return [string]$script:BuildConfig[$Key]
    }

    return ""
}

function Prompt-ForValue {
    param(
        [string]$Key,
        [string]$Message,
        [switch]$Optional
    )

    if (-not $Host.UI -or -not $Host.UI.RawUI) {
        if ($Optional) {
            return ""
        }

        throw "Unable to prompt for $Key. Provide the path via parameter or configure beforehand."
    }

    $inputValue = Read-Host $Message
    if ([string]::IsNullOrWhiteSpace($inputValue)) {
        if ($Optional) {
            return ""
        }

        throw "No value provided for $Key."
    }

    $script:BuildConfig[$Key] = $inputValue
    $script:ConfigUpdated = $true
    return $inputValue
}

function Resolve-Setting {
    param(
        [string]$Key,
        [string]$ParameterValue,
        [string]$Prompt,
        [switch]$Optional
    )

    $value = Get-ConfigValue -Key $Key -ParameterValue $ParameterValue
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    if ($Optional) {
        return ""
    }

    return Prompt-ForValue -Key $Key -Message $Prompt -Optional:$Optional
}

function Ensure-DirectoryExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description path is required."
    }

    if (-not (Test-Path $Path)) {
        throw "$Description path not found: $Path"
    }
}

function Ensure-LibDirectory {
    param([string]$RelativePath)

    $fullPath = Join-Path $LibRoot $RelativePath
    if (-not (Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    }

    return $fullPath
}

function Sync-CitiesSkylinesAssemblies {
    param([string]$SourceDir)

    $managedSource = Join-Path $SourceDir "Cities_Data\Managed"
    Ensure-DirectoryExists -Path $managedSource -Description "Cities: Skylines managed"

    $destination = Ensure-LibDirectory -RelativePath "CitiesSkylines/Cities_Data/Managed"
    foreach ($file in @("ICities.dll", "ColossalManaged.dll", "UnityEngine.dll", "Assembly-CSharp.dll")) {
        $sourcePath = Join-Path $managedSource $file
        if (-not (Test-Path $sourcePath)) {
            throw "Missing required Cities: Skylines assembly: $sourcePath"
        }

        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destination $file) -Force
    }

    return (Join-Path $LibRoot "CitiesSkylines")
}

function Sync-HarmonyAssembly {
    param([string]$SourceDir)

    Ensure-DirectoryExists -Path $SourceDir -Description "Harmony directory"
    $sourcePath = Join-Path $SourceDir "CitiesHarmony.Harmony.dll"
    if (-not (Test-Path $sourcePath)) {
        throw "CitiesHarmony.Harmony.dll not found inside $SourceDir"
    }

    $destinationDir = Ensure-LibDirectory -RelativePath "Harmony"
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destinationDir "CitiesHarmony.Harmony.dll") -Force
    return $destinationDir
}

function Sync-TmpeAssemblies {
    param([string]$SourceDir)

    if ([string]::IsNullOrWhiteSpace($SourceDir)) {
        return ""
    }

    $managedSource = Join-Path $SourceDir "Cities_Data\Managed"
    $trafficManagerDll = Join-Path $managedSource "TrafficManager.dll"
    if (-not (Test-Path $trafficManagerDll)) {
        return ""
    }

    $destination = Ensure-LibDirectory -RelativePath "TMPE/Cities_Data/Managed"
    Copy-Item -LiteralPath $trafficManagerDll -Destination (Join-Path $destination "TrafficManager.dll") -Force
    return (Join-Path $LibRoot "TMPE")
}

function Sync-CsmAssemblies {
    param([string]$SourceDir)

    if (-not (Test-Path $SourceDir)) {
        return ""
    }

    $destination = Ensure-LibDirectory -RelativePath "CSM"
    Get-ChildItem -Path $SourceDir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Force
    }

    $apiPath = Join-Path $destination "CSM.API.dll"
    if (Test-Path $apiPath) {
        return $apiPath
    }

    return ""
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
    $pwshArguments = @("-NoLogo", "-NoProfile", "-WorkingDirectory", $CsmScriptsDir, "-File", $CsmBuildScript) + $Arguments

    Write-Host "[CSM.TmpeSync] -> CSM build.ps1 $($Arguments -join ' ')" -ForegroundColor DarkCyan
    & $pwshExecutable @($pwshArguments)

    if ($LASTEXITCODE -ne 0) {
        throw "CSM build script failed with exit code $LASTEXITCODE."
    }
}

function Configure-Interactive {
    Write-Host "[CSM.TmpeSync] Interactive configuration" -ForegroundColor Cyan
    $script:BuildConfig["CitiesSkylinesDir"] = Prompt-ForValue -Key "CitiesSkylinesDir" -Message "Enter the Cities: Skylines installation directory"
    $optionalTmpe = Prompt-ForValue -Key "TmpeDir" -Message "Enter the TM:PE workshop directory (optional, leave blank to skip)" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalTmpe)) {
        $script:BuildConfig["TmpeDir"] = $optionalTmpe
    }

    $optionalModDir = Prompt-ForValue -Key "ModDirectory" -Message "Enter the target directory for installing CSM.TmpeSync" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalModDir)) {
        $script:BuildConfig["ModDirectory"] = $optionalModDir
    }

    $optionalCsmModDir = Prompt-ForValue -Key "CsmModDirectory" -Message "Enter the target directory for installing CSM" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalCsmModDir)) {
        $script:BuildConfig["CsmModDirectory"] = $optionalCsmModDir
    }

    $optionalSteamMods = Prompt-ForValue -Key "SteamModsDir" -Message "Enter the Cities Skylines steam mods directory (optional)" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalSteamMods)) {
        $script:BuildConfig["SteamModsDir"] = $optionalSteamMods
    }
    $script:ConfigUpdated = $true
    Save-BuildConfig
    Write-Host "[CSM.TmpeSync] Configuration saved." -ForegroundColor Green
}

function Get-SteamWorkshopBaseFromCitiesDir {
    param([string]$CitiesDir)

    if ([string]::IsNullOrWhiteSpace($CitiesDir)) {
        return ""
    }

    $parent = Split-Path -Parent $CitiesDir
    if ([string]::IsNullOrWhiteSpace($parent)) {
        return ""
    }

    $steamAppsDir = Split-Path -Parent $parent
    if ([string]::IsNullOrWhiteSpace($steamAppsDir)) {
        return ""
    }

    return Join-Path $steamAppsDir "workshop\content\255710"
}

function Resolve-HarmonyDirectory {
    param(
        [string]$ExplicitPath,
        [string]$SteamModsDir,
        [string]$CitiesDir
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($SteamModsDir)) {
        $candidates += (Join-Path $SteamModsDir "2040656402")
    }

    $workshopFromCities = Get-SteamWorkshopBaseFromCitiesDir -CitiesDir $CitiesDir
    if (-not [string]::IsNullOrWhiteSpace($workshopFromCities)) {
        $candidates += (Join-Path $workshopFromCities "2040656402")
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $dllPath = Join-Path $candidate "CitiesHarmony.Harmony.dll"
        if (Test-Path $dllPath) {
            return $candidate
        }
    }

    return ""
}

if ($Configure) {
    Configure-Interactive
    if (-not ($Update -or $Build -or $Install)) {
        return
    }
}

$modDirectoryParameter = if ($ModDirectory -eq "Default") { "" } else { $ModDirectory }
$modDirectoryFromConfig = Get-ConfigValue -Key "ModDirectory" -ParameterValue $modDirectoryParameter
if (-not [string]::IsNullOrWhiteSpace($modDirectoryFromConfig)) {
    $ModDirectory = $modDirectoryFromConfig
}
elseif ($ModDirectory -eq "Default") {
    $ModDirectory = Get-DefaultModDirectory -ModName "CSM.TmpeSync"
}

if ($Install) {
    $csmModDirectoryParameter = if ($CsmModDirectory -eq "Default") { "" } else { $CsmModDirectory }
    $csmModDirectoryFromConfig = Get-ConfigValue -Key "CsmModDirectory" -ParameterValue $csmModDirectoryParameter
    if (-not [string]::IsNullOrWhiteSpace($csmModDirectoryFromConfig)) {
        $CsmModDirectory = $csmModDirectoryFromConfig
    }
    elseif ($CsmModDirectory -eq "Default") {
        $CsmModDirectory = Get-DefaultModDirectory -ModName "CSM"
    }
}

if ($Update) {
    $gameDirectoryFromConfig = Get-ConfigValue -Key "GameDirectory" -ParameterValue $GameDirectory
    if (-not [string]::IsNullOrWhiteSpace($gameDirectoryFromConfig)) {
        $GameDirectory = $gameDirectoryFromConfig
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
    $ConfiguredCitiesSkylinesDir = Resolve-Setting -Key "CitiesSkylinesDir" -ParameterValue $CitiesSkylinesDir -Prompt "Enter the Cities: Skylines installation directory"
    $ConfiguredTmpeDir = Get-ConfigValue -Key "TmpeDir" -ParameterValue $TmpeDir
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredTmpeDir)) {
        $TmpeDir = $ConfiguredTmpeDir
    }
    else {
        $ConfiguredTmpeDir = ""
    }

    $configuredSteamModsDir = Get-ConfigValue -Key "SteamModsDir" -ParameterValue $SteamModsDir
    if (-not [string]::IsNullOrWhiteSpace($configuredSteamModsDir)) {
        $SteamModsDir = $configuredSteamModsDir
    }

    if ([string]::IsNullOrWhiteSpace($ConfiguredCitiesSkylinesDir)) {
        throw "Cities: Skylines directory is not configured. Run with -Configure or provide -CitiesSkylinesDir."
    }

    Ensure-DirectoryExists -Path $ConfiguredCitiesSkylinesDir -Description "Cities: Skylines"

    if ([string]::IsNullOrWhiteSpace($SteamModsDir)) {
        $derivedSteamModsDir = Get-SteamWorkshopBaseFromCitiesDir -CitiesDir $ConfiguredCitiesSkylinesDir
        if (-not [string]::IsNullOrWhiteSpace($derivedSteamModsDir) -and (Test-Path $derivedSteamModsDir)) {
            $SteamModsDir = $derivedSteamModsDir
            $script:BuildConfig["SteamModsDir"] = $SteamModsDir
            $script:ConfigUpdated = $true
        }
    }

    $explicitHarmonyDir = Get-ConfigValue -Key "HarmonyDllDir" -ParameterValue $HarmonyDllDir
    $ConfiguredHarmonyDir = Resolve-HarmonyDirectory -ExplicitPath $explicitHarmonyDir -SteamModsDir $SteamModsDir -CitiesDir $ConfiguredCitiesSkylinesDir

    if ([string]::IsNullOrWhiteSpace($ConfiguredHarmonyDir)) {
        throw "Harmony directory could not be resolved automatically. Provide -HarmonyDllDir or configure it in build-settings.json."
    }

    Ensure-DirectoryExists -Path $ConfiguredHarmonyDir -Description "Harmony"
    $script:BuildConfig["HarmonyDllDir"] = $ConfiguredHarmonyDir
    $script:ConfigUpdated = $true

    if ($SkipCsmBuild) {
        Write-Host "[CSM.TmpeSync] Skipping CSM build step (SkipCsmBuild)." -ForegroundColor DarkYellow
    }
    else {
        $csmOutputDir = "..\src\csm\bin\$Configuration"
        $arguments = @("-Build", "-OutputDirectory", $csmOutputDir)
        Invoke-CsmScript -Arguments $arguments
    }

    $csmManagedLib = Sync-CitiesSkylinesAssemblies -SourceDir $ConfiguredCitiesSkylinesDir
    $harmonyLib = Sync-HarmonyAssembly -SourceDir $ConfiguredHarmonyDir
    $tmpeLib = Sync-TmpeAssemblies -SourceDir $ConfiguredTmpeDir

    $csmBinDir = Join-Path $RepoRoot "submodules\CSM\src\csm\bin\$Configuration"
    $apiBinDir = Join-Path $RepoRoot "submodules\CSM\src\api\bin\$Configuration"
    $csmApiFromCsmBuild = Sync-CsmAssemblies -SourceDir $csmBinDir
    $csmApiFromApiBuild = Sync-CsmAssemblies -SourceDir $apiBinDir

    $CitiesSkylinesDir = $csmManagedLib
    $HarmonyDllDir = $harmonyLib
    if (-not [string]::IsNullOrWhiteSpace($tmpeLib)) {
        $TmpeDir = $tmpeLib
    }

    if ([string]::IsNullOrWhiteSpace($CsmApiDllPath)) {
        if (-not [string]::IsNullOrWhiteSpace($csmApiFromApiBuild)) {
            $CsmApiDllPath = $csmApiFromApiBuild
        }
        elseif (-not [string]::IsNullOrWhiteSpace($csmApiFromCsmBuild)) {
            $CsmApiDllPath = $csmApiFromCsmBuild
        }
    }

    Write-Host "[CSM.TmpeSync] Dependency library refreshed under $LibRoot." -ForegroundColor DarkCyan

    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        throw "dotnet CLI not found. Install the .NET SDK to build CSM.TmpeSync."
    }

    $dotnetArguments = @("build", $ProjectPath, "-c", $Configuration, "/restore", "--nologo")

    foreach ($property in @(
        Build-PropertyArgument -Name "CitiesSkylinesDir" -Value $CitiesSkylinesDir,
        Build-PropertyArgument -Name "HarmonyDllDir" -Value $HarmonyDllDir,
        Build-PropertyArgument -Name "CsmApiDllPath" -Value $CsmApiDllPath,
        Build-PropertyArgument -Name "TmpeDir" -Value $TmpeDir,
        Build-PropertyArgument -Name "SteamModsDir" -Value $SteamModsDir,
        Build-PropertyArgument -Name "ModDirectory" -Value $ModDirectory,
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

    Write-Host "[CSM.TmpeSync] Dependency library refreshed under $LibRoot." -ForegroundColor DarkCyan
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

Save-BuildConfig

Write-Host "[CSM.TmpeSync] Done." -ForegroundColor Green
