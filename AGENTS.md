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
- `data/translations/` — manifest aprobado en JSONL (fuente del blob embebido).
- `tests/FFXIVSpanishPatcher.Tests` — unit + integración con EXD **sintético**.
- `build/` — `sync-vendor.ps1`, `sync-translations.ps1` *(F2)*, `build-translations.ps1` *(F2)*.
- `docs/DESIGN.md` — diseño completo y plan por fases.

## Regla de frontera del código vendorizado

`vendor/` es un espejo de solo-lectura de upstream FFXIV-Spanish. **No editar a mano.** Los cambios
fluyen en un solo sentido (upstream → vendor) ejecutando `build/sync-vendor.ps1`; luego se revisa el
diff, se recompila y se commitea. La procedencia (commit upstream + fecha) vive en `vendor/VENDORED.md`.
Si la lógica core necesita un arreglo, se hace en upstream y se re-sincroniza, no en `vendor/`.

## Decisiones cerradas (NO re-litigar)

1. GUI = **.NET 10 + Avalonia UI** (cross-platform; WPF descartado por ser Windows-only).
2. Modelo de datos = traducciones **embebidas** + extracción **lean** (solo sheets traducidos,
   en vivo desde el juego del usuario). Legal-clean: no se redistribuyen bytes de SquareEnix.
3. Reuso = repo standalone que **vendoriza** (copia) el core de FFXIV-Spanish. No submodule.
4. Bundling traducciones = **solo embebido** en el `.exe`. Actualizar traducciones = re-publicar
   (re-correr `build-translations.ps1` + `dotnet publish`). Sin fichero lateral.
5. Test de integración = **EXD sintético** generado en código (no se versionan `.exd` reales).
6. Categorías del panel avanzado = **híbrido**: metadatos curados (nombre/orden/tooltip) en código,
   habilitación y contadores reales según el manifest embebido.

## Comandos

```powershell
dotnet build                              # compila la solución
dotnet test                               # unit + integración
build/sync-vendor.ps1                     # re-sincroniza vendor/ desde upstream
build/sync-translations.ps1               # (F2) trae el manifest JSONL desde upstream
build/build-translations.ps1              # (F2) compacta data/translations -> artifacts/translations.dat
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
- No editar `vendor/` a mano.
- No añadir traducción automática: el corpus llega curado desde upstream.

## Estado

- **F0** hecho: scaffold + vendor de Core/GameData + `sync-vendor.ps1`. Compila limpio.
- **F0.5** hecho: `CLAUDE.md` (→`@AGENTS.md`), este `AGENTS.md`, `docs/DESIGN.md`.
- **F1** hecho: `vendor/XivSpanish.Packaging` (primitivas) + `src/FFXIVSpanishPatcher.Pipeline`
  (orquestación `PatchPipeline` con eventos de progreso, ported del `Program.cs` upstream) + tests
  (14, incl. integración con EXD sintético: content + write-at-offset + broadcast + `.pmp`).
- **Siguiente: F2** — `sync-translations.ps1` + `build-translations.ps1` + `EmbeddedTranslationSource`.
