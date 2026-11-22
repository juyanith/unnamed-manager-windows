# Snapshots of every page in light and dark, on a fresh copy of tools/sample-config.json.
# Usage: tools/snap.ps1                     every page
#        tools/snap.ps1 -Pages settings
#        tools/snap.ps1 -Accent green       another accent from UI/Theme.cs
#        tools/snap.ps1 -Grid               draw the grid and the layout probes
# Writes %TEMP%\UnnamedManager-shots-<checkout>\<page>-light.png and -dark.png, each with a -layout.json.
# -Pages is an array: PowerShell turns `-Pages a,b` into one before the script sees it.
param([string[]]$Pages = @(), [string]$Accent = '', [switch]$Grid)
. "$PSScriptRoot\common.ps1"
$store = Get-TestStore 'test'
$shots = Join-Path $env:TEMP "$AppName-shots-$(Split-Path (Split-Path $PSScriptRoot -Parent) -Leaf)"
# The WPF app's go in a folder of their own, so neither app's snapshots clear the other's.
if ($AppExe -like '*UnnamedManager.Wpf.exe') { $shots += '-wpf' }
# A fresh copy of the sample config: two leaders, nested menus, a direct binding.
Remove-Store $store
New-Item -ItemType Directory -Force (Split-Path $store -Parent) | Out-Null
Copy-Item (Join-Path $PSScriptRoot "sample-config.json") $store
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
$vars = @{ APP_STORE = $store; APP_SNAPSHOT = $shots }
if ($Pages) { $vars.APP_SNAPSHOT_PAGES = $Pages -join ',' }
if ($Accent) { $vars.APP_ACCENT = $Accent }
if ($Grid) { $vars.APP_GRID = '1' }
$code = Invoke-App -Environment $vars
exit $code
