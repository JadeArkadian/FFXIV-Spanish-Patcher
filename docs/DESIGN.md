# FFXIVSpanish Patcher — Diseño y plan

Documento de diseño de la aplicación. Captura las decisiones tomadas, la arquitectura y el estado
actual. Para el contexto operativo de un agente IA ver `../AGENTS.md`.

## 1. Propósito

Aplicación de escritorio de un solo ejecutable (sin instalación, sin runtime previo) que extrae los
`.exd` necesarios de una instalación local de Final Fantasy XIV, los parchea con traducciones al
castellano y emite un `.pmp` instalable con Penumbra. UX simple: indicar ruta del juego → "Generar
mod" → consola de progreso → `.pmp` de salida.

La app es un **shell** sobre lógica sembrada desde el repo upstream **FFXIV-Spanish** (extractor,
parcheo binario de EXD, SeString, packager), mantenida ahora como código vendorizado propio.

## 2. Decisiones cerradas

| # | Decisión | Elegido | Motivo |
|---|----------|---------|--------|
| 1 | GUI framework | .NET 10 + Avalonia UI (MVVM) | Cross-platform, reutiliza el core C# tal cual, tema oscuro Fluent, publica a single-file. WPF sería más rápido en Windows pero Windows-only. |
| 2 | Modelo de datos | Traducciones embebidas + extracción lean | Solo se leen los sheets que se traducen, en vivo desde el juego del usuario. No se redistribuyen assets de Square Enix (legal-clean) y resiste parches del juego. |
| 3 | Reuso / repo | Repo standalone que sembró `vendor/` copiando el core; ahora código propio editable | Producto distribuible desacoplado del repo de traducción. `vendor/` puede divergir de upstream; no hay re-sync automático. |
| 4 | Bundling traducciones | Solo embebido en el `.exe` | Un único fichero para el usuario. Actualizar traducciones = re-publicar. Sin fichero lateral. |
| 5 | Test de integración | EXD sintético generado en código | No se versionan `.exd` reales (regla del repo) ni se depende del juego en CI. Reproducible. |
| 6 | Categorías panel avanzado | Híbrido: metadatos curados + gating por manifest | Etiquetas/tooltips/orden bonitos y controlados, pero contadores y habilitación reales según lo que hay en el manifest. |

## 3. Arquitectura y layout

```
FFXIV-Spanish-Patcher/
  FFXIVSpanishPatcher.slnx
  global.json · Directory.Build.props · .gitignore
  src/
    FFXIVSpanishPatcher.App/        # GUI Avalonia (MVVM, entry point, tema oscuro)   [F3]
    FFXIVSpanishPatcher.Pipeline/   # orquestación extract→patch→package + IProgress  [F1]
  vendor/                           # sembrado desde upstream; código propio editable
    XivSpanish.Core/                #   modelos, ManifestLoader, DomainMap
    XivSpanish.GameData/            #   Lumina, EXD binario, ExdPatcher, SeString, GameLocator
    XivSpanish.Packaging/           #   broadcast, alias, contamination guard, gate SeString
    VENDORED.md                     #   procedencia (commit upstream + fecha)
  data/
    translations.dat                # blob Brotli-JSONL versionado
    recommended-game-version.txt    # versión de FFXIV recomendada para el blob/release
    translations/                   # README + corpus crudo git-ignored en jsonl/
  tests/
    FFXIVSpanishPatcher.Tests/      # unit + integración EXD sintético
    FFXIVSpanishPatcher.App.Tests/  # smoke/headless GUI + blob/viewmodel
  build/
    macos/Info.plist                # metadatos del bundle .app (release)
  tools/
    XivSpanish.BlobBuilder/         # [F2] tool C#: sync (corpus) + build (→ data/translations.dat)
  docs/DESIGN.md
```

### Vendoring (sembrado, no espejo)

`vendor/` se sembró una vez copiando el core de upstream pero es **código propio editable** (regla
read-only levantada 2026-06-24); ya ha divergido de upstream. No hay re-sync automático: se borró
`sync-vendor.ps1` porque reimportaba upstream sobreescribiendo la divergencia. Para una mejora puntual
de upstream, pórtala a mano. DRY entre repos = best-effort (coste aceptado al elegir "copiar" en vez de
submodule/referencia cruzada).

## 4. Capa Pipeline (SOLID)

Cada pieza una responsabilidad; depende de abstracciones (inyectables, testeables). En la
implementación actual varias responsabilidades viven como clases concretas pequeñas alrededor de
`PatchPipeline`, no como todos los interfaces del bosquejo inicial:

```
ITranslationSource     // carga traducciones desde recurso embebido o fixtures
IPatchBackend          // bytes base/layout desde cliente real o snapshot sintético
GamePathDetector       // detecta instalación y lee ffxivgame.ver
PackageWriter          // staging + meta.json + default_mod.json + zip .pmp
IIntegrityVerifier     // re-parse EXD + estructura .pmp
PatchPipeline          // orquesta y emite IProgress<PipelineEvent>
```

Flujo: `load translations → selección/categorías → SeString gate → abrir backend → agrupar páginas →
broadcast → patch → contamination guard → package → verify`. Cada evento alimenta la consola de la
GUI. **Drift de parche:** si un source key no casa con la versión del juego del usuario, se omite con
warning; no debe tumbar la generación completa salvo que el paquete quede inutilizable.

## 5. GUI (Avalonia, mapeo al mockup)

- MainWindow de dos columnas + status bar; FluentTheme dark + acento azul; consola monospace.
- Izquierda: ruta + `Examinar…`; `Generar mod` (deshabilitado hasta ruta válida) + `Abrir carpeta de
  salida`; consola de progreso + `Limpiar`.
- Derecha: panel de información; grid de `CategoryViewModel` (IsChecked, Count, Tooltip,
  enabled-por-manifest); toggle `Verificar integridad al finalizar`.
- Status: nombre de salida `FFXIVSpanish-{yyyy-MM-dd_HH-mm-ss}.pmp`, carpeta, estado y acciones de
  salida/log.
- La app muestra versión propia, comprueba GitHub Releases y avisa si `ffxivgame.ver` difiere de
  `data/recommended-game-version.txt`.
- `Generar mod` corre el Pipeline en background; el progreso se marshala al hilo de UI.

## 6. Datos

El corpus crudo son decenas de MB de JSONL; proyectado y comprimido, el blob actual ronda 8.5 MiB.
Se versiona **solo el blob** compacto y la versión recomendada.

**Blob (versionado): `data/translations.dat`** — Brotli-JSONL (proyectado a approved+gold), fuente de
registro compacta que la App embebe como `EmbeddedResource`; .NET lo lee con `BrotliStream` nativo.

**Versión recomendada (versionada): `data/recommended-game-version.txt`** — versión de FFXIV asociada
al blob/release; la GUI la compara con `ffxivgame.ver` de la instalación local y avisa si difiere.

**Corpus crudo (NO versionado, en `.gitignore`): `data/translations/jsonl/`** — se sincroniza
localmente desde upstream solo para regenerar el blob; su historial línea-a-línea vive en upstream.

```
upstream jsonl → BlobBuilder sync → data/translations/jsonl/ (git-ignored)
              → BlobBuilder build (approved+gold, brotli)
              → data/translations.dat + data/recommended-game-version.txt
              → EmbeddedResource → publish
```

CI (F7) NO reconstruye el blob: usa el `data/translations.dat` ya versionado tras el checkout.

## 7. Build y distribución

```powershell
dotnet publish src/FFXIVSpanishPatcher.App/FFXIVSpanishPatcher.App.csproj -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Ejecutable self-contained single-file, runtime incluido, cero instalación. La publicación actual usa
trimming (`PublishTrimmed=true`, `TrimMode=full`) y rootea `Lumina`/`Lumina.Excel` para conservar los
metadatos que el pipeline consulta por reflexión. No se usa NativeAOT.

Linux/Mac: mismo comando con `-r linux-x64` / `osx-arm64` / `osx-x64`. En releases, macOS se empaqueta
como `.app` con `Info.plist`, icono y firma ad-hoc.

## 8. Tests

- **Unit:** pipeline, detector de rutas/version, carga de traducciones, categorías, broadcast,
  packageable statuses y edge-cases de SeString.
- **Integración (fixture sintético):** construir EXH+EXD mínimo válido en código → `ExdPatcher` con
  traducción de prueba → assert de la string nueva en el offset correcto + caso offset a espacio vacío
  → empaquetado → assert de que el `.pmp` contiene `meta.json`/`default_mod.json` + EXD en la
  ruta interna correcta → verificador pasa.
- **GUI headless:** smoke de Avalonia, carga del blob embebido y validaciones de ViewModel.

## 9. Riesgos / pendientes actuales

1. Validación manual contra juego real sigue siendo necesaria por release/plataforma; CI no puede
   cubrir instalaciones reales ni Penumbra.
2. Trimming + Lumina dependen de roots explícitos; cualquier cambio de publish debe probar binarios
   publicados, no solo `dotnet test`.
3. Los parches oficiales de FFXIV pueden invalidar sources/offsets; la app avisa por versión
   recomendada, pero el usuario puede generar paquetes contra versiones distintas.
4. macOS usa firma ad-hoc, no notarización; los usuarios pueden necesitar aprobación manual de
   Gatekeeper.

## 10. Plan por fases

| Fase | Contenido | Estado |
|------|-----------|--------|
| F0   | Scaffold + git init + sln + sembrado de `vendor/` Core/GameData (vía el extinto `sync-vendor.ps1`). Compila vendored. | hecho |
| F0.5 | `CLAUDE.md` (→`@AGENTS.md`) + `AGENTS.md` + `docs/DESIGN.md`. | hecho |
| F1   | Lib `Pipeline` (interfaces + orquestación + eventos) reusando GameData/Packaging; unit + integración sintética. Headless. | hecho |
| F2   | `tools/XivSpanish.BlobBuilder` (sync+build, C#) + `EmbeddedTranslationSource` (blob brotli versionado, solo approved+gold). | hecho |
| F3   | GUI Avalonia matching mockup, bindeada al Pipeline. | hecho |
| F4   | `GamePathDetector` (registry + Steam vdf + rutas comunes) + integración SO (abrir carpeta, copiar log). | hecho |
| F5   | Publish single-file self-contained + smoke headless de la GUI + pulido. Smoke contra juego real = manual. | hecho |
| F6   | Validación Linux/Mac contra juego real por release/plataforma. | continuo/manual |
| F7   | Workflows GitHub: CI (build+test) + Release matrix (win-x64/linux-x64/osx-arm64/osx-x64). | hecho |

Workflows en `.github/workflows/`: `ci.yml` (push/PR → restore+build+test, incl. smoke headless)
y `release.yml` (tag `vX.Y.Z` → publica los 4 RID self-contained single-file, empaqueta `.app` en
macOS y adjunta los zips a un GitHub Release). El repo remoto actual es
`JadeArkadian/FFXIV-Spanish-Patcher`.
