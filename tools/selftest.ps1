# Runs the in-app self-test with no config file in a fresh %TEMP% folder, so the app writes
# the starter config there. Exit code 0 when every check passed.
. "$PSScriptRoot\common.ps1"
$store = Get-TestStore 'self'
Remove-Store $store
$code = Invoke-App -Environment @{ APP_STORE = $store } -Arguments '--selftest'
exit $code
