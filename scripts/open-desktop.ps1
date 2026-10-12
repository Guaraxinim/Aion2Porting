$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$candidates=@('Aion2Porting.exe') |
    ForEach-Object {Join-Path $root $_} |
    Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} |
    ForEach-Object {Get-Item -LiteralPath $_} |
    Sort-Object LastWriteTimeUtc -Descending
if(!$candidates){throw 'Execute scripts/build-desktop.ps1 para compilar o programa.'}
Start-Process -FilePath $candidates[0].FullName -WorkingDirectory $root -WindowStyle Normal
