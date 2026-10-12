$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$m=Get-Content reports/gm-hair-batch-manifest.json -Raw | ConvertFrom-Json
$selected=Get-Content reports/gm-hair-selected.json -Raw | ConvertFrom-Json
$results=Get-Content (Join-Path $m.ReportDirectory 'blender-batch-results.json') -Raw | ConvertFrom-Json
$exports=Get-Content (Join-Path $m.ReportDirectory 'results.json') -Raw | ConvertFrom-Json
if($results.Count -ne $selected.Count) { throw 'Lote ainda incompleto' }
$details=@()
foreach($item in $results) {
    $type=@($exports | Where-Object { $_.Path -eq $item.Asset -and $_.Mesh } | ForEach-Object Type) -join ','
    $v=if($item.Validation) { Get-Content $item.Validation -Raw | ConvertFrom-Json } else { $null }
    $blends=@(if($item.OutputDirectory) { Get-ChildItem -LiteralPath $item.OutputDirectory -Filter '*.blend' -File })
    $valid=$item.Status -eq 'imported' -and $blends.Count -gt 0 -and @($v.models | Where-Object { -not $_.valid_geometry }).Count -eq 0
    $details += [pscustomobject]@{
        Asset=$item.Asset; Type=$type; Imported=$valid
        BlendFiles=@($blends.FullName); OutputDirectory=$item.OutputDirectory
        Vertices=($v.models.meshes | Measure-Object vertices -Sum).Sum
        Faces=($v.models.meshes | Measure-Object polygons -Sum).Sum
        Bones=($v.models.armatures | Measure-Object bones -Sum).Sum
        SkinningValid=@($v.models | Where-Object { -not $_.valid_skinning }).Count -eq 0
        BaseColorImages=@($v.models.images).Count; Validation=$item.Validation; Error=$item.Error
    }
}
$details | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $m.ReportDirectory 'final-details.json') -Encoding UTF8
$lines=@(
    '# Importacao GM_Hair - 08/10/2026',
    '',
    'Origem: AION2/Content/Character/Player/GM/Hair (instalacao acessada somente para leitura).',
    '',
    'Regra: por numero, priorizar GM_Hair_N.uasset; apenas quando nao existe, selecionar todos os GM_Hair_N_TypeX.uasset. Busca recursiva, com nome completo ancorado.',
    '',
    'Encontrados 291 candidatos em 102 numeros. Todos os numeros possuem versao base nesta instalacao: 103 arquivos selecionados, incluindo a copia de teste do 002. Nenhum Type selecionado.',
    '',
    ('Importados e validados no Blender: '+@($details | Where-Object Imported).Count+'/'+$selected.Count+'.'),
    '',
    'Resultados em Exportados, em uma pasta por arquivo. Pastas existentes foram preservadas; nomes repetidos receberam sufixo.',
    '',
    'Observacoes:',
    '- GM_Hair_000 possui 3 vertices, 1 face e material M_Hide. Parece ser um marcador, nao um cabelo visivel.',
    '- GM_Hair_002 na subpasta test e UStaticMesh: nao possui esqueleto, conforme o tipo original. A versao principal e separada.',
    '- Materiais contem apenas a previa de cor base. Shaders de cabelo, transparencia, mascaras e fidelidade visual ao jogo nao foram validados. Nenhuma preparacao FiveM foi realizada.',
    '',
    '| Arquivo | Tipo | Vertices | Faces | Ossos | Pasta final |',
    '|---|---|---:|---:|---:|---|'
)
foreach($item in $details) {
    $name=[IO.Path]::GetFileNameWithoutExtension($item.Asset)
    if($item.Asset -match '/test/') { $name += ' (test)' }
    $link=$item.OutputDirectory.Replace('\','/').Replace('E:/Download/Aion2Porting/','')
    $lines += "| $name | $($item.Type) | $($item.Vertices) | $($item.Faces) | $($item.Bones) | [Abrir pasta](<$link>) |"
}
$lines | Set-Content Importacao_GM_Hair.md -Encoding UTF8
$summary=[pscustomobject]@{Selected=$selected.Count; Imported=@($details | Where-Object Imported).Count; Failed=@($details | Where-Object { -not $_.Imported }).Count; Skeletal=@($details | Where-Object Type -eq 'USkeletalMesh').Count; Static=@($details | Where-Object Type -eq 'UStaticMesh').Count; Report=$m.ReportDirectory}
$summary | ConvertTo-Json | Set-Content reports/gm-hair-final-summary.json -Encoding UTF8
$summary | Format-List
if($summary.Failed) { exit 1 }
