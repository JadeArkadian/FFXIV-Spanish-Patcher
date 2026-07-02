# Notas para agentes IA

Usa siempre `/caveman full` (coincide con el hook global de SessionStart). Habla en español;
los `.md` de documentación pueden ir en español.

## Qué es esto

**FFXIVSpanish Patcher**: aplicación de escritorio (un solo ejecutable, sin instalación) que:

1. Detecta la ruta de una instalación local de Final Fantasy XIV.
2. Extrae **solo** los `.exd` que se traducen (no el juego entero) vía Lumina.
3. Los parchea con traducciones al castellano **embebidas en el propio ejecutable**.
4. Genera un `.pmp` instalable con Penumbra.

La GUI es **.NET 10 + Avalonia UI (MVVM)**. La aplicación es un *shell* delgado: la lógica real
de extracción / parcheo binario de EXD / SeString / empaquetado se **reutiliza** del repo upstream
**FFXIV-Spanish** mediante código vendorizado (`vendor/`).

## Estructura

- `src/FFXIVSpanishPatcher.App` — GUI Avalonia (MVVM, entry point, tema oscuro). *(F3)*
- `src/FFXIVSpanishPatcher.Pipeline` — orquestación `extract→patch→package` con eventos
  `IProgress<>`; GUI y tests la consumen in-process. *(F1)*
- `vendor/XivSpanish.Core` — modelos de traducción, `ManifestLoader`, `DomainMap`.
- `vendor/XivSpanish.GameData` — Lumina, formato binario EXD, `ExdPatcher`, SeString, `GameLocator`.
- `data/translations.dat` — blob Brotli-JSONL versionado (~5 MB) que la App embebe. El corpus crudo
  `data/translations/jsonl/` (~60 MB) NO se versiona; se sincroniza local solo para regenerar el blob.
- `tests/FFXIVSpanishPatcher.Tests` — unit + integración con EXD **sintético**.
- `tools/XivSpanish.BlobBuilder` — tool C# (subcomandos `sync`/`build`) que regenera el blob *(F2)*.
- `build/` — `macos/Info.plist` (metadatos del bundle `.app`).
- `docs/DESIGN.md` — diseño completo y plan por fases.

## `vendor/`: código propio (origen sembrado desde upstream)

`vendor/` se **sembró** una vez copiando `XivSpanish.Core` / `XivSpanish.GameData` (+ primitivas del
Packager) desde upstream FFXIV-Spanish, pero es **código que mantenemos en este repo y editamos a
mano**. La regla read-only se levantó (2026-06-24) y ya ha divergido de upstream (modelo recortado,
`gold` añadido, código muerto eliminado).

No hay script de re-sync: se borró `sync-vendor.ps1` (2026-06-24) porque reimportaba upstream
**sobreescribiendo** `vendor/`, lo que ahora destruiría esa divergencia. Si algún día quieres una
mejora concreta de upstream (p.ej. lógica de Lumina/EXD), pórtala a mano. La procedencia del sembrado
original vive en `vendor/VENDORED.md` (histórica).

## Decisiones cerradas (NO re-litigar)

1. GUI = **.NET 10 + Avalonia UI** (cross-platform; WPF descartado por ser Windows-only).
2. Modelo de datos = traducciones **embebidas** + extracción **lean** (solo sheets traducidos,
   en vivo desde el juego del usuario). Legal-clean: no se redistribuyen bytes de SquareEnix.
3. Reuso = repo standalone que **sembró** `vendor/` copiando el core de FFXIV-Spanish (no submodule).
   `vendor/` es código propio editable; pueden divergir de upstream (regla read-only levantada 2026-06-24).
4. Bundling traducciones = **solo embebido** en el `.exe`. Actualizar traducciones = re-publicar
   (regenerar el blob con `XivSpanish.BlobBuilder` + `dotnet publish`). Sin fichero lateral. El blob solo contiene
   filas empaquetables (`status ∈ {approved, gold}`); el resto no se aplica y se excluye.
5. Test de integración = **EXD sintético** generado en código (no se versionan `.exd` reales).
6. Categorías del panel avanzado = **híbrido**: metadatos curados (nombre/orden/tooltip) en código,
   habilitación y contadores reales según el manifest embebido.

## Comandos

```powershell
dotnet build                              # compila la solución
dotnet test                               # unit + integración
# Regenerar el blob (corpus crudo desde upstream + compactar). Tool C#, cross-platform:
dotnet run --project tools/XivSpanish.BlobBuilder -- sync --build
dotnet run --project tools/XivSpanish.BlobBuilder -- build   # solo recompacta jsonl -> data/translations.dat
# Publicar single-file self-contained:
dotnet publish src/FFXIVSpanishPatcher.App -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Sin trimming ni NativeAOT: Lumina usa reflexión.

## Tests

- xUnit (igual que upstream).
- Integración: construir un EXH+EXD mínimo **en código**, parchearlo con `ExdPatcher` y hacer assert
  sobre los bytes resultantes (incluido el caso de offset a espacio vacío). Luego empaquetar y validar
  la estructura del `.pmp`. **Nunca** commitear un `.exd` real ni depender del juego instalado en CI.

## No hacer

- No modificar archivos originales de FFXIV ni reinyectar DATs.
- No redistribuir bytes de SquareEnix (`.exd`/`.exh`/`.pmp`/dumps): nunca versionados.
- No añadir traducción automática: el corpus llega curado desde upstream.

## Estado

- **F0** hecho: scaffold + sembrado de `vendor/` Core/GameData (vía el extinto `sync-vendor.ps1`).
  Compila limpio.
- **F0.5** hecho: `CLAUDE.md` (→`@AGENTS.md`), este `AGENTS.md`, `docs/DESIGN.md`.
- **F1** hecho: `vendor/XivSpanish.Packaging` (primitivas) + `src/FFXIVSpanishPatcher.Pipeline`
  (orquestación `PatchPipeline` con eventos de progreso, ported del `Program.cs` upstream) + tests
  (14, incl. integración con EXD sintético: content + write-at-offset + broadcast + `.pmp`).
- **F2** hecho: `tools/XivSpanish.BlobBuilder` (sync+build, C#) + `EmbeddedTranslationSource`.
  Blob `data/translations.dat` versionado (**~5.7 MB**, ~306 k filas empaquetables ≈ `approved` +
  `gold` − filas con target vacío); corpus crudo git-ignored. El tool `XivSpanish.BlobBuilder`
  aplica tres reducciones, todas sin pérdida para el patcher (la ficha completa vive en el corpus
  upstream): (a) **filtra filas** al criterio exacto de `Packageable` (`status ∈ {approved, gold}` +
  target no vacío + sourceKey con sheet+rowId); (b) **proyecta campos**: como `TranslationEntry` está
  recortado a `source/target/status/sourceKey`, reserializar el modelo **es** la proyección (no hay
  lista de campos duplicada); se tiran los metadatos de procedencia que el runtime nunca lee (`hash`,
  `id`, `category`, `translator`, `reviewer`, `notes`, `context`, `subRowId`); y (c) comprime con
  **Brotli** (`CompressionLevel.SmallestSize`) en vez de gzip; .NET lo descomprime con `BrotliStream`
  nativo, **sin dependencia ni en build ni en runtime**. Recorrido total: 20.36 MB (gzip, todo) →
  7.1 MB (gzip, proyectado) → ~5.2 MB (brotli, proyectado). El tool es **C#** (`dotnet run`,
  cross-platform; sin Python ni PowerShell); `dotnet run --project tools/XivSpanish.BlobBuilder --
  sync --build` sincroniza y regenera. Resincronizado 2026-06-24 desde upstream: nuevo dominio `items` (`Item`,
  ~161 639 approved — antes 0) y ~20 sheets nuevos (Aetheryte, Orchestrion, EventItemHelp,
  JournalGenre, Weather…). El pipeline los extrae/parchea (es data-driven vía Lumina, sin allowlist) y
  aplica `{approved, gold}` (`PackageableStatus.Default`; antes solo `approved`). Taxonomía del panel
  ampliada a **10 dominios**: categorías propias `logros`, `registro`, `eventos`, `coleccionables`,
  `clases` (ClassJob/ClassJobCategory, casilla aparte para dejar clases/jobs en inglés) y el resto
  plegado en los 5 buckets existentes, de modo que cada sheet enviado cae en una categoría
  visible/toggleable (sin bucket invisible por-sheet). Cubre las **89 sheets** del blob; al traer más
  se mapean en `TranslationCategories` (último lote: `Fate`/`Leve`→misiones + 9 cola pre-existentes).
  Mapeo en `Pipeline/TranslationCategories.cs`
  (supera a `DomainMap.Sprint2Default` sin tocar `vendor/`); metadatos en `App/Services/CategoryCatalog.cs`.
- **F3** hecho: `src/FFXIVSpanishPatcher.App` (Avalonia MVVM, tema oscuro, layout del mockup:
  ruta+examinar, generar/abrir salida, consola en streaming, categorías EXD híbridas, toggle
  integridad, status bar). Embebe `data/translations.dat`. Compila 0 warnings (vuln DBus fijada).
- **F4** hecho: `GamePathDetector` en Pipeline (XIVLauncher + registry uninstall + Steam
  `libraryfolders.vdf` + rutas comunes); la GUI lo usa. Integración SO (reveal/clipboard) ya en F3.
- **F5** hecho: publish single-file self-contained win-x64 (55.8 MB, comprimido) + smoke headless
  de la GUI (`FFXIVSpanishPatcher.App.Tests`, valida que MainWindow instancia/bind/tema). Smoke
  contra el juego real es manual (requiere FFXIV instalado + display). Total: 21 tests.
- **F7** hecho: `.github/workflows/ci.yml` (push/PR → build+test) y `release.yml` (tag `v*` →
  publica los 3 RID self-contained single-file —win-x64, linux-x64, osx-arm64 (solo Apple
  Silicon)— y los adjunta a un GitHub Release). Se activan al
  crear el repo en GitHub y empujar (no hay remote todavía).
- **F6** diferida: validación Linux/Mac contra juego real (requiere esas plataformas + el juego).
- **Plan F0–F7 completo.** Pendiente del usuario: crear el repo GitHub + `git push`; verificación
  manual de la GUI contra una instalación real de FFXIV.
