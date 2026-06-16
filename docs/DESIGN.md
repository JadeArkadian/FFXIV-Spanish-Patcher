# FFXIVSpanish Patcher — Diseño y plan

Documento de diseño de la aplicación. Captura las decisiones tomadas, la arquitectura y el plan
por fases. Para el contexto operativo de un agente IA ver `../AGENTS.md`.

## 1. Propósito

Aplicación de escritorio de un solo ejecutable (sin instalación, sin runtime previo) que extrae los
`.exd` necesarios de una instalación local de Final Fantasy XIV, los parchea con traducciones al
castellano y emite un `.pmp` instalable con Penumbra. UX simple: indicar ruta del juego → "Generar
mod" → consola de progreso → `.pmp` de salida.

La app es un **shell** sobre lógica ya validada del repo upstream **FFXIV-Spanish** (extractor,
parcheo binario de EXD, SeString, packager), reutilizada por código vendorizado.

## 2. Decisiones cerradas

| # | Decisión | Elegido | Motivo |
|---|----------|---------|--------|
| 1 | GUI framework | .NET 10 + Avalonia UI (MVVM) | Cross-platform, reutiliza el core C# tal cual, tema oscuro Fluent, publica a single-file. WPF sería más rápido en Windows pero Windows-only. |
| 2 | Modelo de datos | Traducciones embebidas + extracción lean | Solo se leen los sheets que se traducen, en vivo desde el juego del usuario. No se redistribuyen assets de SquareEnix (legal-clean) y resiste parches del juego. |
| 3 | Reuso / repo | Repo standalone que vendoriza (copia) el core | Producto distribuible desacoplado del repo de traducción. Coste: sync manual entre repos, acotado por `sync-vendor.ps1`. |
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
  vendor/                           # espejo de upstream, NO editar a mano
    XivSpanish.Core/                #   modelos, hashing, ManifestLoader, DomainMap
    XivSpanish.GameData/            #   Lumina, EXD binario, ExdPatcher, SeString, GameLocator
    VENDORED.md                     #   procedencia (commit upstream + fecha)
  src vendorizado del Packager      # [F1] lógica de Program.cs extraída a librería
  data/translations/                # manifest aprobado en JSONL (fuente del blob)
  tests/FFXIVSpanishPatcher.Tests/  # unit + integración EXD sintético
  build/
    sync-vendor.ps1                 # one-way upstream→vendor (código)
    sync-translations.ps1           # [F2] one-way upstream→data/translations (JSONL)
    build-translations.ps1          # [F2] data/translations → artifacts/translations.dat (embebido)
  docs/DESIGN.md
```

### Frontera de vendoring

`vendor/` no se edita a mano; los cambios entran solo por `sync-vendor.ps1` (upstream → vendor). Si la
lógica core necesita un arreglo, se hace en upstream y se re-sincroniza. DRY entre repos = best-effort
documentado (coste aceptado al elegir "copiar" en vez de submodule/referencia cruzada).

## 4. Capa Pipeline (SOLID)

Cada pieza una responsabilidad; depende de abstracciones (inyectables, testeables):

```
ITranslationSource   // carga el manifest desde el recurso embebido
IGamePathLocator     // detecta y valida la instalación de FFXIV (reusa vendor/GameLocator)
IExtractionService   // ruta + sheets → bytes base EXH/EXD vía GameData/Lumina
IPatchService        // EXD base + entradas → EXD parcheado (ExdPatcher + SeString)
IPackagingService    // EXD parcheados → meta.json + default_mod.json + zip → .pmp
IIntegrityVerifier   // opcional: re-parse EXD + gate SeString + valida estructura del .pmp
PatchPipeline        // orquesta y emite IProgress<PipelineEvent>(componente, msg, estado, count)
```

Flujo: `locate → load translations → por categoría: extract → patch → recolecta → package → verify`.
Cada evento alimenta la consola de la GUI (los `[Extractor]/[Patcher]/[Packager] … OK (255)` salen 1:1
de los eventos). **Drift de parche:** si un source key no casa con la versión del juego del usuario →
skip-con-warning, nunca crash; se reporta en consola y lo cuenta el verificador.

## 5. GUI (Avalonia, mapeo al mockup)

- MainWindow de dos columnas + status bar; FluentTheme dark + acento azul; consola monospace.
- Izquierda: ruta + `Examinar…`; `Generar mod` (deshabilitado hasta ruta válida) + `Abrir carpeta de
  salida`; consola (`ObservableCollection<LogLine>` con runs coloreados) + `Limpiar`.
- Derecha: card de info; grid de `CategoryViewModel` (IsChecked, Count, Tooltip, enabled-por-manifest);
  toggle `Verificar integridad al finalizar`.
- Status: nombre de salida `FFXIVSpanish-{yyyy-MM-dd_HH-mm-ss}.pmp`, badge ÉXITO, carpeta, `Copiar log`.
- `Generar mod` corre el Pipeline en background; el progreso se marshala al hilo de UI.

## 6. Datos

**Fuente (versionada):** `data/translations/*.jsonl` (manifest aprobado; texto curado humano, no
assets). Se sincroniza desde upstream con `sync-translations.ps1`.

**Blob (NO versionado, `artifacts/`):** `build-translations.ps1` compacta el JSONL a
`artifacts/translations.dat`, que se marca `EmbeddedResource` en `FFXIVSpanishPatcher.App`.

```
data/translations/*.jsonl → build-translations.ps1 → artifacts/translations.dat → EmbeddedResource → publish
```

CI hace el mismo flujo tras checkout (el JSONL ya está en el repo); no depende del repo upstream local.

## 7. Build y distribución

```powershell
dotnet publish src/FFXIVSpanishPatcher.App -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Un `.exe` (~70–90 MB), runtime incluido, cero instalación. Sin trimming ni NativeAOT (Lumina usa
reflexión). Linux/Mac: mismo comando con `-r linux-x64` / `osx-arm64` / `osx-x64`.

## 8. Tests

- **Unit:** orquestación del Pipeline (servicios mockeados), parsing de `GameLocator`, carga de
  `TranslationSource`, gating de `CategoryViewModel`, edge-cases de SeString.
- **Integración (fixture sintético):** construir EXH+EXD mínimo válido en código → `ExdPatcher` con
  traducción de prueba → assert de la string nueva en el offset correcto + caso offset a espacio vacío
  → `PackagingService` → assert de que el `.pmp` contiene `meta.json`/`default_mod.json` + EXD en la
  ruta interna correcta → verificador pasa.

## 9. Riesgos / pendientes

1. `Extractor` y `Packager` upstream son `Main` CLI con lógica mezclada (`Program.cs`). Hay que extraer
   esa lógica a forma de librería invocable (vendorizar el Packager y partir su `Main`) — trabajo de F1.
2. Confirmar el esquema exacto del manifest JSONL (`ManifestLoader`) que consume el packager.
3. Confirmar la API in-process del packager para "base bytes from client".
4. Verificar que Lumina no arrastra dependencias nativas en single-file.

## 10. Plan por fases

| Fase | Contenido | Estado |
|------|-----------|--------|
| F0   | Scaffold + git init + sln + vendor Core/GameData + `sync-vendor.ps1`. Compila vendored. | hecho |
| F0.5 | `CLAUDE.md` (→`@AGENTS.md`) + `AGENTS.md` + `docs/DESIGN.md`. | hecho |
| F1   | Lib `Pipeline` (interfaces + orquestación + eventos) reusando GameData/Packaging; unit + integración sintética. Headless. | hecho |
| F2   | `sync-translations.ps1` + `build-translations.ps1` + `TranslationSource` (embebido). | pendiente |
| F3   | GUI Avalonia matching mockup, bindeada al Pipeline. | pendiente |
| F4   | `GamePathLocator` (registry + Steam vdf + rutas comunes) + integración SO (abrir carpeta, copiar log). | pendiente |
| F5   | Publish single-file + smoke contra juego real + pulido del tema. | pendiente |
| F6   | (diferido) Validación Linux/Mac contra juego real. | pendiente |
| F7   | Workflows GitHub: CI (build+test) + Release matrix (win-x64/linux-x64/osx-arm64/osx-x64). | pendiente |
