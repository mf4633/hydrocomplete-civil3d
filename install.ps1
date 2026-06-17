# Build and install HydroComplete.bundle to the per-user auto-load folder.
# Close Civil 3D first if the build reports a locked DLL.
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    & bash package.sh $Configuration
    $bundle = Join-Path $root 'dist\HydroComplete.bundle'
    $dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle'
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Copy-Item $bundle $dest -Recurse -Force
    Write-Host "Installed to $dest"
    Write-Host "Restart Civil 3D. You should see the load banner without NETLOAD."
}
finally {
    Pop-Location
}