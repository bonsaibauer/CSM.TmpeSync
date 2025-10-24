#!/bin/pwsh
param(
    [string]$Configuration = "Release",
    [string]$Profile = "",
    [string]$ModDirectory = "",
    [string]$ModRootDirectory = ""
)

$ErrorActionPreference = "Stop"

$buildScript = Join-Path $PSScriptRoot 'build.ps1'
if (-not (Test-Path $buildScript)) {
    throw "build.ps1 not found next to install.ps1"
}

$arguments = @('-Install', '-Configuration', $Configuration)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $arguments += @('-Profile', $Profile) }
if (-not [string]::IsNullOrWhiteSpace($ModDirectory)) { $arguments += @('-ModDirectory', $ModDirectory) }
if (-not [string]::IsNullOrWhiteSpace($ModRootDirectory)) { $arguments += @('-ModRootDirectory', $ModRootDirectory) }

& $buildScript @arguments
exit $LASTEXITCODE
