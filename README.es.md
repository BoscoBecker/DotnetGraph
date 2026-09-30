<p align="right">
  <a href="README.md" title="Português (Brasil)">🇧🇷 BR</a> ·
  <a href="README.en.md" title="English">🇺🇸 EN</a> ·
  <strong>🇪🇸 ES</strong>
</p>

# Dotnet Graph

Extensión **VSIX** para **Visual Studio 2022 / 2026** que dibuja un grafo de dependencias entre proyectos de la solución abierta (estilo [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)), con soporte inicial para **C#**, **F#** y **VB.NET**.

Al hacer clic en un nodo (círculo), el panel lateral muestra:

- Referencias de proyecto (`ProjectReference`)
- Paquetes NuGet
- Tipos / módulos públicos encontrados en el código fuente

## Cómo usar la extensión

1. [Instale](#instalar) el `.vsix` y abra una solución con proyectos `.csproj`, `.fsproj` o `.vbproj`.
2. En el **Explorador de soluciones**, **clic derecho en la solución** (nodo raíz) y elija **Dotnet Graph**.

   ![Abrir Dotnet Graph desde el menú contextual de la solución](docs/screenshots/architecture.png)

   **Alternativa:** menú **View → Dotnet Graph**.
3. Pulse **Actualizar** si el grafo aún no cargó.
4. Use las vistas **Arquitectura**, **Call Graph**, **Namespaces** y **Types**; exporte con **PNG** o **Mermaid** en la barra superior.

### Capturas de pantalla

| Vista | Descripción |
|-------|-------------|
| [Arquitectura](docs/screenshots/architecture.png) | Dependencias entre proyectos de la solución |
| [Detalle del proyecto](docs/screenshots/architecture-detail.png) | Panel lateral: referencias, NuGet y tipos |
| [Call Graph](docs/screenshots/call-graph.png) | Métodos y llamadas en el proyecto C# seleccionado |
| [Namespaces](docs/screenshots/namespaces.png) | Clusters por namespace |
| [Types](docs/screenshots/types.png) | Grafo de tipos del proyecto |

<p align="center">
  <img src="docs/screenshots/architecture-detail.png" alt="Arquitectura con panel de detalles" width="720"/>
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

> **Imágenes en el repositorio:** si faltan `docs/screenshots/*.png` tras clonar, ejecute `tools\copy-screenshots.cmd` en la raíz (o copie los PNG manualmente a `docs\screenshots\`).

## NuGet (Package Source Mapping)

Si aparece **NU1100** por el mapeo global (p. ej. un feed corporativo), use el `nuget.config` en la raíz del repo: limpia fuentes/mapeos heredados y permite restore solo desde **nuget.org** para este proyecto. Los paquetes quedan en `.nuget/packages` (local al repo).

Después de clonar o cambiar el config:

```cmd
dotnet restore DotnetGraph.sln
```

## Requisitos previos

1. Visual Studio **2022 o 2026** con la carga de trabajo **Desarrollo de extensiones de Visual Studio**.
2. [.NET SDK](https://dotnet.microsoft.com/download) (para compilar `DotnetGraph.Core`).
3. **WebView2 Runtime** (habitual en instalaciones de Windows / Edge).

## Compilar el instalador (.vsix)

En la raíz del repositorio:

```cmd
build.cmd
```

O manualmente:

```cmd
dotnet restore DotnetGraph.sln
dotnet build DotnetGraph.sln -c Release
```

El paquete de instalación está en:

`src\DotnetGraph.Extension\bin\Release\net472\DotnetGraph.Extension.vsix`

Si el `.vsix` no aparece tras `build.cmd`, compruebe que el proyecto Extension tiene `VSSDKBuildToolsAutoSetup=true` (ya configurado) y que la carga **Desarrollo de extensiones de Visual Studio** está instalada.

## Instalar

1. Cierre todas las instancias de Visual Studio (salvo si instala en la instancia experimental con F5).
2. Doble clic en el `.vsix` **o** use **Extensions → Manage Extensions → Install from file**.
3. Abra una solución con proyectos `.csproj`, `.fsproj` o `.vbproj`.
4. Siga [Cómo usar la extensión](#cómo-usar-la-extensión) (**clic derecho en la solución** → **Dotnet Graph**).
5. Use **Arquitectura** (filtro **Carpeta** en el Explorador de soluciones), **Call Graph**, **Namespaces**, **Types** y **¿Quién usa esto?** (impact analysis tras seleccionar un tipo C#).
6. Elija **BR | EN | ES** debajo del título de la ventana (interfaz WPF + textos del grafo).
7. Elija el **tema** (Sistema / Claro / Oscuro) en la barra de la ventana.
8. Pulse **Actualizar** tras cambiar proyectos o referencias.
9. Use **PNG** o **Mermaid** para exportar la vista actual.

El análisis MSBuild/Roslyn se ejecuta en **segundo plano** con overlay de carga para no bloquear Visual Studio.

## Rendimiento y UX (extensión)

- **AsyncPackage** con carga en segundo plano — sin `AutoLoad` al iniciar la IDE; el paquete solo carga al abrir **Dotnet Graph** o usar el comando.
- El comando **View → Dotnet Graph** está oculto mientras no haya solución abierta (`UICONTEXT_EmptySolution`).
- Lectura de `settings.json` y análisis del grafo (MSBuild/Roslyn) fuera del **hilo de UI**; WebView2 inicia tras el primer frame de la ventana.
- UI alineada con **Fluent** de Visual Studio (`EnvironmentColors`, tema **Sistema** / Claro / Oscuro, **Alto contraste** en el canvas cuando VS usa HC).

## Idioma (BR | EN | ES)

En la ventana **Dotnet Graph**, use los botones **BR**, **EN** o **ES** (a la izquierda, debajo del nombre de la solución).

| Botón | Idioma |
|-------|--------|
| **BR** | Portugués (Brasil) — predeterminado |
| **EN** | English |
| **ES** | Español |

La elección actualiza etiquetas de la barra (Actualizar, Tema, secciones del panel lateral, estado del canvas, etc.) y se **guarda automáticamente** para la próxima sesión de Visual Studio.

Archivo de preferencias (Windows):

`%LocalAppData%\DotnetGraph\settings.json`

Campo relevante: `uiLanguage` (`0` = BR, `1` = EN, `2` = ES). En el mismo archivo también están tema, ancho del panel lateral y posiciones guardadas de los nodos.

### Compatibilidad VS 2026

El manifiesto usa `InstallationTarget Version="[17.0,)"`, alineado con el [modelo de compatibilidad por API](https://learn.microsoft.com/visualstudio/extensibility/migration/extension-compatibility) de Visual Studio 2026.

## Estructura

| Proyecto | Función |
|----------|---------|
| `DotnetGraph.Core` | MSBuild + Roslyn: grafo de la solución y composición de proyectos |
| `DotnetGraph.Extension` | VSIX, Tool Window WPF + WebView2, comando en el menú View |

## Desarrollo (F5)

Abra `DotnetGraph.sln` en Visual Studio, establezca **DotnetGraph.Extension** como proyecto de inicio y pulse **F5**. Eso abre la **instancia experimental** (`/rootsuffix Exp`) con la extensión cargada.

## Limitaciones (v0.1)

- Análisis de tipos **C#** y **VB** vía Roslyn sin resolver todas las referencias externas (enfoque en tipos declarados en el proyecto).
- **F#**: detección de `type` y `module` por análisis de sintaxis (sin FSharp.Compiler.Service en esta versión).
- Proyectos fuera de la solución referenciados por ruta absoluta aparecen como nodos extra cuando se encuentran vía `ProjectReference`.

## Roadmap sugerido

- [x] Call Graph (métodos, accesibilidad, ancla del proyecto) y Namespace Map
- [x] Vista Arquitectura (proyectos)
- [x] Idioma **BR | EN | ES** (UI + grafo, persistido en `settings.json`)
- [ ] Impact Analysis / Who uses this? (símbolos en la solución)
- [ ] Drill-down: grafo de tipos dentro del proyecto seleccionado
- [ ] Filtro por capa / carpeta de la solución
- [x] Exportar PNG / Mermaid
- [ ] Integración **Live Graph** al guardar `.csproj`

## Licencia

Este proyecto está licenciado bajo la **[MIT License](LICENSE)**.

Puede usar, copiar, modificar, fusionar, publicar, distribuir, sublicenciar y vender copias del software, siempre que el aviso de copyright y el texto de la licencia se incluyan en todas las copias o partes relevantes.

Copyright (c) 2026 DotnetGraph contributors
