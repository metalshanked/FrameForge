param([switch]$SingleFile, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.local\toolchain\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
if (Test-Path -LiteralPath $localSdk) {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.local\toolchain\packages'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot $(if ($SingleFile) { 'dist\portable' } else { 'artifacts\app' })
}
$arguments = @('publish', (Join-Path $projectRoot 'src\FrameForge\FrameForge.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $OutputDirectory, '-p:DebugType=None', '-p:DebugSymbols=false')
if ($SingleFile) { $arguments += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true') }
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Exit FrameForge before replacing an existing build.' }
Write-Output "Built $OutputDirectory\FrameForge.exe"
