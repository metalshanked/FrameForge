param([string]$MakeNsis, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -SingleFile }
if (-not $MakeNsis) {
    $localCompiler = Join-Path $projectRoot '.local\nsis-tool\tools\Bin\makensis.exe'
    if (Test-Path -LiteralPath $localCompiler) {
        $MakeNsis = $localCompiler
        $env:NSISDIR = Join-Path $projectRoot '.local\nsis-tool\tools'
    } else { $MakeNsis = 'makensis' }
}
& $MakeNsis /V2 (Join-Path $projectRoot 'packaging\FrameForge.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist') -File -Filter '*.exe' | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    Set-Content -LiteralPath ($_.FullName + '.sha256') -Value ($hash + '  ' + $_.Name) -Encoding ascii
}
$portable = Join-Path $projectRoot 'dist\portable\FrameForge.exe'
$portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash
Set-Content -LiteralPath ($portable + '.sha256') -Value ($portableHash + '  FrameForge.exe') -Encoding ascii
Write-Output "Installer: $projectRoot\dist\FrameForge-Setup-0.2.5.exe"
Write-Output "Portable: $portable"
