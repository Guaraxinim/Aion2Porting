$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
. "$PSScriptRoot/export-workflow.ps1"
$m=Get-Content reports/gm-hair-batch-manifest.json -Raw | ConvertFrom-Json
$items=Get-Content (Join-Path $m.ReportDirectory 'batch-items.json') -Raw | ConvertFrom-Json
$results=@()
foreach($item in $items) {
    $record=[ordered]@{Asset=$item.Path; ExportFailures=$item.Failures; Status='pending'; OutputDirectory=$null; Validation=$null; Error=$null}
    try {
        if($item.GlbFiles -lt 1) { throw 'Nenhum GLB gerado' }
        $name=Split-Path $item.OutputDirectory -Leaf
        $log=Join-Path $m.ReportDirectory ("blender-"+$name+'.log')
        $ErrorActionPreference='Continue'
        & 'C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe' --background --factory-startup --python-exit-code 1 --python scripts/validate_blender.py -- $item.OutputDirectory *> $log
        $code=$LASTEXITCODE
        $ErrorActionPreference='Stop'
        if($code -ne 0) { throw "Blender falhou: $log" }
        $report=Join-Path $root ('reports/blender-'+$name+'.json')
        $v=Get-Content $report -Raw | ConvertFrom-Json
        if(@($v.models).Count -lt 1 -or @($v.models | Where-Object { -not $_.valid_geometry }).Count) { throw 'Geometria invalida' }
        $delivery=Copy-FlatExport $item.OutputDirectory $item.Path $root
        $record.OutputDirectory=$delivery.Directory
        $record.Validation=$report
        $record.Status=if($item.Failures -gt 0) { 'imported_with_export_errors' } else { 'imported' }
    } catch { $record.Status='failed'; $record.Error=$_.Exception.Message }
    $results += [pscustomobject]$record
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $m.ReportDirectory 'blender-batch-results.json') -Encoding UTF8
    Write-Output ("{0}/{1} {2} {3}" -f $results.Count,$items.Count,$record.Status,[IO.Path]::GetFileName($item.Path))
}
$results | Group-Object Status | Select-Object Name,Count | ConvertTo-Json | Set-Content (Join-Path $m.ReportDirectory 'blender-batch-summary.json') -Encoding UTF8
