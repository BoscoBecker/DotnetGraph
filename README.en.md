<p align="right">
  <a href="README.md" title="Português (Brasil)">🇧🇷 BR</a> ·
  <strong>🇺🇸 EN</strong> ·
  <a href="README.es.md" title="Español">🇪🇸 ES</a>
</p>

# Dotnet Graph

**VSIX** extension for **Visual Studio 2022 / 2026** that draws a dependency graph between projects in the open solution ([.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) style), with initial support for **C#**, **F#**, and **VB.NET**.

When you click a node (circle), the side panel shows:

- Project references (`ProjectReference`)
- NuGet packages
- Public types / modules found in source code

## How to use the extension

1. [Install](#install) the `.vsix` and open a solution with `.csproj`, `.fsproj`, or `.vbproj` projects.
2. In **Solution Explorer**, **right-click the solution** (root node) and choose **Dotnet Graph**.

   ![Open Dotnet Graph from the solution context menu](docs/screenshots/architecture.png)

   **Alternative:** **View → Dotnet Graph**.
3. Click **Refresh** if the graph has not loaded yet.
4. Switch between **Architecture**, **Call Graph**, **Namespaces**, and **Types**; export the current view with **PNG** or **Mermaid** in the toolbar.

### Screenshots

| View | Description |
|------|-------------|
| [Architecture](docs/screenshots/architecture.png) | Project dependencies in the solution |
| [Project detail](docs/screenshots/architecture-detail.png) | Side panel: references, NuGet, types |
| [Call Graph](docs/screenshots/call-graph.png) | Methods and calls in the selected C# project |
| [Namespaces](docs/screenshots/namespaces.png) | Namespace clusters |
| [Types](docs/screenshots/types.png) | Type graph for the project |

<img width="1582" height="917" alt="image" src="https://github.com/user-attachments/assets/10ca342e-9867-4750-b58a-d41ae93cb617" />
<img width="1568" height="910" alt="image" src="https://github.com/user-attachments/assets/fc604781-209f-4c28-8735-d3fa72e63163" />
<img width="1579" height="912" alt="image" src="https://github.com/user-attachments/assets/46f4078f-d19a-47d9-ba00-cf245cb969e3" />
<img width="1584" height="912" alt="image" src="https://github.com/user-attachments/assets/274e4bb1-6542-418f-b244-a11522248efa" />
<img width="1584" height="910" alt="image" src="https://github.com/user-attachments/assets/04e40a1c-4185-495e-80a0-9cb11da78e40" />

> **Images in the repo:** if `docs/screenshots/*.png` is missing after clone, run `tools\copy-screenshots.cmd` from the repo root (or copy the PNG files into `docs\screenshots\` manually).

## NuGet (Package Source Mapping)

If you see **NU1100** due to global mapping (e.g. a corporate feed), use the root `nuget.config`: it clears inherited sources/mappings and allows **nuget.org** restore for this project only. Packages go under `.nuget/packages` (repo-local).

After cloning or changing the config:

```cmd
dotnet restore DotnetGraph.sln
```

## Prerequisites

1. Visual Studio **2022 or 2026** with the **Visual Studio extension development** workload.
2. [.NET SDK](https://dotnet.microsoft.com/download) (to build `DotnetGraph.Core`).
3. **WebView2 Runtime** (common on Windows / Edge installs).

## Build the installer (.vsix)

From the repository root:

```cmd
build.cmd
```

Or manually:

```cmd
dotnet restore DotnetGraph.sln
dotnet build DotnetGraph.sln -c Release
```

The installer package is at:

`src\DotnetGraph.Extension\bin\Release\net472\DotnetGraph.Extension.vsix`

If the `.vsix` is missing after `build.cmd`, ensure the Extension project has `VSSDKBuildToolsAutoSetup=true` (already set) and that the **Visual Studio extension development** workload is installed.

## Install

1. Close all Visual Studio instances (unless installing into the experimental instance via F5).
2. Double-click the `.vsix` **or** use **Extensions → Manage Extensions → Install from file**.
3. Open a solution with `.csproj`, `.fsproj`, or `.vbproj` projects.
4. Follow [How to use the extension](#how-to-use-the-extension) (**right-click the solution** → **Dotnet Graph**).
5. Use **Architecture** ( **Folder** filter in Solution Explorer), **Call Graph**, **Namespaces**, **Types**, and **Who uses this?** (impact analysis after selecting a C# type).
6. Choose **BR | EN | ES** below the window title (WPF UI + graph strings).
7. Choose **theme** (System / Light / Dark) in the toolbar.
8. Click **Refresh** after changing projects or references.
9. Use **PNG** or **Mermaid** to export the current view.

MSBuild/Roslyn analysis runs in the **background** with a loading overlay so Visual Studio stays responsive.

## Performance and UX (extension)

- **AsyncPackage** with background loading — no startup `AutoLoad`; the package loads when you open **Dotnet Graph** or run the command.
- **View → Dotnet Graph** is hidden until a solution is open (`UICONTEXT_EmptySolution`).
- `settings.json` read and graph analysis (MSBuild/Roslyn) off the **UI thread**; WebView2 starts after the tool window’s first frame.
- UI aligned with Visual Studio **Fluent** (`EnvironmentColors`, **System** / Light / Dark, **High Contrast** on the canvas when VS uses HC).

## Language (BR | EN | ES)

In the **Dotnet Graph** window, use **BR**, **EN**, or **ES** (left side, below the solution name).

| Button | Language |
|--------|----------|
| **BR** | Portuguese (Brazil) — default |
| **EN** | English |
| **ES** | Spanish |

The choice updates toolbar labels (Refresh, Theme, side panel sections, canvas status, etc.) and is **saved automatically** for the next Visual Studio session.

Preferences file (Windows):

`%LocalAppData%\DotnetGraph\settings.json`

Relevant field: `uiLanguage` (`0` = BR, `1` = EN, `2` = ES). The same file stores theme, side panel width, and saved node positions.

### VS 2026 compatibility

The manifest uses `InstallationTarget Version="[17.0,)"`, aligned with the Visual Studio 2026 [API compatibility model](https://learn.microsoft.com/visualstudio/extensibility/migration/extension-compatibility).

## Structure

| Project | Role |
|---------|------|
| `DotnetGraph.Core` | MSBuild + Roslyn: solution graph and project composition |
| `DotnetGraph.Extension` | VSIX, WPF Tool Window + WebView2, View menu command |

## Development (F5)

Open `DotnetGraph.sln` in Visual Studio, set **DotnetGraph.Extension** as the startup project, and press **F5**. That opens the **experimental instance** (`/rootsuffix Exp`) with the extension loaded.

## Limitations (v0.1)

- **C#** and **VB** type analysis via Roslyn without resolving all external references (focus on types declared in the project).
- **F#**: `type` and `module` detection via syntax analysis (no FSharp.Compiler.Service in this version).
- Projects outside the solution referenced by absolute path appear as extra nodes when found via `ProjectReference`.

## Suggested roadmap

- [x] Call Graph (methods, accessibility, project anchor) and Namespace Map
- [x] Architecture view (projects)
- [x] **BR | EN | ES** language (UI + graph, persisted in `settings.json`)
- [ ] Impact Analysis / Who uses this? (symbols across the solution)
- [ ] Drill-down: type graph inside the selected project
- [ ] Filter by layer / solution folder
- [x] Export PNG / Mermaid
- [ ] **Live Graph** integration when saving `.csproj`

## License

This project is licensed under the **[MIT License](LICENSE)**.

You may use, copy, modify, merge, publish, distribute, sublicense, and sell copies of the software, provided the copyright notice and license text are included in all copies or substantial portions.

Copyright (c) 2026 DotnetGraph contributors
