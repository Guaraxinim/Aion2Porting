# Aion2 Porting

Aplicativo Windows WPF para pesquisar assets locais do Aion 2, visualizar modelos, texturas e animacoes, reproduzir audio reconhecido e exportar para Blender.

Versao atual: **0.7.20**. React/Tauri nao e a interface ativa.

## Executar

Abra `Aion2Porting.exe` na raiz. Em **Opcoes**, configure a instalacao do jogo, Blender, pasta de exportacao e idioma (PT-BR ou ingles). A instalacao do jogo e consultada apenas para leitura.

O viewer pode testar modelos, animacoes e montagens. Tipos confirmados, subcategorias manuais, miniaturas e perfis de esqueleto ficam em `cache/*.dat`. O cache e as exportacoes sao locais e nao fazem parte deste repositorio. A preparacao para FiveM requer trabalho posterior no Blender; o programa nao garante compatibilidade geral de todos os assets.

## Compilar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-desktop.ps1
```

O bootstrap prepara o SDK .NET 10.0.401 e o CUE4Parse do commit `3222101ce839c5477df33a1aacf236fe9d130f64`. As fontes CUE4Parse presentes neste repositorio contem ajustes locais; preserve-as. O bootstrap nao substitui uma pasta existente.

Para leitura do jogo, forneca localmente `mappings/AION2-5.3.2.0-1.0.52.0.usmap`, `tools/CUE4Parse-Natives.dll`, `tools/oodle-data-shared.dll` e `tools/vgmstream/vgmstream-cli.exe` com suas dependencias. Esses arquivos nao sao enviados ao GitHub. Blender deve estar instalado para gerar BLEND.

## Verificar

```powershell
./Aion2Porting.exe --self-test
./tools/dotnet/dotnet.exe ./src/Aion2Porting.Cli/bin/Release/net10.0/Aion2Porting.Cli.dll animation-sampling-test
```

O autoteste verifica catalogo quando disponivel, pesquisa, fila e armazenamento DAT. Os resultados ficam em `reports`. Testes de assets reais dependem da instalacao e do mapping local.

## Estrutura

- `src/Aion2Importer`: interface WPF e viewers.
- `src/Aion2Porting.Cli`: leitura/conversao e inspeção.
- `vendor/CUE4Parse`: dependencia com ajustes locais e licencas upstream.
- `scripts`: compilacao, worker e exportacao Blender.
- `licenses`: avisos e licencas de dependencias.
- `GUIA_IMPORTER.md`: uso e historico dos recursos.
- `VERSIONING.md`: historico de atualizacoes.

O projeto usa [CUE4Parse](https://github.com/h4lfheart/CUE4Parse), do ecossistema [FortnitePorting](https://github.com/h4lfheart/FortnitePorting). Consulte LICENSE e NOTICE das dependencias. Nenhum asset extraido do jogo e incluido.