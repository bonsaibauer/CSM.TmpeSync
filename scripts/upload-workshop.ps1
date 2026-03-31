#!/bin/pwsh
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamUser,
    [string]$SteamCmdPath = "",
    [string]$PublisherVdfPath = "",
    [string]$ChangeNote = "",
    [string]$CopyFrom = "",
    [string]$CopyTo = "",
    [string]$SteamPassword = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path $scriptDir -Parent

function Resolve-OrDefault {
    param(
        [string]$InputPath,
        [string]$DefaultRelative
    )

    if (-not [string]::IsNullOrWhiteSpace($InputPath)) {
        return $InputPath
    }

    return Join-Path $scriptDir $DefaultRelative
}

$changelogPanelPath = Join-Path $repoRoot "src/CSM.TmpeSync/Services/UI/ChangelogPanel.cs"

function ConvertTo-VdfValue {
    param([string]$Text)

    if ($null -eq $Text) {
        return ""
    }

    $clean = $Text -replace "(`r`n|`n)+", " "
    $escaped = $clean -replace '\\', '\\\\'
    $escaped = $escaped -replace '"', '\\"'
    return $escaped
}

function Get-LatestChangelogNote {
    param([string]$ChangelogFile)

    if (-not (Test-Path $ChangelogFile)) {
        throw "ChangelogPanel.cs not found: $ChangelogFile"
    }

    $content = Get-Content -LiteralPath $ChangelogFile -Raw
    $regex = [regex]::new(
        'new\s+ChangelogEntry\s*\{\s*.*?Version\s*=\s*"([^"]+)"\s*,.*?Changes\s*=\s*new\s+List<string>\s*\{\s*(.*?)\s*\}',
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    $match = $regex.Match($content)
    if (-not $match.Success) {
        throw "No changelog entry found in $ChangelogFile"
    }

    $version = $match.Groups[1].Value.Trim()
    $changesBlock = $match.Groups[2].Value

    $changes = [System.Text.RegularExpressions.Regex]::Matches($changesBlock, '"([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Version could not be read from changelog."
    }

    $note = "Version: $version"
    if ($changes -and $changes.Count -gt 0) {
        $note += "`n- " + ($changes -join "`n- ")
    }

    return $note
}

function Confirm-ChangelogNote {
    param([string]$InitialNote)

    $note = $InitialNote

    while ($true) {
        Write-Host "[upload-workshop] Current changelog:" -ForegroundColor Cyan
        Write-Host "----------------------------------------" -ForegroundColor DarkGray
        Write-Host $note
        Write-Host "----------------------------------------" -ForegroundColor DarkGray

        $choice = Read-Host "Is this ok? (Y=yes / E=edit / N=abort)"
        $choice = if ($choice) { $choice.Trim().ToLowerInvariant() } else { "" }

        switch ($choice) {
            "" { return $note } # default yes
            "y" { return $note }
            "yes" { return $note }
            "e" {
                $updated = Read-Host "New changelog text (use \\n for newline, empty = keep)"
                if (-not [string]::IsNullOrWhiteSpace($updated)) {
                    $note = $updated -replace '\\n', "`n"
                }
            }
            "edit" {
                $updated = Read-Host "New changelog text (use \\n for newline, empty = keep)"
                if (-not [string]::IsNullOrWhiteSpace($updated)) {
                    $note = $updated -replace '\\n', "`n"
                }
            }
            "n" { throw "Upload aborted: changelog not confirmed." }
            "no" { throw "Upload aborted: changelog not confirmed." }
            default { Write-Host "Please enter Y, E or N." -ForegroundColor Yellow }
        }
    }
}

$steamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"

function Install-FreshSteamCmd {
    param([string]$DestinationRoot)

    $destinationRoot = if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $scriptDir } else { $DestinationRoot }
    if (-not (Test-Path $destinationRoot)) {
        New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    }

    $targetDir = Join-Path $destinationRoot ("steamcmd-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    $tempZip = Join-Path ([System.IO.Path]::GetTempPath()) ("steamcmd-{0}.zip" -f [Guid]::NewGuid().ToString("N"))
    Write-Host "[upload-workshop] Downloading fresh SteamCMD..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $steamCmdDownloadUrl -OutFile $tempZip -UseBasicParsing

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZip, $targetDir)
    Remove-Item $tempZip -Force

    $exePath = Join-Path $targetDir "steamcmd.exe"
    if (-not (Test-Path $exePath)) {
        throw "Fresh SteamCMD download is missing steamcmd.exe in $targetDir"
    }

    return $exePath
}

function Resolve-SteamCmdPath {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        if (-not (Test-Path $PreferredPath)) {
            throw "SteamCMD not found at preferred path: $PreferredPath"
        }
        return $PreferredPath
    }

    $defaultPath = Join-Path $scriptDir "steamcmd.exe"
    if (Test-Path $defaultPath) {
        return $defaultPath
    }

    return Install-FreshSteamCmd -DestinationRoot (Join-Path $scriptDir "bin")
}

function Test-WorkshopUploadSucceeded {
    param(
        [string]$SteamCmdExePath,
        [datetime]$RunStartedAt
    )

    if ([string]::IsNullOrWhiteSpace($SteamCmdExePath)) {
        return $false
    }

    $steamCmdDir = Split-Path $SteamCmdExePath -Parent
    if ([string]::IsNullOrWhiteSpace($steamCmdDir)) {
        return $false
    }

    $workshopLogPath = Join-Path $steamCmdDir "logs/workshop_log.txt"
    if (-not (Test-Path $workshopLogPath)) {
        return $false
    }

    $threshold = $RunStartedAt.AddSeconds(-5)
    $okLineRegex = [regex]::new('^\[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]\s+\[AppID\s+\d+\]\s+Upload finished .* : OK$')
    $timestampFormat = "yyyy-MM-dd HH:mm:ss"
    $culture = [System.Globalization.CultureInfo]::InvariantCulture

    $lines = Get-Content -LiteralPath $workshopLogPath -ErrorAction SilentlyContinue
    foreach ($line in $lines) {
        $match = $okLineRegex.Match($line)
        if (-not $match.Success) {
            continue
        }

        $timestampText = $match.Groups["ts"].Value
        $timestamp = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($timestampText, $timestampFormat, $culture, [System.Globalization.DateTimeStyles]::None, [ref]$timestamp)) {
            continue
        }

        if ($timestamp -ge $threshold) {
            return $true
        }
    }

    return $false
}

$steamCmd = Resolve-SteamCmdPath -PreferredPath $SteamCmdPath
$publisherVdf = Resolve-OrDefault -InputPath $PublisherVdfPath -DefaultRelative "publisher.vdf"

if (-not (Test-Path $steamCmd)) {
    throw "steamcmd.exe not found: $steamCmd"
}

if (-not (Test-Path $publisherVdf)) {
    throw "publisher.vdf not found: $publisherVdf"
}

if ([string]::IsNullOrWhiteSpace($SteamUser)) {
    throw "Please provide -SteamUser."
}

if ([string]::IsNullOrWhiteSpace($CopyFrom) -xor [string]::IsNullOrWhiteSpace($CopyTo)) {
    throw "CopyFrom and CopyTo must both be set or both be empty."
}

if (-not [string]::IsNullOrWhiteSpace($CopyFrom)) {
    if (-not (Test-Path $CopyFrom)) {
        throw "CopyFrom not found: $CopyFrom"
    }

    Write-Host "[upload-workshop] Copying file: $CopyFrom -> $CopyTo" -ForegroundColor Cyan
    Copy-Item -LiteralPath $CopyFrom -Destination $CopyTo -Force
}

$effectiveVdf = $publisherVdf
$tempVdf = $null

$changelogNoteRaw = if (-not [string]::IsNullOrWhiteSpace($ChangeNote)) { $ChangeNote } else { Get-LatestChangelogNote -ChangelogFile $changelogPanelPath }
$changelogNoteRaw = Confirm-ChangelogNote -InitialNote $changelogNoteRaw

$changelogNote = ConvertTo-VdfValue -Text $changelogNoteRaw

if ([string]::IsNullOrWhiteSpace($changelogNote)) {
    throw "Could not determine changenote."
}

if (-not [string]::IsNullOrWhiteSpace($changelogNote)) {
    $replacement = '    "changenote" "{0}"' -f $changelogNote
    $lines = @(Get-Content -LiteralPath $publisherVdf)
    $filtered = @($lines | Where-Object { $_ -notmatch '^\s*"changenote"\s*"' })

    # Ignore trailing blank lines before checking where the root block closes.
    while ($filtered.Count -gt 0 -and [string]::IsNullOrWhiteSpace($filtered[-1])) {
        if ($filtered.Count -eq 1) {
            $filtered = @()
            break
        }

        $filtered = @($filtered[0..($filtered.Count - 2)])
    }

    if ($filtered.Count -eq 0 -or $filtered[-1].Trim() -ne "}") {
        throw "Invalid publisher.vdf format: expected '}' as the last non-empty line."
    }

    $outputLines = @()
    if ($filtered.Count -gt 1) {
        $outputLines += $filtered[0..($filtered.Count - 2)]
    }
    $outputLines += $replacement
    $outputLines += $filtered[-1]

    $content = ($outputLines -join "`n").TrimEnd() + "`n"

    # Persist changenote back to the original publisher.vdf for the next run.
    Set-Content -LiteralPath $publisherVdf -Value $content -Encoding ASCII
    $effectiveVdf = $publisherVdf

    Write-Host "[upload-workshop] Using publisher.vdf with changenote: $changelogNoteRaw" -ForegroundColor Cyan
}

Write-Host "[upload-workshop] Launching SteamCMD..." -ForegroundColor Green
$cmdArgs = if (-not [string]::IsNullOrWhiteSpace($SteamPassword)) {
    @("+login", $SteamUser, $SteamPassword, "+workshop_build_item", $effectiveVdf, "+quit")
}
else {
    @("+login", $SteamUser, "+workshop_build_item", $effectiveVdf, "+quit")
}
$exitCode = $null
$usedFreshSteamCmd = $false

for ($attempt = 1; $attempt -le 2; $attempt++) {
    $attemptStartedAt = Get-Date
    $activeSteamCmdDir = Split-Path $steamCmd -Parent
    if ([string]::IsNullOrWhiteSpace($activeSteamCmdDir) -or -not (Test-Path $activeSteamCmdDir)) {
        $activeSteamCmdDir = $scriptDir
    }

    Push-Location $activeSteamCmdDir
    try {
        & $steamCmd @cmdArgs
    }
    finally {
        Pop-Location
    }

    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and (Test-WorkshopUploadSucceeded -SteamCmdExePath $steamCmd -RunStartedAt $attemptStartedAt)) {
        Write-Host "[upload-workshop] SteamCMD returned ExitCode $exitCode, but workshop logs confirm upload success. Treating as success." -ForegroundColor Yellow
        $exitCode = 0
    }

    if ($exitCode -eq -1 -and -not $usedFreshSteamCmd -and [string]::IsNullOrWhiteSpace($SteamCmdPath)) {
        Write-Host "[upload-workshop] SteamCMD failed with exit code -1. Downloading a fresh copy and retrying..." -ForegroundColor Yellow
        $steamCmd = Install-FreshSteamCmd -DestinationRoot (Join-Path $scriptDir "bin")
        $usedFreshSteamCmd = $true
        continue
    }

    break
}

if ($tempVdf -and (Test-Path $tempVdf)) {
    Remove-Item -LiteralPath $tempVdf -Force
}

if ($exitCode -eq 7) {
    Write-Error "SteamCMD failed with ExitCode 7. Check Steam Guard/login and validate publisher.vdf syntax."
    $global:LASTEXITCODE = $exitCode
    return
}

if ($exitCode -ne 0) {
    Write-Error "SteamCMD failed (ExitCode $exitCode). See SteamCMD output."
    $global:LASTEXITCODE = $exitCode
    return
}

Write-Host "[upload-workshop] Upload completed (ExitCode $exitCode)." -ForegroundColor Green
