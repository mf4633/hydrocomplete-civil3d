# Build Release bundle and zip for Autodesk App Store upload.
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
$bundle = Join-Path $root 'dist\HydroComplete.bundle'
$contents = Join-Path $bundle 'Contents'
$dist = Join-Path $root 'dist'

function Get-ProjectVersion {
    param([string]$ProjectPath)
    [xml]$xml = Get-Content $ProjectPath
    $versionNode = $xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    if (-not $versionNode -or -not $versionNode.Version) {
        throw "Could not read <Version> from $ProjectPath"
    }
    return [string]$versionNode.Version
}

if (-not $Version) {
    $Version = Get-ProjectVersion -ProjectPath $csproj
}

Push-Location $root
try {
    Write-Host "Building HydroComplete.Civil3D (Release) for version $Version..."
    dotnet build $csproj -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $out = Join-Path $root 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows'
    $eng = Join-Path $root 'src\HydroComplete.Engine\bin\Release\netstandard2.0'
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    Copy-Item (Join-Path $out 'HydroComplete.Civil3D.dll') $contents -Force
    Copy-Item (Join-Path $eng 'HydroComplete.Engine.dll') $contents -Force

    Get-ChildItem $out -Filter '*.dll' |
        Where-Object { $_.Name -notmatch '^HydroComplete\.' } |
        Copy-Item -Destination $contents -Force

    $iconDest = Join-Path $contents 'PackageIcon.png'
    if (-not (Test-Path $iconDest)) {
        throw "PackageIcon.png missing in $contents - add a 96x96 PNG before release."
    }
    Write-Host "Bundle contents updated at $contents"

    if (-not (Test-Path (Join-Path $bundle 'PackageContents.xml'))) {
        throw "PackageContents.xml missing in $bundle"
    }

    $zipName = "HydroComplete-$Version.zip"
    $zipPath = Join-Path $dist $zipName
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($bundle, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    Write-Host ""
    Write-Host "Release artifact: $zipPath"
    Write-Host "SHA256: $hash"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}