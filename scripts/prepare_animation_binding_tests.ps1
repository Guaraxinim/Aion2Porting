param([string]$Case='Balgest')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$base=Join-Path $root ('reports/animation-binding-tests/'+$Case)
New-Item -ItemType Directory -Force -Path $base | Out-Null
$path=switch($Case){
    'Balgest' {'AION2/Content/Character/Monster/Animal/Balgest/Balgest_01/MOB_Balgest_01'}
    'Basilisk' {'AION2/Content/Character/Monster/Animal/Basilisk/Basilisk_02/MOB_Basilisk_02'}
    'BasiliskV01' {'AION2/Content/Character/Monster/Animal/Basilisk/Basilisk_02_V01/MOB_Basilisk_02_V01'}
    default {throw 'Unknown test case'}
}
$name=Split-Path $path -Leaf
$parent=$path.Substring(0,$path.LastIndexOf('/'))
foreach($mode in @('preview','animation')){
    $asset=if($mode -eq 'preview'){$path+'.uasset'}else{$parent+'/Ani/'+$name+'_Idle.uasset'}
    $request=Join-Path $base ($mode+'-request.json')
    ConvertTo-Json -InputObject @(@{Path=$asset}) | Set-Content -LiteralPath $request -Encoding UTF8
    $result=Join-Path $base ($mode+'-result.json')
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root/scripts/desktop-worker.ps1" -Mode $mode -RequestFile $request -ResultFile $result *> (Join-Path $base ($mode+'.log'))
    if($LASTEXITCODE -ne 0){throw "Fixture $mode failed; see $result"}
    $data=Get-Content $result -Raw | ConvertFrom-Json
    if($mode -eq 'preview'){
        Copy-Item -LiteralPath $data.AssetRoot -Destination (Join-Path $base 'model') -Recurse -Force
        $model=Get-ChildItem (Join-Path $base 'model') -Filter '*.glb' -Recurse -File | Select-Object -First 1
    }else{Copy-Item -LiteralPath $data.Animation -Destination (Join-Path $base 'clip.json') -Force}
}
@{Clip=(Join-Path $base 'clip.json');Model=$model.FullName;AssetRoot=(Join-Path $base 'model')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $base 'export-request.json') -Encoding UTF8
Write-Output "Prepared $Case"
