# Versões do Aion2 Porting

Versão atual: **0.7.16**. A contagem foi reconstruída pelas solicitações de alterações implementadas após a distribuição 0.7.0. Não havia versões individuais registradas para as primeiras alterações; os números abaixo documentam a contagem retrospectiva, não distribuições publicadas.

1. Classificação de modelos, miniaturas, aparência da lista e seleção do Início.
2. Cache DAT, subcategorias persistentes e visualização por duplo clique.
3. Preservação da posição na lista, avisos integrados e reprodução de áudio.
4. Classificação de áudio provável/confirmado e inspeção de efeitos.
5. Áudios referenciados e ícone de áudio.
6. Materiais manuais e visualização de animações.
7. Exportação de esqueleto e animação.
8. Fila conjunta de modelo, texturas e animação em BLEND.
9. Correção da deformação no Blender com preservação da pose base.
10. Seleção de modelos da biblioteca na animação, textura manual, Visualizar unificado e inspeção nos viewers.
11. Correção do último quadro em animações com FPS fracionário (BlackDragon) e interface em português brasileiro e inglês com preferência persistente.
12. Reprodução de montagens por trechos, recortes, velocidade, repetições e seções; seleção de trilhas; testes gerais de limites de quadros. Teste real: MOB_BlackDragon_01_KnockDownB_AM.

Cada nova solicitação de atualização implementada incrementa o terceiro número uma vez. Perguntas, testes e recompilações da mesma atualização não incrementam a versão. O terceiro número nunca supera 99: depois de 0.7.99 vem 0.8.0.

A versão é definida em `src/Aion2Importer/Aion2Importer.csproj`. A interface mostra a versão do assembly WPF. Use `scripts/build-desktop.ps1` para atualizar o executável; o programa é iniciado por `Aion2Porting.exe`.


Migração 0.7.13 React/Tauri cancelada pelo usuário. Retorno explícito à base WPF 0.7.12, com adaptação da nova referência HTML, correção da seleção inicial e cache/ícone para texturas. A versão foi mantida em 0.7.12 por solicitação expressa.

## 0.7.13 — WPF: filtros e organização dos viewers

Filtro de modelos compatíveis no seletor da biblioteca do viewer de animação: compara os ossos efetivamente ponderados da malha e a pose base, salvando o perfil no DAT. Ative “Somente compatíveis com este esqueleto”; modelos sem perfil ficam ocultos. Visualize o modelo ou aplique-o no viewer para registrar o perfil. Não aplica retargeting.

Texturas usam ícone, sem miniatura na lista. Animações convertidas são classificadas como Animação ou Animação de Montagem, com ícone próprio e + para montagens; tipos persistem no DAT. Montagens percorrem as seções em ordem temporal, saem de Loop e repetem o ciclo completo; não simulam eventos ou mistura entre trilhas.

Busca em três linhas, Visualizar à direita, progresso na lateral, remoção do grupo Configurações da lateral, Abrir Exportação no cabeçalho, Opções com espaçamento revisado e Início ocupando também o espaço do viewer. Botões com conteúdo centralizado. Executável atualizado sem gerar distribuição ou ZIP.

Validação: reports/update-0713-test.json e reports/desktop-self-test.json. Comparação real de MOB_Balgest_01 com a própria animação e rejeição para BlackDragon; cache do perfil recarregado. Montagem real BlackDragon percorre o fim e reinicia. Não foi feita nova exportação BLEND nesta atualização.
## 0.7.14 — Seções e ícone de animação

Oculta os controles de seção quando há somente Default, preservando a reprodução. Novo ícone de esqueleto articulado para animações, mantendo o + nas montagens.
Validação: compilação e self-test passaram; reports/update-0714-test.json confirma Default oculto, reprodução preservada e outras seções visíveis. Ícones conferidos na captura da interface.

## 0.7.15 — Redefinição dos tipos de animação

Limpeza das classificações de animação do cache ativo, preservando os demais tipos, imagens e subcategorias. Animações com apenas seção Default são classificadas como Animação; identificação por nome usa Animação provável. Removida a borda e o fundo do + das montagens.
Validação: build e self-test passaram; reports/update-0715-test.json verifica os tipos e persistência. Cache ativo redefinido e reaberto para conferir ausência dos registros de animação; relatório animation-cache-reset-0715.json.

## 0.7.16 — Verificação retomável de tipos

Removido botão Jogo no cabeçalho; Blender abre o executável configurado. Opções permite verificar todos os assets e retomar. Resultados incrementais em reports/catalog-scan/results.jsonl e progresso em progress.json. O X interrompe; registros concluídos e falhas são pulados enquanto instalação, mapping e contêineres permanecerem iguais. Confirmações usam os decodificadores e viewers reais. Conversor reaproveita índices, reinicia a cada 100 itens e limpa previews; cache gravado antes dos checkpoints.
Validação: build e self-test passaram. Testes reais de modelo, animação, montagem, textura e áudio em reports/update-0716-test.json. Retomada verificada pulando tanto resultados concluídos quanto falhas; varredura completa iniciada após os testes, sem declarar todos os assets compatíveis.

## 0.7.17
- Corrigida interrup��o da varredura ao substituir progress.json bloqueado: tentativas ass�ncronas, registro do erro e continua��o preservando o hist�rico.
- Valida��o: varredura de tr�s assets com progress.json bloqueado concluiu com c�digo 0 e gravou os tr�s registros no hist�rico; self-test aprovado.

## 0.7.18
- Anima��es da varredura usam reports/catalog-scan/work/animation, fora da limpeza de previews. Limpeza compartilhada preserva pr�vias usadas por outra inst�ncia do projeto. Clique no status copia o texto completo.
- Valida��o: self-test aprovado; BlackDragon Idle convertido pelo scan-server e arquivo confirmado em reports/catalog-scan/work/animation/animation-0.json.

## 0.7.19
- Cat�logo, configura��es, retomada/progresso, diagn�sticos e etapas intermedi�rias compactados em DAT. Migra��o de formatos antigos verificada antes de remover os originais. Pr�vias nativas extra�das apenas enquanto necess�rias ao Viewer/fila e descartadas ao fechar.
- Falhas de abertura de cache limpam dados do asset e tentam nova leitura uma vez; subcategorias manuais preservadas e arquivo DAT corrompido guardado para recupera��o.

## 0.7.20
- Limpeza de distribuicoes antigas e ferramentas sem uso. Compilacao atualiza apenas o executavel principal na raiz.
- Preparacao do repositorio privado com fontes e licencas; cache e assets locais excluidos do Git.

## 0.7.21
- Release em executavel unico, com runtime, conversor, ferramentas, mapping e biblioteca inicial compactados internamente.
- Dados persistentes em LocalAppData; seed portavel validado por mapping/UTOC/UCAS e mesclado preservando tipos locais e subcategorias.
- Testes e resultados registrados apos validar o pacote isolado.

- Validacao: EXE isolado/self-test, portabilidade e mescla do cache, miniatura preservada, viewer Balgest, montagem BlackDragon, WAV Wwise e BLEND Balgest salvo/reaberto com 95 ossos, 240 quadros, 2 malhas e 1 imagem. SW_Fly_Aerial_Sound continua sem midia reproduzivel reconhecida.
