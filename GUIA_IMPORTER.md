# Interface atual — WPF 0.7.12

A migração para React/Tauri foi cancelada. Abra `Aion2Porting.exe` na raiz do projeto. A base funcional é a 0.7.12, com aparência adaptada de `design/Aion2Porting-nova-referencia.html` e os campos existentes de busca, exclusões, múltiplos tipos, subcategorias, chunks e fila.

A tela Início abre sem categoria selecionada; o primeiro clique em Todos os assets abre a biblioteca. O cabeçalho EXPORTAR não mostra quantidade. Jogo, Blender, saída e Opções ficam no cabeçalho superior.

Texturas lidas com sucesso pelo Inspecionar/Visualizar recebem **Textura disponível**, ícone de imagem e miniatura JPEG compacta no mesmo `cache/available-models.dat`. O tipo e a miniatura sobrevivem ao fechamento; mudanças na origem invalidam a confirmação. Os nomes sugerem tipos, mas a confirmação depende da leitura real. O cache de prévias continua sendo removido ao fechar normalmente.

# Aion2 Porting — pacote portátil

1. Extraia TODO o ZIP em uma pasta onde você possa gravar arquivos.
2. Abra Aion2Porting.exe. Mantenha as pastas de suporte ao lado dele.
3. Clique em **Jogo…** e selecione a instalação do Aion 2 (ou sua pasta Content/Paks).
4. Clique em **Blender…** e indique o blender.exe instalado.
5. Clique em **Pasta de saída…** e escolha onde salvar os modelos. A escolha fica salva.
6. Clique em **Atualizar catálogo**, pesquise os assets e adicione-os à fila.
7. Clique em **Exportar para Blender**. O resultado de cada item aparece na fila; use **Abrir resultado**.

Requisitos: Windows x64, Aion 2 e Blender instalados. Não precisa instalar Git, SDK .NET ou Python.
A instalação do jogo é usada somente para leitura. Escolha uma saída fora dela.

Busca por palavras, nome exato ou padrões (* e ?), com filtros de categoria, subcategoria, tipo, chunk e pasta.
A opção de preferir o modelo base omite variantes _TypeN quando o mesmo nome base existe.
Os tipos identificados pelo nome são estimativas; o resultado da exportação confirma se o pacote contém uma malha.

Cada asset fica em uma pasta simples com .blend, .glb e arquivos auxiliares. Exportações anteriores são preservadas.
O aplicativo mantém a lista e os filtros depois de exportar.
As configurações e o catálogo ficam em reports; arquivos intermediários ficam em exports, dentro da pasta do aplicativo.
Essas pastas são criadas durante o uso e não fazem parte da distribuição limpa.

A conversão foi validada com Blender 5.2.2 e o mapping AION2-5.3.2.0-1.0.52.0.
Não há garantia de compatibilidade com todos os assets ou futuras atualizações do jogo.
Materiais oferecem prévia de cor base; transparência de cabelo e shaders podem precisar de ajustes.
A preparação para FiveM é uma etapa posterior.

Pastas backend, scripts, mappings e tools são dependências do aplicativo: não é necessário abri-las.
Licenças e avisos disponíveis estão em licenses.

## Visualizar antes de importar (versão 0.4.0)

Selecione um único asset e clique em **Visualizar 3D**. A prévia abre em um painel à direita da janela principal, mantendo a lista acessível e sem executar o Blender.

- Arrastar com o botão esquerdo: girar.
- Arrastar com o botão direito: mover.
- Roda do mouse: aproximar ou afastar.
- **Enquadrar (F)**: centralizar o modelo; **Frente** e **Lado**: vistas rápidas.
- **Textura base (bruta)**: alternar entre sólido e a textura disponível.
- **Adicionar à fila**: selecionar o modelo para exportar depois.
- **Fechar 3D** ou Esc com foco no viewer: fechar o painel e devolver o espaço à lista.

A prévia mostra a pose de referência. Cabelos abrem em modo sólido, assim como os demais modelos: texturas do jogo podem codificar máscaras e exibir cores artificiais sem seus shaders. Não há reprodução de animações, transparência, física ou shaders do Aion 2.

A primeira visualização converte somente o asset selecionado e suas dependências para a pasta local `cache/previews`. Ela não grava na pasta de saída escolhida nem cria .blend. A prévia é reaproveitada durante a sessão; atualizar o catálogo força nova conversão. É possível interromper a preparação pelo botão vermelho **×** na janela principal. O jogo continua somente para leitura.

Pacotes sem malha exibem uma mensagem. O visualizador limita cada arquivo a 256 MB e 2 milhões de triângulos; a exportação normal continua disponível para modelos maiores.

Validação desta versão: prévia real do GM_Hair_412 (21.393 vértices e 27.297 triângulos), HW_0001, controles da câmera, troca entre sólido/textura e mensagem para pacote sem malha. Isso não confirma compatibilidade de todos os assets.

## Fila recolhível e modelos confirmados (versão 0.5.0)

Use **Ocultar fila** para aumentar a altura da lista de assets. **Mostrar fila** restaura a fila com todos os itens e estados preservados. A preferência é salva; o botão vermelho × para cancelar continua acessível mesmo com a fila oculta.

O viewer agora fica integrado à direita da lista. Você pode continuar pesquisando e selecionar outro asset para **Visualizar 3D**. Fechar o painel mantém a pesquisa e a fila.

Quando a geometria é carregada com sucesso no viewer, o campo **Tipo sugerido** muda para **Modelo disponível**. Essa opção também aparece no filtro de tipos. Apenas gerar um GLB, selecionar um arquivo ou falhar no carregamento não confirma o asset.

As confirmações ficam em `cache/available-models.json` e são restauradas ao abrir o programa. Elas registram o asset, tamanho, chunk, número de vértices e triângulos e data de validação. O cache é separado por instalação e mapping; mudanças de tamanho/data dos arquivos UTOC/UCAS são verificadas ao recarregar o catálogo. Isso evita usar uma confirmação anterior para arquivos alterados. A disponibilidade confirma a prévia da malha, não a fidelidade dos shaders ou uma importação concluída no Blender.

O ZIP limpo não inclui seu cache pessoal.

## Busca e organização (versão 0.6.0)

- **Tipos:** abra o seletor e marque vários tipos, como Modelo provável e Modelo disponível. O resultado inclui qualquer um dos tipos marcados. Todos os tipos (ou nenhum marcado) desativa esse filtro.
- **Excluir:** digite um trecho do nome ou caminho. Separe alternativas por ponto e vírgula, por exemplo `_Type; /NPC/; _000`. Qualquer trecho encontrado remove o item dos resultados, sem diferenciar maiúsculas de minúsculas. Espaços dentro de cada trecho são preservados.
- **Biblioteca:** os nomes quebram linha, sem rolagem horizontal. Ocultar biblioteca / Mostrar biblioteca libera e devolve seu espaço; a preferência fica salva.
- **Viewer:** Enquadrar, Frente e Lado ficam na mesma linha, com Textura base abaixo. O seletor de malhas só aparece se o asset tiver mais de uma malha exportada.
- **Progresso:** fica abaixo da descrição da tarefa, com um botão vermelho × para cancelar.
- **Cache temporário:** ao fechar normalmente o programa, `cache/previews` é apagado. O arquivo `cache/available-models.json`, exports e configurações são preservados. Se houver uma tarefa ativa, cancele pelo × antes de fechar. Encerramentos forçados pelo sistema não permitem garantir a limpeza.

## Teste local da interface baseada no HTML

A interface de teste usa o visual de `design/aion_2_importer_ui.html`: fonte Plus Jakarta Sans, fundo azul-escuro, ciano/roxo, cards e painel lateral. Abra `Aion2Porting.exe` na pasta principal (ou `Exportar.cmd`). Nenhum novo ZIP ou diretório de distribuição foi gerado para este teste.

- Início: resumo das funções realmente implementadas e acesso à biblioteca.
- Biblioteca: catálogo real em páginas de 60 cards; Ctrl/Shift seleciona vários cards na página. Adicionar resultados continua considerando toda a busca filtrada.
- Os ícones dos cards são marcadores neutros, não miniaturas do asset. Use Visualizar 3D para inspecionar a geometria real.
- Opções: escolha os caminhos do jogo, Blender e saída. Salvar alterações aplica as escolhas; Cancelar descarta a edição.
- Permanecem disponíveis fila, filtros de múltiplos tipos, exclusões, cache de modelos confirmados, painel 3D e limpeza das prévias ao fechar.

## Identidade e histórico na tela Início

O aplicativo local agora se chama **Aion2 Porting**, com executável `Aion2Porting.exe`. `Exportar.cmd` abre esse executável. A tela Início reúne o histórico das funções implementadas, incluindo busca, viewer, cache, fila, exportação, lote GM_Hair, versões portáteis e leitura dos assets.

O logo transparente e o ícone com fundo branco foram copiados dos arquivos originais fornecidos pelo usuário. O logo é exibido ao lado do nome; o .ico está incorporado ao executável e à janela. Esta alteração permanece local, sem novo ZIP ou distribuição.

## Teste local: classificação e miniaturas

Abra app/Aion2Porting.exe (ou Exportar.cmd). Esta alteração não atualiza a dist.
Quando uma tentativa de prévia falha, o asset fica como Não classificado; cancelamento não altera a classificação. Quando a geometria carrega, fica como Modelo disponível. Assets ainda não testados mantêm a sugestão pelo nome. Esses resultados são persistidos e invalidados quando muda a origem do asset.

A primeira carga bem-sucedida gera uma captura enquadrada do modelo no modo atual do viewer, em JPEG de 192×144. As imagens, os resultados de tipo e as subcategorias editadas ficam reunidos em cache/available-models.dat. Esse arquivo é um contêiner compactado de uso interno. Apenas as miniaturas da página visível são carregadas na memória. O arquivo available-models.dat permanece ao fechar, enquanto cache/previews é apagado. Falhas de captura são registradas em reports/thumbnail-error.txt.

Ainda não há associação verificada entre ícones de interface do jogo e os modelos; as imagens dos cards são capturas do viewer. Ao selecionar Início, os destaques da categoria e dos assets são removidos.
## Cache DAT, subcategorias e duplo clique

A versão local usa cache/available-models.dat para imagens JPEG compactas, resultados da validação 3D e subcategorias personalizadas. Na primeira abertura, importa available-models.json e model-thumbnails.zip caso existam na mesma pasta; os originais são preservados como backup e não são mais atualizados. Não copie caches de outra versão enquanto o aplicativo estiver aberto.

Selecione um ou vários cards e clique em Editar subcategoria. Digite o nome e salve. A alteração aparece no filtro de subcategorias e permanece após fechar, mesmo se o modelo não carregar. Mudar o mapping invalida a confirmação 3D, mas preserva a subcategoria manual para o mesmo caminho na mesma instalação.

Dê dois cliques com o botão esquerdo em um card para tentar abrir a prévia 3D. Enquanto há uma tarefa em andamento, o programa ignora novos pedidos de prévia.

O arquivo DAT é atualizado por substituição atômica. Caso a leitura falhe, o arquivo existente é preservado, sem sobrescrever seus dados. A limpeza ao fechar continua restrita a cache/previews.
## Prévia sem perder a posição, avisos e áudio (teste local)

Ao abrir uma prévia, a lista mantém sua página e rolagem. Só o card afetado é atualizado; se a nova classificação não corresponder aos filtros, ele é removido sem recarregar a lista. A navegação continua sem pular os itens seguintes.

Avisos aparecem acima da barra: amarelo para alerta, vermelho em negrito para erro e verde para uma prévia carregada. Alertas e erros usam um toque curto e suave, com intervalo mínimo entre sons. Passe o mouse sobre a mensagem para ver o texto completo.

Use Ouvir áudio ou dê dois cliques em um asset de Sound/Wwise. O player integrado permite reproduzir, pausar, parar, buscar uma posição e ajustar o volume; eventos com mais de uma mídia oferecem seleção de faixa. A prévia reproduz arquivos de mídia, não simula o sistema de eventos Wwise completo. Assets de controle, eventos sem mídia e codecs não suportados exibem aviso e podem não tocar. O áudio não marca o item como Modelo disponível.

Foi validado AObj_BarrierA: mídia Wwise Vorbis, 44.100 Hz, estéreo, 6,37 segundos, extraída, convertida e com reprodução avançando no player. O teste automatizado usa volume zero.

A conversão usa vgmstream-cli (https://github.com/vgmstream/vgmstream), com o pacote oficial em tools/vgmstream e licenças no arquivo COPYING dessa pasta. Arquivos temporários de áudio ficam em cache/previews e são removidos ao fechar, junto das prévias 3D. A instalação do jogo continua somente para leitura. Nenhuma nova dist foi gerada.
## Áudio confirmado e inspeção de efeitos

Eventos Wwise e arquivos de mídia reconhecidos começam como Áudio provável. Quando o player abre a faixa com duração válida, o tipo muda para Áudio, o card exibe ♫ e a confirmação fica em available-models.dat. A prévia não confirma áudio apenas por extensão ou nome.

A busca na instalação encontrou 59.409 mídias WEM e 38.442 eventos Wwise. Esses números indicam arquivos encontrados, não testes de reprodução de todos eles. Foram extraídos e convertidos um diálogo, três faixas de voz de monstro e uma mídia WEM direta, além do ambiente já validado. O catálogo local foi atualizado para incluir as mídias diretas.

Dê dois cliques em um asset de Efeitos, ou selecione qualquer asset e use Inspecionar. Malhas do próprio asset abrem no viewer 3D; outros assets mostram informações estruturadas, texturas e eventuais malhas associadas. A leitura de Niagara mostra componentes, emissores, formas de desenho, parâmetros e referências. O sistema não simula a animação Niagara, materiais ou shaders do jogo. Ver uma malha associada não classifica todo o efeito como Modelo disponível.

Foram testados o sistema Niagara FX/Particle/00_Debug e a textura FX/Texture/_Flow, incluindo seus painéis na interface. A inspeção tem limites de profundidade e quantidade para evitar carregar dependências sem limite, e informa quando a leitura é parcial. Os arquivos de prévia continuam temporários.

Use app/Aion2Porting.exe para testar. Nenhuma nova distribuição foi gerada nesta alteração.
## Sounds por referência e ícone atualizado

A versão de teste mais recente está em app-test/Aion2Porting.exe. Exportar.cmd seleciona automaticamente a compilação local mais recente entre app, app-test e o executável da raiz. Feche a janela anterior antes de testar a nova, para evitar duas instâncias compartilhando o cache.

O leitor agora acompanha referências explícitas de Sound/Act para eventos Wwise, com limites de profundidade e quantidade. SE_Ferk_01_NormalAtk_Hit_01_Sound, anteriormente sem mídia direta, forneceu três faixas por essa leitura. Eventos, condições e tempos do jogo não são simulados; as faixas são reproduzidas individualmente.

SW_Fly_Aerial_Sound foi lido como AionAct de 419 bytes, com seções Start, Loop e End e TaskLinks vazios. Não foi identificada mídia reproduzível nesse asset com o mapping atual. AutoMove_Sound e Item_Drop_Sound também apresentaram tarefas vazias. O aviso agora explica esse resultado, sem classificá-los como áudios confirmados.

Play_Air_Fly_Wind forneceu 15 faixas decodificáveis. Ele é um evento de vento de voo encontrado na pesquisa; não há associação comprovada entre ele e SW_Fly_Aerial_Sound, portanto não é usado como substituto automático.

O ícone dos áudios confirmados agora é uma forma de onda vetorial azul #4FACFE, a mesma cor do cubo. Não requer novas imagens no cache.

Shaders: o viewer mantém os materiais simples existentes. Não foi adicionado um novo renderizador nem execução de shaders Unreal. A implementação de shaders originais exigiria adaptação e medição de desempenho; ainda não há evidência de que seu custo seria aceitável. A documentação do WPF destaca que materiais em camadas introduzem passagens adicionais de renderização:
https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/maximize-wpf-3d-performance

Nenhuma nova dist foi gerada.
## Materiais manuais e animações — teste local

Abra `Exportar.cmd` ou `app-test/Aion2Porting.exe` após fechar a versão anterior. Nenhuma nova dist foi gerada.

No viewer, **Ativar materiais avançados** aplica textura, alpha e brilho simples apenas quando solicitado. O botão também desativa o modo; abrir outro modelo restaura o modo básico. É uma aproximação de materiais, não execução dos shaders originais da Unreal. A geometria é reutilizada e as texturas da prévia são limitadas a 2048 pixels de largura. Não há garantia de desempenho igual em todas as GPUs.

Para movimentos, procure um asset de animação (por exemplo `GM_SW_Fly_Aerial_Loop`) e clique em **Animações**. Dois cliques em arquivos das pastas `/Ani/` ou `/Animations/` também tentam abrir a animação. Ela começa pausada. Use **Reproduzir**, **Pausar**, **Parar** e a barra de posição.

Inicialmente é mostrado o esqueleto. Para animar uma malha, visualize primeiro seu modelo 3D, abra a animação e clique em **Usar modelo visualizado**. Também é possível escolher um GLB já exportado em **Aplicar a modelo GLB compatível…**. O modelo precisa ter ossos e pose base compatíveis. A malha animada usa material neutro. Não há retargeting automático, física de cabelo/roupa nem simulação de Niagara.

Foi validado o movimento `AION2/Content/Character/Player/GM/Ani/Common/SW/Fly/GM_SW_Fly_Aerial_Loop.uasset` com `AION2/Content/Character/Player/GM/Armor/0000/GM_0000_T01/GM_0000_T01_Body.uasset` (6.726 vértices). O cabelo GM_Hair_412 foi rejeitado por incompatibilidade de ossos, como esperado. Isso não confirma compatibilidade de todas as animações.

A prévia aceita sequências individuais não aditivas, até 60 segundos e 512 ossos, amostradas a 30 quadros/s; modelos animados têm limite de 50 mil vértices e 64 MB. A reprodução reduz a frequência de atualização se o processamento de um quadro demorar. A animação fica no cache temporário de previews, removido ao fechar. Abrir ou falhar uma animação não refaz a lista nem altera a classificação de modelo.
## Colocar esqueleto, animação e modelo na fila

Abra a sequência em **Animações**. O programa usa a animação aberta nesse painel, mesmo se a seleção da biblioteca mudar. O Blender deve estar configurado em Opções.

- **Fila: esqueleto + animação** adiciona uma exportação apenas com Armature e Action.
- **Fila: modelo + texturas + animação** fica disponível depois de carregar um GLB compatível por **Usar modelo visualizado** ou **Aplicar a modelo GLB compatível…**. Ela adiciona uma exportação com malha, pesos, texturas empacotadas, esqueleto completo e Action em um único `.blend`.

A pasta de saída configurada recebe uma pasta com o nome da animação, contendo o `.blend` e um relatório `.validation.json`. Exportações repetidas ganham um sufixo para preservar os arquivos anteriores. As duas opções podem permanecer juntas na fila e são processadas por **Exportar para Blender**. O GLB escolhido fica associado à entrada combinada; mudar a seleção da biblioteca não troca esse modelo.

A exportação é amostrada a 30 quadros/s, com os mesmos limites da prévia (sequência individual não aditiva, até 60 segundos e 512 ossos). Não inclui física, eventos/notifies nem lógica do jogo. Não é uma conversão pronta para o rig do FiveM.

Teste confirmado: `GM_SW_Fly_Aerial_Loop`, esqueleto `SK_GM_Root`, 159 ossos e 30 quadros. A opção combinada com `GM_0000_T01_Body` foi reaberta no Blender 5.2.2 LTS e confirmou a malha ligada ao armature e duas imagens empacotadas. Todas as poses e relações entre ossos foram comparadas com a origem. GLBs incompatíveis são recusados para evitar deformação incorreta. A nova compilação de teste está em `app-test/Aion2Porting.exe`; feche a janela antiga e abra `Exportar.cmd`. Nenhuma nova dist foi gerada.

### Correção da deformação no Blender

A exportação combinada preserva agora o armature, as matrizes da pose base dos ossos, os parentes dos objetos e os modificadores do GLB. Os ossos ausentes são acrescentados ao armature existente. Cada movimento é aplicado em relação à pose base da animação, mantendo as transformações que vinculam a malha ao esqueleto.

A validação anterior conferia as poses dos ossos, mas não detectava o modelo distorcido. O exportador agora reabre o `.blend` e compara também os vértices deformados com o movimento esperado pelos pesos da malha, em cinco posições da animação. Se essa comparação falhar, a exportação não recebe estado Concluído.

Foram reproduzidos e corrigidos `MOB_Balgest_01` com `MOB_Balgest_01_Idle` e `MOB_Basilisk_02_V01` com `MOB_Basilisk_02_V01_Idle`. Também foi validado `MOB_Basilisk_02` com sua sequência Idle. As renderizações antes e depois ficam em `reports/balgest-before.png`, `reports/balgest-corrected.png`, `reports/basilisk-before.png` e `reports/basilisk-corrected.png`. Os arquivos exportados anteriormente precisam ser gerados novamente pela versão local; não são alterados automaticamente.
# Atualização 0.7.12

Montagens são abertas pelo botão **Visualizar**. O viewer monta os trechos respeitando seus tempos, recortes, velocidade e número de repetições. Em montagens com mais de uma trilha, escolha **Trilha da montagem**; as trilhas são visualizadas individualmente, sem mistura de poses.

**Seção da montagem** permite escolher uma seção e iniciar sua reprodução. Com **Seguir seções da montagem** marcado, são respeitados os destinos de cada seção, inclusive repetições. No `MOB_BlackDragon_01_KnockDownB_AM`, **Start** entra em **Loop**, que repete. Para sair, selecione **LoopEnd** e clique em **Reproduzir**. **End** mostra o final. Desmarque a opção para ver a linha do tempo completa de forma contínua.

A fila exporta uma passagem finita da linha do tempo; repetições indefinidas das seções dependem do jogo e não criam quadros infinitos no BLEND. Eventos, física, misturas entre trilhas e trechos aditivos não são simulados. Trechos sobrepostos na mesma trilha são reportados como não suportados.

O limite do último quadro é aplicado a todas as sequências e a cada trecho da montagem. Não é uma exceção específica do BlackDragon.

# Atualização 0.7.11

Em **Opções → Idioma**, escolha **Português (Brasil)** ou **English** e clique em **Salvar alterações**. A interface muda sem reiniciar; a preferência permanece em `reports/desktop-settings.json`. Os nomes e caminhos originais dos assets permanecem iguais.

A leitura de `MOB_BlackDragon_01_Idle` foi corrigida para respeitar o último quadro válido ao converter animações com FPS fracionário para 30 FPS.

# Atualização 0.7.10

Abra `Aion2Porting.exe`. O botão **Visualizar** escolhe a prévia de modelo, animação, áudio ou inspeção conforme o asset. Nos viewers, **ⓘ** inspeciona o asset aberto.

Na animação, abra **Escolher modelo da biblioteca**, busque pelo nome ou caminho e clique em **Aplicar modelo selecionado**. A busca mostra até 100 resultados entre **Modelo provável** e **Modelo disponível**; refine o texto para encontrar outros. A compatibilidade do esqueleto e da pose base é verificada antes de aplicar. Clique em **Textura base** para carregar as texturas disponíveis do modelo; elas não são carregadas automaticamente.


### Atualização 0.7.13
No viewer de animação, abra **Escolher modelo da biblioteca** e ative **Somente compatíveis com este esqueleto**. O filtro usa os ossos e a pose base de modelos previamente visualizados/aplicados, salvos em cache/available-models.dat. Modelos ainda sem perfil não aparecem nesse filtro: desative-o para localizar e testar um candidato. Não há retargeting automático.

As montagens percorrem todas as seções em ordem temporal e repetem o ciclo inteiro. Texturas são representadas por ícones na lista; animações confirmadas têm ícone de filme, com + para montagens. O botão **Abrir Exportação** abre a saída configurada em **Opções**. O progresso está na lateral, e o viewer aparece somente na biblioteca.

## Verificação de tipos — 0.7.16

Em Opções, use **Verificar tipos de todos os arquivos / Retomar**. A rotina percorre o catálogo completo, carrega os recursos no mesmo decodificador/viewer e salva as confirmações no cache DAT. Recursos sem prévia reconhecida não são declarados modelos, animações ou áudio disponíveis.

Use o X vermelho para interromper. A rotina grava um checkpoint a cada 25 itens e também ao interromper normalmente. Depois retome pelo mesmo botão. Itens já registrados, incluindo falhas, são pulados; alterações no mapping ou nos contêineres invalidam a identidade dos itens afetados. Se houver encerramento abrupto, o último grupo ainda sem checkpoint será tentado novamente.

A lista fica em **reports/catalog-scan/results.jsonl**, um registro por linha com Path, Status, Type e Error. **reports/catalog-scan/progress.json** informa o andamento. Confirmed significa que o recurso foi validado; Inspected indica leitura dos dados sem confirmar uma prévia de modelo/áudio/animação/textura; Failed registra a tentativa sem sucesso. A rotina limpa as prévias temporárias entre itens e não cria exports BLEND.

O botão **Blender** no cabeçalho abre o executável configurado em Opções. O caminho do jogo também continua configurável em Opções.

Na vers�o 0.7.17, bloqueios no arquivo de progresso n�o interrompem a verifica��o. O estado mais recente permanece em progress.json.tmp enquanto progress.json estiver bloqueado; o hist�rico results.jsonl continua sendo salvo. Diagn�sticos ficam em reports/catalog-scan/errors.log.

Vers�o 0.7.18: clique sobre o status na lateral para copiar sua mensagem completa. Anima��es tempor�rias da varredura ficam isoladas em reports/catalog-scan/work/animation.


### Armazenamento compacto - 0.7.19
O cache fica em cache/*.dat: available-models (tipos, miniaturas e subcategorias), catalog, settings, scan-results e scan-progress (historico e retomada). Os DAT sao conteineres ZIP compactados e substituem os JSON/JSONL anteriores.
Relatorios gerados ficam em diagnostics.dat; apoio da varredura em scan-support.dat; etapas intermediarias em staging.dat. Previas ociosas ficam em temporary.dat, descartado ao fechar. GLB, audio e texturas permanecem extraidos enquanto o Viewer ou a fila os utiliza. A saida escolhida pelo usuario e preservada.
Falhas de leitura de cache limpam os dados do asset e tentam uma vez novamente a partir do jogo. Subcategorias manuais legiveis sao preservadas. Cache corrompido e guardado em available-models.corrupt-*.dat antes de reconstruir.
