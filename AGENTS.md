# Notas para agentes IA

Usa siempre `/caveman full` (coincide con el hook global de SessionStart). Habla en español;
los `.md` de documentación pueden ir en español.

## Que es esto

**FFXIVSpanish Patcher** es una app de escritorio para generar un mod `.pmp` de Penumbra con la
traduccion al castellano de Final Fantasy XIV.

Landing page publica del proyecto: <https://ffxivspanish.carrd.co/>. Se usa como `Website` en el
`meta.json` del `.pmp` generado.

La app:

1. Detecta o pide la ruta de una instalacion local de FFXIV.
2. Lee la version instalada del juego desde `ffxivgame.ver`.
3. Extrae solo las paginas `.exd` necesarias via Lumina; no dumpea el juego entero.
4. Aplica traducciones embebidas en el ejecutable.
5. Filtra/omite por defecto filas SeString inseguras y sigue empaquetando el resto.
6. Genera un `.pmp` instalable en Penumbra.

No modifica DATs ni otros archivos originales del juego. No versionar ni redistribuir bytes de
Square Enix (`.exd`, `.exh`, `.pmp`, dumps, snapshots reales).

La GUI es **.NET 10 + Avalonia UI (MVVM)**. La app es un shell fino; la logica de extraccion,
parcheo binario EXD, SeString y empaquetado vive en `src/FFXIVSpanishPatcher.Pipeline` y en codigo
vendorizado propio bajo `vendor/`.

## Estructura actual

- `src/FFXIVSpanishPatcher.App` - GUI Avalonia, MVVM, tema oscuro, iconos, update-check de GitHub
  Releases, version recomendada embebida y recursos `translations.dat`.
- `src/FFXIVSpanishPatcher.Pipeline` - orquestacion `load -> SeString gate -> extract -> patch ->
  package -> verify`, detector de ruta/version del juego, categorias, integridad y eventos
  `IProgress<PipelineEvent>`.
- `vendor/XivSpanish.Core` - modelos de traduccion, JSONL, manifest, normalizacion, claves de origen.
- `vendor/XivSpanish.GameData` - Lumina, EXH/EXD, `ExdPatcher`, lectura de filas, parser/tokenizer
  SeString, `GameLocator`.
- `vendor/XivSpanish.Packaging` - primitivas de empaquetado: broadcast, alias de campos,
  contamination guard y gate SeString de manifest.
- `tools/XivSpanish.BlobBuilder` - tool C# `sync`/`build` para regenerar traducciones embebidas y
  `data/recommended-game-version.txt`.
- `data/translations.dat` - blob Brotli-JSONL versionado con filas empaquetables.
- `data/recommended-game-version.txt` - version de FFXIV para la que se construyo el blob/release.
- `data/translation-milestones.md` - historial Markdown embebido que muestra la GUI.
- `data/translations/jsonl/` - corpus crudo local, git-ignored; no se versiona.
- `tests/FFXIVSpanishPatcher.Tests` - tests de pipeline, EXD sintetico, categorias, detector, SeString
  y packaging.
- `tests/FFXIVSpanishPatcher.App.Tests` - tests headless de GUI/blob/viewmodel.
- `.github/workflows/ci.yml` - build + test en `main` y PR.
- `.github/workflows/release.yml` - tags `vX.Y.Z` publican zips self-contained para `win-x64`,
  `linux-x64` y `osx-arm64`.
- `build/macos/Info.plist` - metadatos del bundle `.app`.
- `docs/DESIGN.md` - diseno historico/arquitectura.
- `README.md`, `CONTRIBUTING.md`, `AI_USAGE.md`, `NOTICE.md` - docs publicas/legal/uso.

## `vendor/`: codigo propio

`vendor/` se sembro copiando partes de upstream **FFXIV-Spanish**, pero ahora es codigo mantenido en
este repo. Se puede editar. No hay resync automatico seguro.

No recrear `sync-vendor.ps1` ni sobrescribir `vendor/` desde upstream: destruiria divergencias ya
intencionales (modelo recortado, `gold`, SeString gate, packaging, codigo muerto eliminado, etc.).
Si hace falta una mejora upstream, portarla a mano y probarla aqui.

La procedencia historica vive en `vendor/VENDORED.md`.

## Decisiones cerradas

1. GUI = **.NET 10 + Avalonia UI**, no WPF.
2. Distribucion = ejecutables self-contained single-file por RID; no instalador obligatorio.
3. Traducciones = embebidas por defecto. La release Windows mantiene `translations.dat` adyacente
   como mitigacion de falsos positivos AV. Actualizar traducciones implica regenerar blob y publicar
   nueva release.
4. Datos del juego = extraccion lean desde la instalacion del usuario. No redistribuir archivos de FFXIV.
5. Corpus empaquetable = `status in {approved, gold}` + `target` no vacio + `sourceKey` util.
6. SeString = validar compatibilidad. Por defecto, filas incompatibles se omiten con warning; solo
   empaquetar con `ForceSeString` cuando se quiera diagnosticar conscientemente.
7. Categorias avanzadas = hibridas: catalogo/labels/tooltips curados en app, contadores y enablement
   derivados del manifest embebido.
8. Tests de integracion = EXD sintetico generado en codigo. Nunca depender del juego instalado en CI.
9. Legal/docs = mantener claro que el proyecto no esta afiliado a Square Enix y que las contribuciones
   de traduccion tienen terminos propios (`CONTRIBUTING.md`).

## Comandos

```bash
dotnet restore --locked-mode
```
```bash
dotnet build -c Release --no-restore
```
```bash
dotnet test -c Release --no-build
```

# Regenerar corpus crudo desde upstream y reconstruir blob + version recomendada.
```bash
dotnet run --project tools/XivSpanish.BlobBuilder -- sync --build
```

# Solo compactar data/translations/jsonl/ -> data/translations.dat.
```bash
dotnet run --project tools/XivSpanish.BlobBuilder -- build
```

# Publicar un ejecutable self-contained single-file (Windows y Linux)
```bash
dotnet publish src/FFXIVSpanishPatcher.App/FFXIVSpanishPatcher.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

```bash
dotnet publish src/FFXIVSpanishPatcher.App/FFXIVSpanishPatcher.App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

El proyecto usa `global.json` con SDK `10.0.302` y `rollForward: latestFeature`. Los
`packages.lock.json` estan versionados; CI y releases restauran con `--locked-mode`.

La app publica con trimming activado (`PublishTrimmed=true`, `TrimMode=full`) y rootea `Lumina` /
`Lumina.Excel` para conservar metadatos usados por reflexion. No quitar esos roots sin validar un
publish real.

## Traducciones embebidas

`data/translations.dat` es Brotli-JSONL proyectado. El builder:

- filtra a filas empaquetables (`approved` + `gold`, target no vacio, sourceKey con sheet/row);
- proyecta al modelo runtime (`source`, `target`, `status`, `sourceKey`);
- comprime con `BrotliStream`;
- escribe `data/recommended-game-version.txt` cuando tiene fuente de version.

Estado observado el 2026-07-29:

- `data/translations.dat`: ~16.9 MiB (`17708236` bytes).
- `data/recommended-game-version.txt`: `2026.07.16.0001.0000`.
- remote GitHub configurado: `JadeArkadian/FFXIV-Spanish-Patcher`.
- tags publicados/locales hasta `v0.2.5`.

CI no reconstruye el corpus: usa el blob versionado.

## Pipeline

`PatchPipeline.Run`:

1. carga traducciones;
2. aplica seleccion de categorias;
3. corre `ManifestSeStringGate`;
4. abre backend de juego real o snapshot (`BaseExdDir`);
5. agrupa por pagina EXD;
6. calcula broadcast para duplicados;
7. parchea paginas;
8. aplica contamination guard/min match rate;
9. escribe staging y un `.pmp` temporal;
10. verifica siempre la integridad;
11. promueve la salida verificada de forma atomica.

Resultados esperados:

- `Ok` o `PackagedWithMisses` producen paquete usable.
- `NothingToPackage`, `Contaminated`, `ValidationFailed`, `GameDataError` y `OutputError` fallan sin
  paquete usable.
- `SeStringGate` existe como outcome historico/diagnostico, pero el flujo actual omite filas inseguras
  por defecto en vez de abortar todo el build.

## GUI

La GUI debe seguir siendo una herramienta directa, no landing page:

- selector/deteccion de ruta FFXIV;
- aviso de version recomendada vs instalada;
- confirmacion y best effort auditable cuando las versiones difieren;
- categorias con contadores reales;
- al menos una categoria obligatoria;
- consola streaming grande, coloreada y multiseleccionable;
- historial de hitos desde `data/translation-milestones.md`;
- deteccion silenciosa de Dalamud/Penumbra y correccion opcional del ajuste de carga;
- abrir carpeta/salida;
- comprobacion de nueva version via GitHub Releases;
- textos orientados a usuario final en castellano.

Evitar meter logica de parcheo en ViewModels: la GUI construye `PatchRequest` y consume eventos del
pipeline.

## Tests

Usar xUnit. La suite cubre:

- EXD sintetico y regresiones de SeString;
- broadcast/alias de campos;
- pipeline y resultados;
- categorias;
- detector de rutas/version;
- carga del blob embebido;
- smoke/headless de Avalonia.

No commitear fixtures de FFXIV reales. Si un test necesita EXD, construirlo en codigo como
`SyntheticExd`.

## Releases

Los tags validos son `vX.Y.Z` con cada numero `0..999`. El workflow:

- publica `win-x64` nativamente desde Windows y `linux-x64` desde Ubuntu;
- publica `osx-arm64` desde macOS;
- monta `.app` en macOS, incluye `icon.icns` y firma ad-hoc;
- adjunta zips a GitHub Release;
- inyecta version, `RepositorySlug` y URLs de latest release en assembly metadata.

## No hacer

- No modificar archivos originales de FFXIV ni reinyectar DATs.
- No versionar `.exd`, `.exh`, `.pmp`, dumps ni snapshots reales del juego.
- No anadir traduccion automatica sin decision humana explicita; el corpus debe estar curado/revisado.
- No convertir `vendor/` en submodule ni resync masivo.
- No quitar trimming/Lumina roots/update metadata sin probar publish.
- No cambiar el modelo de distribucion del corpus: embebido por defecto y lateral solo en los
  paquetes Windows que lo declaran mediante `ExternalTranslations`.
- No hacer cambios legales/licencia a la ligera; revisar `LICENSE.md`, `NOTICE.md`, `CONTRIBUTING.md`
  y `AI_USAGE.md`.

## Estado actual

El plan inicial F0-F7 esta completado y el proyecto ya esta en fase de mantenimiento/release. La
evolucion v0.3.0 vive temporalmente en `docs/evolucion-v0.3.0/`; esa carpeta solo se elimina despues
de validar la implementacion y recibir aprobacion expresa. Pendiente normal de cada release:
regenerar blob cuando cambie upstream, publicar tag y validar manualmente contra una instalacion real
de FFXIV y Penumbra en las plataformas que toque. Usar `docs/RELEASE_CHECKLIST.md`.
