$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$ProgressPreference = 'SilentlyContinue'
$env:DOTNET_CLI_HOME = "$root/tools/dotnet-home"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force tools, vendor, downloads, mappings, reports | Out-Null
if (-not (Test-Path tools/dotnet/sdk/10.0.401)) {
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile tools/dotnet-install.ps1 -UseBasicParsing
    & ./tools/dotnet-install.ps1 -Version 10.0.401 -InstallDir "$root/tools/dotnet" -NoPath
}
$repos = @{
    'CUE4Parse' = '3222101ce839c5477df33a1aacf236fe9d130f64'
}
foreach ($name in $repos.Keys) {
    $sha = $repos[$name]
    $destination = "vendor/$name/h4lfheart-$name-$($sha.Substring(0,7))"
    if (Test-Path $destination) { continue }
    Invoke-WebRequest "https://api.github.com/repos/h4lfheart/$name/zipball/$sha" -OutFile "downloads/$name.zip" -UseBasicParsing
    Expand-Archive "downloads/$name.zip" -DestinationPath "vendor/$name"
    $sha | Set-Content "reports/$name-commit.txt"
}
