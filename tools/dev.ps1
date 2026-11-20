# Builds this checkout (Debug) without installing, then runs the self-test or snapshots against that build.
# For agents working in their own git worktree: nothing is installed, so nobody's installed app is closed or replaced.
# Usage: tools/dev.ps1 build
#        tools/dev.ps1 selftest
#        tools/dev.ps1 snap -Pages all,settings
param(
    [Parameter(Mandatory = $true)][ValidateSet('build', 'selftest', 'snap')][string]$Do,
    [string[]]$Pages = @()
)
$name = 'UnnamedManager'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH" }
# The same "+branch.commit[.modified]" stamp as build.ps1, so the version line says which code runs.
$branch = (git -C $root rev-parse --abbrev-ref HEAD 2>$null)
$commit = (git -C $root rev-parse --short HEAD 2>$null)
$dirty = (git -C $root status --porcelain 2>$null)
$stamp = if ($commit) { "$branch.$commit" + $(if ($dirty) { '.modified' } else { '' }) } else { Get-Date -Format 'yyyyMMddHHmm' }
$output = dotnet build "$root\src\$name\$name.csproj" -c Debug --nologo "-p:SourceRevisionId=$stamp" 2>&1
$code = $LASTEXITCODE
$output | Where-Object { $_ -match ': (error|warning) ' } | ForEach-Object { ($_ -replace '\s+\[[^\]]+\.csproj.*\]$', '').Trim() } | Sort-Object -Unique
if ($code -ne 0) { Write-Output 'build failed'; exit 1 }
# The machine's own architecture, which dotnet builds for; this shell may run as x64 under emulation on ARM64.
$native = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
$arch = if ($native -eq 'ARM64') { 'arm64' } else { 'x64' }
# Where the build went: Directory.Build.props, or this PC's Directory.Build.local.props.
$artifacts = (dotnet msbuild "$root\src\$name\$name.csproj" -getProperty:ArtifactsPath).Trim()
$exe = Join-Path $artifacts "bin\$name\debug_win-$arch\$name.exe"
Write-Output "built $exe"
if ($Do -eq 'build') { exit 0 }

$env:APP_EXE = $exe
try {
    if ($Do -eq 'selftest') { & "$PSScriptRoot\selftest.ps1" }
    else { & "$PSScriptRoot\snap.ps1" -Pages $Pages }
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:APP_EXE -ErrorAction SilentlyContinue
}
