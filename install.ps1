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
    # PDFsharp + transitive deps (MIT) for HC_REPORT_PDF
    Get-ChildItem $out -Filter '*.dll' |
        Where-Object { $_.Name -notmatch '^HydroComplete\.' } |
        Copy-Item -Destination $contents -Force

    $bundle = Join-Path $root 'dist\HydroComplete.bundle'
    $dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle'

    if (Test-Path $dest) {
        try {
            Remove-Item $dest -Recurse -Force
            Copy-Item $bundle $dest -Recurse -Force
        }
        catch {
            Write-Host "Bundle locked (Civil 3D open?) - updating files in place..."
            New-Item -ItemType Directory -Force -Path (Join-Path $dest 'Contents') | Out-Null
            Copy-Item "$bundle\PackageContents.xml" $dest -Force
            Copy-Item "$bundle\Contents\*" (Join-Path $dest 'Contents') -Force -ErrorAction Stop
        }
    }
    else {
        Copy-Item $bundle $dest -Recurse -Force
    }

    $packageIcon = Join-Path $bundle 'Contents\PackageIcon.png'
    if (Test-Path $packageIcon) {
        New-Item -ItemType Directory -Force -Path (Join-Path $dest 'Contents') | Out-Null
        Copy-Item $packageIcon (Join-Path $dest 'Contents') -Force
    }

    Write-Host "Installed to $dest"
    Write-Host "Restart Civil 3D. You should see the load banner without NETLOAD."
    & "$root\verify-install.ps1"
}
finally {
    Pop-Location
}