#requires -Version 7
<#
.SYNOPSIS
  Re-sync the vendored core libraries from the upstream FFXIV-Spanish repo.

.DESCRIPTION
  vendor/ mirrors XivSpanish.Core and XivSpanish.GameData from upstream. It is NOT edited by
  hand: changes flow one way (upstream -> vendor) through this script. After running, review the
  diff, rebuild, and commit. Provenance (upstream commit + sync date) is written to
  vendor/VENDORED.md.

.PARAMETER Upstream
  Path to the upstream FFXIV-Spanish repo. Defaults to the sibling directory of this repo.
#>
[CmdletBinding()]
param(
    [string]$Upstream = "$PSScriptRoot/../../FFXIV-Spanish"
)
$ErrorActionPreference = 'Stop'

$repoRoot   = (Resolve-Path "$PSScriptRoot/..").Path
$vendorRoot = Join-Path $repoRoot 'vendor'
$projects   = @('XivSpanish.Core', 'XivSpanish.GameData')

if (-not (Test-Path $Upstream)) { throw "Upstream repo not found: $Upstream" }
$Upstream = (Resolve-Path $Upstream).Path

New-Item -ItemType Directory -Path $vendorRoot -Force | Out-Null

foreach ($proj in $projects) {
    $src = Join-Path $Upstream "src/$proj"
    $dst = Join-Path $vendorRoot $proj
    if (-not (Test-Path $src)) { throw "Missing upstream project: $src" }

    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Path $dst -Force | Out-Null

    # Copy source files only; skip build output (bin/obj).
    Get-ChildItem $src -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $rel    = $_.FullName.Substring($src.Length).TrimStart('\', '/')
            $target = Join-Path $dst $rel
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item $_.FullName $target -Force
        }
    Write-Host "Vendored $proj"
}

# Record provenance.
$commit = (git -C $Upstream rev-parse HEAD).Trim()
$short  = (git -C $Upstream rev-parse --short HEAD).Trim()
$date   = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
$md = @"
# Vendored code

vendor/ mirrors core libraries copied from the upstream FFXIV-Spanish repo.
DO NOT edit these files by hand: run build/sync-vendor.ps1 to refresh, then rebuild and commit.

| Field    | Value |
| -------- | ----- |
| Upstream | FFXIV-Spanish |
| Commit   | $commit |
| Synced   | $date |
| Projects | $($projects -join ', ') |
"@
Set-Content -Path (Join-Path $vendorRoot 'VENDORED.md') -Value $md -Encoding utf8

Write-Host "Wrote VENDORED.md (upstream $short, synced $date)"
