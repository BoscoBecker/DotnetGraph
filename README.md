<p align="right">
  <strong>🇧🇷 BR</strong> ·
  <a href="README.en.md" title="English">🇺🇸 EN</a> ·
  <a href="README.es.md" title="Español">🇪🇸 ES</a>
</p>

# Dotnet Graph

Extensão **VSIX** para **Visual Studio 2022 / 2026** que desenha um grafo de dependências entre projetos da solução aberta (estilo [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)), com suporte inicial a **C#**, **F#** e **VB.NET**.

Ao clicar em um nó (círculo), o painel lateral mostra:

- Referências de projeto (`ProjectReference`)
- Pacotes NuGet
- Tipos / módulos públicos encontrados no código-fonte

## Como usar a extensão

1. [Instale](#instalar) o `.vsix` e abra uma solução com projetos `.csproj`, `.fsproj` ou `.vbproj`.
2. No **Solution Explorer**, **clique com o botão direito na solução** (nó raiz) e escolha **Dotnet Graph**.

   ![Abrir Dotnet Graph pelo menu de contexto da solução](docs/screenshots/architecture.png)

   **Alternativa:** menu **View → Dotnet Graph**.
3. Clique em **Atualizar** se o grafo ainda não carregou.
4. Use as visões **Arquitetura**, **Call Graph**, **Namespaces** e **Types**; exporte com **PNG** ou **Mermaid** na barra superior.

### Capturas de tela

| Visão | Descrição |
|-------|-----------|
| [Arquitetura](docs/screenshots/architecture.png) | Dependências entre projetos da solução |
| [Detalhe do projeto](docs/screenshots/architecture-detail.png) | Painel lateral: referências, NuGet e tipos |
| [Call Graph](docs/screenshots/call-graph.png) | Métodos e chamadas no projeto C# selecionado |
| [Namespaces](docs/screenshots/namespaces.png) | Clusters por namespace |
| [Types](docs/screenshots/types.png) | Grafo de tipos do projeto |

<p align="center">
  <img src="docs/screenshots/architecture-detail.png" alt="Arquitetura com painel de detalhes" width="720"/>
</p>

<p align="center">
  <img src="docs/screenshots/call-graph.png" alt="Call Graph" width="720"/>
</p>

<p align="center">
  <img src="docs/screenshots/namespaces.png" alt="Mapa de namespaces" width="720"/>
</p>

<p align="center">
  <img src="docs/screenshots/types.png" alt="Grafo de tipos" width="720"/>
</p>

> **Imagens no repositório:** se `docs/screenshots/*.png` ainda não existir após o clone, execute `tools\copy-screenshots.cmd` na raiz (ou copie manualmente os PNGs para `docs\screenshots\`).

## NuGet (Package Source Mapping)

Se aparecer **NU1100** por causa do mapeamento global (ex.: feed Transmoreno), use o `nuget.config` na raiz deste repo: ele limpa fontes/mapeamentos herdados e permite restore de **nuget.org** apenas para este projeto. Pacotes ficam em `.nuget/packages` (local ao repo).

Depois de clonar ou alterar o config:

```cmd
dotnet restore DotnetGraph.sln
```

## Pré-requisitos

1. Visual Studio **2022 ou 2026** com a carga de trabalho **Desenvolvimento de extensão do Visual Studio**.
2. [.NET SDK](https://dotnet.microsoft.com/download) (para compilar `DotnetGraph.Core`).
3. **WebView2 Runtime** (já presente na maioria das instalações do Windows / Edge).

## Compilar o instalador (.vsix)

Na raiz do repositório:

```cmd
build.cmd
```

Ou manualmente:

```cmd
dotnet restore DotnetGraph.sln
dotnet build DotnetGraph.sln -c Release
```

O pacote de instalação fica em:

`src\DotnetGraph.Extension\bin\Release\net472\DotnetGraph.Extension.vsix`

Se o `.vsix` não aparecer após `build.cmd`, confira que o projeto Extension tem `VSSDKBuildToolsAutoSetup=true` (já configurado) e que a carga **Desenvolvimento de extensão do Visual Studio** está instalada.

## Instalar

1. Feche todas as instâncias do Visual Studio (exceto se for instalar na instância experimental via F5).
2. Dê duplo clique no `.vsix` **ou** use **Extensions → Manage Extensions → Install from file**.
3. Abra uma solução com projetos `.csproj`, `.fsproj` ou `.vbproj`.
4. Siga [Como usar a extensão](#como-usar-a-extensão) (botão direito na **solução** → **Dotnet Graph**).
5. Use **Arquitetura** (filtro **Pasta** no Solution Explorer), **Call Graph**, **Namespaces**, **Types** e **Quem usa isto?** (impact analysis no painel lateral, após selecionar um tipo C#).
6. Escolha o **idioma** **BR | EN | ES** abaixo do título da janela (interface WPF + textos do grafo).
7. Escolha o **tema** (Sistema / Claro / Escuro) na barra da janela.
8. Clique em **Atualizar** após mudar projetos ou referências.
9. Use **PNG** ou **Mermaid** na barra para exportar a visão atual (Arquitetura, Call Graph, Namespaces, Types ou Impact).

A análise MSBuild/Roslyn roda em **background** com overlay de loading para evitar travar o Visual Studio.

## Desempenho e UX (extensão)

- **AsyncPackage** com carregamento em segundo plano — sem `AutoLoad` na inicialização da IDE; o pacote só carrega ao abrir **Dotnet Graph** ou usar o comando.
- Comando **View → Dotnet Graph** oculto enquanto não houver solução aberta (`UICONTEXT_EmptySolution`).
- Leitura de `settings.json` e análise do grafo (MSBuild/Roslyn) fora da **UI thread**; WebView2 inicializa após o primeiro frame da janela.
- UI alinhada ao **Fluent** do Visual Studio (cores `EnvironmentColors`, tema **Sistema** / Claro / Escuro, **Alto Contraste** no canvas quando o VS está em HC).

## Idioma (BR | EN | ES)

Na janela **Dotnet Graph**, use os botões **BR**, **EN** ou **ES** (alinhados à esquerda, abaixo do nome da solução).

| Botão | Idioma |
|-------|--------|
| **BR** | Português (Brasil) — padrão |
| **EN** | English |
| **ES** | Español |

A escolha atualiza rótulos da barra (Atualizar, Tema, seções do painel lateral, status do canvas, etc.) e é **salva automaticamente** para a próxima vez que abrir o Visual Studio.

Arquivo de preferências (Windows):

`%LocalAppData%\DotnetGraph\settings.json`

Campo relevante: `uiLanguage` (`0` = BR, `1` = EN, `2` = ES). No mesmo arquivo ficam também tema, largura do painel lateral e posições salvas dos nós no grafo.

### Compatibilidade VS 2026

O manifesto usa `InstallationTarget Version="[17.0,)"`, alinhado ao [modelo de compatibilidade por API](https://learn.microsoft.com/visualstudio/extensibility/migration/extension-compatibility) do Visual Studio 2026.

## Estrutura

| Projeto | Função |
|---------|--------|
| `DotnetGraph.Core` | MSBuild + Roslyn: grafo da solução e composição dos projetos |
| `DotnetGraph.Extension` | VSIX, Tool Window WPF + WebView2, comando no menu View |

## Desenvolvimento (F5)

Abra `DotnetGraph.sln` no Visual Studio, defina **DotnetGraph.Extension** como projeto de inicialização e pressione **F5**. Isso abre a **instância experimental** (`/rootsuffix Exp`) com a extensão carregada.

## Limitações (v0.1)

- Análise de tipos **C#** e **VB** via Roslyn sem resolver todas as referências externas (foco em tipos declarados no projeto).
- **F#**: deteção de `type` e `module` por análise de sintaxe (sem FSharp.Compiler.Service nesta versão).
- Projetos fora da solução referenciados por caminho absoluto aparecem como nós extras quando encontrados via `ProjectReference`.

## Roadmap sugerido

- [x] Call Graph (métodos, acessibilidade, âncora do projeto) e Namespace Map
- [x] Visão Arquitetura (projetos)
- [x] Idioma **BR | EN | ES** (UI + grafo, persistido em `settings.json`)
- [ ] Impact Analysis / Who uses this? (símbolos na solução)
- [ ] Drill-down: grafo de tipos dentro do projeto selecionado
- [ ] Filtro por camada / pasta da solução
- [x] Export PNG / Mermaid
- [ ] Integração com **Live Graph** ao salvar `.csproj`

## Licença

Este projeto está licenciado sob a **[MIT License](LICENSE)**.

Você pode usar, copiar, modificar, mesclar, publicar, distribuir, sublicenciar e vender cópias do software, desde que o aviso de copyright e o texto da licença sejam incluídos em todas as cópias ou partes relevantes.

Copyright (c) 2026 DotnetGraph contributors
