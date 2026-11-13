# Runs an edit script: tools/rep.ps1 <script.ps1>
# The script calls Rep <file> <old> <new> for each change, usually with here-strings (@' ... '@).
# Rep replaces exactly one match: none, or more than one, stops the script with an error.
# The script is read as UTF-8, so curly quotes, › and other non-ASCII text survive
# Windows PowerShell 5.1. Relative file paths are from the repo root.
param([Parameter(Mandatory)][string]$Script)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Rep($file, $old, $new) {
  $path = if ([IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $root $file }
  $t = [IO.File]::ReadAllText($path)
  $at = $t.IndexOf($old, [StringComparison]::Ordinal)
  if ($at -lt 0) { throw "not found in ${file}: $old" }
  if ($t.IndexOf($old, $at + 1, [StringComparison]::Ordinal) -ge 0) { throw "more than one match in ${file}: $old" }
  [IO.File]::WriteAllText($path, $t.Substring(0, $at) + $new + $t.Substring($at + $old.Length), (New-Object Text.UTF8Encoding $false))
}

. ([ScriptBlock]::Create([IO.File]::ReadAllText((Resolve-Path $Script), [Text.Encoding]::UTF8)))
