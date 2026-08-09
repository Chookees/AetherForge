<#
.SYNOPSIS
  Publishes Aether Forge release binaries into ./release

.DESCRIPTION
  Creates:
    release\API\Backend+API.exe   — standalone ASP.NET Core API / backend
    release\GUI\GUI.exe           — Avalonia desktop app (embeds local API)

  Self-contained win-x64 single-file publishes. Requires .NET 10 SDK.
  Exes are renamed after publish (AssemblyName is left unchanged to avoid NuGet restore conflicts).

.EXAMPLE
  .\build\publish-release.ps1
  .\build\publish-release.ps1 -Configuration Release -Runtime win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $Runtime = "win-x64",

    [switch] $SkipClean
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ReleaseRoot = Join-Path $RepoRoot "release"
$ApiOut = Join-Path $ReleaseRoot "API"
$GuiOut = Join-Path $ReleaseRoot "GUI"
$ApiProject = Join-Path $RepoRoot "src\AomrModEditor.Api\AomrModEditor.Api.csproj"
$GuiProject = Join-Path $RepoRoot "src\AomrModEditor.App\AomrModEditor.App.csproj"

Write-Host "==> Repo: $RepoRoot" -ForegroundColor Cyan
Write-Host "==> Release: $ReleaseRoot" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}

$sdk = dotnet --version
Write-Host "==> SDK: $sdk"

if (-not $SkipClean -and (Test-Path $ReleaseRoot)) {
    Write-Host "==> Cleaning previous release folder…" -ForegroundColor Yellow
    Remove-Item -LiteralPath $ReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $ApiOut | Out-Null
New-Item -ItemType Directory -Force -Path $GuiOut | Out-Null

$commonPublishArgs = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Write-Host "==> Publishing Backend+API…" -ForegroundColor Green
dotnet publish $ApiProject @commonPublishArgs -o $ApiOut
if ($LASTEXITCODE -ne 0) { throw "API publish failed (exit $LASTEXITCODE)." }

$publishedApi = Join-Path $ApiOut "AomrModEditor.Api.exe"
$apiExe = Join-Path $ApiOut "Backend+API.exe"
if (-not (Test-Path -LiteralPath $publishedApi)) { throw "Missing output: $publishedApi" }
Move-Item -LiteralPath $publishedApi -Destination $apiExe -Force

Write-Host "==> Publishing GUI…" -ForegroundColor Green
dotnet publish $GuiProject @commonPublishArgs -o $GuiOut
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed (exit $LASTEXITCODE)." }

$publishedGui = Join-Path $GuiOut "AomrModEditor.App.exe"
$guiExe = Join-Path $GuiOut "GUI.exe"
if (-not (Test-Path -LiteralPath $publishedGui)) { throw "Missing output: $publishedGui" }
Move-Item -LiteralPath $publishedGui -Destination $guiExe -Force

# Convenience copies at release root
Copy-Item -LiteralPath $apiExe -Destination (Join-Path $ReleaseRoot "Backend+API.exe") -Force
Copy-Item -LiteralPath $guiExe -Destination (Join-Path $ReleaseRoot "GUI.exe") -Force

$readme = @"
Aether Forge — Release ($Configuration / $Runtime)
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

Executables
-----------
Backend+API.exe  Standalone REST API / backend (ASP.NET Core)
GUI.exe          Desktop UI (Avalonia). Starts an embedded local API itself.

Folders
-------
API\   Full publish output for the API (exe + any sidecars)
GUI\   Full publish output for the GUI

Notes
-----
- Self-contained: no separate .NET runtime install required on the target PC.
- Prefer starting GUI.exe for normal use.
- Backend+API.exe is for API-only / CLI / external clients.
"@
Set-Content -LiteralPath (Join-Path $ReleaseRoot "README.txt") -Value $readme -Encoding UTF8

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  $apiExe"
Write-Host "  $guiExe"
Write-Host "Also copied to:"
Write-Host "  $(Join-Path $ReleaseRoot 'Backend+API.exe')"
Write-Host "  $(Join-Path $ReleaseRoot 'GUI.exe')"
