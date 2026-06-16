#requires -Version 7
<#
.SYNOPSIS
  One-way sync of the approved JSONL corpus from the upstream FFXIV-Spanish repo into
  data/translations/jsonl.

.DESCRIPTION
  Translation cadence differs from code cadence, so this is separate from sync-vendor.ps1: update
  translations without re-syncing the vendored libraries. Pass -Build to also regenerate
  artifacts/translations.dat afterwards.

.PARAMETER Upstream
  Path to the upstream FFXIV-Spanish repo. Defaults to the sibling directory of this repo.
.PARAMETER Build
  Also run build-translations.ps1 after syncing.
#>
[CmdletBinding()]
param(
    [string]$Upstream = "$PSScriptRoot/../../FFXIV-Spanish",
    [switch]$Build
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Upstream)) { throw "Upstream repo not found: $Upstream" }
$Upstream = (Resolve-Path $Upstream).Path

$src = Join-Path $Upstream 'data/translations/jsonl'
if (-not (Test-Path $src)) { throw "Upstream corpus not found: $src" }

$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$dst      = Join-Path $repoRoot 'data/translations/jsonl'

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Path $dst -Force | Out-Null

Get-ChildItem $src -Recurse -File -Filter *.jsonl | ForEach-Object {
    $rel    = $_.FullName.Substring($src.Length).TrimStart('\', '/')
    $target = Join-Path $dst $rel
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item $_.FullName $target -Force
}

$count = (Get-ChildItem $dst -Recurse -File -Filter *.jsonl).Count
Write-Host "Synced $count .jsonl files from upstream into data/translations/jsonl"

if ($Build) { & "$PSScriptRoot/build-translations.ps1" }
