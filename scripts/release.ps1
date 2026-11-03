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
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    # Start clean so DLLs dropped from the allowlist don't linger from older builds.
    Get-ChildItem $contents -Filter '*.dll' -File | Remove-Item -Force
    $net48Dir = Join-Path $contents 'net48'
    if (Test-Path $net48Dir) { Get-ChildItem $net48Dir -Filter '*.dll' -File | Remove-Item -Force }

    # Civil 3D 2025/2026 (.NET 8): ship only what the plugin loads. The engine comes from the
    # add-in's own output folder, which holds its net8.0 asset (in-box System.Text.Json).
    # Never ship copies of assemblies .NET 8 has in-box (System.Text.Json, DiagnosticSource,
    # Asn1...), and leave out PDFsharp modules we don't use (BarCodes, Quality, Snippets,
    # Cryptography + its Pkcs dependency). Autodesk's Black Duck scan reads every DLL shipped.
    $net8Allow = @(
        'HydroComplete.Civil3D.dll', 'HydroComplete.Engine.dll',
        'PdfSharp.dll', 'PdfSharp.Shared.dll', 'PdfSharp.System.dll', 'PdfSharp.Charting.dll', 'PdfSharp.WPFonts.dll',
        'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
        'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'Microsoft.Web.WebView2.Wpf.dll'
    )
    foreach ($name in $net8Allow) {
        $src = Join-Path $out $name
        if (-not (Test-Path $src)) { throw "Expected $name in $out" }
        Copy-Item $src $contents -Force
    }

    Write-Host 'Building net48 target for Civil 3D 2024 (R24.3)...'
    & (Join-Path $root 'scripts\build-net48.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Building net10 target for Civil 3D 2027 (R26.0)...'
    & (Join-Path $root 'scripts\build-net10.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

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