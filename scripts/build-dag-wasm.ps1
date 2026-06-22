# Build the Rust WASM DAG editor and copy it into the Civil 3D bundle.
# Run from the repo root, or from scripts/ (path is auto-detected).
#
#   .\scripts\build-dag-wasm.ps1
#   .\scripts\build-dag-wasm.ps1 -Release   # wasm-opt (default) = release
#   .\scripts\build-dag-wasm.ps1 -Dev       # no wasm-opt, faster rebuild

param([switch]$Dev)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dagDir    = Join-Path (Split-Path -Parent $repoRoot) 'hydrocomplete-dag'
$bundleOut = Join-Path $repoRoot 'dist\HydroComplete.bundle\Contents\dag'

if (-not (Test-Path $dagDir)) {
    throw "DAG crate not found at $dagDir.  Clone hydrocomplete-dag alongside this repo."
}

Write-Host "Building Rust WASM DAG editor..."
Push-Location $dagDir
try {
    $args = @('build', '--target', 'web', '--out-dir', 'www/pkg')
    if ($Dev) { $args += '--dev' }
    & wasm-pack @args
    if ($LASTEXITCODE -ne 0) { throw "wasm-pack failed" }
} finally {
    Pop-Location
}

Write-Host "Copying www/ to bundle at $bundleOut ..."
if (Test-Path $bundleOut) { Remove-Item $bundleOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $bundleOut | Out-Null
Copy-Item (Join-Path $dagDir 'www\*') $bundleOut -Recurse -Force

Write-Host ""
Write-Host "DAG editor deployed to: $bundleOut"
Write-Host "  index.html + pkg/ ($(
    (Get-ChildItem (Join-Path $bundleOut 'pkg') -File | Measure-Object -Property Length -Sum).Sum / 1KB | ForEach-Object { '{0:0}' -f $_ }
) KB WASM pkg)"
Write-Host ""
Write-Host "Next: run .\scripts\release.ps1 to rebuild the plugin zip."
