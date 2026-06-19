param([string]$Configuration = "Debug")

$projDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $projDir "bin\$Configuration"
$dll = Join-Path $bin "JumpKhaunter67.dll"
if (-not (Test-Path $dll)) { Write-Error "DLL not found at $dll"; exit 1 }

$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$zipName = Join-Path $bin "JumpKhaunter67-package-$timestamp.zip"
$tmp = Join-Path $env:TEMP ("jk67_package_$timestamp")
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

Copy-Item -Path $dll -Destination $tmp -Force
Copy-Item -Path (Join-Path $bin "JumpKhaunter67.json") -Destination $tmp -Force

# Include dependency DLLs (System.Drawing.Common, Microsoft.Win32.SystemEvents) that are NuGet packages, not part of the runtime
$deps = @("System.Drawing.Common.dll", "Microsoft.Win32.SystemEvents.dll")
foreach ($dep in $deps) {
    $depPath = Join-Path $bin $dep
    if (Test-Path $depPath) { Copy-Item -Path $depPath -Destination $tmp -Force }
}

$imagesSrc = Join-Path $projDir "images"
$audioSrc = Join-Path $projDir "audio"
if (Test-Path $imagesSrc) { Copy-Item -Path $imagesSrc -Destination (Join-Path $tmp "images") -Recurse -Force }
if (Test-Path $audioSrc) { Copy-Item -Path $audioSrc -Destination (Join-Path $tmp "audio") -Recurse -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($tmp, $zipName)
Write-Output "Created package: $zipName"

Remove-Item -Path $tmp -Recurse -Force
