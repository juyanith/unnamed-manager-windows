# Snapshots of every page in light and dark, on a fresh test config (the starter).
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
# Start from nothing: the app writes the starter config.
Remove-Store $store
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
$vars = @{ APP_STORE = $store; APP_SNAPSHOT = $shots }
if ($Pages) { $vars.APP_SNAPSHOT_PAGES = $Pages -join ',' }
if ($Accent) { $vars.APP_ACCENT = $Accent }
if ($Grid) { $vars.APP_GRID = '1' }
$code = Invoke-App -Environment $vars
exit $code
