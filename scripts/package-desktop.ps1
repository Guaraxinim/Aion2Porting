param([string]$Version='0.7.20', [switch]$Zip)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao invalida.' }
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_HOME="$root/tools/dotnet-home"
$env:NUGET_PACKAGES="$root/tools/nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$stage=Join-Path $root "dist/Aion2Porting-$Version-win-x64"
New-Item -ItemType Directory -Force -Path "$root/reports","$stage/scripts","$stage/tools","$stage/mappings","$stage/licenses" | Out-Null
foreach($dir in @('reports','exports','Exportados','cache','src','vendor')) {
    if(Test-Path -LiteralPath (Join-Path $stage $dir)) { throw "A pasta de distribuicao contem dados de trabalho: $dir. Use uma pasta limpa." }
}
& "$root/tools/dotnet/dotnet.exe" publish src/Aion2Porting.Cli -c Release -r win-x64 --self-contained true -p:CUE4PARSE_SKIP_NATIVE=true -p:DebugType=None -o "$stage/backend" *> reports/portable-backend-build.log
if($LASTEXITCODE -ne 0) { Get-Content reports/portable-backend-build.log -Tail 20; throw 'Build do motor falhou.' }
[xml]$desktopProject=Get-Content "$root/src/Aion2Importer/Aion2Importer.csproj"
$desktopVersion=$desktopProject.Project.PropertyGroup.Version
if($Version -ne $desktopVersion){throw "A versão solicitada deve coincidir com $desktopVersion."}
& "$root/tools/dotnet/dotnet.exe" publish src/Aion2Importer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --nologo -o "$stage" *> reports/portable-desktop-build.log
if($LASTEXITCODE -ne 0){throw 'Build da interface WPF falhou.'}
Copy-Item scripts/desktop-worker.ps1,scripts/export-workflow.ps1,scripts/validate_blender.py,scripts/export_animation_blender.py "$stage/scripts" -Force
Copy-Item tools/CUE4Parse-Natives.dll "$stage/tools" -Force
Copy-Item tools/oodle-data-shared.dll "$stage/tools" -Force
Copy-Item -LiteralPath tools/vgmstream -Destination "$stage/tools" -Recurse -Force
Copy-Item mappings/AION2-5.3.2.0-1.0.52.0.usmap "$stage/mappings" -Force
Copy-Item GUIA_DISTRIBUICAO.md "$stage/LEIA-ME.md" -Force
Copy-Item vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/LICENSE "$stage/licenses/CUE4Parse-LICENSE.txt" -Force
if(Test-Path vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/NOTICE) { Copy-Item vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/NOTICE "$stage/licenses/CUE4Parse-NOTICE.txt" -Force }
Copy-Item tools/dotnet/LICENSE.txt "$stage/licenses/dotnet-LICENSE.txt" -Force
Copy-Item tools/dotnet/ThirdPartyNotices.txt "$stage/licenses/dotnet-ThirdPartyNotices.txt" -Force
Copy-Item tools/nuget/microsoft.windowsdesktop.app.runtime.win-x64/10.0.12/LICENSE "$stage/licenses/WindowsDesktop-LICENSE.txt" -Force
Copy-Item licenses/SharpGLTF-LICENSE.txt "$stage/licenses/SharpGLTF-LICENSE.txt" -Force
Copy-Item src/Aion2Importer/Assets/PlusJakartaSans-OFL.txt "$stage/licenses/PlusJakartaSans-OFL.txt" -Force
if($Zip) {
    $archive=Join-Path $root "dist/Aion2Porting-$Version-win-x64.zip"
    Compress-Archive -Path "$stage/*" -DestinationPath $archive -CompressionLevel Optimal -Force
    $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$archive.sha256","$hash  $(Split-Path $archive -Leaf)")
}
Get-Item -LiteralPath $stage | Select-Object FullName