$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_HOME="$root/tools/dotnet-home"
$env:NUGET_PACKAGES="$root/tools/nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
& "$root/tools/dotnet/dotnet.exe" build src/Aion2Porting.Cli -c Release -p:CUE4PARSE_SKIP_NATIVE=true --nologo -v quiet *> reports/desktop-backend-build.log
if($LASTEXITCODE -ne 0) { Get-Content reports/desktop-backend-build.log -Tail 20; exit $LASTEXITCODE }
[xml]$project=Get-Content -LiteralPath "$root/src/Aion2Importer/Aion2Importer.csproj"
$version=[version]$project.Project.PropertyGroup.Version
if($version.Build -gt 99){throw 'O terceiro número da versão deve ficar entre 0 e 99.'}
& "$root/tools/dotnet/dotnet.exe" publish src/Aion2Importer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --nologo -o app-next *> reports/desktop-publish.log
if($LASTEXITCODE -ne 0) { Get-Content reports/desktop-publish.log -Tail 30; exit $LASTEXITCODE }
$prefix=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\'
Get-Process Aion2Porting -ErrorAction SilentlyContinue | ForEach-Object {
    if($_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){
        $running=$_
        [void]$running.CloseMainWindow()
        if(-not $running.WaitForExit(5000)){Stop-Process -Id $running.Id -Force}
    }
}
Copy-Item -LiteralPath "$root/app-next/Aion2Porting.exe" -Destination "$root/Aion2Porting.exe" -Force
Get-Item "$root/Aion2Porting.exe" | Select-Object Name,Length,LastWriteTime
