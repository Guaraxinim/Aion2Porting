# Aion2 Porting 0.7.0

Abra Aion2Porting.exe. Mantenha todas as pastas de suporte junto ao executável; para compartilhar ou mover, copie esta pasta inteira.

Requisitos: Windows x64 e Aion 2 instalado. O runtime já está incluído; não é necessário instalar Git ou SDK .NET. Blender instalado é necessário para gerar os arquivos .blend, mas não para a prévia 3D.

Em Opções, indique a instalação do jogo, o executável do Blender e a pasta de destino dos arquivos exportados. O jogo é acessado somente para leitura.

Na Biblioteca, use busca, exclusões, categorias e seleção de múltiplos tipos. Selecione um asset para abrir a prévia 3D ou adicione itens à fila de exportação. A fila e a biblioteca podem ser ocultadas para aumentar o espaço disponível.

Uma prévia 3D carregada com sucesso registra o asset como Modelo disponível. Essa informação fica no cache local. As prévias temporárias são apagadas ao fechar o programa; a lista de modelos disponíveis é preservada.

A distribuição começa sem catálogo, configurações, cache ou exports pessoais. O programa cria esses dados ao ser usado; mantenha a pasta em um local com permissão de escrita.

A compatibilidade depende do asset. A visualização usa geometria e, quando disponível, textura base; não reproduz integralmente shaders, transparência, animações ou física do jogo. A exportação não substitui a preparação posterior para FiveM. Consulte os relatórios gerados quando uma operação falhar.

Licenças de componentes incluídas na pasta licenses.