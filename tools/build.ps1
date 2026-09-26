# Build script: produces two flavours
#
#   self-contained  -- single file, runs without .NET installed (~66 MB)
#   lite            -- single file, needs the .NET 8 Desktop Runtime (~1 MB)
#
# Usage (from anywhere):
#   pwsh -File tools/build.ps1
#   pwsh -File tools/build.ps1 -OutRoot D:\out
#
# All paths are relative to this script; nothing depends on a local absolute path.

param(
    [string]$Configuration = 'Release',
    [string]$OutRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\KksSceneConv\KksSceneConv.csproj'

if (-not (Test-Path $proj)) { throw "Project file not found: $proj" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet not found; install the .NET 8 SDK (or newer)' }

$ver = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "== Building KksSceneConv $ver ($Configuration / $Runtime) ==" -ForegroundColor Cyan

$sc = Join-Path $OutRoot 'self-contained'
$lite = Join-Path $OutRoot 'lite'

Write-Host '-- self-contained single file --' -ForegroundColor Yellow
# EnableCompressionInSingleFile matters: without it the single-file build is more than twice as large.
& dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishTrimmed=false -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $sc
if ($LASTEXITCODE -ne 0) { throw "self-contained build failed (exit code $LASTEXITCODE)" }

Write-Host '-- lite (framework-dependent) --' -ForegroundColor Yellow
& dotnet publish $proj -c $Configuration -r $Runtime --self-contained false `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false `
    -o $lite
if ($LASTEXITCODE -ne 0) { throw "lite build failed (exit code $LASTEXITCODE)" }

foreach ($d in @($sc, $lite)) {
    $exe = Join-Path $d 'KksSceneConv.exe'
    if (-not (Test-Path $exe)) { throw "Build finished but output not found: $exe" }
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ("   {0}  ({1} MB)" -f $exe, $mb) -ForegroundColor Green
}

Write-Host ''
Write-Host 'Done. Self-test: pwsh -File tools/selftest.ps1 -Scene <your KKS scene.png>' -ForegroundColor Cyan
