# Marks a story done: tools/mark-story.ps1 -Id S-061 -VerifiedFile <file.txt>
# Sets the story's "Status: todo" in docs/STORIES.md to "Status: done" and adds a
# "Verified: ..." line with the file's text. The text comes from a file, so no shell
# escaping touches it. Stops with an error if the story has no "Status: todo".
param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][string]$VerifiedFile)
$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\STORIES.md'
$text = [IO.File]::ReadAllText($path)
$verified = [IO.File]::ReadAllText((Resolve-Path $VerifiedFile), [Text.Encoding]::UTF8).Trim()
$start = $text.IndexOf("### $Id ")
if ($start -lt 0) { throw "no story $Id" }
$status = $text.IndexOf("Status: todo", $start)
$next = $text.IndexOf("`n### ", $start + 5)
if ($status -lt 0 -or ($next -ge 0 -and $status -gt $next)) { throw "$Id has no Status: todo" }
$text = $text.Substring(0, $status) + "Status: done`nVerified: $verified" + $text.Substring($status + "Status: todo".Length)
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
"marked $Id done"
