# Auditoría técnica para v0.5.0 y v0.6.0

## 1. Resumen ejecutivo

El repositorio ya contiene casi todo el motor necesario para un plugin Dalamud: corpus embebido,
parcheo EXD, SeString gate, compatibilidad best effort, estadísticas, staging, verificación y
promoción atómica. No conviene reescribir ese motor.

Las brechas reales son:

1. el empaquetador actual solo emite un mod plano en formato Penumbra v3;
2. `PatchPipeline.Run` mezcla generación del árbol y compresión `.pmp`;
3. el filtrado de categorías excluye datos, mientras el nuevo contrato exige paquete completo;
4. el backend de producción abre y posee su propia instancia de Lumina;
5. la lógica compartible de categorías y recursos vive parcialmente en la App Avalonia;
6. no existe proyecto Dalamud, adaptador IPC, configuración de plugin ni release de plugin;
7. documentación e instrucciones tienen deriva comprobada.

Conclusión: v0.5.0 debe crear primero un modelo de mod categorizado, verificable e independiente del
archivo `.pmp`. v0.6.0 puede reutilizarlo desde Dalamud y limitarse a orquestación, UI, IPC y ciclo de
vida.

## 2. Estado del repositorio

### 2.1 Rama y documentación

- `v0.5.0`, `v0.6.0`, `v0.4.1` y `main` apuntaban a `1cfbac6` al iniciar la auditoría.
- Existe tag `v0.4.0`; todavía no existen tags v0.5.0/v0.6.0.
- Usar ramas llamadas `v0.5.0` y `v0.6.0` junto a tags homónimos crea referencias ambiguas para
  comandos humanos. Antes de publicar, se recomienda renombrarlas a `feature/v0.5.0` y
  `feature/v0.6.0` o usar siempre `refs/heads/...` y `refs/tags/...` explícitos.
- `docs/evolucion-v0.3.0/` fue eliminada deliberadamente en `96ad447`, pero `AGENTS.md` todavía dice
  que existe y que no debe borrarse.
- `AGENTS.md` incluye `@RTK.md`; `RTK.md` no existe.
- `AGENTS.md` conserva snapshots antiguos: SDK `10.0.302`, blob de unos 16,9 MiB, versión de juego
  `2026.07.16.0001.0000` y tags hasta v0.2.5. La realidad es SDK `10.0.400`, blob de 22.807.014
  bytes, versión `2026.08.11.0000.0000` y tag v0.4.0.
- No existe `Directory.Packages.props`; cada `.csproj` declara versiones directamente.

### 2.2 Baseline automático

Ejecutado el 20 de agosto de 2026:

```text
dotnet restore --locked-mode                              OK
dotnet build -c Release --no-restore                     OK, 0 warnings, 0 errors
dotnet test -c Release --no-build                        OK
FFXIVSpanishPatcher.Tests                                217 passed
FFXIVSpanishPatcher.App.Tests                             28 passed
Total                                                    245 passed
```

No se verificaron publishes nativos ni funcionamiento dentro de FFXIV.

## 3. Pipeline

### 3.1 Contrato y flujo

`PatchPipeline.Run(PatchRequest, IProgress<PipelineEvent>?, CancellationToken)` es síncrono
(`src/FFXIVSpanishPatcher.Pipeline/PatchPipeline.cs:36`). Ejecuta:

1. carga completa del corpus mediante `ITranslationSource.Load()` (`:56-70`);
2. selección de categorías y estados packageable (`:71-77`);
3. validación SeString, omitiendo inseguras salvo `ForceSeString` (`:105-134`);
4. apertura del backend real o snapshot (`:138-155`);
5. resolución y agrupación por página EXD (`:159-223`);
6. catálogo de broadcast para duplicados (`:225-229`);
7. lectura, aliases, broadcast y parcheo de cada página (`:231-462`);
8. contamination guard, estricto o best effort (`:480-500`);
9. `.pmp` temporal, verificación y promoción atómica (`:502-548`);
10. limpieza best effort de temporales en `finally` (`:556-564`).

La cancelación se consulta entre fases, entradas y páginas. No interrumpe una descompresión, una
operación individual de Lumina, `ExdPatcher.Patch`, la compresión ZIP ni la verificación ya iniciada.

### 3.2 Petición y resultado

`PatchRequest` contiene ruta del juego, categorías, estados, salida, staging, snapshot opcional,
umbral de match, `ForceSeString`, modo de compatibilidad, debug y metadatos
(`src/FFXIVSpanishPatcher.Pipeline/PatchRequest.cs:23-77`).

`PatchStatistics` ya separa candidatas, escrituras, misses, hojas/páginas ausentes, filas fuera de
versión, SeString, páginas no soportadas, parcheadas y omitidas
(`src/FFXIVSpanishPatcher.Pipeline/PatchResult.cs:31-65`). Los resultados utilizables son `Ok` y
`PackagedWithMisses` (`:67-82`).

`PipelineEvent` expone componente, mensaje, nivel y un contador opcional, pero no fase, total ni
porcentaje (`src/FFXIVSpanishPatcher.Pipeline/PipelineEvent.cs:30-34`). El plugin necesitará un
snapshot de progreso adicional, sin retirar los eventos de consola existentes.

### 3.3 Acoplamientos

- `ClientPatchBackendFactory.Open` siempre llama `GameLocator.Open`, incluso si se suministra
  `BaseExdDir` (`src/FFXIVSpanishPatcher.Pipeline/ClientPatchBackend.cs:12-26`).
- `ClientPatchBackend.Dispose` dispone el objeto de datos recibido (`:29-60`). Esto impide entregarle
  directamente `IDataManager.GameData`, propiedad de Dalamud.
- `EmbeddedTranslationSource.Load` descomprime todo el JSONL a una lista en memoria y no acepta
  cancelación (`EmbeddedTranslationSource.cs:26-49`).
- No hay estado global mutable, `Console`, `Environment.Exit` ni dependencia de Avalonia en el
  pipeline.
- Las rutas por defecto son relativas (`artifacts/...`) y no sirven dentro del proceso del juego; el
  plugin debe proporcionar rutas absolutas bajo su configuración o el root de Penumbra.

### 3.4 Riesgo Lumina

El repo compila `XivSpanish.GameData` con Lumina `7.6.1` y Lumina.Excel `7.5.0`
(`vendor/XivSpanish.GameData/XivSpanish.GameData.csproj:8-11`). Dalamud 15 comparte sus propias
asambleas Lumina con plugins y, en el snapshot auditado, declara Lumina `7.6.0` y registra Lumina y
Lumina.Excel como shared assemblies.

No debe darse por probada la carga del ProjectReference actual dentro de Dalamud. La primera etapa
v0.6.0 incluye un spike obligatorio: compilar, inspeccionar el ZIP para confirmar que no lleva copias
conflictivas y cargar el plugin en un cliente real. Si hay incompatibilidad binaria, se alinean las
versiones o se introduce un pequeño boundary compatible; no se duplica Lumina en el plugin.

## 4. Datos de juego y código vendorizado

### 4.1 Core

`vendor/XivSpanish.Core` aporta modelos y JSONL sin dependencias NuGet. `TranslationEntry` y
`TranslationSourceKey` conservan source, target, status y la identidad sheet/row/field/exdPath. El
pipeline considera packageable `approved`/`gold`, target no vacío y source key útil.

No hay UI ni estado global. Es reutilizable desde plugin.

### 4.2 GameData

`GameLocator.Open` resuelve `sqpack` desde ruta explícita o `launcherConfigV3.json` y crea
`new Lumina.GameData(sqpack)` (`vendor/XivSpanish.GameData/GameLocator.cs:15-23`). No configura
explícitamente `FileShare`; ese comportamiento queda delegado a Lumina.

`ClientExdSource` usa `ExdResolver` para bytes, layout y nombres de campos
(`vendor/XivSpanish.Packaging/BaseExdSource.cs:31-45`). `DirectoryExdSource` permite snapshots
sintéticos/reproducibles (`:47-104`). `ExdPatcher`, el lector de filas y el árbol/tokenizador
SeString son código puro sobre buffers y se pueden reutilizar.

Dentro del plugin, `IDataManager.GameData` ya ofrece la instancia Lumina del proceso y `IDataManager`
expone ficheros y sheets. Usarla evita abrir un segundo juego de índices y reduce el riesgo de locks,
pero exige un backend no propietario y una prueba de concurrencia real Windows/Linux.

### 4.3 Packaging

`XivSpanish.Packaging` contiene primitives de broadcast, aliases, contamination y SeString, pero el
escritor concreto vive en Pipeline.

La implementación v0.5 sustituye el escritor v3 por `PenumbraModTreeWriter` y `PmpArchiveWriter`:

- escribe cada EXD bajo `files/categories/{orden}-{dominio}/...`;
- genera `meta.json` `FileVersion: 4` con un grupo `Multi` y diez opciones estables;
- no crea `default_mod.json` ni `group_*.json`;
- calcula `DefaultSettings` desde la selección de la interfaz sin recortar el contenido;
- verifica primero el árbol con `ModTreeVerifier` y luego reabre el ZIP con `IntegrityVerifier`.

Ambos verificadores exigen manifiesto v4, rutas seguras, redirects presentes y únicos, cabecera
`EXDF`, sin payload huérfano ni enlaces simbólicos. Fallos estructurales conservan la salida previa.

## 5. App Avalonia

### 5.1 Lógica reutilizable

- `CategoryCatalog`: diez dominios, etiquetas, tooltips y orden
  (`src/FFXIVSpanishPatcher.App/Services/CategoryCatalog.cs:14-38`).
- `AppBuildInfo`: versión y URLs desde metadata de assembly
  (`Services/AppBuildInfo.cs:5-59`).
- `GitHubReleaseUpdateCheckService`: latest release con timeout de 3 s y comparación semver simple
  (`Services/UpdateCheckService.cs:55-136`).
- `TranslationMilestoneService`: carga/renderiza el Markdown embebido.
- lectura de `recommended-game-version.txt` y comparación exacta en `MainViewModel`.

Estas piezas no deben referenciarse desde el plugin a través de la App. El catálogo mínimo, lectura de
recursos y fingerprint de versiones se extraen a una capa compartida; el renderizado Avalonia y el
update check del ejecutable permanecen en App.

### 5.2 Flujo actual

`MainViewModel` carga el corpus con `Task.Run` (`MainViewModel.cs:299`), construye `PatchRequest`
(`:447-456`) y ejecuta el pipeline con `Task.Run` (`:458-462`). Ese patrón confirma que el motor es
bloqueante y ya se aísla del dispatcher.

La UI compara versión instalada/recomendada y exige confirmación para best effort (`:408-421`). El
plugin puede reutilizar el contrato, no los modales Avalonia.

### 5.3 Dalamud/Penumbra existente

`DalamudPenumbraService`:

- busca raíces conocidas de XIVLauncher/XIVLauncher.Core/XIV on Mac (`:137-159`);
- exige manifiesto real de Penumbra (`:167-207`);
- lee `IsResumeGameAfterPluginLoad` en `dalamudConfig.json` (`:24-56`);
- si el usuario acepta, escribe solo esa propiedad mediante temporal, `WriteThrough`, hash de
  concurrencia, `File.Replace` y relectura (`:64-128`).

Esto es correcto para el patcher externo. El plugin cargado dentro de Dalamud no debe reescribir el
archivo de configuración activo; debe mostrar el estado/recomendación y dejar la corrección externa
al patcher o al usuario.

## 6. CI, release y activos

### 6.1 CI actual

`.github/workflows/ci.yml` restaura locked, compila y ejecuta toda la suite en Ubuntu (`:9-31`).
Después publica y mantiene vivos durante 10 segundos los binarios Windows, Linux y macOS en runners
nativos (`:33-127`).

La rama del plugin necesitará un job Windows con el SDK de Dalamud descargado, build locked, pruebas
sin cliente y validación del ZIP. Las pruebas IPC usan fakes; el cliente real queda como gate humano.

### 6.2 Release actual

`.github/workflows/release.yml`:

- valida tags `vX.Y.Z` con componentes 0..999 (`:15-32`);
- publica tres RIDs (`:34-77`);
- empaqueta/firma macOS y adjunta Linux/macOS (`:90-133`);
- firma Windows en Ubuntu cuando existen secretos (`:176-255`);
- reutiliza los ZIP para Nexus.

El comentario inicial dice que Windows externaliza el corpus, pero la matriz release actual usa
`external_translations: false` en los tres RIDs (`:43-48`): comentario obsoleto.

El workflow ya admite que varios jobs adjunten assets a la misma release. Puede añadirse un job de
plugin dependiente de `validate-tag`, seguido de otro job que actualice `pluginmaster.json` solo si
todos los assets y checksums existen.

### 6.3 Activos

Hay `.ico`, `.icns`, logos e iconos de expansiones, pero no un `icon.png` cuadrado dedicado al
installer de Dalamud. Debe crearse/derivarse y revisarse visualmente antes de publicar el repo custom.

## 7. Investigación externa verificada

Snapshot consultado el 20 de agosto de 2026:

| Fuente | Commit/versión | Hallazgo |
| --- | --- | --- |
| [SamplePlugin csproj](https://github.com/goatcorp/SamplePlugin/blob/b8477daaa678a5dd72cdcd4a32bc249d3c23a598/SamplePlugin/SamplePlugin.csproj) | `b8477da` | `Dalamud.NET.Sdk/15.0.0`; metadata en csproj |
| [Dalamud SDK props](https://github.com/goatcorp/Dalamud.NET.Sdk/blob/18377d560976f9b200094b19441710486537433d/Dalamud.NET.Sdk/Sdk/Sdk.props) | `15.0.0` | TFM `net10.0-windows`, C# 14 |
| [Dalamud IDalamudPlugin](https://github.com/goatcorp/Dalamud/blob/83042016d0e9996dc44c9f7fd96a8d33a5e586f2/Dalamud/Plugin/IDalamudPlugin.cs) | `8304201` | contrato sync = `IDisposable` |
| [Dalamud IAsyncDalamudPlugin](https://github.com/goatcorp/Dalamud/blob/83042016d0e9996dc44c9f7fd96a8d33a5e586f2/Dalamud/Plugin/IAsyncDalamudPlugin.cs) | `8304201` | `LoadAsync` y `DisposeAsync` esperados por host |
| [Dalamud IDataManager](https://github.com/goatcorp/Dalamud/blob/83042016d0e9996dc44c9f7fd96a8d33a5e586f2/Dalamud/Plugin/Services/IDataManager.cs) | `8304201` | expone `Lumina.GameData`, sheets y files |
| [Penumbra.Api csproj](https://github.com/Ottermandias/Penumbra.Api/blob/f6c1e61f0ef354e044fb3048cd474232a8b3e530/Penumbra.Api.csproj) | `5.17.0` | SDK 15, NuGet MIT |
| [Penumbra API version](https://github.com/xivdev/Penumbra/blob/fcc86e0e04cc37eb03eb3d8bed4332b58d0236c1/Penumbra/Api/Api/PenumbraApi.cs) | `5.17` | breaking 5, feature 17 |
| [Penumbra IPC mods](https://github.com/Ottermandias/Penumbra.Api/blob/f6c1e61f0ef354e044fb3048cd474232a8b3e530/IpcSubscribers/Mods.cs) | `f6c1e61` | `InstallMod`, `AddMod`, `ReloadMod`, eventos |
| [Penumbra IPC settings](https://github.com/Ottermandias/Penumbra.Api/blob/f6c1e61f0ef354e044fb3048cd474232a8b3e530/IpcSubscribers/ModSettings.cs) | `f6c1e61` | settings por GUID y `ModSettingChanged` |
| [Penumbra schema v4](https://github.com/xivdev/Penumbra/blob/fcc86e0e04cc37eb03eb3d8bed4332b58d0236c1/schemas/mod_meta-v4.json) | `fcc86e0` | `DefaultData`, `Groups`, `PageNames` en meta |
| [Penumbra PathResolver](https://github.com/xivdev/Penumbra/blob/fcc86e0e04cc37eb03eb3d8bed4332b58d0236c1/Penumbra/Interop/PathResolving/PathResolver.cs) | `fcc86e0` | EXD usa siempre Base/Default; solo `ResourceCategory.Ui` usa Interface |
| [Penumbra manifest](https://github.com/xivdev/Penumbra/blob/fcc86e0e04cc37eb03eb3d8bed4332b58d0236c1/Penumbra/Penumbra.json) | `fcc86e0` | `LoadPriority: 69420`, `LoadRequiredState: 2`, `LoadSync: true` |
| [Dalamud PluginManager](https://github.com/goatcorp/Dalamud/blob/83042016d0e9996dc44c9f7fd96a8d33a5e586f2/Dalamud/Plugin/Internal/PluginManager.cs) | `8304201` | carga sync secuencial por prioridad y espera global condicionada por `IsResumeGameAfterPluginLoad` |
| [Heliosphere Penumbra IPC](https://github.com/heliosphere-xiv/plugin/blob/2e2e1b4bbfad6c097bb47fc5cedd1cf070f128a3/PenumbraIpc.cs) | `4.8.1` | wrappers tipados, reconexión en `Initialized` |
| [Heliosphere install](https://github.com/heliosphere-xiv/plugin/blob/2e2e1b4bbfad6c097bb47fc5cedd1cf070f128a3/DownloadTask.cs) | `4.8.1` | IO en background; IPC en framework thread |

Documentación oficial relevante:

- [Publicar en repositorios custom](https://dalamud.dev/plugin-publishing/custom-repositories/)
- [Estructura y configuración de proyecto](https://dalamud.dev/plugin-development/project-layout/)
- [Metadata del plugin](https://dalamud.dev/plugin-development/plugin-metadata/)

### 7.1 Penumbra IPC necesario

Wrappers mínimos previstos:

- disponibilidad: `ApiVersion`, `GetEnabledState`, `GetModDirectory`, `Initialized`, `Disposed`,
  `ModDirectoryChanged`, `EnabledChange`;
- identidad/instalación: `GetModList`, `GetModPath`, `AddMod`, `ReloadMod`, `SetModPath`;
- colecciones: `GetCollection(ApiCollectionType.Default)`; el payload EXD no usa la colección de
  interfaz ni colecciones por personaje;
- estado: `GetAvailableModSettings`, `GetCurrentModSettings`, `TrySetMod`,
  `TrySetModSettings`, `ModSettingChanged`;
- UX: `OpenMainWindow` para abrir la ficha del mod.

`InstallMod` puede importar un `.pmp`, pero no es la vía recomendada para regeneraciones frecuentes:
añade cola/importación y dificulta una promoción controlada. `AddMod` sobre un árbol ya generado,
como hace Heliosphere, encaja mejor.

### 7.2 UI y notificaciones Dalamud

Dalamud 15 ofrece `WindowSystem`, bindings ImGui, draw lists, atlas de fuentes administrado,
`ITextureProvider` para recursos embebidos e `INotificationManager` con título, icono, duración,
estado minimizado y progreso. La fidelidad visual solicitada es viable, pero exige una maqueta y
capturas comparativas; no basta con `ImGui.Begin` + controles estándar.

`IToastGui` pertenece a los toasts del juego. Para operaciones persistentes y accionables se
recomienda `INotificationManager`; los toasts del juego se reservan para avisos muy breves y nunca
son el único canal de un error.

### 7.3 Ciclo de vida

Dalamud no expone al plugin una razón fiable que distinga desinstalación, actualización, desactivado
o unload. Por tanto, cualquier limpieza destructiva en `Dispose` sería incorrecta. El mod se conserva
siempre, tal como exige producto.

## 8. Riesgos priorizados

### P0 — deben resolverse antes de ampliar UI

1. Compatibilidad binaria Lumina/Dalamud con los ProjectReference actuales.
2. Contrato v4 exacto y verificación del árbol categorizado.
3. Propiedad de `IDataManager.GameData`: nunca disponer la instancia de Dalamud.
4. Instalación/rollback transaccional dentro del directorio de Penumbra.
5. Evitar bucles en la sincronización bidireccional de la colección Base/Default.

### P1 — antes de beta humana

1. Startup sync: medir duración y asegurar que el cliente no queda bloqueado.
2. Cancelación real al descargar/actualizar plugin.
3. Adopción sin falsos positivos ni pérdida de settings.
4. Coherencia entre versión assembly, tag, meta del mod y pluginmaster.
5. Push automático a `main` compatible con branch protection.
6. Pruebas reales Windows y XIVLauncher.Core.

### P2 — antes de release pública

1. Accesibilidad, escalado y fidelidad visual ImGui.
2. Icono PNG y capturas del installer.
3. README reducido y documentación permanente actualizada.
4. Mensajes de recuperación, logs exportables y soporte.
5. Observación de descargas y rollback de pluginmaster.

## 9. Viabilidad

Viabilidad técnica: alta. La parte difícil no es traducir EXD; ya está resuelta y probada. El trabajo
crítico es convertir el resultado en un modelo de mod categorizado y gestionar su ciclo de vida sin
bloquear ni corromper el proceso del juego.

No se recomienda comenzar por la UI del plugin. Orden seguro: formato v4 y tests, árbol compartido,
spike Dalamud/Lumina, IPC fake, instalación transaccional, sincronización, orquestación y por último
la interfaz completa.
