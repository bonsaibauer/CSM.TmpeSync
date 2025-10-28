<# 
.SYNOPSIS
    Manage subtrees and vendor submodules recursively for CSM.TmpeSync.
.DESCRIPTION
    PowerShell-Portierung des Python-Skripts manage_subtrees.py
.PARAMETER NoSquash
.PARAMETER DryRun
.PARAMETER AutoStash
#>

param(
    [switch]$NoSquash,
    [switch]$DryRun,
    [switch]$AutoStash
)

# ===== Konfiguration =====
$REPOS = @(
    @{ name = "TMPE";           url = "https://github.com/CitiesSkylinesMods/TMPE";             use_latest_release = $true },
    @{ name = "CSM";            url = "https://github.com/CitiesSkylinesMultiplayer/CSM";       use_latest_release = $true },
    @{ name = "CitiesHarmony";  url = "https://github.com/boformer/CitiesHarmony";              use_latest_release = $true }
)
$SELF_REPO_URL    = "https://github.com/bonsaibauer/CSM.TmpeSync"
$BASE_PREFIX      = "subtrees"
$MOD_METADATA_PATH= "src/CSM.TmpeSync/Mod/ModMetadata.cs"

# ===== Farben (robust) =====
$UseColor = $false
try { $UseColor = ($PSVersionTable.PSVersion.Major -ge 7) -and ($Host.UI.SupportsVirtualTerminal -eq $true) } catch { $UseColor = $false }
function C {
    param([string]$Text, [ValidateSet('green','yellow','red','cyan')] [string]$Color)
    if (-not $UseColor) { return $Text }
    $map = @{ green="`e[32m"; yellow="`e[33m"; red="`e[31m"; cyan="`e[36m"; reset="`e[0m" }
    if (-not $map.ContainsKey($Color)) { return $Text }
    return "$($map[$Color])$Text$($map['reset'])"
}

# ===== Shell Helpers =====
function Invoke-Cmd {
    param(
        [Parameter(Mandatory)] [string[]]$Cmd,
        [string]$Cwd = $null,
        [switch]$Check,
        [switch]$CaptureOutput,
        [switch]$DryRun
    )
    $cmdStr = ($Cmd -join " ")
    if ($DryRun) {
        Write-Host "$(C('[DRY-RUN]','cyan')) $cmdStr"
        return @{ ExitCode = 0; StdOut = ""; StdErr = "" }
    }
    Write-Host "$(C('$ ','cyan'))$cmdStr"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName  = $Cmd[0]
    $psi.Arguments = ($Cmd | Select-Object -Skip 1) -join " "
    if ($Cwd) { $psi.WorkingDirectory = $Cwd }
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.UseShellExecute = $false
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($CaptureOutput -and $stdout.Trim()) { Write-Host $stdout.Trim() }
    if ($Check -and $p.ExitCode -ne 0) {
        Write-Host (C(("Fehler ({0}):" -f $p.ExitCode), 'red'))
        if ($stderr) { Write-Host $stderr.Trim() }
        exit $p.ExitCode
    }
    return @{ ExitCode = $p.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function Ensure-GitRootAndChdir {
    $r = Invoke-Cmd -Cmd @("git","rev-parse","--show-toplevel") -Check -CaptureOutput:($true)
    $root = $r.StdOut.Trim()
    $cur  = (Get-Location).Path
    if ([IO.Path]::GetFullPath($cur).ToLower() -ne [IO.Path]::GetFullPath($root).ToLower()) {
        Write-Host "$(C('Wechsle ins Repo-Root:','yellow')) $root"
        Set-Location $root
    }
    return $root
}

# ===== Repo-Cleanliness =====
function Test-RepoClean {
    Invoke-Cmd -Cmd @("git","update-index","-q","--refresh") | Out-Null
    & git diff --no-ext-diff --quiet --exit-code
    $wt = ($LASTEXITCODE -eq 0)
    & git diff --cached --no-ext-diff --quiet --exit-code
    $idx = ($LASTEXITCODE -eq 0)
    $r = Invoke-Cmd -Cmd @("git","ls-files","--others","--exclude-standard")
    $untrackedEmpty = [string]::IsNullOrWhiteSpace($r.StdOut)
    return ($wt -and $idx -and $untrackedEmpty)
}

function Invoke-CommitIfDirty {
    param([string]$Message, [switch]$DryRun)
    if (Test-RepoClean) {
        Write-Host (C("Repo ist clean – kein Commit nötig.",'green'))
        return $false
    }
    Write-Host (C("Repo ist dirty – committe ALLES …",'yellow'))
    Invoke-Cmd -Cmd @("git","add","-A") -DryRun:$DryRun | Out-Null
    Invoke-Cmd -Cmd @("git","commit","-m",$Message) -DryRun:$DryRun | Out-Null
    return $true
}

function Ensure-CleanForSubtree { param([string]$StepDesc, [switch]$DryRun)
    Invoke-CommitIfDirty -Message ("chore(subtree): prepare {0}" -f $StepDesc) -DryRun:$DryRun | Out-Null
}

function Use-AutoStash {
    param([switch]$Enable)
    if (-not $Enable) {
        if (-not (Test-RepoClean)) {
            Write-Host "$(C('Working Tree ist nicht clean.','red')) Entweder committen/stashen oder nutze --auto-stash."
            exit 1
        }
        return @{ DidStash = $false; Name = "" }
    }
    $name = "auto-stash-before-subtrees"
    Write-Host "$(C('Auto-Stash aktiv.','yellow')) Stashe lokale Änderungen ($name) …"
    Invoke-Cmd -Cmd @("git","stash","push","-u","-m",$name) -Check | Out-Null
    return @{ DidStash = $true; Name = $name }
}

function Restore-AutoStash { param([bool]$DidStash)
    if ($DidStash) { Write-Host (C("Stelle Stash wieder her …",'yellow')); & git stash pop | Out-Null }
}

# ===== Commit Helper (Prefix + Fallback) =====
function Invoke-CommitPrefixIfNeeded {
    param([string]$Prefix, [string]$Message, [switch]$DryRun)
    Invoke-Cmd -Cmd @("git","add","-A","--",$Prefix) -DryRun:$DryRun | Out-Null
    & git diff --cached --quiet -- $Prefix
    $rc = $LASTEXITCODE
    if ($rc -eq 1) {
        Write-Host "$(C('Committe Änderungen unter','yellow')) $Prefix …"
        Invoke-Cmd -Cmd @("git","commit","-m",$Message) -DryRun:$DryRun | Out-Null
        return $true
    } else {
        Write-Host (C(("Nichts zu committen unter {0}." -f $Prefix),'green'))
        return $false
    }
}

# ===== ModMetadata.cs =====
function Update-ModMetadataFile {
    param([hashtable]$ReleaseRefs, [switch]$DryRun)
    $path = $MOD_METADATA_PATH
    $dir  = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    $existingVersion = "0.1.0.0"
    $currentText = ""
    if (Test-Path $path) {
        try { $currentText = Get-Content -Raw -Encoding UTF8 -Path $path } catch { $currentText = "" }
        $m = [regex]::Match($currentText,'NewVersion\s*=\s*"([^"]*)"')
        if ($m.Success) { $existingVersion = $m.Groups[1].Value }
    }

    $entries = @(
        @{ name="CSM.TmpeSync"; const="LatestCsmTmpeSyncReleaseTag"; desc="CSM TM:PE Sync" },
        @{ name="TMPE";         const="LatestTmpeReleaseTag";         desc="Traffic Manager: President Edition" },
        @{ name="CSM";          const="LatestCsmReleaseTag";           desc="Cities: Skylines Multiplayer" },
        @{ name="CitiesHarmony";const="LatestCitiesHarmonyReleaseTag"; desc="Cities Harmony" }
    )
    function Escape([string]$v) { return $v.Replace('\','\\').Replace('"','\"') }
    function ExtractExisting([string]$const) {
        if (-not $currentText) { return "" }
        $m = [regex]::Match($currentText, [regex]::Escape($const) + '\s*=\s*"([^"]*)"')
        if ($m.Success) { return $m.Groups[1].Value } else { return "" }
    }

    $lines = @(
        "// <auto-generated>",
        "// This file is generated by scripts/manage_subtrees.ps1. Do not edit manually.",
        "// </auto-generated>",
        "",
        "namespace CSM.TmpeSync.Mod",
        "{",
        "    internal static class ModMetadata",
        "    {",
        "        /// <summary>",
        "        /// Current version of the CSM TM:PE Sync mod. Update this value when publishing new builds.",
        "        /// </summary>",
        ('        internal const string NewVersion = "{0}";' -f (Escape $existingVersion)),
        ""
    )

    foreach ($e in $entries) {
        $cur = ExtractExisting $e.const
        $val = if ($ReleaseRefs.ContainsKey($e.name) -and $ReleaseRefs[$e.name]) { $ReleaseRefs[$e.name] } else { $cur }
        $lines += "        /// <summary>"
        $lines += ("        /// Latest release tag for {0}." -f $e.desc)
        $lines += "        /// </summary>"
        $lines += ('        internal const string {0} = "{1}";' -f $e.const, (Escape $val))
        $lines += ""
    }
    $lines += @("    }","}","")
    $newContent = ($lines -join "`n")

    if ((Test-Path $path) -and ($currentText -eq $newContent)) {
        Write-Host (C("ModMetadata.cs ist bereits aktuell.",'green')); return
    }
    if ($DryRun) { Write-Host "$(C('[DRY-RUN]','cyan')) Aktualisiere $path mit neuen Release-Tags."; return }
    $newContent | Out-File -FilePath $path -Encoding UTF8 -Force
    Write-Host (C("ModMetadata.cs aktualisiert.",'green'))
    Invoke-CommitPrefixIfNeeded -Prefix $dir -Message "chore: update dependency release tags" -DryRun:$DryRun | Out-Null
}

# ===== Git Basics =====
function Get-DefaultBranch {
    param([string]$RepoUrl, [switch]$DryRun)
    try {
        $r = Invoke-Cmd -Cmd @("git","ls-remote","--symref",$RepoUrl,"HEAD") -DryRun:$DryRun
        $txt = ($r.StdOut + $r.StdErr)
        foreach ($line in ($txt -split "`n")) {
            $line = $line.Trim()
            if ($line.StartsWith("ref:") -and $line.Contains("HEAD")) {
                $parts = $line -split "\s+"
                if ($parts.Length -ge 2 -and $parts[1].StartsWith("refs/heads/")) {
                    return $parts[1].Substring(11)
                }
            }
        }
    } catch {}
    foreach ($c in @("main","master")) {
        $r2 = Invoke-Cmd -Cmd @("git","ls-remote",$RepoUrl,$c) -DryRun:$DryRun
        if ($r2.StdOut -and $r2.StdOut.Trim()) { return $c }
    }
    return "main"
}

function Parse-GitHubOwnerRepo {
    param([string]$Url)
    $Url = $Url.Trim()
    $host=$null;$owner=$null;$repo=$null
    if ($Url.StartsWith("git@")) {
        try {
            $right = $Url.Split("@",2)[1]
            $hostPart, $pathPart = $right.Split(":",2)
            $host = $hostPart
            $parts = $pathPart.Trim("/").Split("/")
            if ($parts.Length -ge 2) { $owner=$parts[0]; $repo=$parts[1] }
        } catch { return $null,$null,$null }
    } else {
        try {
            $u = [uri]$Url
            $host = $u.Host
            $parts = $u.AbsolutePath.Trim("/").Split("/")
            if ($parts.Length -ge 2) { $owner=$parts[0]; $repo=$parts[1] }
        } catch { return $null,$null,$null }
    }
    if ($repo -and $repo.EndsWith(".git")) { $repo = $repo.Substring(0,$repo.Length-4) }
    if ($host -and $owner -and $repo) { return $host,$owner,$repo } else { return $null,$null,$null }
}

function Get-LatestReleaseTag {
    param([string]$RepoUrl)
    $host,$owner,$repo = Parse-GitHubOwnerRepo $RepoUrl
    if (-not $host -or $host.ToLower() -ne "github.com" -or -not $owner -or -not $repo) { return "" }
    $api = "https://api.github.com/repos/$owner/$repo/releases/latest"
    Write-Host "$(C('Prüfe neuesten Release für ','cyan'))$owner/$repo …"
    try {
        $resp = Invoke-WebRequest -UseBasicParsing -Headers @{ "Accept"="application/vnd.github+json"; "User-Agent"="PowerShell" } -Uri $api -ErrorAction Stop
        if ($resp.StatusCode -ne 200) { return "" }
        $data = $resp.Content | ConvertFrom-Json
        $tag  = ($data.tag_name | ForEach-Object { $_ }) -as [string]
        if ($tag) { Write-Host (C(("Neuester Release-Tag: {0}" -f $tag),'green')) } else { Write-Host (C("Antwort enthielt keinen tag_name.",'yellow')) }
        return ($tag ?? "")
    } catch {
        if ($_.Exception.Response -and ($_.Exception.Response.StatusCode.Value__ -eq 404)) {
            Write-Host (C(("Keine Releases für {0}/{1} gefunden." -f $owner,$repo),'yellow')); return ""
        }
        Write-Host (C(("Fehler beim Abruf der Releases: {0}" -f $_.Exception.Message),'red')); return ""
    }
}

# ===== Subtree Utilities =====
function Test-SubtreeExists { param([string]$Path) return (Test-Path $Path) -and (Get-ChildItem -Force -LiteralPath $Path | Measure-Object).Count -gt 0 }
function Test-EmptyDir      { param([string]$Path) return (Test-Path $Path -PathType Container) -and (-not (Get-ChildItem -LiteralPath $Path -Force | Select-Object -First 1)) }

function Invoke-SubtreeAdd {
    param([string]$Prefix,[string]$Url,[string]$Branch,[switch]$Squash,[switch]$DryRun)
    Ensure-CleanForSubtree -StepDesc ("add {0}" -f $Prefix) -DryRun:$DryRun
    $args = @("git","subtree","add","--prefix=$Prefix",$Url,$Branch)
    if ($Squash) { $args += "--squash" }
    Write-Host "$(C('==> ADD subtree:','yellow')) $Prefix ($Url@$Branch)"
    Invoke-Cmd -Cmd $args -Check -DryRun:$DryRun | Out-Null
}

function Invoke-SubtreePull {
    param([string]$Prefix,[string]$Url,[string]$Branch,[switch]$Squash,[switch]$DryRun)
    Ensure-CleanForSubtree -StepDesc ("pull {0}" -f $Prefix) -DryRun:$DryRun
    $args = @("git","subtree","pull","--prefix=$Prefix",$Url,$Branch)
    if ($Squash) { $args += "--squash" }
    Write-Host "$(C('==> PULL subtree:','yellow')) $Prefix ($Url@$Branch)"
    Invoke-Cmd -Cmd $args -Check -DryRun:$DryRun | Out-Null
}

# ===== .gitmodules =====
function Parse-Gitmodules {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @() }
    $text = Get-Content -Raw -Encoding UTF8 -Path $Path
    $out = @()
    $current = $null
    foreach ($line in ($text -split "`n")) {
        $line = $line.Trim()
        if ($line -match '^\[submodule "(.*)"\]\s*$') {
            if ($current) { $out += $current }
            $current = @{ path=""; url=""; branch="" }
        } elseif ($line -match '^(path|url|branch)\s*=\s*(.+)$') {
            if ($current) { $current[$matches[1]] = $matches[2].Trim() }
        }
    }
    if ($current) { $out += $current }
    return $out
}

function Resolve-SubmoduleUrl {
    param([string]$ParentUrl,[string]$SubUrl)
    $u = $SubUrl.Trim()
    if ($u.Contains("://") -or $u.StartsWith("git@")) { return $u }
    $host,$owner,$null = Parse-GitHubOwnerRepo $ParentUrl
    if (-not $host -or $host.ToLower() -ne "github.com" -or -not $owner) { return $u }
    $parts = $u.Split("/") | Where-Object { $_ -ne "" -and $_ -ne "." }
    if ($parts.Count -eq 0) { return $u }
    if ($parts.Count -eq 1) { return "https://github.com/$owner/$($parts[0])" }
    if ($parts.Count -eq 2 -and $parts[0] -ne "..") { return "https://github.com/$($parts[0])/$($parts[1])" }
    if ($parts[0] -eq ".." -and $parts.Count -ge 2) { return "https://github.com/$owner/$($parts[-1])" }
    return "https://github.com/$owner/$($parts[-1])"
}

function Vendor-SubmodulesRecursively {
    param(
        [string]$RootPrefix,
        [string]$ParentRepoUrl,
        [switch]$Squash,
        [switch]$DryRun,
        [hashtable]$Visited
    )
    if (-not $Visited) { $Visited = @{} }
    $gm = Join-Path $RootPrefix ".gitmodules"
    $mods = Parse-Gitmodules -Path $gm
    if (-not $mods -or $mods.Count -eq 0) { Write-Host (C(("Keine .gitmodules in {0} gefunden." -f $RootPrefix),'green')); return }

    Write-Host (C(("Gefundene Submodule in {0}:" -f $RootPrefix),'yellow'))
    foreach ($m in $mods) {
        $brInfo = if ($m.branch) { " [branch={0}]" -f $m.branch } else { "" }
        Write-Host ("  - {0} ({1}){2}" -f $m.path,$m.url,$brInfo)
    }

    foreach ($m in $mods) {
        $rel = ($m.path -replace "\\","/").Trim("/")
        $subPath = Join-Path $RootPrefix $rel
        $subUrl  = Resolve-SubmoduleUrl -ParentUrl $ParentRepoUrl -SubUrl $m.url
        $branch  = if ($m.branch) { $m.branch } else { Get-DefaultBranch -RepoUrl $subUrl -DryRun:$DryRun }

        $key = "{0}|{1}" -f ([IO.Path]::GetFullPath($subPath)), $subUrl
        if ($Visited.ContainsKey($key)) { continue }
        $Visited[$key] = $true

        if ((Test-Path $subPath) -and (Test-EmptyDir $subPath)) {
            Write-Host "$(C('Leerer Ordner vorhanden, entferne vor Subtree-ADD:','yellow')) $subPath"
            if (-not $DryRun) { Remove-Item -LiteralPath $subPath -Force }
            Ensure-CleanForSubtree -StepDesc ("prepare {0}" -f $subPath) -DryRun:$DryRun
        }
        $parentDir = Split-Path -Parent $subPath
        if (-not (Test-Path $parentDir)) { New-Item -ItemType Directory -Force -Path $parentDir | Out-Null }

        if (Test-SubtreeExists $subPath) {
            Invoke-SubtreePull -Prefix $subPath -Url $subUrl -Branch $branch -Squash:$Squash -DryRun:$DryRun
            $changed = Invoke-CommitPrefixIfNeeded -Prefix $subPath -Message ("chore(subtree): pull {0} ({1}) -> {2}" -f $subUrl,$branch,$subPath) -DryRun:$DryRun
            if (-not $changed) { Invoke-CommitIfDirty -Message ("chore(subtree): finalize pull {0} ({1})" -f $subUrl,$branch) -DryRun:$DryRun | Out-Null }
        } else {
            Invoke-SubtreeAdd -Prefix $subPath -Url $subUrl -Branch $branch -Squash:$Squash -DryRun:$DryRun
            $changed = Invoke-CommitPrefixIfNeeded -Prefix $subPath -Message ("chore(subtree): add {0} ({1}) -> {2}" -f $subUrl,$branch,$subPath) -DryRun:$DryRun
            if (-not $changed) { Invoke-CommitIfDirty -Message ("chore(subtree): finalize add {0} ({1})" -f $subUrl,$branch) -DryRun:$DryRun | Out-Null }
        }

        Vendor-SubmodulesRecursively -RootPrefix $subPath -ParentRepoUrl $subUrl -Squash:$Squash -DryRun:$DryRun -Visited $Visited
    }
}

# ===== Main =====
$null = Ensure-GitRootAndChdir
$did = $false
try {
    $stashInfo = Use-AutoStash -Enable:$AutoStash
    $did = $stashInfo.DidStash
    if (-not (Test-Path $BASE_PREFIX)) { New-Item -ItemType Directory -Force -Path $BASE_PREFIX | Out-Null }
    Write-Host (C(("Starte Subtree-Update unter '{0}' ..." -f $BASE_PREFIX),'green'))

    $releaseRefs = @{}
    foreach ($repo in $REPOS) {
        $name = $repo.name
        $url  = $repo.url
        $releaseTag = ""
        if ($repo.use_latest_release) { $releaseTag = Get-LatestReleaseTag -RepoUrl $url }
        $branch = if ($releaseTag) { $releaseTag } else { Get-DefaultBranch -RepoUrl $url -DryRun:$DryRun }
        $prefix = Join-Path $BASE_PREFIX $name

        $releaseRefs[$name] = $releaseTag
        Write-Host "$(C('Repository:','green')) $name  $(C($url,'cyan'))  (Ref: $branch)"

        if (Test-SubtreeExists $prefix) {
            Invoke-SubtreePull -Prefix $prefix -Url $url -Branch $branch -Squash:(!$NoSquash) -DryRun:$DryRun
            $changed = Invoke-CommitPrefixIfNeeded -Prefix $prefix -Message ("chore(subtree): pull {0} ({1}) -> {2}" -f $url,$branch,$prefix) -DryRun:$DryRun
            if (-not $changed) { Invoke-CommitIfDirty -Message ("chore(subtree): finalize pull {0} ({1})" -f $url,$branch) -DryRun:$DryRun | Out-Null }
        } else {
            Invoke-SubtreeAdd -Prefix $prefix -Url $url -Branch $branch -Squash:(!$NoSquash) -DryRun:$DryRun
            $changed = Invoke-CommitPrefixIfNeeded -Prefix $prefix -Message ("chore(subtree): add {0} ({1}) -> {2}" -f $url,$branch,$prefix) -DryRun:$DryRun
            if (-not $changed) { Invoke-CommitIfDirty -Message ("chore(subtree): finalize add {0} ({1})" -f $url,$branch) -DryRun:$DryRun | Out-Null }
        }

        Vendor-SubmodulesRecursively -RootPrefix $prefix -ParentRepoUrl $url -Squash:(!$NoSquash) -DryRun:$DryRun
    }

    if ($SELF_REPO_URL) { $releaseRefs["CSM.TmpeSync"] = Get-LatestReleaseTag -RepoUrl $SELF_REPO_URL }
    Update-ModMetadataFile -ReleaseRefs $releaseRefs -DryRun:$DryRun
    Write-Host (C("Fertig.",'green'))
}
finally { Restore-AutoStash -DidStash:$did }

<# 
Aufruf:
    pwsh .\scripts\manage_subtrees.ps1
    pwsh .\scripts\manage_subtrees.ps1 --dry-run
    pwsh .\scripts\manage_subtrees.ps1 --no-squash --auto-stash
#>
