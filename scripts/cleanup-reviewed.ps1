param([switch]$Apply)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$prefix=[IO.Path]::GetFullPath($project).TrimEnd('\')+'\'
$targets=@('app','app-next','app-test','dist','downloads','vendor/FortnitePorting','tools/cargo','tools/rust','tools/rustup','tools/node','tools/cue4parse-native-package','tools/cue4parse-native-package.zip','tools/fmodel-native-source.zip','tools/vgmstream-win64.zip','Aion2Importer.exe','Exportar.cmd','Exportar-classico.cmd','scripts/build-wpf-legacy.ps1','scripts/export-auto.ps1','scripts/export-navigation.ps1','scripts/asset-categories.ps1','scripts/chunk-categories.ps1')
$targets+=@('src/Aion2Porting.Desktop','design/react-reference','design/ui-de-aplicativo-react.zip','scripts/build-tauri.ps1')
$rows=@()
foreach($relative in $targets){
    $path=[IO.Path]::GetFullPath((Join-Path $project $relative))
    if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)-or $path.StartsWith($prefix+'cache',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup target'}
    if(-not(Test-Path -LiteralPath $path)){continue}
    $item=Get-Item -LiteralPath $path -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected link'}
    $rows+=@{Path=$relative;Reason='Unused by the current WPF build and runtime'}
    if($Apply){Remove-Item -LiteralPath $path -Recurse -Force}
}
$rows | ConvertTo-Json | Set-Content (Join-Path $project 'reports/cleanup-reviewed.json') -Encoding UTF8
Write-Output "Reviewed targets: $($rows.Count); applied: $Apply"
