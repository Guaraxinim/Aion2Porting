param([string]$Version='0.7.21')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_HOME="$root/tools/dotnet-home"
$env:NUGET_PACKAGES="$root/tools/nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
[xml]$project=Get-Content "$root/src/Aion2Importer/Aion2Importer.csproj"
if($Version -ne $project.Project.PropertyGroup.Version){throw 'Versao diferente do projeto.'}
$stage=Join-Path $root "dist/Aion2Porting-$Version-win-x64"
$build=Join-Path $root ("reports/release-build-$Version-"+[guid]::NewGuid().ToString('N'))
$support=Join-Path $build "support"
if(Test-Path $support){throw 'Use uma pasta de compilacao limpa.'}
New-Item -ItemType Directory -Force -Path "$support/scripts","$support/tools","$support/mappings","$support/licenses","$support/seed",$stage | Out-Null
& tools/dotnet/dotnet.exe publish src/Aion2Porting.Cli -c Release -r win-x64 --self-contained true -p:CUE4PARSE_SKIP_NATIVE=true -p:DebugType=None -o "$support/backend" *> reports/portable-backend-build.log
if($LASTEXITCODE -ne 0){throw 'Build do motor falhou.'}
& tools/dotnet/dotnet.exe build src/Aion2Importer -c Release --nologo *> reports/release-seed-build.log
if($LASTEXITCODE -ne 0){throw 'Build do gerador do cache falhou.'}
$paks='C:\Program Files (x86)\Steam\steamapps\common\AION2\Aion2\Content\Paks'
& tools/dotnet/dotnet.exe src/Aion2Importer/bin/Release/net10.0-windows/Aion2Porting.dll --build-library-seed $root $paks "$support/seed/library.dat" *> reports/release-seed-summary.json
if($LASTEXITCODE -ne 0){throw 'Geracao do cache inicial falhou.'}
Copy-Item cache/catalog.dat "$support/seed/catalog.dat"
Copy-Item scripts/desktop-worker.ps1,scripts/export-workflow.ps1,scripts/validate_blender.py,scripts/export_animation_blender.py "$support/scripts"
Copy-Item tools/CUE4Parse-Natives.dll,tools/oodle-data-shared.dll "$support/tools"
Copy-Item tools/vgmstream "$support/tools" -Recurse
Copy-Item mappings/AION2-5.3.2.0-1.0.52.0.usmap "$support/mappings"
Copy-Item vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/LICENSE "$support/licenses/CUE4Parse-LICENSE.txt"
if(Test-Path vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/NOTICE){Copy-Item vendor/CUE4Parse/h4lfheart-CUE4Parse-3222101/NOTICE "$support/licenses/CUE4Parse-NOTICE.txt"}
Copy-Item tools/dotnet/LICENSE.txt "$support/licenses/dotnet-LICENSE.txt"
Copy-Item tools/dotnet/ThirdPartyNotices.txt "$support/licenses/dotnet-ThirdPartyNotices.txt"
Copy-Item tools/nuget/microsoft.windowsdesktop.app.runtime.win-x64/10.0.12/LICENSE "$support/licenses/WindowsDesktop-LICENSE.txt"
Copy-Item licenses/SharpGLTF-LICENSE.txt "$support/licenses"
Copy-Item src/Aion2Importer/Assets/PlusJakartaSans-OFL.txt "$support/licenses"
Copy-Item GUIA_DISTRIBUICAO.md "$support/LEIA-ME.md"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle=Join-Path $build "runtime.dat"
[IO.Compression.ZipFile]::CreateFromDirectory($support,$bundle,[IO.Compression.CompressionLevel]::Optimal,$false)
& tools/dotnet/dotnet.exe publish src/Aion2Importer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None "-p:BundleArchivePath=$bundle" --nologo -o "$stage" *> reports/portable-desktop-build.log
if($LASTEXITCODE -ne 0){throw 'Build do executavel unico falhou.'}
$exe=Join-Path $stage 'Aion2Porting.exe'
$hash=(Get-FileHash $exe -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$exe.sha256","$hash  Aion2Porting.exe`n")
Get-Item $exe | Select-Object FullName,Length
