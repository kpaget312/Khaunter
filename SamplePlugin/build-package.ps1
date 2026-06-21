param([string]$Configuration = "Debug")

$projDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $projDir "bin\$Configuration"
# Handle Platform-specific output (e.g. bin\x64\Release\)
$dll = if (Test-Path (Join-Path $bin "JumpKhaunter67.dll")) { Join-Path $bin "JumpKhaunter67.dll" } else { Join-Path (Join-Path $bin "x64") "JumpKhaunter67.dll" }
if (-not (Test-Path $dll)) { Write-Error "DLL not found at $dll"; exit 1 }
$binDir = Split-Path $dll -Parent

$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$zipName = Join-Path $bin "JumpKhaunter67-package-$timestamp.zip"
$tmp = Join-Path $env:TEMP ("jk67_package_$timestamp")
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

Copy-Item -Path $dll -Destination $tmp -Force
$jsonSrc = Join-Path $binDir "JumpKhaunter67.json"
if (-not (Test-Path $jsonSrc)) { $jsonSrc = Join-Path $bin "JumpKhaunter67.json" }
Copy-Item -Path $jsonSrc -Destination $tmp -Force

$imagesSrc = Join-Path $projDir "images"
$audioSrc = Join-Path $projDir "audio"
if (Test-Path $imagesSrc) { Copy-Item -Path $imagesSrc -Destination (Join-Path $tmp "images") -Recurse -Force }
if (Test-Path $audioSrc) { Copy-Item -Path $audioSrc -Destination (Join-Path $tmp "audio") -Recurse -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($tmp, $zipName)
Write-Output "Created package: $zipName"

Remove-Item -Path $tmp -Recurse -Force
