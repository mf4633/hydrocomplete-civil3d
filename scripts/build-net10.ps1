# Build the net10.0-windows target for Civil 3D 2027 (R26.0) and stage Contents/net10 in the bundle.
# 2027 runs .NET 10 and is binary-breaking, so it needs its own compile against the 2027 API.
# Compiles without Civil 3D 2027 installed (Autodesk's AutoCAD.NET 26.0.0 + Civil3D.NET 13.9.628
# NuGet API packages). Requires the .NET 10 SDK.
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
$bundleContents = Join-Path $root 'dist\HydroComplete.bundle\Contents'
$net10Out = Join-Path $root "src\HydroComplete.Civil3D\bin\$Configuration\net10.0-windows"

$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw '.NET 10 SDK not found (dotnet --list-sdks). Install it: winget install Microsoft.DotNet.SDK.10'
}

Push-Location $root
try {
    Write-Host "Building HydroComplete.Civil3D net10.0-windows ($Configuration) for Civil 3D 2027..."
    dotnet build $csproj -c $Configuration -p:BuildNet10=true
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $net10Contents = Join-Path $bundleContents 'net10'
    New-Item -ItemType Directory -Force -Path $net10Contents | Out-Null
    Get-ChildItem $net10Contents -Filter '*.dll' -File | Remove-Item -Force

    # Same allowlist as the net8 folder (see release.ps1): only what the plugin loads.
    $allow = @(
        'HydroComplete.Civil3D.dll', 'HydroComplete.Engine.dll',
        'PdfSharp.dll', 'PdfSharp.Shared.dll', 'PdfSharp.System.dll', 'PdfSharp.Charting.dll', 'PdfSharp.WPFonts.dll',
        'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
        'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'Microsoft.Web.WebView2.Wpf.dll'
    )
    foreach ($name in $allow) {
        $src = Join-Path $net10Out $name
        if (-not (Test-Path $src)) { throw "Expected $name in $net10Out" }
        Copy-Item $src $net10Contents -Force
    }
    # Host assemblies must never ship.
    $leak = Get-ChildItem $net10Contents -Filter '*.dll' | Where-Object { $_.Name -match '^(Ac|Aec|Aecc|AdWindows)' }
    if ($leak) { throw "Autodesk host DLLs leaked into the bundle: $($leak.Name -join ', ')" }

    Write-Host "net10 bundle staged at $net10Contents"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}
