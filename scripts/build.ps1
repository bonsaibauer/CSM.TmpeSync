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

function Normalize-Path {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    try {
        return (Get-Item -LiteralPath $Path).FullName
    }
    catch {
        return $Path
    }
}

function Get-WorkshopContentDir {
    param([string]$CitiesSkylinesDir)

    $resolved = Normalize-Path -Path $CitiesSkylinesDir
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        return ""
    }

    $current = $resolved
    while ($true) {
        $leaf = Split-Path $current -Leaf
        if ([string]::IsNullOrWhiteSpace($leaf)) {
            break
        }

        if ($leaf.Equals("steamapps", [System.StringComparison]::OrdinalIgnoreCase)) {
            $candidate = Join-Path $current "workshop\content\255710"
            if (Test-Path $candidate) {
                return $candidate
            }
        }

        $parent = Split-Path $current -Parent
        if ($parent -eq $current -or [string]::IsNullOrWhiteSpace($parent)) {
            break
        }
        $current = $parent
    }

    $defaults = @()
    if (${env:ProgramFiles(x86)}) {
        $defaults += (Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\workshop\content\255710")
    }
    if ($env:ProgramFiles) {
        $defaults += (Join-Path $env:ProgramFiles "Steam\steamapps\workshop\content\255710")
    }

    foreach ($path in $defaults) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path $path)) {
            return $path
        }
    }

    return ""
}

function Sync-CitiesSkylinesAssemblies {
    param([string]$SourceDir)

    $managedSource = Join-Path $SourceDir "Cities_Data\Managed"
    Ensure-DirectoryExists -Path $managedSource -Description "Cities: Skylines managed"

    $destination = Ensure-LibDirectory -RelativePath "CitiesSkylines/Cities_Data/Managed"
    foreach ($file in @("ICities.dll", "ColossalManaged.dll", "UnityEngine.dll", "UnityEngine.UI.dll", "Assembly-CSharp.dll")) {
        $sourcePath = Join-Path $managedSource $file
        if (-not (Test-Path $sourcePath)) {
            throw "Missing required Cities: Skylines assembly: $sourcePath"
        }

        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destination $file) -Force
    }

    return (Join-Path $LibRoot "CitiesSkylines")
}

function Sync-HarmonyAssembly {
    param([string[]]$CandidateRoots)

    $destinationDir = Ensure-LibDirectory -RelativePath "Harmony"
    foreach ($root in $CandidateRoots) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }

        if (-not (Test-Path $root)) {
            continue
        }

        $item = Get-Item -LiteralPath $root -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            continue
        }

        if ($item.PSIsContainer) {
            $sourcePath = Join-Path $item.FullName "CitiesHarmony.Harmony.dll"
            if (Test-Path $sourcePath) {
                Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destinationDir "CitiesHarmony.Harmony.dll") -Force
                return $destinationDir
            }
        }
        elseif ($item.Extension -eq ".dll" -and $item.Name -eq "CitiesHarmony.Harmony.dll") {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $destinationDir "CitiesHarmony.Harmony.dll") -Force
            return $destinationDir
        }
    }

    $existing = Join-Path $destinationDir "CitiesHarmony.Harmony.dll"
    if (Test-Path $existing) {
        return $destinationDir
    }

    throw "CitiesHarmony.Harmony.dll not found. Install Harmony or provide -HarmonyDllDir."
}

function Sync-TmpeAssemblies {
    param([string[]]$CandidateRoots)

    $destination = Ensure-LibDirectory -RelativePath "TMPE/Cities_Data/Managed"
    foreach ($root in $CandidateRoots) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }

        if (-not (Test-Path $root)) {
            continue
        }

        $item = Get-Item -LiteralPath $root -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            continue
        }

        if ($item.PSIsContainer) {
            $managedDir = Join-Path $item.FullName "Cities_Data\Managed"
            $sourcePath = Join-Path $managedDir "TrafficManager.dll"
            if (-not (Test-Path $sourcePath)) {
                continue
            }

            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destination "TrafficManager.dll") -Force
            return (Join-Path $LibRoot "TMPE")
        }
        elseif ($item.Extension -eq ".dll" -and $item.Name -eq "TrafficManager.dll") {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $destination "TrafficManager.dll") -Force
            return (Join-Path $LibRoot "TMPE")
        }
    }

    $existing = Join-Path $destination "TrafficManager.dll"
    if (Test-Path $existing) {
        return (Join-Path $LibRoot "TMPE")
    }

    return ""
}

function Sync-CsmAssemblies {
    param([string[]]$CandidatePaths)

    $destination = Ensure-LibDirectory -RelativePath "CSM"
    foreach ($path in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        if (-not (Test-Path $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            continue
        }

        if ($item.PSIsContainer) {
            Get-ChildItem -Path $item.FullName -Filter "*.dll" -File -ErrorAction SilentlyContinue | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Force
            }
        }
        elseif ($item.Extension -eq ".dll") {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $destination $item.Name) -Force
        }
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

    $optionalModDir = Prompt-ForValue -Key "ModDirectory" -Message "Enter the target directory for installing CSM.TmpeSync" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalModDir)) {
        $script:BuildConfig["ModDirectory"] = $optionalModDir
    }

    $optionalCsmModDir = Prompt-ForValue -Key "CsmModDirectory" -Message "Enter the target directory for installing CSM" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalCsmModDir)) {
        $script:BuildConfig["CsmModDirectory"] = $optionalCsmModDir
    }

    $optionalSteamMods = Prompt-ForValue -Key "SteamModsDir" -Message "Enter the Cities Skylines Steam mods directory (optional)" -Optional
    if (-not [string]::IsNullOrWhiteSpace($optionalSteamMods)) {
        $script:BuildConfig["SteamModsDir"] = $optionalSteamMods
    }

    $script:ConfigUpdated = $true
    Save-BuildConfig
    Write-Host "[CSM.TmpeSync] Configuration saved." -ForegroundColor Green
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

$steamModsFromConfig = Get-ConfigValue -Key "SteamModsDir" -ParameterValue $SteamModsDir
if (-not [string]::IsNullOrWhiteSpace($steamModsFromConfig)) {
    $SteamModsDir = $steamModsFromConfig
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

    Ensure-DirectoryExists -Path $ConfiguredCitiesSkylinesDir -Description "Cities: Skylines"

    $workshopContentDir = Get-WorkshopContentDir -CitiesSkylinesDir $ConfiguredCitiesSkylinesDir

    if ($SkipCsmBuild) {
        Write-Host "[CSM.TmpeSync] Skipping CSM build step (SkipCsmBuild)." -ForegroundColor DarkYellow
    }
    else {
        $csmOutputDir = "..\src\csm\bin\$Configuration"
        $arguments = @("-Build", "-OutputDirectory", $csmOutputDir)
        Invoke-CsmScript -Arguments $arguments
    }

    $csmManagedLib = Sync-CitiesSkylinesAssemblies -SourceDir $ConfiguredCitiesSkylinesDir

    $harmonyCandidates = @($HarmonyDllDir)
    if (-not [string]::IsNullOrWhiteSpace($workshopContentDir)) {
        $harmonyCandidates += (Join-Path $workshopContentDir "2040656402")
    }
    $harmonyCandidates += (Join-Path $LibRoot "Harmony")
    $HarmonyDllDir = Sync-HarmonyAssembly -CandidateRoots $harmonyCandidates

    $tmpeCandidates = @($TmpeDir)
    if (-not [string]::IsNullOrWhiteSpace($workshopContentDir)) {
        $tmpeCandidates += (Join-Path $workshopContentDir "1637663252")
    }
    $tmpeCandidates += (Join-Path $LibRoot "TMPE")
    $TmpeDir = Sync-TmpeAssemblies -CandidateRoots $tmpeCandidates

    $csmBinDir = Join-Path $RepoRoot "submodules\CSM\src\csm\bin\$Configuration"
    $apiBinDir = Join-Path $RepoRoot "submodules\CSM\src\api\bin\$Configuration"

    $csmCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($CsmApiDllPath)) {
        $csmCandidates += $CsmApiDllPath
    }
    $csmCandidates += @($apiBinDir, $csmBinDir)
    if (-not [string]::IsNullOrWhiteSpace($workshopContentDir)) {
        $csmCandidates += (Join-Path $workshopContentDir "1558438291")
    }
    $csmCandidates += (Join-Path $LibRoot "CSM")

    $CsmApiDllPath = Sync-CsmAssemblies -CandidatePaths $csmCandidates
    if ([string]::IsNullOrWhiteSpace($CsmApiDllPath)) {
        throw "CSM.API.dll not found. Build the CSM submodule or provide -CsmApiDllPath."
    }

    $CitiesSkylinesDir = $csmManagedLib

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
