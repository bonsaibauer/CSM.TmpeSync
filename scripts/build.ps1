#!/bin/pwsh
param(
    [switch]$Update = $false,
    [switch]$Build = $false,
    [switch]$Install = $false,
    [switch]$Configure = $false,
    [string]$Configuration = "Release",
    [string]$Profile = "",
    [string]$GameDirectory = "",
    [string]$SteamModsDir = "",
    [string]$HarmonyDllDir = "",
    [string]$CsmApiDllPath = "",
    [string]$TmpeDir = "",
    [string]$ModDirectory = "",
    [string]$ModRootDirectory = "",
    [string]$HarmonySourceDir = "",
    [string]$CsmSourceDir = "",
    [string]$TmpeSourceDir = ""
)

$ErrorActionPreference = "Stop"

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:ProjectPath = Join-Path $script:RepoRoot "src/CSM.TmpeSync/CSM.TmpeSync.csproj"
$script:LibRoot = Join-Path $script:RepoRoot "lib"
$script:ConfigPath = Join-Path $PSScriptRoot "build-settings.json"
$script:SettingsChanged = $false
$script:IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$script:MsBuildPath = $null
$script:ModProjectsCache = $null

function Resolve-MsBuildPath {
    if (-not $script:IsWindowsPlatform) {
        return $null
    }

    if ($script:MsBuildPath -and (Test-Path $script:MsBuildPath)) {
        return $script:MsBuildPath
    }

    $candidatePaths = @(
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    $msbuildCommand = Get-Command "msbuild" -ErrorAction SilentlyContinue
    if ($msbuildCommand) {
        $candidatePaths += $msbuildCommand.Source
    }

    foreach ($candidate in ($candidatePaths | Where-Object { $_ } | Select-Object -Unique)) {
        if (Test-Path $candidate) {
            $script:MsBuildPath = $candidate
            break
        }
    }

    if (-not $script:MsBuildPath) {
        throw "MSBuild.exe not found. Install Visual Studio Build Tools 2019 or newer."
    }

    return $script:MsBuildPath
}

function Resolve-Net35ReferenceInfo {
    if (-not $script:IsWindowsPlatform) {
        return $null
    }

    $version = "1.0.3"
    $candidateRoots = @(
        (Join-Path $env:UserProfile ".nuget\packages\microsoft.netframework.referenceassemblies.net35\$version"),
        "C:\\Program Files (x86)\\Microsoft Visual Studio\\Shared\\NuGetPackages\\microsoft.netframework.referenceassemblies.net35\\$version",
        "C:\\Program Files\\dotnet\\library-packs\\microsoft.netframework.referenceassemblies.net35\\$version",
        "C:\\Program Files\\dotnet\\packs\\Microsoft.NETFramework.ReferenceAssemblies.net35\\$version"
    )

    foreach ($root in $candidateRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $frameworkCandidates = @(
            (Join-Path $root "build\\.NETFramework\\v3.5"),
            (Join-Path $root "ref\\.NETFramework\\v3.5")
        )

        foreach ($frameworkDir in $frameworkCandidates) {
            if (-not (Test-Path $frameworkDir)) {
                continue
            }

            $mscorlibPath = Join-Path $frameworkDir "mscorlib.dll"
            if (-not (Test-Path $mscorlibPath)) {
                continue
            }

            $tfDir = Split-Path $frameworkDir -Parent
            $rootPath = Split-Path $tfDir -Parent

            return [pscustomobject]@{
                RootPath = $rootPath
                FrameworkPath = $frameworkDir
            }
        }
    }

    $fallbacks = @(
        "$env:WINDIR\\Microsoft.NET\\Framework\\v2.0.50727",
        "$env:WINDIR\\Microsoft.NET\\Framework64\\v2.0.50727"
    )

    foreach ($fallback in $fallbacks) {
        if (-not (Test-Path (Join-Path $fallback "mscorlib.dll"))) {
            continue
        }

        return [pscustomobject]@{
            RootPath = Split-Path $fallback
            FrameworkPath = $fallback
        }
    }

    return $null
}

function Format-MsBuildProperty {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    return [string]::Format('/p:{0}={1}', $Name, $Value)
}

function Format-DotnetProperty {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    return [string]::Format('-p:{0}={1}', $Name, $Value)
}

function Ensure-Hashtable {
    param([object]$Value)

    if ($Value -is [System.Collections.Hashtable]) {
        return $Value
    }

    if ($null -eq $Value) {
        return @{}
    }

    $table = @{}
    $Value.GetEnumerator() | ForEach-Object { $table[$_.Key] = $_.Value }
    return $table
}

function Load-BuildSettings {
    if (-not (Test-Path $script:ConfigPath)) {
        return @{ ActiveProfile = ""; Profiles = @{} }
    }

    try {
        $raw = Get-Content -LiteralPath $script:ConfigPath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return @{ ActiveProfile = ""; Profiles = @{} }
        }

        $data = $raw | ConvertFrom-Json -AsHashtable
        if ($null -eq $data) {
            return @{ ActiveProfile = ""; Profiles = @{} }
        }

        if (-not $data.ContainsKey('Profiles') -or $null -eq $data.Profiles) {
            $data.Profiles = @{}
            $script:SettingsChanged = $true
        }
        else {
            $data.Profiles = Ensure-Hashtable $data.Profiles
            foreach ($key in @($data.Profiles.Keys)) {
                $data.Profiles[$key] = Ensure-Hashtable $data.Profiles[$key]
            }
        }

        if (-not $data.ContainsKey('ActiveProfile')) {
            $data.ActiveProfile = ""
            $script:SettingsChanged = $true
        }

        return $data
    }
    catch {
        Write-Warning "[CSM.TmpeSync] Failed to read build-settings.json. A new configuration will be created."
        return @{ ActiveProfile = ""; Profiles = @{} }
    }
}

function Save-BuildSettings {
    param([hashtable]$Settings)

    if (-not $script:SettingsChanged) {
        return
    }

    $directory = Split-Path -Parent $script:ConfigPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $Settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $script:ConfigPath -Encoding UTF8
    $script:SettingsChanged = $false
}

function Set-ProfileValue {
    param(
        [hashtable]$Profile,
        [string]$Key,
        [object]$Value
    )

    if ($null -eq $Profile) {
        return
    }

    $current = $null
    if ($Profile.ContainsKey($Key)) {
        $current = $Profile[$Key]
    }

    if ($current -ne $Value) {
        $Profile[$Key] = $Value
        $script:SettingsChanged = $true
    }
}

function Resolve-AbsolutePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return (Join-Path $script:RepoRoot $Path)
}

function Get-AssemblyNameFromProject {
    param([string]$ProjectPath)

    if ([string]::IsNullOrWhiteSpace($ProjectPath) -or -not (Test-Path $ProjectPath)) {
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $ProjectPath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }

        $xml = [xml]$raw
        $propertyGroups = $xml.Project.PropertyGroup
        if ($null -eq $propertyGroups) {
            return $null
        }

        if ($propertyGroups -isnot [System.Array]) {
            $propertyGroups = @($propertyGroups)
        }

        foreach ($group in $propertyGroups) {
            if ($group.AssemblyName) {
                return [string]$group.AssemblyName
            }
        }
    }
    catch {
        return $null
    }

    return $null
}

function Get-ModProjects {
    if ($script:ModProjectsCache) {
        return $script:ModProjectsCache
    }

    $srcDir = Join-Path $script:RepoRoot 'src'
    if (-not (Test-Path $srcDir)) {
        $script:ModProjectsCache = @()
        return $script:ModProjectsCache
    }

    $script:ModProjectsCache = @()

    $rootProjects = Get-ChildItem -LiteralPath $srcDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'CSM.TmpeSync*.csproj' }

    foreach ($project in $rootProjects) {
        $assemblyName = Get-AssemblyNameFromProject -ProjectPath $project.FullName
        if ([string]::IsNullOrWhiteSpace($assemblyName)) {
            $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
        }

        $script:ModProjectsCache += [pscustomobject]@{
            ProjectPath  = $project.FullName
            AssemblyName = $assemblyName
        }
    }

    $projectDirectories = Get-ChildItem -Path $srcDir -Directory -ErrorAction SilentlyContinue

    foreach ($directory in $projectDirectories) {
        $projectFiles = Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'CSM.TmpeSync*.csproj' }

        foreach ($project in $projectFiles) {
            $assemblyName = Get-AssemblyNameFromProject -ProjectPath $project.FullName
            if ([string]::IsNullOrWhiteSpace($assemblyName)) {
                $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
            }

            $script:ModProjectsCache += [pscustomobject]@{
                ProjectPath  = $project.FullName
                AssemblyName = $assemblyName
            }
        }
    }

    $script:ModProjectsCache = $script:ModProjectsCache | Sort-Object -Property ProjectPath -Unique

    return $script:ModProjectsCache
}

function Get-ModAssemblyNames {
    $projects = Get-ModProjects
    if (-not $projects -or $projects.Count -eq 0) {
        return @()
    }

    return $projects |
        Select-Object -ExpandProperty AssemblyName |
        Sort-Object -Unique |
        ForEach-Object { "{0}.dll" -f $_ }
}

function Get-DefaultProfileName {
    return "Steam"
}

function Get-AvailableProfiles {
    return @("Steam", "Custom")
}

function Prompt-ForProfileSelection {
    param(
        [string[]]$AvailableProfiles,
        [string]$CurrentProfile
    )

    if ($AvailableProfiles.Count -eq 0) {
        throw "No profiles are available."
    }

    if (-not $Host.UI -or -not $Host.UI.RawUI) {
        if ($AvailableProfiles -contains $CurrentProfile) {
            return $CurrentProfile
        }

        return $AvailableProfiles[0]
    }

    Write-Host "Select Cities: Skylines game version:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $AvailableProfiles.Count; $i++) {
        Write-Host ("[{0}] {1}" -f ($i + 1), $AvailableProfiles[$i])
    }

    $currentIndex = [Array]::IndexOf($AvailableProfiles, $CurrentProfile)
    if ($currentIndex -lt 0) {
        $currentIndex = 0
    }

    $defaultChoice = ($currentIndex + 1).ToString()
    $selection = Prompt-ForInput -Message "Enter the desired option" -DefaultValue $defaultChoice

    [int]$choice = 0
    if ([int]::TryParse($selection, [ref]$choice)) {
        if ($choice -ge 1 -and $choice -le $AvailableProfiles.Count) {
            return $AvailableProfiles[$choice - 1]
        }

        return $AvailableProfiles[0]
    }

    return $AvailableProfiles[$currentIndex]
}

function Ensure-Profile {
    param(
        [hashtable]$Settings,
        [string]$ProfileName
    )

    $profiles = Ensure-Hashtable $Settings.Profiles
    $Settings.Profiles = $profiles

    if (-not $profiles.ContainsKey($ProfileName)) {
        $profiles[$ProfileName] = @{}
        $script:SettingsChanged = $true
    }

    $profile = Ensure-Hashtable $profiles[$ProfileName]
    $profiles[$ProfileName] = $profile
    return $profile
}

function Prompt-ForInput {
    param(
        [string]$Message,
        [string]$DefaultValue
    )

    if (-not $Host.UI -or -not $Host.UI.RawUI) {
        return if ([string]::IsNullOrWhiteSpace($DefaultValue)) { "" } else { $DefaultValue }
    }

    $prompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) { $Message } else { "$Message [`$DefaultValue`]" }
    $response = Read-Host $prompt
    if ([string]::IsNullOrWhiteSpace($response)) {
        return $DefaultValue
    }

    return $response
}

function Prompt-ForConfirmation {
    param(
        [string]$Message,
        [bool]$Default = $true
    )

    if (-not $Host.UI -or -not $Host.UI.RawUI) {
        return $Default
    }

    $suffix = if ($Default) { "[Y/n]" } else { "[y/N]" }

    while ($true) {
        $response = Read-Host "$Message $suffix"

        if ([string]::IsNullOrWhiteSpace($response)) {
            return $Default
        }

        switch ($response.Trim().ToLowerInvariant()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
        }

        Write-Host "Please answer with yes or no." -ForegroundColor Yellow
    }
}

function Get-SteamDefaults {
    $steamGameDir = 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cities_Skylines'
    $steamWorkshopBase = 'C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\255710'
    $steamHarmony = Join-Path $steamWorkshopBase '2040656402'
    $steamCsm = Join-Path $steamWorkshopBase '1558438291'
    $steamTmpe = Join-Path $steamWorkshopBase '1637663252'
    $modRoot = 'C:\\Users\\mail\\AppData\\Local\\Colossal Order\\Cities_Skylines\\Addons\\Mods'

    return @{
        GameDirectory      = $steamGameDir
        SteamModsDir       = $steamWorkshopBase
        HarmonySourceDir   = $steamHarmony
        CsmSourceDir       = $steamCsm
        TmpeSourceDir      = $steamTmpe
        ModRootDirectory   = $modRoot
    }
}

function Configure-Profile {
    param(
        [hashtable]$Settings,
        [string]$ProfileName,
        [string]$ParameterGameDir,
        [string]$ParameterModRoot
    )

    $defaults = Get-SteamDefaults
    $profile = Ensure-Profile -Settings $Settings -ProfileName $ProfileName

    $currentGameDir = if ($ParameterGameDir) { $ParameterGameDir } elseif ($profile.ContainsKey('GameDirectory')) { [string]$profile.GameDirectory } else { $defaults.GameDirectory }
    $gameDir = Prompt-ForInput -Message "Path to Cities: Skylines game directory" -DefaultValue $currentGameDir
    if ([string]::IsNullOrWhiteSpace($gameDir)) {
        throw "Cities: Skylines game directory is required."
    }

    $currentModRoot = if ($ParameterModRoot) { $ParameterModRoot } elseif ($profile.ContainsKey('ModRootDirectory')) { [string]$profile.ModRootDirectory } else { $defaults.ModRootDirectory }
    $modRoot = Prompt-ForInput -Message "Path where the mod should be installed" -DefaultValue $currentModRoot
    if ([string]::IsNullOrWhiteSpace($modRoot)) {
        throw "Mod installation directory is required."
    }

    $modDirectory = Join-Path $modRoot 'CSM.TmpeSync'

    if ($ProfileName -eq 'Steam') {
        $confirmed = Prompt-ForConfirmation -Message "Have you subscribed to the latest Harmony, TM:PE, and CSM workshop items?" -Default $true
        if (-not $confirmed) {
            throw "Subscribe to Harmony, TM:PE, and CSM on the Steam Workshop before continuing."
        }
    }

    Set-ProfileValue -Profile $profile -Key 'GameDirectory' -Value $gameDir
    Set-ProfileValue -Profile $profile -Key 'ModRootDirectory' -Value $modRoot
    Set-ProfileValue -Profile $profile -Key 'ModDirectory' -Value $modDirectory
    Set-ProfileValue -Profile $profile -Key 'SteamModsDir' -Value $defaults.SteamModsDir
    Set-ProfileValue -Profile $profile -Key 'HarmonySourceDir' -Value $defaults.HarmonySourceDir
    Set-ProfileValue -Profile $profile -Key 'CsmSourceDir' -Value $defaults.CsmSourceDir
    Set-ProfileValue -Profile $profile -Key 'TmpeSourceDir' -Value $defaults.TmpeSourceDir

    $harmonyLibDir = Join-Path $script:LibRoot 'Harmony'
    $csmLibDir = Join-Path $script:LibRoot 'CSM'
    $tmpeLibDir = Join-Path $script:LibRoot 'TMPE'
    Set-ProfileValue -Profile $profile -Key 'HarmonyDllDir' -Value $harmonyLibDir
    Set-ProfileValue -Profile $profile -Key 'TmpeDir' -Value $tmpeLibDir
    $csmApiPath = Join-Path $csmLibDir 'CSM.API.dll'
    Set-ProfileValue -Profile $profile -Key 'CsmApiDllPath' -Value $csmApiPath
    Set-ProfileValue -Profile $profile -Key 'CsmLibDir' -Value $csmLibDir

    $available = Get-AvailableProfiles
    if ($available -notcontains $ProfileName) {
        throw "Unsupported profile: $ProfileName"
    }

    if ($Settings.ActiveProfile -ne $ProfileName) {
        $Settings.ActiveProfile = $ProfileName
        $script:SettingsChanged = $true
    }

    Write-Host "[CSM.TmpeSync] Profile '$ProfileName' configured." -ForegroundColor Cyan
}

function Determine-ActiveProfile {
    param(
        [hashtable]$Settings,
        [string]$RequestedProfile
    )

    $available = Get-AvailableProfiles
    if (-not [string]::IsNullOrWhiteSpace($RequestedProfile)) {
        if ($available -notcontains $RequestedProfile) {
            throw "Unknown profile '$RequestedProfile'. Available profiles: $($available -join ', ')"
        }
        return $RequestedProfile
    }

    if (-not [string]::IsNullOrWhiteSpace($Settings.ActiveProfile)) {
        return $Settings.ActiveProfile
    }

    return (Get-DefaultProfileName)
}

function Test-ProfileIsConfigured {
    param([hashtable]$Profile)

    if ($null -eq $Profile) {
        return $false
    }

    if (-not $Profile.ContainsKey('GameDirectory')) {
        return $false
    }

    $gameDir = [string]$Profile.GameDirectory
    return -not [string]::IsNullOrWhiteSpace($gameDir)
}

function Ensure-ConfiguredProfile {
    param(
        [hashtable]$Settings,
        [string]$ProfileName
    )

    $profile = Ensure-Profile -Settings $Settings -ProfileName $ProfileName

    if (-not (Test-ProfileIsConfigured -Profile $profile)) {
        throw "Profile '$ProfileName' is not configured. Run build.ps1 -Configure first."
    }

    return $profile
}

function Reset-Directory {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Destination path is required."
    }

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExcludeExtensions = @()
    )

    if (-not (Test-Path $Source)) {
        throw "Source directory not found: $Source"
    }

    Reset-Directory -Path $Destination

    $normalizedExclusions = $ExcludeExtensions | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.ToLowerInvariant() }

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
        }
        else {
            if ($normalizedExclusions -and ($normalizedExclusions -contains $_.Extension.ToLowerInvariant())) {
                return
            }
            Copy-Item -LiteralPath $_.FullName -Destination $Destination -Force
        }
    }
}

function Ensure-DirectoryExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description is required."
    }

    if (-not (Test-Path $Path)) {
        throw "$Description not found: $Path"
    }
}

function Invoke-BuildProject {
    param(
        [hashtable]$Profile,
        [string]$Configuration
    )

    $citiesDir = Resolve-AbsolutePath -Path ([string]$Profile.GameDirectory)
    $harmonyDir = Resolve-AbsolutePath -Path ([string]$Profile.HarmonyDllDir)
    $csmApiPath = Resolve-AbsolutePath -Path ([string]$Profile.CsmApiDllPath)

    $propertySpecs = @(
        @{ Name = 'CitiesSkylinesDir'; Value = $citiesDir },
        @{ Name = 'HarmonyDllDir'; Value = $harmonyDir },
        @{ Name = 'CsmApiDllPath'; Value = $csmApiPath }
    )

    if ($Profile.ContainsKey('TmpeDir') -and -not [string]::IsNullOrWhiteSpace([string]$Profile.TmpeDir)) {
        $tmpeDir = Resolve-AbsolutePath -Path ([string]$Profile.TmpeDir)
        $propertySpecs += @{ Name = 'TmpeDir'; Value = $tmpeDir }
    }

    if ($Profile.ContainsKey('SteamModsDir') -and -not [string]::IsNullOrWhiteSpace([string]$Profile.SteamModsDir)) {
        $steamMods = Resolve-AbsolutePath -Path ([string]$Profile.SteamModsDir)
        $propertySpecs += @{ Name = 'SteamModsDir'; Value = $steamMods }
    }

    if ($Profile.ContainsKey('ModDirectory') -and -not [string]::IsNullOrWhiteSpace([string]$Profile.ModDirectory)) {
        $modDir = Resolve-AbsolutePath -Path ([string]$Profile.ModDirectory)
        $propertySpecs += @(
            @{ Name = 'ModDirectory'; Value = $modDir },
            @{ Name = 'ModsOutDir'; Value = $modDir }
        )
    }

    $net35Info = Resolve-Net35ReferenceInfo
    if ($net35Info) {
        $propertySpecs += @(
            @{ Name = 'TargetFrameworkRootPath'; Value = $net35Info.RootPath },
            @{ Name = 'FrameworkPathOverride'; Value = $net35Info.FrameworkPath },
            @{ Name = 'ReferencePath'; Value = $net35Info.FrameworkPath }
        )
    }

    $dotnetArguments = @('build', $script:ProjectPath, '-c', $Configuration, '/restore', '--nologo')
    $msbuildArguments = @()

    foreach ($spec in $propertySpecs) {
        $dotnetArg = Format-DotnetProperty -Name $spec.Name -Value $spec.Value
        if ($dotnetArg) {
            $dotnetArguments += $dotnetArg
        }

        $msbuildArg = Format-MsBuildProperty -Name $spec.Name -Value $spec.Value
        if ($msbuildArg) {
            $msbuildArguments += $msbuildArg
        }
    }

    $buildSucceeded = $false

    if ($script:IsWindowsPlatform) {
        try {
            $msbuildPath = Resolve-MsBuildPath
            $msbuildInvocation = @(
                $script:ProjectPath,
                '/restore',
                '/t:Build',
                "/p:Configuration=$Configuration",
                '/nologo'
            ) + $msbuildArguments

            Write-Host "[CSM.TmpeSync] Building ($Configuration) with MSBuild..." -ForegroundColor Cyan
            & $msbuildPath @msbuildInvocation
            if ($LASTEXITCODE -ne 0) {
                throw "MSBuild failed with exit code $LASTEXITCODE."
            }
            $buildSucceeded = $true
        }
        catch {
            if ($_.Exception.Message -notlike '*MSBuild.exe not found*') {
                throw
            }
        }
    }

    if (-not $buildSucceeded) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw "dotnet CLI not found. Install the .NET SDK."
        }

        Write-Host "[CSM.TmpeSync] Building ($Configuration) with dotnet..." -ForegroundColor Cyan
        & $dotnet.Source @dotnetArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }
}

function Get-OutputDirectory {
    param(
        [string]$Configuration,
        [string]$ProjectPath = $script:ProjectPath
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        throw "Project path not configured."
    }

    $projectDirectory = Split-Path -Parent $ProjectPath
    return Join-Path $projectDirectory ("bin/{0}/net35" -f $Configuration)
}

. (Join-Path $PSScriptRoot 'update.ps1')
. (Join-Path $PSScriptRoot 'install.ps1')

if (-not ($Update -or $Build -or $Install -or $Configure)) {
    Write-Host "[CSM.TmpeSync] No action specified. Use -Update, -Build, -Install and/or -Configure." -ForegroundColor Yellow
    exit 0
}

$settings = Load-BuildSettings
$availableProfiles = Get-AvailableProfiles
$profileName = Determine-ActiveProfile -Settings $settings -RequestedProfile $Profile
$shouldConfigure = $Configure
$requiresConfiguredProfile = ($Update -or $Build -or $Install)
$promptedForProfile = $false

if ($requiresConfiguredProfile) {
    $profilesTable = Ensure-Hashtable $settings.Profiles
    $settings.Profiles = $profilesTable

    if ([string]::IsNullOrWhiteSpace($Profile)) {
        $profileName = Prompt-ForProfileSelection -AvailableProfiles $availableProfiles -CurrentProfile $profileName
        $promptedForProfile = $true
    }

    $existingProfile = if ($profilesTable.ContainsKey($profileName)) {
        Ensure-Hashtable $profilesTable[$profileName]
    }

    if (-not $shouldConfigure -and -not (Test-ProfileIsConfigured -Profile $existingProfile)) {
        Write-Host "[CSM.TmpeSync] No configured profile found. Launching configuration." -ForegroundColor Yellow
        $shouldConfigure = $true
    }
}

if ($shouldConfigure -and -not $promptedForProfile -and [string]::IsNullOrWhiteSpace($Profile)) {
    $profileName = Prompt-ForProfileSelection -AvailableProfiles $availableProfiles -CurrentProfile $profileName
    $promptedForProfile = $true
}

if ($shouldConfigure) {
    Configure-Profile -Settings $settings -ProfileName $profileName -ParameterGameDir $GameDirectory -ParameterModRoot $ModRootDirectory
}

$usingPromptedProfile = ([string]::IsNullOrWhiteSpace($Profile) -and $promptedForProfile)
if ($usingPromptedProfile -and $settings.ActiveProfile -ne $profileName) {
    $settings.ActiveProfile = $profileName
    $script:SettingsChanged = $true
}

$profile = Ensure-Profile -Settings $settings -ProfileName $profileName

if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
    Set-ProfileValue -Profile $profile -Key 'GameDirectory' -Value $GameDirectory
}
if (-not [string]::IsNullOrWhiteSpace($SteamModsDir)) {
    Set-ProfileValue -Profile $profile -Key 'SteamModsDir' -Value $SteamModsDir
}
if (-not [string]::IsNullOrWhiteSpace($HarmonySourceDir)) {
    Set-ProfileValue -Profile $profile -Key 'HarmonySourceDir' -Value $HarmonySourceDir
}
if (-not [string]::IsNullOrWhiteSpace($CsmSourceDir)) {
    Set-ProfileValue -Profile $profile -Key 'CsmSourceDir' -Value $CsmSourceDir
}
if (-not [string]::IsNullOrWhiteSpace($TmpeSourceDir)) {
    Set-ProfileValue -Profile $profile -Key 'TmpeSourceDir' -Value $TmpeSourceDir
}
if (-not [string]::IsNullOrWhiteSpace($HarmonyDllDir)) {
    Set-ProfileValue -Profile $profile -Key 'HarmonyDllDir' -Value $HarmonyDllDir
}
if (-not [string]::IsNullOrWhiteSpace($CsmApiDllPath)) {
    Set-ProfileValue -Profile $profile -Key 'CsmApiDllPath' -Value $CsmApiDllPath
}
if (-not [string]::IsNullOrWhiteSpace($TmpeDir)) {
    Set-ProfileValue -Profile $profile -Key 'TmpeDir' -Value $TmpeDir
}
if (-not [string]::IsNullOrWhiteSpace($ModRootDirectory)) {
    Set-ProfileValue -Profile $profile -Key 'ModRootDirectory' -Value $ModRootDirectory
    if ([string]::IsNullOrWhiteSpace($ModDirectory)) {
        $ModDirectory = Join-Path $ModRootDirectory 'CSM.TmpeSync'
    }
}
if (-not [string]::IsNullOrWhiteSpace($ModDirectory)) {
    Set-ProfileValue -Profile $profile -Key 'ModDirectory' -Value $ModDirectory
}

if ($Update) {
    $configuredProfile = Ensure-ConfiguredProfile -Settings $settings -ProfileName $profileName
    Update-Dependencies -Profile $configuredProfile

    $updateScriptParams = @{
        SkipBuildStep      = $true
        Configure          = $Configure
        Profile            = $profileName
        GameDirectory      = $GameDirectory
        SteamModsDir       = $SteamModsDir
        HarmonySourceDir   = $HarmonySourceDir
        CsmSourceDir       = $CsmSourceDir
        TmpeSourceDir      = $TmpeSourceDir
        HarmonyDllDir      = $HarmonyDllDir
        CsmApiDllPath      = $CsmApiDllPath
        TmpeDir            = $TmpeDir
        ModDirectory       = $ModDirectory
        ModRootDirectory   = $ModRootDirectory
        SubtreesNoSquash   = $false
        SubtreesDryRun     = $false
        SubtreesAutoStash  = $false
    }

    Invoke-CsmTmpeSyncUpdate @updateScriptParams
}

if ($Build) {
    $configuredProfile = Ensure-ConfiguredProfile -Settings $settings -ProfileName $profileName
    Invoke-BuildProject -Profile $configuredProfile -Configuration $Configuration
}

if ($Install) {
    $configuredProfile = Ensure-ConfiguredProfile -Settings $settings -ProfileName $profileName
    Invoke-InstallMod -Profile $configuredProfile -Configuration $Configuration -OverrideModDirectory $ModDirectory
}

Save-BuildSettings -Settings $settings

Write-Host "[CSM.TmpeSync] Done." -ForegroundColor Green
