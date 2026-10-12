param([ValidateSet('catalog','export','preview','audio','inspect','animation','animation-export')][string]$Mode,[string]$RequestFile,[string]$ResultFile,[string]$Blender,[string]$Game='C:\Program Files (x86)\Steam\steamapps\common\AION2',[string]$OutputRoot)
$ErrorActionPreference='Stop'
$supportRoot=Split-Path $PSScriptRoot -Parent
$root=if($env:AION2_WORK_ROOT){$env:AION2_WORK_ROOT}else{$supportRoot}
$root=[IO.Path]::GetFullPath($root)
$env:AION2_SUPPORT_ROOT=$supportRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path "$root/reports","$root/exports" | Out-Null
. "$PSScriptRoot/export-workflow.ps1"
$env:AION2_ROOT=$root
Remove-Item Env:AION2_EXPORT_ROOT -ErrorAction SilentlyContinue
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$manifest=Join-Path $root ('reports/desktop-'+[guid]::NewGuid().ToString('N')+'.json')
if($env:AION2_CATALOG_SCAN -eq '1'){$manifest=Join-Path $root 'reports/catalog-scan/audio-manifest.json'}
$engine="$supportRoot/backend/Aion2Porting.Cli.exe"
$engineArgs=@()
if(-not(Test-Path -LiteralPath $engine)) {
    $engine="$supportRoot/tools/dotnet/dotnet.exe"
    $engineArgs=@("$supportRoot/src/Aion2Porting.Cli/bin/Release/net10.0/Aion2Porting.Cli.dll")
}
try {
    $paks=@((Join-Path $Game 'Aion2/Content/Paks'),(Join-Path $Game 'Content/Paks'),$Game) | Where-Object { (Test-Path -LiteralPath $_ -PathType Container) -and @(Get-ChildItem -LiteralPath $_ -Filter '*.utoc' -File).Count -gt 0 } | Select-Object -First 1
    if(-not $paks) { throw 'Indices UTOC nao encontrados. Selecione a instalacao no botao Jogo.' }
    $env:AION2_GAME_DIR=[IO.Path]::GetFullPath($paks)
    $protected=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    if((Split-Path $paks -Leaf) -eq 'Paks' -and (Split-Path (Split-Path $paks -Parent) -Leaf) -eq 'Content') {
        $protected=Split-Path (Split-Path $paks -Parent) -Parent
        if((Split-Path $protected -Leaf) -eq 'Aion2') { $protected=Split-Path $protected -Parent }
    }
    if(-not $OutputRoot) { $OutputRoot=Join-Path $root 'Exportados' }
    $OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
    if($OutputRoot.Equals($protected,[StringComparison]::OrdinalIgnoreCase) -or $OutputRoot.StartsWith($protected.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Escolha uma pasta de saida fora da instalacao do jogo.' }

    if($Mode -eq 'animation-export') {
        if(-not(Test-Path -LiteralPath $Blender -PathType Leaf)){throw 'Blender não encontrado. Configure o executável nas opções.'}
        $request=Get-Content -LiteralPath $RequestFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $clip=Get-Content -LiteralPath $request.Clip -Raw -Encoding UTF8 | ConvertFrom-Json
        $name=$clip.Name -replace '[^\p{L}\p{N}_.-]', '_'
        if([string]::IsNullOrWhiteSpace($name)){$name='Animacao'}
        $stage=Join-Path $root ('exports/animation-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        $blend=Join-Path $stage ($name+'.blend')
        $log=Join-Path $root ('reports/animation-export-'+[guid]::NewGuid().ToString('N')+'.log')
        $model=if($request.Model){[IO.Path]::GetFullPath([string]$request.Model)}else{''}
        $assetRoot=if($request.AssetRoot){[IO.Path]::GetFullPath([string]$request.AssetRoot)}else{''}
        if($model -and (!(Test-Path -LiteralPath $model -PathType Leaf) -or [IO.Path]::GetExtension($model) -ne '.glb')){throw 'O modelo GLB selecionado não está mais disponível.'}
        $ErrorActionPreference='Continue'
        & $Blender --background --factory-startup --python-exit-code 1 --python "$supportRoot/scripts/export_animation_blender.py" -- $request.Clip $blend $model $assetRoot *> $log
        $code=$LASTEXITCODE
        $ErrorActionPreference='Stop'
        if($code -ne 0){throw "Não foi possível exportar a animação. Log: $log"}
        $validation=[IO.Path]::ChangeExtension($blend,'validation.json')
        $v=Get-Content -LiteralPath $validation -Raw | ConvertFrom-Json
        if(-not $v.Success -or -not $v.Reopened){throw 'O Blender não confirmou o esqueleto e a animação salvos.'}
        if($model -and (-not $v.BindPosePreserved -or $v.CheckedVertices -lt 1 -or $v.CheckedMeshFrames -lt 1 -or $v.MaxVertexErrorMeters -gt 0.0002)){
            throw 'A deformação da malha animada não foi validada. O arquivo não será entregue como concluído.'
        }
        $delivery=Copy-FlatExport $stage ($name+'.uasset') $root $OutputRoot
        @{Success=$true;Output=$delivery.Directory;Blend=$delivery.BlendFiles[0];Bones=$v.Bones;Frames=$v.Frames;Fps=$v.Fps;ModelIncluded=$v.ModelIncluded;Meshes=$v.Meshes;PackedImages=$v.PackedImages} | ConvertTo-Json | Set-Content -LiteralPath $ResultFile -Encoding UTF8
        exit 0
    }
    if($Mode -eq 'catalog') {
        & $engine @engineArgs catalog 1 '*' '*' $manifest
        if($LASTEXITCODE -ne 0) { throw 'Falha ao ler os indices do jogo.' }
        $m=Get-Content $manifest -Raw | ConvertFrom-Json
        Copy-Item -LiteralPath (Join-Path $m.ReportDirectory 'catalog.json') -Destination "$root/reports/desktop-catalog.json" -Force
        @{Success=$true; Catalog="$root/reports/desktop-catalog.json"} | ConvertTo-Json | Set-Content $ResultFile -Encoding UTF8
        exit 0
    }
    if($Mode -eq 'animation') {
        $selection=@(Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json)
        if($selection.Count -ne 1){throw 'Selecione uma sequência de animação.'}
        & $engine @engineArgs animation 1 $selection[0].Path '*' $manifest
        $code=$LASTEXITCODE
        $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        $data=Join-Path $m.ReportDirectory 'animation.json'
        if($code -ne 0 -or !(Test-Path -LiteralPath $data)){
            $fatal=Join-Path $m.ReportDirectory 'fatal.json'
            if(Test-Path -LiteralPath $fatal){$failure=Get-Content $fatal -Raw -Encoding UTF8|ConvertFrom-Json;if($failure.Message){throw $failure.Message}}
            throw 'Não foi possível decodificar a animação.'
        }
        Copy-Item -LiteralPath $data -Destination $ResultFile -Force
        exit 0
    }
    if($Mode -eq 'inspect') {
        $selection=@(Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json)
        if($selection.Count -ne 1){throw 'Selecione um asset para inspecionar.'}
        & $engine @engineArgs inspect 1 $selection[0].Path '*' $manifest
        $code=$LASTEXITCODE
        $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        $inspection=Join-Path $m.ReportDirectory 'inspection.json'
        if($code -ne 0 -or !(Test-Path -LiteralPath $inspection)){throw "Não foi possível ler este asset. Consulte $($m.ReportDirectory)."}
        Copy-Item -LiteralPath $inspection -Destination $ResultFile -Force
        exit 0
    }

    if($Mode -eq 'audio') {
        $selection=@(Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json)
        if($selection.Count -ne 1){throw 'Selecione exatamente um asset de áudio.'}
        & $engine @engineArgs audio 1 $selection[0].Path '*' $manifest
        $code=$LASTEXITCODE
        $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        $audioReport=Join-Path $m.ReportDirectory 'audio.json'
        if($code -ne 0 -or !(Test-Path -LiteralPath $audioReport)){
            $fatal=Join-Path $m.ReportDirectory 'fatal.json'
            if(Test-Path -LiteralPath $fatal){
                $failure=Get-Content -LiteralPath $fatal -Raw -Encoding UTF8 | ConvertFrom-Json
                if($failure.Message){throw $failure.Message}
            }
            throw "Este asset não forneceu mídia de áudio. Consulte $($m.ReportDirectory)."
        }
        $audio=Get-Content -LiteralPath $audioReport -Raw | ConvertFrom-Json
        $converter=Join-Path $supportRoot 'tools/vgmstream/vgmstream-cli.exe'
        if(!(Test-Path -LiteralPath $converter)){throw 'Conversor de áudio ausente: tools/vgmstream/vgmstream-cli.exe'}
        $tracks=@();$failed=0
        foreach($source in @($audio.Files | Select-Object -First 64)) {
            $target="$source.wav"
            & $converter -i -o $target $source
            if($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -gt 44) {$tracks+=$target}
            else {$failed++}
        }
        if($tracks.Count -eq 0){throw "Nenhuma faixa pôde ser decodificada. Consulte $($m.ReportDirectory)."}
        @{Success=$true; Tracks=$tracks; Failures=$failed; ReportDirectory=$m.ReportDirectory} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
        exit 0
    }

    if($Mode -eq 'preview') {
        $selection=@(Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json)
        if($selection.Count -ne 1) { throw 'Selecione exatamente um asset para a previa.' }
        $env:AION2_EXPORT_ROOT=Join-Path $root 'cache/previews'
        & $engine @engineArgs batch 1 $selection[0].Path '*' $manifest $RequestFile
        $code=$LASTEXITCODE
        $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        if(-not(Test-Path -LiteralPath (Join-Path $m.ReportDirectory 'batch-items.json'))) { throw "Falha ao converter o asset. Consulte $($m.ReportDirectory)." }
        $items=@(Get-Content -LiteralPath (Join-Path $m.ReportDirectory 'batch-items.json') -Raw | ConvertFrom-Json)
        $item=$items | Select-Object -First 1
        $models=@(Get-ChildItem -LiteralPath $item.OutputDirectory -Filter '*.glb' -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
        if($models.Count -eq 0) { throw "Este pacote nao gerou uma malha 3D. Consulte $($m.ReportDirectory)." }
        @{Success=$true; Models=$models; AssetRoot=$item.OutputDirectory; ExportFailures=$item.Failures; ReportDirectory=$m.ReportDirectory} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
        exit 0
    }
    if(-not(Test-Path -LiteralPath $Blender -PathType Leaf)) { throw 'Blender nao encontrado. Selecione o executavel nas configuracoes.' }
    & $engine @engineArgs batch 10000 '*' '*' $manifest $RequestFile
    $exportCode=$LASTEXITCODE
    $m=Get-Content $manifest -Raw | ConvertFrom-Json
    $itemsPath=Join-Path $m.ReportDirectory 'batch-items.json'
    if(-not(Test-Path $itemsPath)) { throw "A exportacao falhou (codigo $exportCode). Consulte $($m.ReportDirectory)." }
    $items=Get-Content $itemsPath -Raw | ConvertFrom-Json
    $results=@()
    foreach($item in $items) {
        $record=[ordered]@{Path=$item.Path; Success=$false; Output=''; Error=''; ExportFailures=$item.Failures}
        try {
            if($item.GlbFiles -lt 1) { throw 'Este pacote nao gerou uma malha.' }
            Write-Output ('BLENDER '+$item.Path)
            $log=Join-Path $m.ReportDirectory ('blender-'+(Split-Path $item.OutputDirectory -Leaf)+'.log')
            $ErrorActionPreference='Continue'
            & $Blender --background --factory-startup --python-exit-code 1 --python "$supportRoot/scripts/validate_blender.py" -- $item.OutputDirectory *> $log
            $blenderCode=$LASTEXITCODE
            $ErrorActionPreference='Stop'
            if($blenderCode -ne 0) { throw "Falha no Blender. Log: $log" }
            $validation="$root/reports/blender-$(Split-Path $item.OutputDirectory -Leaf).json"
            $v=Get-Content $validation -Raw | ConvertFrom-Json
            if(@($v.models).Count -lt 1 -or @($v.models | Where-Object { -not $_.valid_geometry }).Count) { throw 'Geometria nao validada.' }
            $delivery=Copy-FlatExport $item.OutputDirectory $item.Path $root $OutputRoot
            $record.Output=$delivery.Directory
            $record.Success=$item.Failures -eq 0
            if($item.Failures -gt 0) { $record.Error='Geometria importada; houve erros em arquivos auxiliares. Consulte o relatorio.' }
        } catch { $record.Error=$_.Exception.Message }
        $results += [pscustomobject]$record
        @{ReportDirectory=$m.ReportDirectory; Items=$results} | ConvertTo-Json -Depth 6 | Set-Content $ResultFile -Encoding UTF8
        Write-Output ('DONE '+$results.Count+'/'+$items.Count+' '+$item.Path)
    }
    exit 0
} catch {
    @{Success=$false; Error=$_.Exception.Message} | ConvertTo-Json | Set-Content $ResultFile -Encoding UTF8
    Write-Output $_.Exception.Message
    exit 1
}
