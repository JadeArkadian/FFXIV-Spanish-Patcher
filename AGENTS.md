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
- `vendor/XivSpanish.Core` — modelos de traducción, hashing, `ManifestLoader`, `DomainMap`.
- `vendor/XivSpanish.GameData` — Lumina, formato binario EXD, `ExdPatcher`, SeString, `GameLocator`.
- `data/translations.dat` — blob gzip-JSONL versionado (~9 MB) que la App embebe. El corpus crudo
  `data/translations/jsonl/` (~60 MB) NO se versiona; se sincroniza local solo para regenerar el blob.
- `tests/FFXIVSpanishPatcher.Tests` — unit + integración con EXD **sintético**.
- `build/` — `sync-vendor.ps1`, `sync-translations.ps1` *(F2)*, `build-translations.py` *(F2)*.
- `docs/DESIGN.md` — diseño completo y plan por fases.

## `vendor/`: código propio (origen sembrado desde upstream)

`vendor/` se **sembró** copiando `XivSpanish.Core` / `XivSpanish.GameData` (+ primitivas del Packager)
desde upstream FFXIV-Spanish, pero es **código que mantenemos en este repo y se puede editar a mano**.
La antigua regla read-only se levantó (2026-06-24): el patcher y upstream pueden divergir.

⚠️ `build/sync-vendor.ps1` **reimporta upstream borrando y recopiando `vendor/`**, así que
**sobreescribe cualquier edición local**. Por eso ahora exige `-Force` y avisa: úsalo solo cuando
quieras adoptar el estado de upstream (perdiendo tus cambios locales) o tras haber portado tus
ediciones a upstream. La procedencia del sembrado vive en `vendor/VENDORED.md` (histórica).

## Decisiones cerradas (NO re-litigar)

1. GUI = **.NET 10 + Avalonia UI** (cross-platform; WPF descartado por ser Windows-only).
2. Modelo de datos = traducciones **embebidas** + extracción **lean** (solo sheets traducidos,
   en vivo desde el juego del usuario). Legal-clean: no se redistribuyen bytes de SquareEnix.
3. Reuso = repo standalone que **sembró** `vendor/` copiando el core de FFXIV-Spanish (no submodule).
   `vendor/` es código propio editable; pueden divergir de upstream (regla read-only levantada 2026-06-24).
4. Bundling traducciones = **solo embebido** en el `.exe`. Actualizar traducciones = re-publicar
   (re-correr `build-translations.py` + `dotnet publish`). Sin fichero lateral. El blob solo contiene
   filas empaquetables (`status ∈ {approved, gold}`); el resto no se aplica y se excluye.
5. Test de integración = **EXD sintético** generado en código (no se versionan `.exd` reales).
6. Categorías del panel avanzado = **híbrido**: metadatos curados (nombre/orden/tooltip) en código,
   habilitación y contadores reales según el manifest embebido.

## Comandos

```powershell
dotnet build                              # compila la solución
dotnet test                               # unit + integración
build/sync-vendor.ps1                     # re-sincroniza vendor/ desde upstream
build/sync-translations.ps1 -Build        # trae el corpus crudo desde upstream y regenera el blob (llama a python)
python build/build-translations.py        # compacta data/translations/jsonl (approved+gold) -> data/translations.dat
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
- No correr `build/sync-vendor.ps1` sin querer adoptar upstream: **sobreescribe** `vendor/` (exige `-Force`).
- No añadir traducción automática: el corpus llega curado desde upstream.

## Estado

- **F0** hecho: scaffold + vendor de Core/GameData + `sync-vendor.ps1`. Compila limpio.
- **F0.5** hecho: `CLAUDE.md` (→`@AGENTS.md`), este `AGENTS.md`, `docs/DESIGN.md`.
- **F1** hecho: `vendor/XivSpanish.Packaging` (primitivas) + `src/FFXIVSpanishPatcher.Pipeline`
  (orquestación `PatchPipeline` con eventos de progreso, ported del `Program.cs` upstream) + tests
  (14, incl. integración con EXD sintético: content + write-at-offset + broadcast + `.pmp`).
- **F2** hecho: `sync-translations.ps1` + `build-translations.py` + `EmbeddedTranslationSource`.
  Blob `data/translations.dat` versionado (**7.12 MB**, 296 344 filas empaquetables ≈ 295 648
  `approved` + 697 `gold` − filas con target vacío); corpus crudo git-ignored. `build-translations.py`
  aplica dos reducciones, ambas sin pérdida para el patcher (la ficha completa vive en el corpus
  upstream): (a) **filtra filas** al criterio exacto de `Packageable` (`status ∈ {approved, gold}` +
  target no vacío + sourceKey con sheet+rowId); (b) **proyecta campos**: solo emite `source`,
  `target`, `status` y `sourceKey{sheet,rowId,field,exdPath}`, tirando metadatos de procedencia que el
  runtime nunca lee (`hash` —hex aleatorio casi incompresible—, `id`, `category`, `translator`,
  `reviewer`, `notes`, `context`, `subRowId`). La proyección recorta el gzip ~65 % (antes 20.36 MB).
  El creador del blob es **Python** (no PowerShell); `sync-translations.ps1 -Build` lo invoca. Resincronizado 2026-06-24 desde upstream: nuevo dominio `items` (`Item`,
  ~161 639 approved — antes 0) y ~20 sheets nuevos (Aetheryte, Orchestrion, EventItemHelp,
  JournalGenre, Weather…). El pipeline los extrae/parchea (es data-driven vía Lumina, sin allowlist) y
  aplica `{approved, gold}` (`PackageableStatus.Default`; antes solo `approved`). Taxonomía del panel
  ampliada a **9 dominios**: categorías propias `logros`, `registro`, `eventos`, `coleccionables` y el
  resto plegado en los 5 buckets existentes, de modo que cada sheet enviado cae en una categoría
  visible/toggleable (sin bucket invisible por-sheet). Mapeo en `Pipeline/TranslationCategories.cs`
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
  publica los 4 RID self-contained single-file y los adjunta a un GitHub Release). Se activan al
  crear el repo en GitHub y empujar (no hay remote todavía).
- **F6** diferida: validación Linux/Mac contra juego real (requiere esas plataformas + el juego).
- **Plan F0–F7 completo.** Pendiente del usuario: crear el repo GitHub + `git push`; verificación
  manual de la GUI contra una instalación real de FFXIV.
