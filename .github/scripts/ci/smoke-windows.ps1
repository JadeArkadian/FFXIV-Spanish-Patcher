$stdout = Join-Path $env:RUNNER_TEMP 'FFXIVSpanishPatcher-smoke-stdout.log'
$stderr = Join-Path $env:RUNNER_TEMP 'FFXIVSpanishPatcher-smoke-stderr.log'
$app = Join-Path $env:GITHUB_WORKSPACE 'publish/win-x64/FFXIVSpanishPatcher.exe'
$process = Start-Process -FilePath $app -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

Start-Sleep -Seconds 10
if ($process.HasExited) {
    Get-Content $stdout, $stderr -ErrorAction SilentlyContinue
    throw "FFXIVSpanishPatcher exited during Windows smoke test with code $($process.ExitCode)."
}

Stop-Process -Id $process.Id -Force
