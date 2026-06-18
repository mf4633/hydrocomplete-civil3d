# Sync release version across csproj, bundle manifest, startup banner, and listing copy.
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$targets = @(
    @{
        Path = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
        Pattern = '(<Version>)[^<]+(</Version>)'
        Replace = "`${1}$Version`${2}"
        Label = 'HydroComplete.Civil3D.csproj <Version>'
    },
    @{
        Path = Join-Path $root 'src\HydroComplete.Engine\HydroComplete.Engine.csproj'
        Pattern = '(<Version>)[^<]+(</Version>)'
        Replace = "`${1}$Version`${2}"
        Label = 'HydroComplete.Engine.csproj <Version>'
    },
    @{
        Path = Join-Path $root 'dist\HydroComplete.bundle\PackageContents.xml'
        Pattern = '(AppVersion=")[^"]+(")'
        Replace = "`${1}$Version`${2}"
        Label = 'PackageContents.xml AppVersion'
    },
    @{
        Path = Join-Path $root 'src\HydroComplete.Civil3D\Plugin.cs'
        Pattern = 'for Civil 3D \d+\.\d+\.\d+ loaded'
        Replace = "for Civil 3D $Version loaded"
        Label = 'Plugin.cs startup banner'
    },
    @{
        Path = Join-Path $root 'verify-install.ps1'
        Pattern = 'Startup banner: HydroComplete for Civil 3D \d+\.\d+\.\d+ loaded'
        Replace = "Startup banner: HydroComplete for Civil 3D $Version loaded"
        Label = 'verify-install.ps1 banner hint'
    },
    @{
        Path = Join-Path $root 'dist\app-store\LISTING.md'
        Pattern = '(\*\*Version at submission:\*\* )\d+\.\d+\.\d+'
        Replace = "`${1}$Version"
        Label = 'LISTING.md version line'
    },
    @{
        Path = Join-Path $root 'src\HydroComplete.Civil3D\Commands\HydroCommands.cs'
        Pattern = '=== HydroComplete for Civil 3D \d+\.\d+\.\d+ ==='
        Replace = "=== HydroComplete for Civil 3D $Version ==="
        Label = 'HydroCommands.cs HC_ABOUT header'
    }
)

$updated = 0
foreach ($target in $targets) {
    if (-not (Test-Path $target.Path)) {
        throw "Missing file: $($target.Path)"
    }

    $text = Get-Content $target.Path -Raw
    $newText = [regex]::Replace($text, $target.Pattern, $target.Replace)
    if ($newText -eq $text) {
        Write-Warning "No change for $($target.Label) (already $Version or pattern mismatch)"
        continue
    }

    Set-Content -Path $target.Path -Value $newText -NoNewline -Encoding UTF8
    Write-Host "Updated $($target.Label) -> $Version"
    $updated++
}

Write-Host ""
Write-Host "Version bump complete: $updated file(s) updated to $Version"
Write-Host "Next: .\scripts\app-store-preflight.ps1  then  .\scripts\release.ps1"