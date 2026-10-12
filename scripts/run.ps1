param(
    [ValidateSet('inventory','probe','export','chunks','batch')][string]$Mode = 'inventory',
    [int]$Limit = 10,
    [string]$Filter = '*',
    [string]$Chunk = 'pakchunk30100-Windows_0_P.utoc',
    [string]$ResultFile = '',
    [string]$SelectionFile = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_HOME = "$root/tools/dotnet-home"
$env:NUGET_PACKAGES = "$root/tools/nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
New-Item -ItemType Directory -Force "$root/reports" | Out-Null
& "$root/tools/dotnet/dotnet.exe" build src/Aion2Porting.Cli -c Release -p:CUE4PARSE_SKIP_NATIVE=true --nologo -v quiet *> "$root/reports/build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$root/reports/build.log" -Tail 40; exit $LASTEXITCODE }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
& "$root/tools/dotnet/dotnet.exe" src/Aion2Porting.Cli/bin/Release/net10.0/Aion2Porting.Cli.dll $Mode $Limit $Filter $Chunk $ResultFile $SelectionFile *> "$root/reports/run-$stamp.log"
$runExitCode = $LASTEXITCODE
Get-Content "$root/reports/run-$stamp.log" -Tail 12
Write-Host "Full log: reports/run-$stamp.log"
exit $runExitCode
