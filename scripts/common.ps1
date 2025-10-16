$ErrorActionPreference = "Stop"

if (-not (Get-Variable -Name CSMTmpeSyncCommonInitialized -Scope Script -ErrorAction SilentlyContinue)) {
    Set-Variable -Name CSMTmpeSyncCommonInitialized -Scope Script -Value $true

    $script:HostIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    $script:HostIsLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
    $script:HostIsMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

    $script:ScriptRoot = $PSScriptRoot
    $script:RepoRoot = Split-Path -Parent $script:ScriptRoot
    $script:ProjectPath = Join-Path $script:RepoRoot "src\CSM.TmpeSync\CSM.TmpeSync.csproj"
    $script:LibRoot = Join-Path $script:RepoRoot "lib"

    $script:ConfigFile = Join-Path $script:ScriptRoot "build-settings.json"
    $script:BuildConfig = @{}
    $script:ConfigUpdated = $false

    $script:CsmRepoDir = Join-Path $script:RepoRoot "submodules\CSM"
    if (-not (Test-Path $script:CsmRepoDir)) {
        $script:CsmRepoDir = $null
    }

    if ($script:CsmRepoDir) {
        $script:CsmScriptsDir = Join-Path $script:CsmRepoDir "scripts"
        $script:CsmBuildScriptPath = Join-Path $script:CsmScriptsDir "build.ps1"
        $script:CsmSolutionPath = Join-Path $script:CsmRepoDir "CSM.sln"
        $script:CsmAssembliesDir = Join-Path $script:CsmRepoDir "assemblies"
    }
    else {
        $script:CsmScriptsDir = ""
        $script:CsmBuildScriptPath = ""
        $script:CsmSolutionPath = ""
        $script:CsmAssembliesDir = ""
    }

    function Get-RepoRoot {
        return $script:RepoRoot
    }

    function Get-ProjectPath {
        return $script:ProjectPath
    }

    function Get-LibRoot {
        return $script:LibRoot
    }

    function Get-OutputDirectory {
        param([string]$Configuration)

        if ([string]::IsNullOrWhiteSpace($Configuration)) {
            $Configuration = "Release"
        }

        return Join-Path $script:RepoRoot ("src\CSM.TmpeSync\bin\{0}\net35" -f $Configuration)
    }

    function Load-BuildConfig {
        if (-not (Test-Path $script:ConfigFile)) {
            return
        }

        try {
            $json = Get-Content -LiteralPath $script:ConfigFile -Raw -ErrorAction Stop
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $data = $json | ConvertFrom-Json -AsHashtable
                if ($null -ne $data) {
                    $script:BuildConfig = $data
                }
            }
        }
        catch {
            Write-Warning "[CSM.TmpeSync] Unable to read build-settings.json. A new configuration will be created."
        }
    }

    function Save-BuildConfig {
        if (-not $script:ConfigUpdated) {
            return
        }

        $directory = Split-Path -Parent $script:ConfigFile
        if (-not (Test-Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $script:BuildConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $script:ConfigFile -Encoding UTF8
        $script:ConfigUpdated = $false
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

    function Set-ConfigValue {
        param(
            [string]$Key,
            [string]$Value
        )

        if ([string]::IsNullOrWhiteSpace($Key) -or [string]::IsNullOrWhiteSpace($Value)) {
            return
        }

        $script:BuildConfig[$Key] = $Value
        $script:ConfigUpdated = $true
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

            throw "Unable to prompt for $Key. Provide the value using script parameters or configure beforehand."
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

        $fullPath = Join-Path $script:LibRoot $RelativePath
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
        foreach ($file in @("ICities.dll", "ColossalManaged.dll", "UnityEngine.dll", "UnityEngine.UI.dll", "Assembly-CSharp.dll")) {
            $sourcePath = Join-Path $managedSource $file
            if (-not (Test-Path $sourcePath)) {
                throw "Missing Cities: Skylines assembly: $sourcePath"
            }

            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destination $file) -Force
        }

        return (Join-Path $script:LibRoot "CitiesSkylines")
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
        return (Join-Path $script:LibRoot "TMPE")
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

    function Sync-CsmSubmoduleAssemblies {
        param([string]$LibraryRoot)

        if (-not $script:CsmRepoDir) {
            return
        }

        if ([string]::IsNullOrWhiteSpace($LibraryRoot)) {
            return
        }

        $managedSource = Join-Path $LibraryRoot "Cities_Data\Managed"
        Ensure-DirectoryExists -Path $managedSource -Description "CSM dependency source"

        if (-not (Test-Path $script:CsmAssembliesDir)) {
            New-Item -ItemType Directory -Path $script:CsmAssembliesDir -Force | Out-Null
        }

        foreach ($file in @("Assembly-CSharp.dll", "ColossalManaged.dll", "ICities.dll", "UnityEngine.dll", "UnityEngine.UI.dll")) {
            $sourcePath = Join-Path $managedSource $file
            if (-not (Test-Path $sourcePath)) {
                throw "Missing required Cities: Skylines assembly for CSM: $sourcePath"
            }

            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $script:CsmAssembliesDir $file) -Force
        }
    }

    $script:DotnetCliPath = $null
    function Resolve-DotnetCli {
        if ($script:DotnetCliPath) {
            return $script:DotnetCliPath
        }

        $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw "dotnet CLI not found. Install the .NET SDK."
        }

        $script:DotnetCliPath = $dotnet.Source
        return $script:DotnetCliPath
    }

    function Invoke-Dotnet {
        param(
            [string[]]$Arguments,
            [string]$ErrorContext = "dotnet command"
        )

        $dotnetCli = Resolve-DotnetCli
        & $dotnetCli @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$ErrorContext failed with exit code $LASTEXITCODE."
        }
    }

    function Invoke-CsmBuildScript {
        param(
            [switch]$Update,
            [switch]$Build,
            [switch]$Install,
            [string]$GameDirectory,
            [string]$OutputDirectory,
            [string]$ModDirectory
        )

        if (-not $script:CsmBuildScriptPath) {
            return $false
        }

        if (-not ($Update -or $Build -or $Install)) {
            return $false
        }

        $shell = Get-Command "pwsh" -ErrorAction SilentlyContinue
        $shellArgs = @("-NoProfile", "-File", ".\build.ps1")

        if (-not $shell) {
            $shell = Get-Command "powershell" -ErrorAction SilentlyContinue
            if (-not $shell) {
                throw "Unable to locate a PowerShell executable for running the CSM build script."
            }

            $shellArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\build.ps1")
        }

        $logTokens = @()
        if ($Update) {
            $shellArgs += "-Update"
            $logTokens += "-Update"
        }
        if ($Build) {
            $shellArgs += "-Build"
            $logTokens += "-Build"
        }
        if ($Install) {
            $shellArgs += "-Install"
            $logTokens += "-Install"
        }

        if ($Update -and -not [string]::IsNullOrWhiteSpace($GameDirectory)) {
            $shellArgs += "-GameDirectory"
            $shellArgs += $GameDirectory
            $logTokens += "-GameDirectory"
            $logTokens += ("`"{0}`"" -f $GameDirectory)
        }
        if (($Build -or $Install) -and -not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
            $shellArgs += "-OutputDirectory"
            $shellArgs += $OutputDirectory
            $logTokens += "-OutputDirectory"
            $logTokens += ("`"{0}`"" -f $OutputDirectory)
        }
        if ($Install -and -not [string]::IsNullOrWhiteSpace($ModDirectory)) {
            $shellArgs += "-ModDirectory"
            $shellArgs += $ModDirectory
            $logTokens += "-ModDirectory"
            $logTokens += ("`"{0}`"" -f $ModDirectory)
        }

        $shellPath = $shell.Source
        Push-Location -Path $script:CsmScriptsDir
        try {
            if ($logTokens.Count -gt 0) {
                Write-Host ("[CSM.TmpeSync] Invoking submodule build.ps1 {0}" -f ($logTokens -join " ")) -ForegroundColor DarkCyan
            }

            & $shellPath @shellArgs
            if ($LASTEXITCODE -ne 0) {
                throw "CSM submodule build.ps1 failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }

        return $true
    }

    function Get-CsmScriptConfiguration {
        param([string]$RequestedConfiguration)

        if (-not $script:CsmBuildScriptPath) {
            return $RequestedConfiguration
        }

        if ([string]::IsNullOrWhiteSpace($RequestedConfiguration)) {
            return "Release"
        }

        if ($RequestedConfiguration -match '^(?i)Release$') {
            return "Release"
        }

        Write-Warning "[CSM.TmpeSync] CSM build.ps1 supports Release builds. Using Release for the submodule."
        return "Release"
    }

    function Build-CsmSolution {
        param(
            [string]$RequestedConfiguration,
            [switch]$SkipBuild
        )

        if (-not $script:CsmRepoDir) {
            throw "CSM submodule not found. Ensure the repository is initialized under submodules\CSM."
        }

        $effectiveConfiguration = Get-CsmScriptConfiguration -RequestedConfiguration $RequestedConfiguration
        $csmOutputDir = Join-Path $script:CsmRepoDir ("src\csm\bin\{0}" -f $effectiveConfiguration)

        if ($SkipBuild) {
            Write-Host "[CSM.TmpeSync] Skipping CSM build (SkipCsmBuild)." -ForegroundColor DarkYellow
        }
        else {
            if (Invoke-CsmBuildScript -Build -OutputDirectory $csmOutputDir) {
                return $effectiveConfiguration
            }

            if (-not (Test-Path $script:CsmSolutionPath)) {
                throw "CSM solution not found under $script:CsmSolutionPath."
            }

            Write-Host "[CSM.TmpeSync] Falling back to dotnet build for CSM (Configuration=$effectiveConfiguration)." -ForegroundColor DarkYellow
            Invoke-Dotnet -Arguments @("build", $script:CsmSolutionPath, "-c", $effectiveConfiguration, "/restore", "--nologo") -ErrorContext "dotnet build for CSM"
        }

        return $effectiveConfiguration
    }

    function Install-CsmMod {
        param(
            [string]$Configuration,
            [switch]$SkipInstall,
            [string]$ModDirectory
        )

        if (-not $script:CsmRepoDir) {
            throw "CSM submodule not found. Ensure the repository is initialized under submodules\CSM."
        }

        $effectiveConfiguration = Get-CsmScriptConfiguration -RequestedConfiguration $Configuration
        $sourceDir = Join-Path $script:CsmRepoDir ("src\csm\bin\{0}" -f $effectiveConfiguration)

        if ($SkipInstall) {
            Write-Host "[CSM.TmpeSync] Skipping CSM install (SkipCsmInstall)." -ForegroundColor DarkYellow
            return
        }

        if (Invoke-CsmBuildScript -Install -OutputDirectory $sourceDir -ModDirectory $ModDirectory) {
            return
        }

        if (-not (Test-Path $sourceDir)) {
            throw "CSM build output not found at $sourceDir. Build the CSM mod first."
        }

        $assemblies = Get-ChildItem -Path $sourceDir -Filter "*.dll" -ErrorAction SilentlyContinue
        if (-not $assemblies) {
            throw "No CSM assemblies were produced under $sourceDir."
        }

        Write-Host "[CSM.TmpeSync] Installing CSM mod to $ModDirectory." -ForegroundColor Cyan
        Remove-Item -Path $ModDirectory -Recurse -ErrorAction Ignore
        New-Item -ItemType Directory -Path $ModDirectory -Force | Out-Null

        foreach ($assembly in $assemblies) {
            Copy-Item -Path $assembly.FullName -Destination $ModDirectory -Force
        }

        $pdbFiles = Get-ChildItem -Path $sourceDir -Filter "*.pdb" -ErrorAction SilentlyContinue
        foreach ($pdb in $pdbFiles) {
            Copy-Item -Path $pdb.FullName -Destination $ModDirectory -Force
        }

        Write-Host "[CSM.TmpeSync] CSM install completed." -ForegroundColor Green
    }

    function Get-DefaultModDirectory {
        param([string]$ModName)

        if ($script:HostIsLinux) {
            return "~/.local/share/Colossal Order/Cities_Skylines/Addons/Mods/$ModName"
        }

        if ($script:HostIsMacOS) {
            return "~/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/$ModName"
        }

        return "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\$ModName"
    }

    function Configure-Interactive {
        Write-Host "[CSM.TmpeSync] Interactive configuration" -ForegroundColor Cyan

        $citiesDir = Prompt-ForValue -Key "CitiesSkylinesDir" -Message "Enter the Cities: Skylines installation directory"
        Ensure-DirectoryExists -Path $citiesDir -Description "Cities: Skylines"

        $steamModsDir = Prompt-ForValue -Key "SteamModsDir" -Message "Enter the Steam workshop directory (optional)" -Optional
        if (-not [string]::IsNullOrWhiteSpace($steamModsDir)) {
            Ensure-DirectoryExists -Path $steamModsDir -Description "Steam workshop"
        }

        $harmonyDir = Prompt-ForValue -Key "HarmonyDllDir" -Message "Enter the CitiesHarmony directory (optional)" -Optional
        if (-not [string]::IsNullOrWhiteSpace($harmonyDir)) {
            Ensure-DirectoryExists -Path $harmonyDir -Description "Harmony"
        }

        $tmpeDir = Prompt-ForValue -Key "TmpeDir" -Message "Enter the TM:PE directory (optional)" -Optional
        if (-not [string]::IsNullOrWhiteSpace($tmpeDir)) {
            Ensure-DirectoryExists -Path $tmpeDir -Description "TM:PE"
        }

        $modDir = Prompt-ForValue -Key "ModDirectory" -Message "Enter the install directory for CSM.TmpeSync (optional)" -Optional
        if (-not [string]::IsNullOrWhiteSpace($modDir)) {
            Ensure-DirectoryExists -Path $modDir -Description "Mod installation"
        }

        $csmModDir = Prompt-ForValue -Key "CsmModDirectory" -Message "Enter the install directory for the CSM mod (optional)" -Optional
        if (-not [string]::IsNullOrWhiteSpace($csmModDir)) {
            Ensure-DirectoryExists -Path $csmModDir -Description "CSM install"
        }

        Write-Host "[CSM.TmpeSync] Configuration updated." -ForegroundColor Green
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

    function Resolve-CsmApiFromLib {
        $candidate = Join-Path $script:LibRoot "CSM\CSM.API.dll"
        if (Test-Path $candidate) {
            return $candidate
        }

        $legacyCandidate = Join-Path $script:LibRoot "CSM.API.dll"
        if (Test-Path $legacyCandidate) {
            return $legacyCandidate
        }

        return ""
    }

    function Invoke-TmpeSyncUpdate {
        param(
            [string]$CitiesSkylinesDir,
            [string]$GameDirectory,
            [string]$HarmonyDllDir,
            [string]$TmpeDir,
            [string]$SteamModsDir,
            [switch]$IncludeCsm,
            [switch]$SkipCsmUpdate
        )

        $initialCitiesDir = if (-not [string]::IsNullOrWhiteSpace($CitiesSkylinesDir)) {
            $CitiesSkylinesDir
        }
        elseif (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
            $GameDirectory
        }
        else {
            ""
        }

        $configuredCitiesDir = Resolve-Setting -Key "CitiesSkylinesDir" -ParameterValue $initialCitiesDir -Prompt "Enter the Cities: Skylines installation directory"
        if ([string]::IsNullOrWhiteSpace($configuredCitiesDir)) {
            throw "Cities: Skylines directory is not configured. Provide -CitiesSkylinesDir or run with -Configure."
        }

        Ensure-DirectoryExists -Path $configuredCitiesDir -Description "Cities: Skylines"
        $script:BuildConfig["GameDirectory"] = $configuredCitiesDir
        $script:ConfigUpdated = $true

        $steamMods = Get-ConfigValue -Key "SteamModsDir" -ParameterValue $SteamModsDir
        if ([string]::IsNullOrWhiteSpace($steamMods)) {
            $derived = Get-SteamWorkshopBaseFromCitiesDir -CitiesDir $configuredCitiesDir
            if (-not [string]::IsNullOrWhiteSpace($derived) -and (Test-Path $derived)) {
                $steamMods = $derived
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($steamMods)) {
            Set-ConfigValue -Key "SteamModsDir" -Value $steamMods
        }

        if ($IncludeCsm -and -not $SkipCsmUpdate) {
            Invoke-CsmBuildScript -Update -GameDirectory $configuredCitiesDir | Out-Null
        }
        elseif ($IncludeCsm -and $SkipCsmUpdate) {
            Write-Host "[CSM.TmpeSync] Skipping CSM update step (SkipCsmUpdate)." -ForegroundColor DarkYellow
        }

        $libCitiesDir = Sync-CitiesSkylinesAssemblies -SourceDir $configuredCitiesDir
        if ($IncludeCsm) {
            Sync-CsmSubmoduleAssemblies -LibraryRoot $libCitiesDir
        }

        $explicitHarmonyDir = Get-ConfigValue -Key "HarmonyDllDir" -ParameterValue $HarmonyDllDir
        $resolvedHarmony = Resolve-HarmonyDirectory -ExplicitPath $explicitHarmonyDir -SteamModsDir $steamMods -CitiesDir $configuredCitiesDir
        if (-not [string]::IsNullOrWhiteSpace($resolvedHarmony) -and (Test-Path $resolvedHarmony)) {
            Set-ConfigValue -Key "HarmonyDllDir" -Value $resolvedHarmony
        }

        $tmpeConfigured = Get-ConfigValue -Key "TmpeDir" -ParameterValue $TmpeDir
        if (-not [string]::IsNullOrWhiteSpace($tmpeConfigured)) {
            Ensure-DirectoryExists -Path $tmpeConfigured -Description "TM:PE directory"
        }

        $harmonyLib = if (-not [string]::IsNullOrWhiteSpace($resolvedHarmony)) {
            Sync-HarmonyAssembly -SourceDir $resolvedHarmony
        } else { "" }

        $tmpeLib = if (-not [string]::IsNullOrWhiteSpace($tmpeConfigured)) {
            Sync-TmpeAssemblies -SourceDir $tmpeConfigured
        } else { "" }

        if (-not [string]::IsNullOrWhiteSpace($tmpeLib)) {
            Set-ConfigValue -Key "TmpeDir" -Value $tmpeConfigured
        }

        Write-Host "[CSM.TmpeSync] Dependency library refreshed under $script:LibRoot." -ForegroundColor DarkCyan

        return [pscustomobject]@{
            SourceCitiesDir = $configuredCitiesDir
            LibraryCitiesDir = $libCitiesDir
            HarmonyDir = $harmonyLib
            HarmonySource = $resolvedHarmony
            TmpeDir = $tmpeLib
            SteamModsDir = $steamMods
        }
    }
    function Invoke-TmpeSyncBuild {
        param(
            [string]$Configuration,
            [string]$CitiesSkylinesDir,
            [string]$HarmonyDllDir,
            [string]$CsmApiDllPath,
            [string]$TmpeDir,
            [string]$SteamModsDir,
            [string]$ModDirectory,
            [switch]$IncludeCsm,
            [switch]$SkipCsmBuild
        )

        $configuredCitiesDir = Resolve-Setting -Key "CitiesSkylinesDir" -ParameterValue $CitiesSkylinesDir -Prompt "Enter the Cities: Skylines installation directory"
        if ([string]::IsNullOrWhiteSpace($configuredCitiesDir)) {
            throw "Cities: Skylines directory is not configured. Provide -CitiesSkylinesDir or run with -Configure."
        }

        Ensure-DirectoryExists -Path $configuredCitiesDir -Description "Cities: Skylines"

        $steamModsResolved = Get-ConfigValue -Key "SteamModsDir" -ParameterValue $SteamModsDir
        if ([string]::IsNullOrWhiteSpace($steamModsResolved)) {
            $derived = Get-SteamWorkshopBaseFromCitiesDir -CitiesDir $configuredCitiesDir
            if (-not [string]::IsNullOrWhiteSpace($derived) -and (Test-Path $derived)) {
                $steamModsResolved = $derived
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($steamModsResolved)) {
            Set-ConfigValue -Key "SteamModsDir" -Value $steamModsResolved
        }

        $explicitHarmonyDir = Get-ConfigValue -Key "HarmonyDllDir" -ParameterValue $HarmonyDllDir
        $resolvedHarmony = Resolve-HarmonyDirectory -ExplicitPath $explicitHarmonyDir -SteamModsDir $steamModsResolved -CitiesDir $configuredCitiesDir
        if ([string]::IsNullOrWhiteSpace($resolvedHarmony)) {
            throw "Harmony directory could not be resolved automatically. Provide -HarmonyDllDir or configure it in build-settings.json."
        }

        Ensure-DirectoryExists -Path $resolvedHarmony -Description "Harmony"
        Set-ConfigValue -Key "HarmonyDllDir" -Value $resolvedHarmony

        $tmpeConfigured = Get-ConfigValue -Key "TmpeDir" -ParameterValue $TmpeDir
        if (-not [string]::IsNullOrWhiteSpace($tmpeConfigured)) {
            Ensure-DirectoryExists -Path $tmpeConfigured -Description "TM:PE directory"
        }

        $csmLibRoot = Sync-CitiesSkylinesAssemblies -SourceDir $configuredCitiesDir
        if ($IncludeCsm) {
            Sync-CsmSubmoduleAssemblies -LibraryRoot $csmLibRoot
        }

        $csmBuildConfiguration = if ($IncludeCsm) {
            Build-CsmSolution -RequestedConfiguration $Configuration -SkipBuild:$SkipCsmBuild
        } else {
            ""
        }

        $harmonyLib = Sync-HarmonyAssembly -SourceDir $resolvedHarmony
        $tmpeLib = Sync-TmpeAssemblies -SourceDir $tmpeConfigured

        if (-not [string]::IsNullOrWhiteSpace($tmpeLib)) {
            Set-ConfigValue -Key "TmpeDir" -Value $tmpeConfigured
        }

        $csmBinDir = if ($IncludeCsm -and -not [string]::IsNullOrWhiteSpace($csmBuildConfiguration)) {
            Join-Path $script:CsmRepoDir ("src\csm\bin\{0}" -f $csmBuildConfiguration)
        } else {
            ""
        }
        $apiBinDir = if ($IncludeCsm -and -not [string]::IsNullOrWhiteSpace($csmBuildConfiguration)) {
            Join-Path $script:CsmRepoDir ("src\api\bin\{0}" -f $csmBuildConfiguration)
        } else {
            ""
        }

        $csmApiFromApiBuild = if ($IncludeCsm) { Sync-CsmAssemblies -SourceDir $apiBinDir } else { "" }
        $csmApiFromCsmBuild = if ($IncludeCsm) { Sync-CsmAssemblies -SourceDir $csmBinDir } else { "" }

        $effectiveCitiesDir = $csmLibRoot
        $effectiveHarmonyDir = $harmonyLib
        $effectiveTmpeDir = if (-not [string]::IsNullOrWhiteSpace($tmpeLib)) { $tmpeLib } else { "" }

        $effectiveCsmApi = $CsmApiDllPath
        if ([string]::IsNullOrWhiteSpace($effectiveCsmApi)) {
            $effectiveCsmApi = Get-ConfigValue -Key "CsmApiDllPath" -ParameterValue ""
        }
        if ([string]::IsNullOrWhiteSpace($effectiveCsmApi)) {
            if (-not [string]::IsNullOrWhiteSpace($csmApiFromApiBuild)) {
                $effectiveCsmApi = $csmApiFromApiBuild
            }
            elseif (-not [string]::IsNullOrWhiteSpace($csmApiFromCsmBuild)) {
                $effectiveCsmApi = $csmApiFromCsmBuild
            }
            else {
                $effectiveCsmApi = Resolve-CsmApiFromLib
            }
        }

        if ([string]::IsNullOrWhiteSpace($effectiveCsmApi)) {
            throw "CSM.API.dll not found. Run the script with -CSM or supply -CsmApiDllPath."
        }

        Set-ConfigValue -Key "CsmApiDllPath" -Value $effectiveCsmApi

        $dotnetArguments = @("build", $script:ProjectPath, "-c", $Configuration, "/restore", "--nologo")
        $propertySpecs = @(
            @{ Name = "CitiesSkylinesDir"; Value = $effectiveCitiesDir },
            @{ Name = "HarmonyDllDir"; Value = $effectiveHarmonyDir },
            @{ Name = "CsmApiDllPath"; Value = $effectiveCsmApi },
            @{ Name = "TmpeDir"; Value = $effectiveTmpeDir },
            @{ Name = "SteamModsDir"; Value = $steamModsResolved },
            @{ Name = "ModDirectory"; Value = $ModDirectory },
            @{ Name = "ModsOutDir"; Value = $ModDirectory }
        )

        foreach ($spec in $propertySpecs) {
            $property = Build-PropertyArgument -Name $spec.Name -Value $spec.Value
            if ($property) {
                $dotnetArguments += $property
            }
        }

        Write-Host "[CSM.TmpeSync] Building add-on (Configuration=$Configuration)." -ForegroundColor Cyan
        Invoke-Dotnet -Arguments $dotnetArguments -ErrorContext "dotnet build for CSM.TmpeSync"

        Write-Host "[CSM.TmpeSync] Build completed." -ForegroundColor Green
    }

    function Invoke-TmpeSyncInstall {
        param(
            [string]$Configuration,
            [string]$ModDirectory,
            [switch]$IncludeCsm,
            [switch]$SkipCsmInstall,
            [string]$CsmModDirectory
        )

        $outputDir = Get-OutputDirectory -Configuration $Configuration
        if (-not (Test-Path $outputDir)) {
            throw "Build output directory $outputDir not found. Run with -Build first."
        }

        $assemblies = Get-ChildItem -Path $outputDir -Filter "CSM.TmpeSync*.dll" -ErrorAction Stop
        if (-not $assemblies) {
            throw "No CSM.TmpeSync assemblies were produced under $outputDir."
        }

        Write-Host "[CSM.TmpeSync] Installing add-on to $ModDirectory." -ForegroundColor Cyan
        Remove-Item -Path $ModDirectory -Recurse -ErrorAction Ignore
        New-Item -ItemType Directory -Path $ModDirectory -Force | Out-Null

        foreach ($assembly in $assemblies) {
            Copy-Item -Path $assembly.FullName -Destination $ModDirectory -Force
        }

        $pdbFiles = Get-ChildItem -Path $outputDir -Filter "CSM.TmpeSync*.pdb" -ErrorAction SilentlyContinue
        foreach ($pdb in $pdbFiles) {
            Copy-Item -Path $pdb.FullName -Destination $ModDirectory -Force
        }

        Write-Host "[CSM.TmpeSync] Install completed." -ForegroundColor Green

        if ($IncludeCsm) {
            Install-CsmMod -Configuration $Configuration -SkipInstall:$SkipCsmInstall -ModDirectory $CsmModDirectory
        }
    }

    Load-BuildConfig
}
