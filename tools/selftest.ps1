# Self-test: convert one KKS scene with the freshly built exe and re-parse the
# result (check must reach the KStudio tail marker).
#
#   pwsh -File tools/selftest.ps1 -Scene "D:\scenes\my_kks_scene.png"
#   pwsh -File tools/selftest.ps1 -Scene ... -Exe dist\lite\KksSceneConv.exe

param(
    [Parameter(Mandatory = $true)][string]$Scene,
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path $Scene)) { throw "Scene not found: $Scene" }

if ($Exe -eq '') {
    foreach ($cand in @('dist\lite\KksSceneConv.exe', 'dist\self-contained\KksSceneConv.exe', 'src\KksSceneConv\bin\Release\net8.0-windows\KksSceneConv.exe')) {
        $p = Join-Path $repo $cand
        if (Test-Path $p) { $Exe = $p; break }
    }
}
if ($Exe -eq '' -or -not (Test-Path $Exe)) { throw 'No built exe found; run tools/build.ps1 first (or pass -Exe)' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('kks-selftest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
$outCs = Join-Path $tmp 'out_cs.png'

# KksSceneConv.exe is a GUI-subsystem exe: `& $Exe` would return before it
# finishes and $LASTEXITCODE would stay empty, so run it via Start-Process -Wait
# with stdout captured to a file.
function Invoke-Exe([string[]]$ArgList, [int]$Tail = 0) {
    $o = Join-Path $tmp ('out-' + [guid]::NewGuid().ToString('N') + '.txt')
    $quoted = $ArgList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $Exe -ArgumentList $quoted -Wait -PassThru -NoNewWindow -RedirectStandardOutput $o
    $lines = @(Get-Content $o -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ($Tail -gt 0) { $lines = $lines | Select-Object -Last $Tail }
    $lines | ForEach-Object { Write-Host $_ }
    return $p.ExitCode
}

Write-Host "== exe: $Exe" -ForegroundColor Cyan
$code = Invoke-Exe @('convert', $Scene, $outCs)
if ($code -ne 0) { throw "convert failed (exit $code)" }
$code = Invoke-Exe @('check', $outCs) -Tail 2
if ($code -ne 0) { throw "check of converted output failed (exit $code)" }

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
Write-Host 'Self-test OK' -ForegroundColor Green
