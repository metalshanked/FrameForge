param([switch]$Ui, [string]$Executable, [int]$TimeoutSeconds = 90)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $Executable) { $Executable = Join-Path $projectRoot 'artifacts\app\FrameForge.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Build FrameForge first.' }
$previousData = $env:FRAMEFORGE_DATA
$previousOutput = $env:FRAMEFORGE_TEST_OUTPUT
$previousBundle = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $projectRoot '.local\bundle-cache'
    $env:FRAMEFORGE_DATA = Join-Path $projectRoot 'artifacts\tests\data'
    $env:FRAMEFORGE_TEST_OUTPUT = Join-Path $projectRoot 'artifacts\tests\results'
    $arguments = if ($Ui) { '--ui-test' } else { '--self-test' }
    $name = if ($Ui) { 'ui-test-results.txt' } else { 'test-results.txt' }
    if ($Ui) { Write-Output 'Activate the FrameForge test window if Windows leaves it in the background.' }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Test watchdog stopped its FrameForge process after $TimeoutSeconds seconds."
    }
    Get-Content -LiteralPath (Join-Path $env:FRAMEFORGE_TEST_OUTPUT $name)
    if ($process.ExitCode -ne 0) { throw 'One or more tests failed.' }
} finally {
    $env:FRAMEFORGE_DATA = $previousData
    $env:FRAMEFORGE_TEST_OUTPUT = $previousOutput
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundle
}
