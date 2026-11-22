# Makes a build you can give to someone else: it runs on a PC with no .NET installed.
# Usage: tools/publish.ps1           self-contained (JIT): any PC with the .NET SDK can make it
#        tools/publish.ps1 -Aot      NativeAOT: about 2.5x faster start, smaller; needs the MSVC linker
#        tools/publish.ps1 -Zip      also zip the folder for a release or WinGet
#        tools/publish.ps1 -Version 0.2.0 -Zip   a release: that version, with no commit in the version line
#        tools/publish.ps1 -App wpf -Zip         the WPF app, the same way
#        tools/publish.ps1 -Arch x64             the x64 build on an Arm64 PC, checked under emulation
# Writes %LOCALAPPDATA%\Dev\UnnamedManager\publish\<kind>\ and checks the result with the self-test.
# Without -Arch it builds for the architecture dotnet runs as; the release workflow runs it on each.
#
# Why not build.ps1: build.ps1 installs a framework-dependent build for this PC. A publish carries .NET too.
# EnableMsixTooling=true is what makes dotnet publish keep the app's .pri (with the compiled XAML)
# for an unpackaged WinUI app; without it the published app dies in InitializeComponent.
param([switch]$Aot, [switch]$Zip, [string]$Version,
    [ValidateSet('winui', 'wpf')][string]$App = 'winui',
    [ValidateSet('', 'x64', 'arm64')][string]$Arch = '')
$ErrorActionPreference = 'Continue'
$wpf = $App -eq 'wpf'
if ($wpf -and $Aot) { Write-Output "WPF doesn't support NativeAOT: run tools/publish.ps1 -App wpf"; exit 1 }
$product = 'UnnamedManager'
$name = if ($wpf) { "$product.Wpf" } else { $product }
$root = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH" }
$kind = if ($Aot) { 'aot' } else { 'self-contained' }
$folder = if ($wpf) { "wpf-$kind" } else { $kind }
$out = Join-Path $env:LOCALAPPDATA "Dev\$product\publish\$folder"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
# The architecture the build is for: the one asked for, or the dotnet process's.
$target = if ($Arch) { $Arch } elseif (& dotnet --info | Select-String 'RID:\s+win-arm64') { 'arm64' } else { 'x64' }

$publishArgs = @("$root\src\$name\$name.csproj", '-c', 'Release', '-r', "win-$target", '--self-contained', 'true', '-o', $out, '--nologo')
if (-not $wpf) { $publishArgs += '-p:EnableMsixTooling=true' }
if ($Version) {
    # The tray shows "Version 0.2.0", with no "+commit" after it.
    $publishArgs += "-p:Version=$Version", '-p:IncludeSourceRevisionInInformationalVersion=false'
}
if ($Aot) {
    # NativeAOT links with the MSVC linker from Visual Studio Build Tools ("Desktop development with C++"),
    # and its targets find it with vswhere.exe, which is not on the PATH by default.
    $installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    if (-not (Test-Path "$installer\vswhere.exe") -or -not (& "$installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)) {
        Write-Output 'NativeAOT needs the MSVC linker. Install it with:'
        Write-Output '  winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"'
        Write-Output 'Or run tools/publish.ps1 without -Aot.'
        exit 1
    }
    $env:PATH = "$installer;$env:PATH"
    $publishArgs += '-p:PublishAot=true'
}

$output = & dotnet publish @publishArgs 2>&1
$code = $LASTEXITCODE
# Trim and AOT warnings (IL2xxx, IL3xxx) mean something may break at run time: treat them as errors.
$warnings = $output | Where-Object { $_ -match ': (error|warning) ' } | ForEach-Object { ($_ -replace '\s+\[[^\]]+\.csproj.*\]$', '').Trim() } | Sort-Object -Unique
$warnings
if ($code -ne 0) { Write-Output 'publish failed'; exit 1 }
if ($Aot -and ($warnings | Where-Object { $_ -match 'warning IL[23]\d{3}' })) { Write-Output 'publish has trim or AOT warnings: fix them before shipping'; exit 1 }
if (-not $wpf -and -not (Test-Path "$out\$name.pri")) { Write-Output "publish is missing $name.pri: the app would not start"; exit 1 }
$size = [int]((Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Output "published $out\$name.exe ($kind, $size MB)"

# The published app runs the same self-test as the installed one.
$env:APP_EXE = "$out\$name.exe"
# Only failures and the last line show, and on GitHub Actions each failure is also an annotation.
try {
    & "$PSScriptRoot\selftest.ps1" | ForEach-Object -Begin { $last = $null } -Process {
        $last = $_
        if ($_ -match '^(FAIL|CRASH|TIMEOUT)') { $_; if ($env:GITHUB_ACTIONS) { "::error title=Self-test::$_" } }
    } -End { $last }
} finally { Remove-Item Env:APP_EXE -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0) { Write-Output 'the published app failed its self-test'; exit 1 }

if ($Zip) {
    $version = (Get-Item "$out\$name.exe").VersionInfo.ProductVersion -replace '\+.*$', ''
    $suffix = if ($Aot) { '-aot' } else { '' }
    $zipPath = Join-Path $env:LOCALAPPDATA "Dev\$product\publish\$name-$version-win-$target$suffix.zip"
    Compress-Archive -Path "$out\*" -DestinationPath $zipPath -Force
    Write-Output "zipped $zipPath"
}
exit 0
