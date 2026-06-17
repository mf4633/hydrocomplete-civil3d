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
    Write-Host "Building HydroComplete.Civil3D ($Configuration)..."
    dotnet build "src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj" -c $Configuration

    $out = "src\HydroComplete.Civil3D\bin\$Configuration\net8.0-windows"
    $eng = "src\HydroComplete.Engine\bin\$Configuration\netstandard2.0"
    $contents = "dist\HydroComplete.bundle\Contents"
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    Copy-Item "$out\HydroComplete.Civil3D.dll" $contents -Force
    Copy-Item "$eng\HydroComplete.Engine.dll" $contents -Force

    $bundle = Join-Path $root 'dist\HydroComplete.bundle'
    $dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle'

    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Copy-Item $bundle $dest -Recurse -Force

    Write-Host "Installed to $dest"
    Write-Host "Restart Civil 3D. You should see the load banner without NETLOAD."
    & "$root\verify-install.ps1"
}
finally {
    Pop-Location
}