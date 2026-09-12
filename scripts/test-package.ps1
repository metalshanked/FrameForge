
param([string]$Installer)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (-not $Installer) { $Installer = Join-Path $projectRoot 'dist\FrameForge-Setup-0.2.5.exe' }
$testRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts\installer-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
if (-not $testRoot.StartsWith($projectRoot.TrimEnd('\') + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test installation must stay inside project artifacts.' }
if (Test-Path -LiteralPath $testRoot) { throw 'Test directory already exists.' }
$report = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [bool]$Condition) {
    $line = $(if ($Condition) { 'PASS' } else { 'FAIL' }) + ' | ' + $Name
    $report.Add($line)
    Write-Output $line
    if (-not $Condition) { throw $Name }
}
function RunInstaller {
    $process = Start-Process -FilePath $Installer -ArgumentList ('/S /TESTMODE /D=' + $testRoot) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw 'Installer did not finish within two minutes.' }
    if ($process.ExitCode -ne 0) { throw ('Installer failed: ' + $process.ExitCode) }
}
$testKey = 'HKCU:\Software\FrameForge.PackageTest'
if (Test-Path -LiteralPath $testKey) { throw 'A previous package test registry entry remains; inspect it before retrying.' }
$installedExe = Join-Path $testRoot 'FrameForge.exe'
$previousBundle = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $projectRoot '.local\bundle-cache'
try {
    RunInstaller
    Check 'Installer exits successfully and writes the app' (Test-Path -LiteralPath $installedExe)
    $expected = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'dist\portable\FrameForge.exe')).Hash
    Check 'Installed executable matches portable SHA256' ((Get-FileHash -LiteralPath $installedExe).Hash -eq $expected)
    $entry = Get-ItemProperty -LiteralPath ($testKey + '\Uninstall')
    Check 'Per-user uninstall registration has version and location' ($entry.DisplayVersion -eq '0.2.5' -and $entry.InstallLocation -eq $testRoot)
    Check 'Startup is opt-in and disabled on a fresh install' (-not (Test-Path -LiteralPath ($testKey + '\Run')))
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $testRoot '.test-shell\StartMenu\FrameForge.lnk'))
    Check 'Start-menu shortcut targets the installed app' ($shortcut.TargetPath -eq $installedExe)
    Check 'Installer includes MIT and third-party licenses' ((Test-Path -LiteralPath (Join-Path $testRoot 'LICENSE')) -and (Test-Path -LiteralPath (Join-Path $testRoot 'licenses\NSIS.txt')))
    & (Join-Path $PSScriptRoot 'test.ps1') -Executable $installedExe -TimeoutSeconds 120
    Check 'Installed single-file app passes its self-test suite' $true
    $sentinel = Join-Path $testRoot 'user-created-file.txt'
    [IO.File]::WriteAllText($sentinel, 'Preserve user-created files during upgrade and uninstall.')
    New-Item -Path ($testKey + '\Run') -Force | Out-Null
    New-ItemProperty -LiteralPath ($testKey + '\Run') -Name FrameForge -Value '"Z:\previous-location\FrameForge.exe" --background' -PropertyType String -Force | Out-Null
    RunInstaller
    Check 'Upgrade preserves user-created files' ((Get-Content -LiteralPath $sentinel -Raw) -eq 'Preserve user-created files during upgrade and uninstall.')
    $startup = (Get-ItemProperty -LiteralPath ($testKey + '\Run')).FrameForge
    Check 'Upgrade preserves prior startup opt-in with updated executable path' ($startup -eq ('"' + $installedExe + '" --background'))
    $uninstaller = Start-Process -FilePath (Join-Path $testRoot 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -PassThru
    if (-not $uninstaller.WaitForExit(120000)) { throw 'Uninstaller did not finish within two minutes.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (((Test-Path -LiteralPath $installedExe) -or (Test-Path -LiteralPath $testKey)) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    Check 'Uninstall removes its executable and integration entries' ((-not (Test-Path -LiteralPath $installedExe)) -and (-not (Test-Path -LiteralPath $testKey)))
    Check 'Uninstall preserves user-created files' ((Get-Content -LiteralPath $sentinel -Raw) -eq 'Preserve user-created files during upgrade and uninstall.')
    Check 'Uninstall removes its shortcuts' (-not (Test-Path -LiteralPath (Join-Path $testRoot '.test-shell\StartMenu\FrameForge.lnk')))
    Check 'App test data is preserved after uninstall' (Test-Path -LiteralPath (Join-Path $projectRoot 'artifacts\tests\data'))
} finally {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundle
    $reportPath = Join-Path $projectRoot 'artifacts\package-test-results.txt'
    [IO.File]::WriteAllLines($reportPath, $report)
}
