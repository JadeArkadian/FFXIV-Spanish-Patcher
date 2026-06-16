#requires -Version 7
<#
.SYNOPSIS
  Compact the versioned JSONL corpus into artifacts/translations.dat, the gzip-JSONL blob the app
  embeds as a resource.

.DESCRIPTION
  Concatenates every data/translations/jsonl/**/*.jsonl line (skipping blanks) into one gzip stream.
  Run this after a translation update, then re-publish the app so the embedded resource changes.
  The output (data/translations.dat) IS versioned: it is the compact source-of-record this repo
  ships, regenerated from the raw jsonl tree which is synced locally and git-ignored.
#>
[CmdletBinding()]
param(
    [string]$Source = "$PSScriptRoot/../data/translations/jsonl",
    [string]$Output = "$PSScriptRoot/../data/translations.dat"
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Source)) {
    throw "Translation source not found: $Source. Run build/sync-translations.ps1 first."
}

$files = Get-ChildItem $Source -Recurse -File -Filter *.jsonl | Sort-Object FullName
if ($files.Count -eq 0) {
    throw "No .jsonl files found under $Source."
}

$outDir = Split-Path $Output
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$lines  = 0
$writer = $null
$outFs  = [System.IO.File]::Create($Output)
try {
    $gzip   = [System.IO.Compression.GZipStream]::new($outFs, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = [System.IO.StreamWriter]::new($gzip, [System.Text.UTF8Encoding]::new($false))
    $writer.NewLine = "`n"
    foreach ($file in $files) {
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $writer.WriteLine($line)
                $lines++
            }
        }
    }
}
finally {
    # Disposing the writer flushes and disposes the whole stream chain (gzip + file).
    if ($null -ne $writer) { $writer.Dispose() } else { $outFs.Dispose() }
}

$size = (Get-Item $Output).Length
Write-Host ("Wrote {0}: {1} entries, {2} MB compressed (from {3} files)." -f `
    $Output, $lines, [math]::Round($size / 1MB, 2), $files.Count)
