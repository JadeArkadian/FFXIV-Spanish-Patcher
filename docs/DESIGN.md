# FFXIVSpanish Patcher — diseño técnico

Documento de arquitectura vigente a partir de v0.3.0. Para instrucciones operativas de agentes,
consultar `../AGENTS.md`; para la política de desfase de versiones, `COMPATIBILITY.md`.

## Propósito

Aplicación de escritorio self-contained que lee únicamente las páginas EXD necesarias de una
instalación local de Final Fantasy XIV, aplica traducciones al castellano y genera un `.pmp`
instalable con Penumbra.

La aplicación no modifica la instalación ni distribuye datos del juego. La GUI es un shell fino:
todo el procesamiento vive en el pipeline y en las bibliotecas propias bajo `vendor/`.

## Decisiones cerradas

| Área | Decisión |
| --- | --- |
| GUI | .NET 10, Avalonia UI y MVVM |
| Paridad | Una sola vista XAML y el mismo diseño en Windows, Linux y macOS |
| Distribución | Self-contained; single-file salvo paquete portable especial para Nexus |
| Traducciones | Blob Brotli-JSONL embebido; en Windows puede ir adyacente por mitigación AV |
| Datos FFXIV | Extracción lean desde la instalación del usuario |
| Integridad | Obligatoria; no existe opción para desactivarla |
| Version mismatch | Confirmación explícita y *best effort* auditable |
| Categorías | Metadatos curados, contadores/enablement derivados del corpus |
| Tests EXD | Fixtures sintéticos generados en código |
| Markdown | Markdig validado y controles Avalonia nativos; sin WebView |

## Layout

```text
src/
  FFXIVSpanishPatcher.App/
    Services/                       shell, actualizaciones, Dalamud, Markdown
    ViewModels/                     estado MVVM, consola y etapas
    Views/                          MainWindow, renderer Markdown, consola seleccionable
  FFXIVSpanishPatcher.Pipeline/     load → resolve → patch → package → verify
vendor/
  XivSpanish.Core/                  modelos y manifest
  XivSpanish.GameData/              Lumina, EXH/EXD, SeString y patcher binario
  XivSpanish.Packaging/             broadcast, alias, guards y gates
data/
  translations.dat                 corpus runtime versionado
  recommended-game-version.txt     referencia exacta de FFXIV
  translation-milestones.md        historial mostrado por la GUI
tests/
  FFXIVSpanishPatcher.Tests/        unitarias e integración EXD sintética
  FFXIVSpanishPatcher.App.Tests/    ViewModel, servicios y Avalonia headless
tools/
  XivSpanish.BlobBuilder/           sync/build del blob
```

`vendor/` se sembró desde `FFXIV-Spanish`, pero ahora es código propio de este repositorio. No debe
sobrescribirse mediante una resincronización masiva; las mejoras se portan manualmente y se prueban.

## Pipeline

### Abstracciones

```text
ITranslationSource      carga corpus embebido o fixtures
IPatchBackend           resuelve sheets/rows y expone IBaseExdSource
IPatchBackendFactory    abre cliente real o backend sintético
IIntegrityVerifier      valida estructura y contenido del paquete
PatchPipeline           orquesta y emite PipelineEvent
```

### Flujo

```mermaid
flowchart LR
  A["Cargar corpus"] --> B["Filtrar categorías"]
  B --> C["SeString gate"]
  C --> D["Resolver hojas, filas y páginas"]
  D --> E["Leer EXD y aplicar replacements"]
  E --> F["Guard de contaminación"]
  F --> G["Empaquetar en temporal"]
  G --> H["Verificar siempre"]
  H --> I["Promover atómicamente"]
```

`IPatchBackend.ResolveExd` devuelve `Resolved`, `MissingSheet` o `UnresolvedRow`. El pipeline sigue
con las páginas válidas, emite sus avisos y devuelve estadísticas estructuradas.

La baja coincidencia solo puede ignorarse si la GUI ha comparado dos versiones conocidas y el
usuario ha confirmado `BestEffortVersionMismatch`. En modo estricto, el guard conserva su función de
detectar una base contaminada o incompatible.

### Salidas

- `Ok`: `.pmp` verificado, sin omisiones.
- `PackagedWithMisses`: `.pmp` verificado y utilizable, con cobertura parcial.
- `NothingToPackage`: ninguna escritura; no se publica paquete vacío.
- `Contaminated`: guard estricto.
- `ValidationFailed`: el temporal no pasó integridad.
- `GameDataError`: no se pudo abrir o leer la instalación.
- `OutputError`: fallo al escribir o promover.

El resultado incluye `PatchStatistics`; la GUI no reconstruye métricas analizando texto.

### Transacción de salida

Cada ejecución usa un staging con GUID y un temporal hermano de la salida. El verificador se ejecuta
siempre sobre ese temporal. Solo un paquete válido reemplaza de forma atómica el destino; cualquier
fallo conserva la salida anterior. Staging y temporales se limpian en `finally`.

## GUI

`MainWindow.axaml` es común a todos los RIDs. Tamaño de referencia `1240 × 820`, mínimo
`1080 × 720`, tema oscuro azul noche. Las superficies usan degradados lineales y radiales nativos de
Avalonia: no hay HTML, WebView ni una implementación visual distinta por plataforma.

La tipografía de interfaz es Inter, suministrada por `Avalonia.Fonts.Inter`. Los titulares
editoriales usan Noto Serif embebida bajo SIL OFL 1.1. De este modo la métrica, el peso y el
interlineado son reproducibles en Windows, Linux y macOS aunque el sistema no tenga esas fuentes.

La vista contiene:

- cabecera con logo centrado, edición ARR e indicadores `Preparación → Generando → Resultado`;
- comprobaciones de juego, versión, corpus y Penumbra;
- categorías avanzadas en cinco columnas y dos filas;
- aviso persistente y modal para version mismatch;
- hito ARR e historial Markdown;
- estado listo/progreso/resultado;
- consola grande de ancho completo;
- pie con salida, corpus y estado.

Los indicadores de etapa no son botones. No hay cancelación porque el proceso es corto. No hay toggle
de integridad. Cero categorías deshabilita la acción principal.

### Consola

`ConsoleLogTextBlock` deriva de `AvaloniaEdit.TextEditor` en modo solo lectura. El documento rope
conserva el historial completo, pero el editor crea líneas visuales únicamente para el viewport.
Un `DocumentColorizingTransformer` aplica los colores de hora, componente y nivel al construir cada
línea visible. Así se mantienen selección continua, `Ctrl+A`, `Ctrl+C` y scroll fluido sin crear
miles de controles ni un árbol de `Run` completo.

Los eventos se acumulan en el dispatcher y cada lote se inserta como un único bloque de texto. El
append conserva la selección y solo hace autoscroll si el usuario estaba abajo y no ha desplazado la
vista desde que se programó. Ese seguimiento mueve únicamente el eje vertical, conserva la posición
horizontal y no permite desplazarse bajo el final del documento. El pipeline sigue emitiendo una
línea de resultado por cada página y todos los avisos normales; `--debug` añade trazas internas de
broadcast y `miss`.

### Markdown

`TranslationMilestoneService` parsea `data/translation-milestones.md` con Markdig, valida una lista
cerrada de nodos y rechaza HTML, imágenes y enlaces no HTTP(S). `MarkdownAvaloniaRenderer` crea
controles nativos para conservar el diseño y evitar una superficie web.

### Dalamud/Penumbra

`DalamudPenumbraService` inspecciona raíces conocidas de XIVLauncher, XLCore y XIV on Mac. Solo
considera Penumbra presente si encuentra un manifiesto identificable. Si
`IsResumeGameAfterPluginLoad` no es `true`, la GUI ofrece corregirlo.

La edición se hace con temporal en el mismo directorio, flush, detección SHA-256 de cambios
concurrentes, reemplazo atómico y relectura. Todo fallo de esta integración externa es silencioso por
contrato.

## Datos

`translations.dat` contiene solo filas empaquetables (`approved`/`gold`, target no vacío y source key
útil). `recommended-game-version.txt` y `translation-milestones.md` se embeben como recursos.

```text
FFXIV-Spanish JSONL
  → BlobBuilder sync (corpus crudo local, git-ignored)
  → BlobBuilder build
  → translations.dat + recommended-game-version.txt
  → recurso de la aplicación
```

CI no reconstruye el corpus. Consume exactamente el blob versionado.

## Build y dependencias

El SDK está fijado por `global.json`. Todos los proyectos generan y versionan `packages.lock.json`;
CI y release restauran con `--locked-mode`. El grafo común declara `win-x64`, `linux-x64` y
`osx-arm64`, de modo que el mismo lock soporta toda la matriz y no depende del último RID restaurado.

La publicación usa trimming completo y rootea `Lumina`/`Lumina.Excel`, porque se consulta metadata
generada mediante reflexión. Cambiar dependencias o roots exige ejecutar los binarios publicados.

RIDs de release:

- `win-x64`: self-contained; `translations.dat` adyacente para reducir falsos positivos.
- `linux-x64`: self-contained single-file.
- `osx-arm64`: bundle `.app`, icono y firma ad-hoc.

## Estrategia de pruebas

- pipeline y outcomes con backend en memoria;
- EXD sintético para parcheo, broadcast, aliases e integridad;
- hojas/páginas ausentes, `misses`, mismatch y cero aplicadas;
- promoción transaccional y preservación de salida;
- detector de juego/versiones;
- Dalamud/Penumbra y JSON concurrente/incorrecto;
- Markdown válido e inseguro;
- consola con 10.000 líneas y selección;
- smoke headless de la ventana real;
- native publish smoke para los tres RIDs.

No se aceptan fixtures reales de FFXIV.

## Riesgos residuales

1. CI no sustituye una prueba manual con instalación real y Penumbra.
2. Un paquete parcial puede traducir menos contenido; la interfaz debe mantener visibles los avisos.
3. Los cambios de estructura EXD pueden requerir soporte nuevo aunque el *best effort* conserve el
   resto.
4. macOS usa firma ad-hoc, no notarización pública.
5. Las herramientas de terceros pueden mover sus rutas/configuración; esa integración debe seguir
   siendo acotada y silenciosa ante fallos.

## Documentos relacionados

- `COMPATIBILITY.md`: contrato de versiones y fallos.
- `TRANSLATION_MILESTONES.md`: edición del historial Markdown.
- `RELEASE_CHECKLIST.md`: cierre reproducible.
- `RELEASE_SIGNING.md`: firma y verificación.
