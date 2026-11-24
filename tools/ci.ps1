# Runs what CI runs, on this PC, so nothing waits on GitHub until it's ready to push.
# Usage: tools/ci.ps1          both architectures: builds, unit tests, both apps' self-tests, the grid
#        tools/ci.ps1 -Quick   the same without the self-tests and x64, for the middle of a story
# On an Arm64 PC the x64 half is self-contained Release builds run under emulation. On an x64 PC
# there is no Arm64 half: only CI can run that one.
# Nothing is installed, and every app run uses a config in %TEMP%. Each step's output goes to a
# log file. The last line is LOCAL CI PASS or LOCAL CI FAIL.
param([switch]$Quick)
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH" }
. "$PSScriptRoot\common.ps1"
Push-Location $root

$logs = Join-Path $env:TEMP "$AppName-ci-$(Split-Path $root -Leaf)"
New-Item -ItemType Directory -Force $logs | Out-Null
$artifacts = (dotnet msbuild "$root\src\$AppName\$AppName.csproj" -getProperty:ArtifactsPath).Trim()
$native = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
$arch = if ($native -eq 'ARM64') { 'arm64' } else { 'x64' }
$script:failed = @()

# Runs one step, keeps its output in a log, and shows only what went wrong. The step returns
# its exit code as the last thing it outputs.
function Step([string]$Title, [scriptblock]$Body) {
    $log = Join-Path $logs (($Title -replace '[^A-Za-z0-9]+', '-') + '.log')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = @(& $Body 6>&1 2>&1)
    $code = $output[-1]
    $lines = @($output | Select-Object -SkipLast 1 | ForEach-Object { "$_" })
    $lines | Out-File -FilePath $log -Encoding utf8
    # Warnings fail too: a change is done when the build has none.
    $bad = @($lines | Where-Object { $_ -cmatch '^(FAIL|CRASH|TIMEOUT)|: (warning|error) |^\s+Failed ' } | Sort-Object -Unique)
    $ok = ("$code" -eq '0') -and ($bad.Count -eq 0)
    Write-Output ('{0} {1} ({2:N0} s)' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $Title, $watch.Elapsed.TotalSeconds)
    if (-not $ok) {
        $script:failed += $Title
        $bad | Select-Object -First 12 | ForEach-Object { Write-Output "  $_" }
        Write-Output "  log: $log"
    }
}

function Run-App([string]$Exe, [string]$Purpose, [string]$Argument) {
    $script:AppExe = $Exe
    $store = Get-TestStore $Purpose
    Remove-Store $store
    Invoke-App -Environment @{ APP_STORE = $store } -Arguments $Argument -TimeoutSec 300
}

$bin = Join-Path $artifacts 'bin'
# --no-incremental: a build that skips the compiler doesn't repeat the warnings it found last time.
Step "WinUI build ($arch)" { dotnet build src/UnnamedManager -c Debug --nologo --no-incremental; $LASTEXITCODE }
Step "WPF build ($arch)" { dotnet build src/UnnamedManager.Wpf -c Debug --nologo --no-incremental; $LASTEXITCODE }
Step "unit tests ($arch)" { dotnet test tests/UnnamedManager.Core.Tests --nologo; $LASTEXITCODE }
Step 'grid lint' { powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\grid-lint.ps1"; $LASTEXITCODE }

if (-not $Quick) {
    Step "WinUI self-test ($arch)" { Run-App "$bin\$AppName\debug_win-$arch\$AppName.exe" 'ci' '--selftest' }
    Step "WPF check ($arch)" { Run-App "$bin\$AppName.Wpf\debug\$AppName.Wpf.exe" 'ci-wpf' '--check' }
    Step "WPF self-test ($arch)" { Run-App "$bin\$AppName.Wpf\debug\$AppName.Wpf.exe" 'ci-wpf-self' '--selftest' }

    if ($arch -eq 'arm64') {
        $x64 = @('-r', 'win-x64', '--self-contained', 'true', '--nologo')
        Step 'WinUI build (x64)' { dotnet build src/UnnamedManager -c Release @x64; $LASTEXITCODE }
        Step 'WPF build (x64)' { dotnet build src/UnnamedManager.Wpf -c Release @x64; $LASTEXITCODE }
        Step 'unit tests build (x64)' { dotnet build tests/UnnamedManager.Core.Tests -c Debug @x64; $LASTEXITCODE }
        Step 'unit tests (x64)' { & "$bin\$AppName.Core.Tests\debug_win-x64\$AppName.Core.Tests.exe"; $LASTEXITCODE }
        Step 'WinUI self-test (x64)' { Run-App "$bin\$AppName\release_win-x64\$AppName.exe" 'ci-x64' '--selftest' }
        Step 'WPF check (x64)' { Run-App "$bin\$AppName.Wpf\release_win-x64\$AppName.Wpf.exe" 'ci-wpf-x64' '--check' }
        Step 'WPF self-test (x64)' { Run-App "$bin\$AppName.Wpf\release_win-x64\$AppName.Wpf.exe" 'ci-wpf-self-x64' '--selftest' }
    }
}

Pop-Location
if ($script:failed.Count) {
    Write-Output "LOCAL CI FAIL: $($script:failed -join ', ')"
    exit 1
}
Write-Output $(if ($Quick) { 'LOCAL CI PASS (quick)' } else { 'LOCAL CI PASS' })
exit 0
