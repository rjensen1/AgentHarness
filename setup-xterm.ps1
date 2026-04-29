# setup-xterm.ps1 — downloads xterm.js 5.x into wwwroot/
# Run once before building: .\setup-xterm.ps1
# xterm.js is MIT licensed. https://github.com/xtermjs/xterm.js

$version = "5.3.0"
$outDir  = "$PSScriptRoot\AgentHarness\wwwroot"

Write-Host "Downloading xterm.js $version to $outDir ..."

Invoke-WebRequest "https://unpkg.com/@xterm/xterm@$version/lib/xterm.js"  -OutFile "$outDir\xterm.js"
Invoke-WebRequest "https://unpkg.com/@xterm/xterm@$version/css/xterm.css" -OutFile "$outDir\xterm.css"

Write-Host "Done. xterm.js and xterm.css are in wwwroot/."
