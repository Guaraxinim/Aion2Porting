function Copy-FlatExport([string]$Source, [string]$AssetPath, [string]$ProjectRoot, [string]$OutputRoot = '') {
    $sourceRoot = [IO.Path]::GetFullPath($Source)
    $allowed = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'exports')) + [IO.Path]::DirectorySeparatorChar
    if (-not $sourceRoot.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Origem fora da pasta de exports.' }
    $name = [IO.Path]::GetFileNameWithoutExtension($AssetPath) -replace '[^\p{L}\p{N}_.-]', '_'
    if ([string]::IsNullOrWhiteSpace($name)) { $name = 'Objeto' }
    $destination = if ($OutputRoot) { Join-Path $OutputRoot $name } else { Join-Path $ProjectRoot ("Exportados/" + $name) }
    if (Test-Path -LiteralPath $destination) {
        $destination += '_' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '_' + [guid]::NewGuid().ToString('N').Substring(0,6)
    }
    [void](New-Item -ItemType Directory -Path $destination)
    $used = @{}
    $mapping = @()
    foreach ($file in (Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName)) {
        $targetName = $file.Name
        $suffix = 1
        while ($used.ContainsKey($targetName) -or (Test-Path -LiteralPath (Join-Path $destination $targetName))) {
            $targetName = $file.BaseName + '_' + $suffix + $file.Extension
            $suffix++
        }
        $used[$targetName] = $true
        $target = Join-Path $destination $targetName
        Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Copia divergente: $target" }
        $mapping += [pscustomobject]@{Source=$file.FullName; Destination=$target}
    }
    $blends = @($mapping | Where-Object { [IO.Path]::GetExtension($_.Destination) -eq '.blend' } | ForEach-Object Destination)
    if ($blends.Count -eq 0) { throw 'Nenhum .blend foi criado para a pasta simplificada.' }
    return [pscustomobject]@{Directory=$destination; BlendFiles=$blends; Files=$mapping}
}
function Invoke-AssetSelectionLoop($Rows, [scriptblock]$Select, [scriptblock]$Export) {
    while ($true) {
        $choice = & $Select $Rows
        if ($null -eq $choice) { return }
        try { & $Export $choice.Name }
        catch { Write-Host $_.Exception.Message -ForegroundColor Red }
        Write-Host 'Voltando para a mesma lista. Escolha outro objeto ou cancele para sair.'
    }
}
