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
    # Civil 3D 2024 (net48) was dropped in 1.10.0: remove any leftover net48 folder so its
    # .NET Framework polyfill DLLs (Black Duck findings) can never ship again.
    $net48Dir = Join-Path $contents 'net48'
    if (Test-Path $net48Dir) { Remove-Item $net48Dir -Recurse -Force }

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

    Write-Host 'Building net10 target for Civil 3D 2027 (R26.0)...'
    & (Join-Path $root 'scripts\build-net10.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $iconDest = Join-Path $contents 'PackageIcon.png'
    if (-not (Test-Path $iconDest)) {
        throw "PackageIcon.png missing in $contents - add a 96x96 PNG before release."
    }
    Write-Host "Bundle contents updated at $contents"

    # Ribbon (CUIX with embedded icons), F1 help page and manifest, all from scripts/ribbon.json.
    python (Join-Path $root 'scripts\build-icons.py'); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\build-cuix.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python (Join-Path $root 'scripts\build-help.py'); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python (Join-Path $root 'scripts\gen-packagecontents.py') $Version; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $zipName = "HydroComplete-$Version.zip"
    $zipPath = Join-Path $dist $zipName
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($bundle, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    # Marketplace upload layout: user guide + HydroComplete.bundle/ at the zip root.
    $stage = Join-Path $dist '_appstore_stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $bundle (Join-Path $stage 'HydroComplete.bundle') -Recurse
    Copy-Item (Join-Path $root 'docs\USER-GUIDE.md') (Join-Path $stage 'HydroComplete-UserGuide.txt')
    $storeZip = Join-Path $dist "HydroComplete-$Version-appstore.zip"
    if (Test-Path $storeZip) { Remove-Item $storeZip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $storeZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Remove-Item $stage -Recurse -Force

    Write-Host ""
    Write-Host "Release artifact: $zipPath"
    Write-Host "SHA256: $hash"
    Write-Host "Marketplace upload: $storeZip"
    Write-Host "SHA256: $((Get-FileHash $storeZip -Algorithm SHA256).Hash)"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}