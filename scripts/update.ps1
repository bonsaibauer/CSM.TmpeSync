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
    [string]$ModRootDirectory = "",
    [switch]$SubmodulesDryRun = $false,
    [switch]$SkipSubmodules = $false
)

$ErrorActionPreference = "Stop"

$Script:ManagedSubmoduleRepos = @(
    @{ Name = "TMPE"; Url = "https://github.com/CitiesSkylinesMods/TMPE"; UseLatestRelease = $true },
    @{ Name = "CSM"; Url = "https://github.com/CitiesSkylinesMultiplayer/CSM"; UseLatestRelease = $true },
    @{ Name = "CitiesHarmony"; Url = "https://github.com/boformer/CitiesHarmony"; UseLatestRelease = $true }
)
$Script:SelfRepoUrl = "https://github.com/bonsaibauer/CSM.TmpeSync"
$Script:SubmoduleBaseRelative = "submodule"
$Script:ModMetadataRelativePath = "src/CSM.TmpeSync/Mod/ModMetadata.cs"

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

function Invoke-CommandHelper {
    param(
        [Parameter(Mandatory = $true)][string[]]$Command,
        [string]$WorkingDirectory = $null,
        [switch]$Check,
        [switch]$Capture,
        [switch]$DryRun,
        [switch]$PrintOutput
    )

    if (-not $PSBoundParameters.ContainsKey('Check')) { $Check = $true }
    if (-not $PSBoundParameters.ContainsKey('Capture')) { $Capture = $true }
    if (-not $PSBoundParameters.ContainsKey('PrintOutput')) { $PrintOutput = $true }

    if ($Command.Count -eq 0) {
        throw "Command must not be empty"
    }

    $cmdStr = $Command -join ' '
    if ($DryRun) {
        Write-Host "[DRY-RUN] $cmdStr"
        $global:LASTEXITCODE = 0
        return [pscustomobject]@{ ExitCode = 0; StdOut = "" }
    }

    $exe = $Command[0]
    $args = @()
    if ($Command.Count -gt 1) {
        $args = $Command[1..($Command.Count - 1)]
    }

    Write-Host "\$ $cmdStr"

    if ($WorkingDirectory) { Push-Location $WorkingDirectory }
    try {
        if ($Capture) {
            $output = & $exe @args 2>&1
        } else {
            & $exe @args
            $output = @()
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
    }

    if ($Capture -and $PrintOutput -and $output) {
        if ($output -is [Array]) {
            Write-Host (($output | ForEach-Object { $_.ToString() }) -join "`n")
        } else {
            Write-Host $output
        }
    }

    if ($Check -and $exitCode -ne 0) {
        $text = if ($Capture -and $output) { ($output | ForEach-Object { $_.ToString() }) -join "`n" } else { "" }
        throw "Command failed ($exitCode): $cmdStr`n$text"
    }

    $stdOut = if ($Capture -and $output) {
        if ($output -is [Array]) { ($output | ForEach-Object { $_.ToString() }) -join "`n" } else { [string]$output }
    } else {
        ""
    }

    $global:LASTEXITCODE = $exitCode
    return [pscustomobject]@{ ExitCode = $exitCode; StdOut = $stdOut }
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    try {
        return [System.IO.Path]::GetRelativePath($BasePath, $TargetPath)
    }
    catch {
        $baseTrimmed = $BasePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $baseUri = New-Object System.Uri(($baseTrimmed + [System.IO.Path]::DirectorySeparatorChar))
        $targetUri = New-Object System.Uri($TargetPath)
        return $baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    }
}

function Ensure-GitRootAndChdir {
    $result = Invoke-CommandHelper -Command @('git', 'rev-parse', '--show-toplevel') -PrintOutput:$false
    $root = ($result.StdOut).Trim()
    if (-not $root) {
        throw "Unable to determine git repository root"
    }
    $current = (Get-Location).ProviderPath
    if ([System.IO.Path]::GetFullPath($current) -ne [System.IO.Path]::GetFullPath($root)) {
        Write-Host "Wechsle ins Repo-Root: $root"
        Set-Location $root
    }
    return $root
}

function Test-RepoIsClean {
    Invoke-CommandHelper -Command @('git', 'update-index', '-q', '--refresh') -Check:$false -Capture:$false -PrintOutput:$false | Out-Null

    $wt = Invoke-CommandHelper -Command @('git', 'diff', '--no-ext-diff', '--quiet', '--exit-code') -Check:$false -Capture:$false -PrintOutput:$false
    $idx = Invoke-CommandHelper -Command @('git', 'diff', '--cached', '--no-ext-diff', '--quiet', '--exit-code') -Check:$false -Capture:$false -PrintOutput:$false
    $untracked = Invoke-CommandHelper -Command @('git', 'ls-files', '--others', '--exclude-standard') -Check:$false -PrintOutput:$false

    $untrackedEmpty = [string]::IsNullOrWhiteSpace($untracked.StdOut)
    return ($wt.ExitCode -eq 0) -and ($idx.ExitCode -eq 0) -and $untrackedEmpty
}

function Commit-RepoIfDirty {
    param(
        [string]$Message,
        [switch]$DryRun
    )

    if (Test-RepoIsClean) {
        Write-Host "Repo is clean - no commit needed."
        return $false
    }
    Write-Host "Repo is dirty - committing everything..."
    Invoke-CommandHelper -Command @('git', 'add', '-A') -DryRun:$DryRun | Out-Null
    Invoke-CommandHelper -Command @('git', 'commit', '-m', $Message) -DryRun:$DryRun | Out-Null
    return $true
}

function Commit-PrefixIfNeeded {
    param(
        [string]$PrefixRelative,
        [string]$Message,
        [switch]$DryRun
    )

    Invoke-CommandHelper -Command @('git', 'add', '-A', '--', $PrefixRelative) -DryRun:$DryRun | Out-Null
    $diff = Invoke-CommandHelper -Command @('git', 'diff', '--cached', '--quiet', '--', $PrefixRelative) -Check:$false -Capture:$false -PrintOutput:$false
    if ($diff.ExitCode -eq 1) {
        Write-Host "Committing changes under $PrefixRelative ..."
        Invoke-CommandHelper -Command @('git', 'commit', '-m', $Message) -DryRun:$DryRun | Out-Null
        return $true
    }
    Write-Host "Nothing to commit under $PrefixRelative."
    return $false
}

function Update-ModMetadataFile {
    param(
        [string]$RepoRoot,
        [hashtable]$ReleaseRefs,
        [hashtable]$LegacyReleaseRefs,
        [switch]$DryRun
    )

    $path = Join-Path $RepoRoot $Script:ModMetadataRelativePath
    $directory = Split-Path -Path $path -Parent
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $existingVersion = '0.1.0.0'
    $currentText = ''
    if (Test-Path $path) {
        try {
            $currentText = Get-Content -Path $path -Raw -Encoding UTF8
        }
        catch {
            $currentText = ''
        }
        $match = [regex]::Match($currentText, 'NewVersion\s*=\s*"([^\"]*)"')
        if ($match.Success) {
            $existingVersion = $match.Groups[1].Value
        }
    }

    if ($null -eq $LegacyReleaseRefs) { $LegacyReleaseRefs = @{} }

    $entries = @(
        @{ Name = 'CSM.TmpeSync'; LatestConst = 'LatestCsmTmpeSyncReleaseTag'; LegacyConst = 'LegacyCsmTmpeSyncReleaseTags'; Description = 'CSM TM:PE Sync' },
        @{ Name = 'TMPE'; LatestConst = 'LatestTmpeReleaseTag'; LegacyConst = 'LegacyTmpeReleaseTags'; Description = 'Traffic Manager: President Edition' },
        @{ Name = 'CSM'; LatestConst = 'LatestCsmReleaseTag'; LegacyConst = 'LegacyCsmReleaseTags'; Description = 'Cities: Skylines Multiplayer' },
        @{ Name = 'CitiesHarmony'; LatestConst = 'LatestCitiesHarmonyReleaseTag'; LegacyConst = 'LegacyCitiesHarmonyReleaseTags'; Description = 'Cities Harmony' }
    )

    $escapeValue = {
        param([string]$value)
        if ($null -eq $value) { return '' }
        $v = $value -replace '\\', '\\\\'
        return $v -replace '"', '\\"'
    }

    $extractExisting = {
        param([string]$constName, [string]$text)
        if ([string]::IsNullOrEmpty($text)) { return '' }
        $pattern = [string]::Format('{0}\\s*=\\s*"([^\"]*)"', [regex]::Escape($constName))
        $m = [regex]::Match($text, $pattern)
        if ($m.Success) { return $m.Groups[1].Value }
        return ''
    }

    $extractExistingArray = {
        param([string]$constName, [string]$text)
        if ([string]::IsNullOrEmpty($text)) { return @() }
        $pattern = [string]::Format('internal\s+static\s+readonly\s+string\[\]\s+{0}\s*=\s*new\s*\[\]\s*\{{(.*?)\}};', [regex]::Escape($constName))
        $m = [regex]::Match($text, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if (-not $m.Success) { return @() }
        $content = $m.Groups[1].Value
        $results = @()
        foreach ($match in [regex]::Matches($content, '"((?:\\.|[^"\\])*)"')) {
            $value = $match.Groups[1].Value
            $results += [System.Text.RegularExpressions.Regex]::Unescape($value)
        }
        return $results
    }

    $escapedVersion = & $escapeValue $existingVersion

    $lines = @(
        '// <auto-generated>',
        '// This file is generated by scripts/update.ps1. Do not edit manually.',
        '// </auto-generated>',
        '',
        'namespace CSM.TmpeSync.Mod',
        '{',
        '    internal static class ModMetadata',
        '    {',
        '        /// <summary>',
        '        /// Current version of the CSM TM:PE Sync mod. Update this value when publishing new builds.',
        '        /// </summary>',
        "        internal const string NewVersion = ""$escapedVersion"";",
        ''
    )

    foreach ($entry in $entries) {
        $currentValue = & $extractExisting $entry.LatestConst $currentText
        $value = $ReleaseRefs[$entry.Name]
        if ([string]::IsNullOrEmpty($value)) {
            $value = $currentValue
        }
        $escapedValue = & $escapeValue $value
        $lines += '        /// <summary>'
        $lines += "        /// Latest release tag for $($entry.Description)."
        $lines += '        /// </summary>'
        $lines += "        internal const string $($entry.LatestConst) = ""$escapedValue"";"
        $lines += ''

        $legacyValues = @()
        if ($LegacyReleaseRefs.ContainsKey($entry.Name)) {
            $legacyValues = $LegacyReleaseRefs[$entry.Name]
        }
        if ($null -eq $legacyValues) { $legacyValues = @() }
        if ($legacyValues.Count -eq 0) {
            $legacyValues = & $extractExistingArray $entry.LegacyConst $currentText
        }

        if ($value -and $legacyValues) {
            $legacyValues = $legacyValues | Where-Object { $_ -ne $value }
        }

        $escapedLegacy = @()
        foreach ($legacy in $legacyValues) {
            if (-not [string]::IsNullOrEmpty($legacy)) {
                $escapedLegacy += & $escapeValue $legacy
            }
        }

        $lines += '        /// <summary>'
        $lines += "        /// Legacy release tags for $($entry.Description) (excluding the latest)."
        $lines += '        /// </summary>'
        if ($escapedLegacy.Count -eq 0) {
            $lines += "        internal static readonly string[] $($entry.LegacyConst) = new string[0];"
        }
        else {
            $lines += "        internal static readonly string[] $($entry.LegacyConst) = new[]"
            $lines += '        {'
            foreach ($legacyEscaped in $escapedLegacy) {
                $lines += "            `"$legacyEscaped`",";
            }
            $lines += '        };'
        }
        $lines += ''
    }

    $lines += '    }'
    $lines += '}'
    $lines += ''

    $newContent = ($lines -join "`n")

    if ($currentText -eq $newContent) {
        Write-Host "ModMetadata.cs ist bereits aktuell."
        return
    }

    if ($DryRun) {
        Write-Host "[DRY-RUN] Aktualisiere $($Script:ModMetadataRelativePath) mit neuen Release-Tags."
        return
    }

    Set-Content -Path $path -Value $newContent -Encoding UTF8
    Write-Host "ModMetadata.cs aktualisiert."

    $prefixRelative = Get-RelativePath -BasePath $RepoRoot -TargetPath $directory
    Commit-PrefixIfNeeded -PrefixRelative $prefixRelative -Message 'chore: update dependency release tags' -DryRun:$DryRun | Out-Null
}

function Get-ReleaseRefs {
    param(
        [switch]$DryRun
    )

    $releaseRefs = @{}

    foreach ($repo in $Script:ManagedSubmoduleRepos) {
        $name = $repo.Name
        $tag = ''
        if ($repo.ContainsKey('UseLatestRelease') -and $repo.UseLatestRelease) {
            $tag = Get-LatestReleaseTag -RepoUrl $repo.Url -DryRun:$DryRun
        }
        $releaseRefs[$name] = $tag
    }

    if (-not [string]::IsNullOrWhiteSpace($Script:SelfRepoUrl)) {
        $releaseRefs['CSM.TmpeSync'] = Get-LatestReleaseTag -RepoUrl $Script:SelfRepoUrl -DryRun:$DryRun
    }

    return $releaseRefs
}

function Parse-GitHubOwnerRepo {
    param([string]$Url)

    $urlTrimmed = ($Url ?? '').Trim()
    if ([string]::IsNullOrEmpty($urlTrimmed)) { return $null }

    $hostName = $null
    $owner = $null
    $repo = $null

    if ($urlTrimmed.StartsWith('git@')) {
        try {
            $parts = $urlTrimmed.Split('@', 2)
            $right = $parts[1]
            $rightParts = $right.Split(':', 2)
            $hostName = $rightParts[0]
            $pathPart = $rightParts[1]
            $segments = $pathPart.Trim('/').Split('/')
            if ($segments.Length -ge 2) {
                $owner = $segments[0]
                $repo = $segments[1]
            }
        }
        catch {
            return $null
        }
    }
    else {
        try {
            $uri = [Uri]$urlTrimmed
            $hostName = $uri.Host
            $segments = $uri.AbsolutePath.Trim('/').Split('/')
            if ($segments.Length -ge 2) {
                $owner = $segments[0]
                $repo = $segments[1]
            }
        }
        catch {
            return $null
        }
    }

    if ($repo -and $repo.EndsWith('.git')) {
        $repo = $repo.Substring(0, $repo.Length - 4)
    }

    if ($hostName -and $owner -and $repo) {
        return @{ Host = $hostName; Owner = $owner; Repo = $repo }
    }
    return $null
}

function Get-DefaultBranch {
    param(
        [string]$RepoUrl,
        [switch]$DryRun
    )

    try {
        $result = Invoke-CommandHelper -Command @('git', 'ls-remote', '--symref', $RepoUrl, 'HEAD') -DryRun:$DryRun -Check:$false -PrintOutput:$false
        $combined = $result.StdOut
        if (-not [string]::IsNullOrEmpty($combined)) {
            foreach ($line in $combined.Split("`n")) {
                $trim = $line.Trim()
                if ($trim.StartsWith('ref:') -and $trim.Contains('HEAD')) {
                    $parts = $trim.Split()
                    if ($parts.Length -ge 2 -and $parts[1].StartsWith('refs/heads/')) {
                        return $parts[1].Substring('refs/heads/'.Length)
                    }
                }
            }
        }
    }
    catch {
    }

    foreach ($candidate in @('main', 'master')) {
        $check = Invoke-CommandHelper -Command @('git', 'ls-remote', $RepoUrl, $candidate) -DryRun:$DryRun -Check:$false -PrintOutput:$false
        if (-not [string]::IsNullOrWhiteSpace($check.StdOut)) {
            return $candidate
        }
    }

    return 'main'
}

function Get-LatestReleaseTag {
    param(
        [string]$RepoUrl,
        [switch]$DryRun
    )

    $parsed = Parse-GitHubOwnerRepo -Url $RepoUrl
    if ($null -eq $parsed -or $parsed.Host.ToLowerInvariant() -ne 'github.com') {
        return ''
    }

    $apiUrl = "https://api.github.com/repos/$($parsed.Owner)/$($parsed.Repo)/releases/latest"
    Write-Host "Checking latest release for $($parsed.Owner)/$($parsed.Repo) ..."

    try {
        $response = Invoke-WebRequest -Uri $apiUrl -Headers @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'CSM.TmpeSync-update-script' } -ErrorAction Stop
    }
    catch [System.Net.WebException] {
        $webEx = $_.Exception
        if ($webEx.Response -and $webEx.Response.StatusCode.value__ -eq 404) {
            Write-Host "No releases found for $($parsed.Owner)/$($parsed.Repo)."
            return ''
        }
        Write-Host "Network error while fetching releases."
        return ''
    }
    catch {
        Write-Host "Unexpected error while fetching releases: $($_.Exception.Message)"
        return ''
    }

    if ($null -eq $response -or $response.StatusCode -ne 200) {
        return ''
    }

    try {
        $data = $response.Content | ConvertFrom-Json
        $tagValue = $null
        if ($null -ne $data) {
            $tagValue = $data | Select-Object -ExpandProperty tag_name -ErrorAction SilentlyContinue
        }
        $tag = [string]::Empty
        if ($null -ne $tagValue) { $tag = [string]$tagValue }
        $tag = $tag.Trim()
        if (-not [string]::IsNullOrEmpty($tag)) {
            Write-Host "Neuester Release-Tag: $tag"
        }
        else {
            Write-Host "Antwort enthielt keinen tag_name."
        }
        return $tag
    }
    catch {
        Write-Host "Could not read API response."
        return ''
    }
}

function Get-AllReleaseTags {
    param(
        [string]$RepoUrl,
        [switch]$DryRun
    )

    $parsed = Parse-GitHubOwnerRepo -Url $RepoUrl
    if ($null -eq $parsed -or $parsed.Host.ToLowerInvariant() -ne 'github.com') {
        return @()
    }

    $owner = $parsed.Owner
    $repo = $parsed.Repo
    $page = 1
    $perPage = 100
    $tags = @()

    Write-Host "Fetching all release tags for $owner/$repo ..."

    while ($true) {
        $apiUrl = "https://api.github.com/repos/$owner/$repo/releases?per_page=$perPage&page=$page"
        try {
            $response = Invoke-WebRequest -Uri $apiUrl -Headers @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'CSM.TmpeSync-update-script' } -ErrorAction Stop
        }
        catch [System.Net.WebException] {
            $webEx = $_.Exception
            if ($webEx.Response -and $webEx.Response.StatusCode.value__ -eq 404) {
                Write-Host "No releases found for $owner/$repo."
                return @()
            }
            Write-Host "Network error while fetching releases for $owner/$repo."
            return @()
        }
        catch {
            Write-Host "Unexpected error while fetching releases for $($owner)/$($repo): $($_.Exception.Message)"
            return @()
        }

        if ($null -eq $response -or $response.StatusCode -ne 200) {
            break
        }

        $data = @()
        try {
            $json = $response.Content | ConvertFrom-Json
            if ($null -ne $json) {
                if ($json -is [System.Array]) {
                    $data = $json
                }
                else {
                    $data = @($json)
                }
            }
        }
        catch {
            Write-Host "Could not read API response for all releases."
            break
        }

        if ($data.Count -eq 0) {
            break
        }

        foreach ($release in $data) {
            if ($null -ne $release -and $release.PSObject.Properties.Match('tag_name').Count -gt 0) {
                $tag = [string]$release.tag_name
                if (-not [string]::IsNullOrWhiteSpace($tag)) {
                    $tags += $tag.Trim()
                }
            }
        }

        if ($data.Count -lt $perPage) {
            break
        }

        $page += 1
        if ($page -gt 10) {
            Write-Host "Stopping after 10 pages to avoid excessive API calls."
            break
        }
    }

    $unique = $tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $result = @()
    foreach ($item in $unique) {
        $result += [string]$item
    }
    return $result
}

function Get-LegacyReleaseRefs {
    param(
        [hashtable]$LatestReleaseRefs,
        [switch]$DryRun
    )

    $legacyRefs = @{}

    foreach ($repo in $Script:ManagedSubmoduleRepos) {
        $name = $repo.Name
        $url = $repo.Url
        $allTags = Get-AllReleaseTags -RepoUrl $url -DryRun:$DryRun
        $latest = ''
        if ($LatestReleaseRefs.ContainsKey($name)) {
            $latest = $LatestReleaseRefs[$name]
        }
        if (-not [string]::IsNullOrEmpty($latest)) {
            $allTags = $allTags | Where-Object { $_ -ne $latest }
        }
        $legacyRefs[$name] = @($allTags)
    }

    if (-not [string]::IsNullOrWhiteSpace($Script:SelfRepoUrl)) {
        $selfTags = Get-AllReleaseTags -RepoUrl $Script:SelfRepoUrl -DryRun:$DryRun
        $selfLatest = ''
        if ($LatestReleaseRefs.ContainsKey('CSM.TmpeSync')) {
            $selfLatest = $LatestReleaseRefs['CSM.TmpeSync']
        }
        if (-not [string]::IsNullOrEmpty($selfLatest)) {
            $selfTags = $selfTags | Where-Object { $_ -ne $selfLatest }
        }
        $legacyRefs['CSM.TmpeSync'] = @($selfTags)
    }

    return $legacyRefs
}

function Parse-GitModules {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return @() }

    $result = @()
    $current = $null
    foreach ($lineRaw in Get-Content -Path $Path) {
        $line = $lineRaw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
        if ($line -match '^\[(.+)\]$') {
            if ($current -ne $null) { $result += $current }
            $section = $Matches[1]
            if ($section.ToLowerInvariant().StartsWith('submodule')) {
                $current = @{ }
            }
            else {
                $current = $null
            }
            continue
        }
        if ($current -eq $null) { continue }
        if ($line -match '^(?<key>[^=]+)=(?<value>.+)$') {
            $key = $Matches['key'].Trim()
            $value = $Matches['value'].Trim()
            $current[$key] = $value
        }
    }
    if ($current -ne $null) { $result += $current }
    return $result
}

function Resolve-SubmoduleUrl {
    param(
        [string]$ParentUrl,
        [string]$SubUrl
    )

    $u = ($SubUrl ?? '').Trim()
    if ($u -match '://') { return $u }
    if ($u.StartsWith('git@')) { return $u }

    $parsedParent = Parse-GitHubOwnerRepo -Url $ParentUrl
    if ($null -eq $parsedParent -or $parsedParent.Host.ToLowerInvariant() -ne 'github.com') { return $u }

    $parts = $u.Split('/') | Where-Object { $_ -and $_ -ne '.' }
    if ($parts.Length -eq 0) { return $u }

    if ($parts.Length -eq 1) {
        return "https://github.com/$($parsedParent.Owner)/$($parts[0])"
    }
    if ($parts.Length -eq 2 -and $parts[0] -ne '..') {
        return "https://github.com/$($parts[0])/$($parts[1])"
    }
    if ($parts[0] -eq '..' -and $parts.Length -ge 2) {
        return "https://github.com/$($parsedParent.Owner)/$($parts[-1])"
    }
    return "https://github.com/$($parsedParent.Owner)/$($parts[-1])"
}

function Test-EmptyDirectory {
    param([string]$Path)

    if (-not (Test-Path $Path -PathType Container)) { return $false }
    $items = Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    return $items -eq $null
}

function Test-SubmoduleRegistered {
    param([string]$RelativePath)

    $normalized = ($RelativePath ?? '').Replace('\\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }

    $result = Invoke-CommandHelper -Command @('git', 'config', '-f', '.gitmodules', '--get', "submodule.$normalized.url") -Check:$false -PrintOutput:$false
    return -not [string]::IsNullOrWhiteSpace($result.StdOut)
}

function Ensure-SubmoduleBranchConfig {
    param(
        [string]$RelativePath,
        [string]$Branch,
        [switch]$DryRun
    )

    if ([string]::IsNullOrWhiteSpace($Branch)) { return }
    $normalized = ($RelativePath ?? '').Replace('\\', '/').Trim('/')
    Invoke-CommandHelper -Command @('git', 'config', '-f', '.gitmodules', "submodule.$normalized.branch", $Branch) -DryRun:$DryRun -Check:$false -PrintOutput:$false | Out-Null
    Invoke-CommandHelper -Command @('git', 'submodule', 'sync', '--', $normalized) -DryRun:$DryRun | Out-Null
}

function Update-SubmoduleRecursive {
    param(
        [string]$RelativePath,
        [switch]$Remote,
        [switch]$DryRun
    )

    $normalized = ($RelativePath ?? '').Replace('\\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return }

    $args = @('git', 'submodule', 'update', '--init', '--recursive', '--progress', '--', $normalized)
    if ($Remote) { $args = @('git', 'submodule', 'update', '--init', '--recursive', '--remote', '--progress', '--', $normalized) }
    Invoke-CommandHelper -Command $args -DryRun:$DryRun | Out-Null
}

function Invoke-AddOrUpdateSubmodule {
    param(
        [string]$RepoRoot,
        [string]$Name,
        [string]$Url,
        [string]$Branch,
        [switch]$DryRun
    )

    $relativePath = Join-Path $Script:SubmoduleBaseRelative $Name
    $normalizedPath = ($relativePath ?? '').Replace('\\', '/')
    $fullPath = Join-Path $RepoRoot $relativePath

    $parentDir = Split-Path -Path $fullPath -Parent
    if (-not (Test-Path $parentDir)) {
        New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
    }

    $isRegistered = Test-SubmoduleRegistered -RelativePath $normalizedPath

    if (-not $isRegistered -and (Test-Path $fullPath)) {
        $allowForceAdd = $false
        if (-not (Test-EmptyDirectory -Path $fullPath)) {
            $repoCheck = Invoke-CommandHelper -Command @('git', '-C', $fullPath, 'rev-parse', '--is-inside-work-tree') -Check:$false -PrintOutput:$false
            if ($repoCheck.ExitCode -eq 0) {
                $allowForceAdd = $true
                Write-Host "Reusing existing git directory at '$normalizedPath' while registering submodule."
            }
            else {
                throw "Path '$normalizedPath' already exists and is not an empty directory. Please clean it up or configure it as a submodule."
            }
        }
        if (-not $DryRun) {
            if (-not $allowForceAdd -and (Test-EmptyDirectory -Path $fullPath)) {
                Remove-Item -Path $fullPath -Force
            }
        }
    }

    if (-not $isRegistered) {
        Write-Host "Adding submodule '$Name' ($Url@$Branch) -> $normalizedPath"
        $addArgs = @('git', 'submodule', 'add', '-b', $Branch, $Url, $normalizedPath)
        if ((Test-Path $fullPath -PathType Container) -and -not (Test-EmptyDirectory -Path $fullPath)) {
            $addArgs = @('git', 'submodule', 'add', '--force', '-b', $Branch, $Url, $normalizedPath)
        }
        Invoke-CommandHelper -Command $addArgs -DryRun:$DryRun | Out-Null
        Update-SubmoduleRecursive -RelativePath $normalizedPath -Remote:$true -DryRun:$DryRun
    }
    else {
        Write-Host "Updating submodule '$Name' ($Url@$Branch) under $normalizedPath"
        Ensure-SubmoduleBranchConfig -RelativePath $normalizedPath -Branch $Branch -DryRun:$DryRun
        Update-SubmoduleRecursive -RelativePath $normalizedPath -Remote:$true -DryRun:$DryRun
    }
}

function Invoke-ManageSubmodules {
    param(
        [string]$RepoRoot,
        [switch]$DryRun
    )

    Push-Location $RepoRoot
    try {
        Ensure-GitRootAndChdir | Out-Null

        $basePath = Join-Path $RepoRoot $Script:SubmoduleBaseRelative
        if (-not (Test-Path $basePath)) {
            Write-Host "Creating folder '$($Script:SubmoduleBaseRelative)' for submodules."
            if (-not $DryRun) {
                New-Item -ItemType Directory -Path $basePath -Force | Out-Null
            }
        }

        foreach ($repo in $Script:ManagedSubmoduleRepos) {
            $branch = if ($repo.ContainsKey('Branch') -and $repo.Branch) { $repo.Branch } else { Get-DefaultBranch -RepoUrl $repo.Url -DryRun:$DryRun }
            Invoke-AddOrUpdateSubmodule -RepoRoot $RepoRoot -Name $repo.Name -Url $repo.Url -Branch $branch -DryRun:$DryRun
        }
    }
    finally {
        Pop-Location
    }
}


function Invoke-CsmTmpeSyncUpdate {
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
        [string]$ModRootDirectory = "",
        [switch]$SubmodulesDryRun = $false,
        [switch]$SkipSubmodules = $false,
        [switch]$SkipBuildStep = $false
    )

    if (-not $SkipBuildStep) {
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
        if ($buildExit -ne 0) {
            throw "build.ps1 failed with exit code $buildExit"
        }
    }

    # --- Determine repo root robustly ---
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrEmpty($scriptPath)) { throw "Cannot resolve script path. Run with: pwsh -File .\scripts\update.ps1" }
    $scriptDir = Split-Path -Path $scriptPath -Parent
    $repoRoot  = Split-Path -Path $scriptDir -Parent

    # Always resolve real release tags; SubmodulesDryRun should only influence submodule handling.
    $releaseRefs = Get-ReleaseRefs -DryRun:$false
    $shouldManageSubmodules = -not $SkipSubmodules
    if (-not $SkipSubmodules -and $Host.UI -and $Host.UI.RawUI) {
        $prompt = Read-Host "Initialize/update submodules? (yes/no) [yes]"
        if ($prompt -match '^(?i)n(o)?$') {
            $shouldManageSubmodules = $false
        }
    }

    if ($shouldManageSubmodules) {
        Invoke-ManageSubmodules -RepoRoot $repoRoot -DryRun:$SubmodulesDryRun
    }
    else {
        Write-Host "Skipping submodule initialization/update."
    }
    if ($null -eq $releaseRefs) {
        $releaseRefs = @{}
    }
    $legacyReleaseRefs = Get-LegacyReleaseRefs -LatestReleaseRefs $releaseRefs -DryRun:$false
    Update-ModMetadataFile -RepoRoot $repoRoot -ReleaseRefs $releaseRefs -LegacyReleaseRefs $legacyReleaseRefs -DryRun:$false

    # --- Interactive update of NewVersion based on LatestCsmTmpeSyncReleaseTag ---
    $modMetadataPath = Join-Path $repoRoot 'src/CSM.TmpeSync/Mod/ModMetadata.cs'

    # wait up to 5s if file was just created
    $maxWaitMs = 5000
    $stepMs = 200
    $waited = 0
    while ((-not (Test-Path -Path $modMetadataPath)) -and ($waited -lt $maxWaitMs)) {
        Start-Sleep -Milliseconds $stepMs
        $waited += $stepMs
    }

    if (-not (Test-Path $modMetadataPath)) {
        throw "File not found: $modMetadataPath"
    }

    # Read file
    $content = Get-Content -Path $modMetadataPath -Raw -Encoding UTF8

    # Extract current LatestCsmTmpeSyncReleaseTag
    $tagMatch = [regex]::Match($content, 'internal\s+const\s+string\s+LatestCsmTmpeSyncReleaseTag\s*=\s*"([^\"]+)"\s*;')
    $currentTag = if ($tagMatch.Success) { $tagMatch.Groups[1].Value } else { "<unknown>" }

    Write-Host "LatestCsmTmpeSyncReleaseTag: $currentTag"
    $change = Read-Host "Do you want to change the constant 'NewVersion'? (yes/no)"

    if ($change -match '^(?i)y(es)?$') {
        $newVersion = Read-Host "Enter new string for 'NewVersion' (leave empty to keep current)"
        if (-not [string]::IsNullOrWhiteSpace($newVersion)) {
            $escaped = $newVersion -replace '"', '\\"'
            $newContent = [regex]::Replace(
                $content,
                '(?m)^\s*internal\s+const\s+string\s+NewVersion\s*=\s*"[^\"]*"\s*;',
                ("        internal const string NewVersion = ""$escaped"";")
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
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-CsmTmpeSyncUpdate @PSBoundParameters
    exit 0
}



