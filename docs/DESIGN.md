# FFXIVSpanish Patcher — diseño técnico

Documento de arquitectura vigente a partir de v0.6.0. Para instrucciones operativas de agentes,
consultar `../AGENTS.md`; para la política de desfase de versiones, `COMPATIBILITY.md`.

## Propósito

Aplicación de escritorio self-contained que lee únicamente las páginas EXD necesarias de una
instalación local de Final Fantasy XIV, aplica traducciones al castellano y genera un `.pmp`
instalable con Penumbra.

La aplicación no modifica la instalación ni distribuye datos del juego. La GUI es un shell fino:
todo el procesamiento vive en el pipeline y en las bibliotecas propias bajo `vendor/`.

Desde v0.6.0 el mismo pipeline tiene un segundo anfitrión: un plugin de Dalamud que genera el mismo
mod dentro del juego y lo instala directamente en Penumbra, sin pasar por un `.pmp`. Se describe en
[Plugin de Dalamud](#plugin-de-dalamud).

## Decisiones cerradas

| Área | Decisión |
| --- | --- |
| GUI | .NET 10, Avalonia UI y MVVM |
| Paridad | Una sola vista XAML y el mismo diseño en Windows, Linux y macOS |
| Distribución | Self-contained y single-file por RID; Nexus reutiliza el mismo ZIP de GitHub para Linux y macOS (Windows solo en GitHub) |
| Traducciones | Blob Zstandard-JSONL embebido en el ejecutable single-file |
| Datos FFXIV | Extracción lean desde la instalación del usuario |
| Integridad | Obligatoria; no existe opción para desactivarla |
| Version mismatch | Confirmación explícita y *best effort* auditable |
| Categorías | Metadatos curados, contadores/enablement derivados del corpus |
| Tests EXD | Fixtures sintéticos generados en código |
| Markdown | Markdig validado y controles Avalonia nativos; sin WebView |
| Plugin | Dalamud API 15; reutiliza el pipeline, no duplica parcheo ni verificación |
| Penumbra desde el plugin | Contratos IPC v5 por nombre; sin dependencia binaria de `Penumbra.Api` |
| Alcance del plugin | Solo la colección Base/Default; nunca borra el mod |
| Versionado | Patcher, plugin y corpus comparten versión: un tag, una release |

## Layout

```text
src/
  FFXIVSpanishPatcher.App/
    Services/                       shell, actualizaciones, Dalamud, Markdown
    ViewModels/                     estado MVVM, consola y etapas
    Views/                          MainWindow, renderer Markdown, consola seleccionable
  FFXIVSpanishPatcher.Pipeline/     load → resolve → patch → package → verify
  FFXIVSpanishPatcher.Plugin/       plugin de Dalamud: sesión, ventana ImGui, IPC e instalador
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
  FFXIVSpanishPatcher.Plugin.Tests/ estado, IPC con fakes, instalador y paquete del plugin
tools/
  XivSpanish.BlobBuilder/           sync/build del blob
dalamud/
  pluginmaster.json                repositorio de Dalamud; lo escribe la release
```

`vendor/` se sembró desde `FFXIV-Spanish`, pero ahora es código propio de este repositorio. No debe
sobrescribirse mediante una resincronización masiva; las mejoras se portan manualmente y se prueban.

## Pipeline

### Abstracciones

```text
ITranslationSource      carga corpus embebido o fixtures
IPatchBackend           resuelve sheets/rows y expone IBaseExdSource
IPatchBackendFactory    abre cliente real o backend sintético
ModTreeVerifier          valida árbol Penumbra v4 antes de archivarlo
IIntegrityVerifier       reabre ZIP y valida estructura y contenido
PatchPipeline           orquesta y emite PipelineEvent
```

### Flujo

```mermaid
flowchart LR
  A["Cargar corpus"] --> B["Generar todas categorías"]
  B --> C["SeString gate"]
  C --> D["Resolver hojas, filas y páginas"]
  D --> E["Leer EXD y aplicar replacements"]
  E --> F["Guard de contaminación"]
  F --> G["Escribir árbol v4 temporal"]
  G --> H["Verificar árbol"]
  H --> I["Comprimir y verificar ZIP"]
  I --> J["Promover atómicamente"]
```

`IPatchBackend.ResolveExd` devuelve `Resolved`, `MissingSheet` o `UnresolvedRow`. El pipeline sigue
con las páginas válidas, emite sus avisos y devuelve estadísticas estructuradas.

### Paquete Penumbra v4 y categorías

El `.pmp` contiene siempre todas las páginas EXD que se hayan podido parchear. `meta.json` usa
`FileVersion: 4`, sin `default_mod.json` ni `group_*.json`, y define un único grupo Multi estable:
`Categorías de traducción`. Sus diez opciones agrupan los redirects por dominio. La selección de
Avalonia solo calcula `DefaultSettings`, es decir, qué casillas aparecen activadas al importar en
Penumbra; no excluye datos del paquete.

Cada payload se escribe bajo `files/categories/{orden}-{dominio}/exd/...`. Antes de crear el ZIP,
`ModTreeVerifier` reabre `meta.json` y todos los archivos del árbol: exige v4, grupo/opciones/orden
estables, bitfield válido, redirects seguros y únicos, payload existente sin symlinks u huérfanos y
cabecera `EXDF`. `IntegrityVerifier` repite la comprobación relevante tras reabrir el ZIP. Cualquier
fallo conserva la salida anterior.

### Contrato binario SeString

Las longitudes de cuerpos macro y runs usan enteros compactos del cliente:

- Los bytes de tipo `0x01..0xCF` son enteros inline. Como almacenan `longitud + 1`, `0xCF`
  representa la longitud inline máxima: 206 bytes.
- `0xD0..0xEF` son tipos de expresión, no longitudes. Nunca deben emitirse como prefijo.
- Desde 207 bytes se usa la forma extendida `0xF0..0xFE`; por ejemplo, 207 se codifica `F0 CF`.

`SeStringTree` recalcula de dentro hacia fuera todos los prefijos afectados. No se acortan
traducciones para evitar esta frontera. Las pruebas deben fijar bytes esperados en 206/207 y cubrir
un EXD sintético completo: validar solo encode y decode con el mismo codec puede ocultar un error
autoconsistente que el cliente interpretaría como una expresión.

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

`MainWindow.axaml` es común a todos los RIDs. Tamaño por defecto `1280 × 845`, mínimo
`1200 × 680` para caber en un portátil de `1366 × 768` y en 1080p con escala del 125-150 %. Al
abrirse, la ventana se ajusta al área de trabajo de la pantalla. Tema oscuro azul noche. Las superficies usan degradados lineales y radiales nativos de
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

Reglas de ergonomía que la vista mantiene:

- ningún texto informativo baja de 10,5 px;
- una comprobación solo muestra ✓ cuando es cierta: ruta inválida, versión sin leer y corpus cargando
  tienen su propio icono, y «FFXIV compatible» solo aparece si las versiones coinciden;
- una baldosa que hace algo lo dice en cian (`Cambiar ruta…`, `Clic para revisar`); la de Penumbra
  solo es pulsable mientras hay algo que revisar;
- el progreso indica `Paso N de 5` y el resultado, el siguiente paso;
- `Esc` cierra hitos y modales sin aceptarlos, y al abrirse reciben el foco en la opción que no
  cambia nada;
- durante la generación no se puede cambiar la ruta del juego.

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

El botón **Pásate al plugin de Dalamud** añade el repositorio personalizado del plugin a
`ThirdRepoList`, tras una confirmación que explica el cambio. Respeta el formato de Dalamud
(Newtonsoft con `$type` como primera propiedad), reactiva la entrada si ya existía desactivada y no
toca una lista con una forma desconocida. No necesita Penumbra: basta con que exista la configuración
de Dalamud.

El botón solo se muestra mientras la invitación se puede cumplir (`CanInstallPlugin`): hay
configuración de Dalamud, se puede abrir en escritura exclusiva, el juego está cerrado, el
repositorio no está añadido y el plugin no está instalado. Se evalúa al arrancar y al pulsar; no se
vuelve a evaluar si el juego se cierra con el patcher abierto.

Después instala el plugin. Dalamud no tiene cola de instalación: al arrancar carga lo que haya en
`installedPlugins/<InternalName>/<versión>/` y activa en el perfil por defecto un plugin que ningún
perfil conoce. `GitHubPluginPackageSource` lee `dalamud/pluginmaster.json`, solo sigue un
`DownloadLinkInstall` que apunte a las releases del propio repositorio y descarga el ZIP con límite
de tamaño. `DalamudPenumbraService.InstallPlugin` lo extrae en un directorio temporal junto a
`installedPlugins`, rechaza entradas que escapen de él, exige el DLL y un manifiesto con el
`InternalName` esperado y una versión válida, anota `InstalledFromUrl` y `WorkingPluginId` como hace
el instalador de Dalamud y lo mueve a su sitio. Nunca sustituye un plugin ya instalado: actualizarlo
es cosa de Dalamud. Es la segunda conexión de red del patcher, junto a la comprobación de versión, y
solo ocurre tras la confirmación. La descarga va antes de tocar Dalamud: si falla, no se cambia nada
y el botón sigue disponible. El repositorio se compara por URL exacta, igual que hace Dalamud al
emparejar un plugin con su repositorio; reutilizar una entrada con otra grafía dejaría el plugin
huérfano y sin cargar. Esta ruta depende del comportamiento interno de Dalamud, no de un contrato público:
revisarla si una versión nueva deja de cargar el plugin.

Las dos ediciones de la configuración se hacen con temporal en el mismo directorio, flush, detección SHA-256 de cambios
concurrentes, reemplazo atómico y relectura, y nunca con el juego abierto. Todo fallo de esta
integración externa es silencioso por contrato.

Los destellos del botón y de la barra de progreso animan un pincel de gradiente. El pincel en reposo
del control animado debe ser también un gradiente: con un color sólido, Avalonia lanza una excepción
al arrancar o parar la animación.

## Plugin de Dalamud

`FFXIVSpanishPatcher.Plugin` es un segundo anfitrión del pipeline. No contiene lógica de parcheo,
SeString ni verificación: construye un `PatchRequest`, obtiene un árbol v4 verificado y se ocupa de
lo que el patcher deja al usuario, es decir, instalarlo, activarlo y mantenerlo.

### Componentes

```text
Plugin                          punto de entrada; conecta servicios, comando y ventana
TranslationManagerSession       orquestación: carga, reconciliación, regeneración, eventos
TranslationManagerWindow        ventana ImGui; solo dibuja y pide acciones a la sesión
TranslationManagerNotifications avisos de Dalamud (carga temprana, reinicio, versión nueva)
TranslationManagerController    máquina de estados y exclusión mutua de operaciones
PipelineTreeBuilder             ejecuta el pipeline y devuelve un árbol v4 verificado
DalamudPatchBackend             IPatchBackend sobre IDataManager.GameData
PenumbraIpcClient               wrappers tipados de la API IPC v5 y disponibilidad
PenumbraModIdentityService      localiza y adopta el mod existente
ModInstaller                    promoción transaccional con copia de seguridad y rollback
CategorySyncService             categorías y activación en Base/Default, en ambos sentidos
RegenerationPlanner             decide si hay que regenerar a partir del fingerprint
PluginUpdateCheck               compara la versión instalada con la publicada en el repositorio
```

Las reglas que no dependen de Dalamud (estado de cabecera, activación, reinicio pendiente, modelo
de la ventana) son clases puras con tests propios.

### Datos del juego

El plugin no abre la instalación por su cuenta: lee EXH y EXD a través del `GameData` que ya
mantiene Dalamud, sin poseerlo ni liberarlo. El corpus, la versión recomendada y los hitos van
embebidos en la DLL.

### Hilos

Descompresión del corpus, lectura EXD, parcheo, hash, escritura y verificación se ejecutan fuera
del hilo del juego. Las llamadas IPC a Penumbra se serializan en el hilo de framework, salvo
durante la carga de arranque, en la que Dalamud aún retiene el juego. La primera lectura de la
biblioteca de mods también se hace fuera de ese hilo.

### Penumbra

El plugin consume la API pública IPC de Penumbra por nombre y firma. Exige versión breaking `5` y
feature `15` o superior; con cualquier otra cosa carga en modo degradado y explica qué falta. Se
suscribe a la inicialización, descarga y cambios de directorio de Penumbra para reconectar sin
recargar.

Identidad del mod, compartida con el patcher:

- directorio canónico `FFXIVSpanish`, nombre `FFXIV en Español`;
- marcador `ffxivspanish.identity.json` dentro del árbol y tag `ffxiv-spanish-managed`;
- los paquetes v0.5 sin marcador se reconocen por la firma estructural exacta de su árbol;
- v0.4 y anteriores no se adoptan.

### Instalación

`ModInstaller` copia el candidato verificado a un staging en el mismo volumen que el directorio de
mods, aparta el árbol anterior como copia de seguridad, promueve el nuevo por movimiento y avisa a
Penumbra (`AddMod` o `ReloadMod`). El árbol anterior solo se limpia tras el éxito; ante cualquier
fallo se restaura. Nunca se llama a `DeleteMod` y descargar el plugin no borra nada.

Deuda conocida: no hay recuperación automática tras un cierre brusco del proceso entre apartar el
árbol anterior y completar la promoción.

### Categorías y activación

Solo se administra la colección Base/Default, que es la que Penumbra usa para resolver EXD. El
estado se lee siempre de Penumbra; el plugin escribe con `TrySetModSettings` y reconcilia después.
Un cambio hecho en Penumbra (`ModSettingChanged`) se adopta y se persiste, de modo que ambos lados
muestran lo mismo. Un ajuste heredado no se convierte en propio sin confirmación.

### Cuándo se regenera

`BuildFingerprint` guarda las entradas de la última instalación correcta: versión del plugin,
SHA-256 del corpus, versión de FFXIV recomendada e instalada, esquema de categorías, formato del
paquete e identidad del mod. Al cargar se compara con las entradas actuales:

- iguales: no se hace ningún trabajo;
- distintas y regeneración automática activa: se regenera durante `LoadAsync`, antes de que Dalamud
  deje continuar al juego;
- distintas y automática desactivada: se avisa y se espera al usuario.

Cambiar categorías no altera el fingerprint. Una regeneración cancelada o fallida pausa la
automática hasta que el usuario actúe.

Si la versión instalada de FFXIV difiere de la recomendada, el plugin aplica *best effort* sin
pedir confirmación y lo refleja en el estado. Los fallos estructurales y de verificación siguen
siendo fatales.

### Carga temprana y reinicio

El manifiesto declara `LoadSync` y `LoadRequiredState: 2`. Con el ajuste de Dalamud
`IsResumeGameAfterPluginLoad` activo, el juego no continúa hasta que Penumbra y el plugin terminan.
Sin él, el plugin no instala ni edita: ofrece activarlo y exige reiniciar.

El juego solo lee los EXD al arrancar, así que cualquier cambio efectivo (archivos regenerados,
activación o categorías distintas de las del arranque) deja el estado en *reinicio necesario*.
Ese estado se guarda en memoria del proceso del juego, no del plugin: recargar el plugin ni inventa
un reinicio pendiente ni olvida uno real.

### Aviso de versión nueva

Dalamud no indica a un plugin si tiene una actualización pendiente, y el usuario puede tener las
actualizaciones desactivadas o ignorarlas. Un plugin antiguo sigue funcionando, pero instala
traducciones antiguas. Por eso, una vez por carga, el plugin descarga el mismo
`dalamud/pluginmaster.json` del que Dalamud lo instala y compara su `AssemblyVersion` con la del
ensamblado en ejecución.

- La consulta corre en paralelo a la carga y nunca la bloquea: Dalamud retiene el juego hasta que
  `LoadAsync` termina.
- Solo avisa de una versión estrictamente mayor; no ofrece retroceder tras un rollback.
- Cualquier fallo de red o de formato se ignora en silencio.
- No se hace en compilaciones de desarrollo: plugin cargado como dev plugin o versión `0.0.0`.
- Es la única conexión de red del plugin y no envía ningún dato del usuario.

El resultado se muestra como notificación en el primer tick de framework, cuando el juego ya
dibuja, y como aviso fijo en la ventana.

### Empaquetado y distribución

DalamudPackager genera el manifiesto desde el `.csproj` y un ZIP con los archivos de primer nivel
de la salida, sin los ensamblados que aporta Dalamud (`Dalamud`, `Lumina`, `Lumina.Excel`). La
release adjunta ese ZIP y después escribe `dalamud/pluginmaster.json` en la rama principal,
apuntando al asset versionado.

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

El plugin usa `Dalamud.NET.Sdk` y compila contra los ensamblados de Dalamud que encuentra en
`DALAMUD_HOME` o en la instalación local de XIVLauncher. Son solo referencia de compilación; CI los
descarga de la distribución oficial y nunca se redistribuyen.

RIDs de release:

- `win-x64`: self-contained single-file con `translations.dat` Zstandard embebido.
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
- native publish smoke para los tres RIDs;
- plugin: máquina de estados, fingerprint, contratos IPC de Penumbra con fakes, instalador sobre
  filesystem sintético con fallos inyectados, sincronización de categorías y contenido del ZIP.

No se aceptan fixtures reales de FFXIV.

## Riesgos residuales

1. CI no sustituye una prueba manual con instalación real y Penumbra.
2. Un paquete parcial puede traducir menos contenido; la interfaz debe mantener visibles los avisos.
3. Los cambios de estructura EXD pueden requerir soporte nuevo aunque el *best effort* conserve el
   resto.
4. macOS usa firma ad-hoc, no notarización pública.
5. Las herramientas de terceros pueden mover sus rutas/configuración; esa integración debe seguir
   siendo acotada y silenciosa ante fallos.
6. El plugin depende de la API IPC de Penumbra y del nivel de API de Dalamud. Un cambio incompatible
   en cualquiera de los dos lo deja en modo degradado hasta publicar una versión nueva.
7. La ventana del plugin no tiene pruebas automáticas de dibujo; su validación es manual.

## Documentos relacionados

- `PLUGIN.md`: guía de uso y soporte del plugin.
- `COMPATIBILITY.md`: contrato de versiones y fallos.
- `TRANSLATION_MILESTONES.md`: edición del historial Markdown.
- `RELEASE_CHECKLIST.md`: cierre reproducible.
- `RELEASE_SIGNING.md`: firma y verificación.
