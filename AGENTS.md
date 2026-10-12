# Instruções do projeto

- A instalação do Aion 2 é apenas para leitura. Grave alterações e resultados na pasta deste projeto.
- A cada solicitação nova de atualização implementada, incremente uma vez o patch da versão em `src/Aion2Importer/Aion2Importer.csproj` e registre a alteração em `VERSIONING.md`. Perguntas e recompilações da mesma solicitação não incrementam. Patch máximo 99; após x.y.99 use x.(y+1).0.
- A versão atual é 0.7.20, na interface WPF. A interface principal é WPF em src/Aion2Importer. A migração React/Tauri foi cancelada e removida do projeto.
- Preserve os identificadores internos de filtros e os nomes/caminhos dos assets ao traduzir a interface. As traduções ficam em `src/Aion2Importer/Translations.json`.
- Atualize e use `Aion2Porting.exe`, sem depender de arquivos CMD. O usuário autorizou fechar o executável deste projeto para atualizá-lo. Prefira fechamento normal; não encerre instâncias em outras pastas.
- Confirme os testes antes de declarar compatibilidade ou exportação bem-sucedida.
