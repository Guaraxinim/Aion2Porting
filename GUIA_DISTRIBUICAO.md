# Aion2 Porting 0.7.21

Baixe e abra Aion2Porting.exe. Este é o único arquivo necessário para distribuir o aplicativo. Windows x64; .NET e componentes do conversor estão incluídos. Blender continua sendo uma instalação externa, necessária para gerar .blend.

Na primeira abertura, os componentes internos são preparados em %LOCALAPPDATA%\Aion2Porting\runtime. Os dados persistentes ficam em %LOCALAPPDATA%\Aion2Porting\cache, em arquivos DAT compactados. Trocar o EXE não apaga esses dados. O cache inicial inclui catálogo, tipos confirmados, miniaturas e perfis de compatibilidade de esqueletos. Configurações pessoais, modelos, texturas e áudios do jogo não são distribuídos.

O cache inicial é aplicado por asset somente quando mapping, hash do índice UTOC e tamanho do UCAS correspondem. Tipos locais e subcategorias editadas pelo usuário são preservados. Uma alteração nos arquivos do jogo pode exigir Atualizar biblioteca; o catálogo incluído representa a instalação usada para preparar esta versão, não comprova compatibilidade com outras versões do jogo.

Em Opções, configure instalação do Aion 2, Blender, idioma e pasta de saída. O jogo é acessado apenas para leitura. A saída padrão fica em %LOCALAPPDATA%\Aion2Porting\Exportados; ela pode ser alterada. As prévias nativas são temporárias e descartadas ao fechar. Elas precisam existir como arquivos enquanto conversores, Blender e viewers as utilizam; não se promete ausência total de arquivos temporários durante o uso.

Tipos e miniaturas não significam que todos os assets sejam compatíveis. Visualização e exportação dependem de cada asset. Shaders são ativados manualmente e não reproduzem todos os efeitos do jogo. A preparação para FiveM é uma etapa posterior.

Licenças e este guia ficam junto aos componentes internos em runtime. O release também fornece SHA256 para conferir o download.
