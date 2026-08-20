# Plan de implementación v0.6.0 — plugin Dalamud

> Comenzar únicamente desde v0.5.0 aprobada. El MVP descrito aquí es completo; no desplazar
> silenciosamente requisitos a versiones posteriores.

## 1. Objetivo de producto

Añadir **FFXIV Spanish Translation Manager** al mismo repositorio. El plugin:

- contiene el mismo corpus que el patcher;
- genera un mod Penumbra v4 usando el motor compartido;
- instala/adopta/actualiza ese mod sin tocar archivos originales;
- sincroniza categorías con Penumbra;
- regenera solo cuando cambia juego, plugin o corpus;
- muestra estado, progreso editorial, hitos, ajustes, diagnóstico y legal en castellano;
- se publica en un repo custom de Dalamud con el mismo tag/release que el patcher;
- nunca borra el mod durante unload, actualización o desinstalación.

## 2. Límites no negociables

- No escribir DAT ni archivos de FFXIV.
- No ejecutar IO/parcheo/compresión en framework/render thread.
- No copiar código de Penumbra o Heliosphere.
- No cargar otra copia privada de Dalamud/Lumina sin una prueba y decisión expresa.
- No actualizar el mod sin staging, verificación y rollback.
- No interpretar `DisposeAsync` como desinstalación.
- No esconder fallos solo en logs; UI y notificación deben conservar una ruta de recuperación.
- No usar un timer periódico.
- No crear un segundo sistema de categorías distinto al de v0.5.0.
- No publicar pluginmaster si el ZIP de esa versión no existe y no ha pasado validación.

## 3. Estructura propuesta

```text
src/
  FFXIVSpanishPatcher.App/                 Avalonia existente
  FFXIVSpanishPatcher.Pipeline/            motor y árbol v4 compartidos
  FFXIVSpanishPatcher.Plugin/              entrypoint Dalamud, UI, servicios
tests/
  FFXIVSpanishPatcher.Tests/               motor/packaging
  FFXIVSpanishPatcher.App.Tests/           Avalonia
  FFXIVSpanishPatcher.Plugin.Tests/         estado, IPC, instalación, ViewModels puros
dalamud/
  pluginmaster.json                         repo custom publicado
  legal-and-credits.md                      contenido curado embebido
  README.md                                 URL e instrucciones del repo custom
docs/
  PLUGIN.md                                 guía permanente de usuario/soporte
```

Proyecto plugin:

- SDK `Dalamud.NET.Sdk/15.0.0`;
- TFM aportado por SDK: `net10.0-windows`;
- `Penumbra.Api/5.17.0` con lockfile;
- referencias a Pipeline/Core/GameData/Packaging solo después del spike Lumina;
- `translations.dat`, versión recomendada, hitos, legal, icono y fuentes como recursos embebidos;
- `InternalName`: `FFXIVSpanishTranslationManager` antes de la primera publicación.

## 4. Arquitectura runtime

### 4.1 Componentes

```text
Plugin : IAsyncDalamudPlugin
  |- PluginConfiguration
  |- PluginWindowSystem
  |- TranslationManagerController
  |- BuildFingerprintService
  |- DalamudPatchBackendFactory
  |- PenumbraIpcClient
  |- PenumbraModIdentityService
  |- ModInstaller
  |- CategorySyncService
  |- NotificationService
  `- DiagnosticLog
```

### 4.2 Estado explícito

El controlador usa estados mutuamente exclusivos:

```text
Starting
PenumbraUnavailable
WelcomeRequired
Checking
UpToDate
Generating
AwaitingCompatibilityConsent
Installing
Reloading
Ready
ReadyWithOmissions
Cancelled
ErrorRecoverable
ErrorFatal
Disposed
```

Toda transición se prueba. Solo una operación de generación/instalación puede existir a la vez.

### 4.3 Fingerprint de cambio real

Persistir tras una instalación correcta:

```text
schemaVersion
pluginAssemblyVersion
corpusSha256
recommendedGameVersion
installedGameVersion
categorySchemaVersion
packageFormatVersion
modIdentity
successfulPayloadId
completedAtUtc
```

Regenerar cuando falte estado, cambie cualquiera de esos campos o el árbol instalado no pase una
verificación ligera. Un cambio de settings de categorías no regenera EXD: solo usa IPC.

No usar timestamps de archivo como verdad primaria.

### 4.4 Hilos

- `LoadAsync`/comandos/UI disparan una operación serializada.
- Corpus y pipeline: `Task.Run`/worker dedicado con `CancellationToken`.
- Estado observable: snapshots inmutables y cola concurrente de eventos.
- UI ImGui: solo lee snapshot y drena eventos durante draw.
- IPC Penumbra: wrappers invocados mediante `IFramework.Run`/`RunOnFrameworkThread` según contrato,
  sin esperar síncronamente desde el mismo callback.
- `DisposeAsync`: cancela CTS, espera worker con límite, revierte transacción incompleta y dispone
  eventos, IPC, WindowSystem, fuentes, texturas y comandos.

## 5. Contrato Penumbra

### 5.1 Disponibilidad

Al iniciar y ante eventos:

1. `ApiVersion`: breaking debe ser exactamente compatible con 5; feature debe cubrir wrappers usados.
2. `GetEnabledState`: Penumbra debe estar habilitado.
3. `GetModDirectory`: no vacío y directorio válido.
4. suscribir `Launching`, `Initialized`, `Disposed`, `EnabledChange` y `ModDirectoryChanged`.

`Initialized` recrea/revalida subscribers y reanuda el check; `Disposed` entra en degradación sin
descargar el plugin.

### 5.2 Identidad y adopción

Identidad fuerte propuesta:

- nombre meta exacto `FFXIV en Español`;
- Website `https://ffxivspanish.carrd.co/`;
- tag de mod `ffxiv-spanish-managed` nuevo;
- directorio canónico `FFXIVSpanish` para instalaciones nuevas.

Adopción:

1. consultar `GetModList`;
2. revisar candidatos por nombre;
3. resolver ruta bajo mod root y leer `meta.json` de forma segura;
4. exigir combinación de nombre + Website o marcador gestionado;
5. si hay un candidato inequívoco, conservar su directory id/path y settings;
6. si hay varios, pedir selección; nunca borrar o fusionar automáticamente;
7. si es v3, dejar que la nueva escritura v4 lo sustituya tras backup/rollback interno.

No seguir symlinks fuera del mod root ni aceptar path traversal.

### 5.3 Instalación y actualización

Primera instalación:

1. generar árbol en staging del mismo volumen que mod root;
2. verificar completamente;
3. mover a nombre canónico libre;
4. llamar `AddMod` en framework thread;
5. activar mod en la colección Base/Default;
6. aplicar categorías iniciales;
7. persistir fingerprint.

Actualización/adopción:

1. conservar `meta.json` y payload activo anteriores;
2. generar payload con ID de fingerprint;
3. verificar árbol candidato completo;
4. reemplazar atómicamente `meta.json` para apuntar al candidato;
5. llamar `ReloadMod`;
6. comprobar settings y disponibilidad;
7. si todo pasa, borrar payload anterior best effort;
8. si falla, restaurar `meta.json`, llamar `ReloadMod` de rollback y conservar ambos payloads para
   diagnóstico si la limpieza no es segura.

No usar `DeleteMod` durante una actualización normal. No importar un `.pmp` en cada regeneración.

### 5.4 Categorías y colecciones

El payload actual contiene únicamente EXD. Penumbra resuelve esos recursos con su colección
Base/Default, aunque el texto resultante aparezca dentro de la interfaz. Por tanto:

- obtener `ApiCollectionType.Default` y mostrar su nombre;
- leer `GetCurrentModSettings` con herencia visible;
- activar/desactivar grupo mediante `TrySetModSettings`;
- escuchar `ModSettingChanged`;
- ignorar eventos producidos por la propia escritura mediante generation token + comparación de
  snapshot, no con un boolean global frágil;
- reconciliar siempre desde Penumbra después de escribir;
- si cambian grupos/nombres tras update, ejecutar migración nominal explícita.

La UI muestra explícitamente que administra **Base/Default**. No modifica la colección Interface,
Current ni las individuales. Si una versión futura añade recursos `ui/`, esa ampliación requiere
otra decisión y pruebas de alcance.

## 6. Carga y ajuste “esperar a plugins”

Manifest recomendado:

```text
LoadSync: true
LoadRequiredState: 2
LoadPriority: 0
CanUnloadAsync: true (solo si se usa IDalamudPlugin sync)
```

Preferencia: implementar `IAsyncDalamudPlugin`; Dalamud espera `LoadAsync` y `DisposeAsync` de forma
nativa. Penumbra usa actualmente prioridad `69420`, por lo que carga antes que este plugin dentro de
la misma fase sync. El spike debe demostrar el orden real y detener la instalación si Penumbra aún
no está inicializado; no se permite continuar silenciosamente y aplicar EXD tarde.

`LoadAsync` hace inicialización y, solo cuando el fingerprint exige regeneración automática, espera
la generación y la verificación obligatoria sin ocupar el framework thread. Debe terminar antes del
timeout de host y capturar fallos recuperables para que el plugin cargue en estado degradado.

`IsResumeGameAfterPluginLoad=true` permite que XIVLauncher/Dalamud no reanude el juego hasta completar
plugins sync. El patcher externo puede seguir ofreciendo su corrección atómica. El plugin, ejecutado
dentro de Dalamud, no debe editar `dalamudConfig.json` activo: muestra instrucciones y solicita
reinicio si el ajuste está desactivado.

La primera instalación sucede necesariamente con el proceso del juego ya abierto. Se genera,
verifica, instala y activa el mod, pero se marca **reinicio obligatorio** para evitar EXD ya
cacheados. La garantía de carga anticipada comienza en el siguiente arranque con
`IsResumeGameAfterPluginLoad=true`.

Medir la duración real. Si el pipeline supera un umbral aceptable, no ocultarlo como “background”:
mostrar progreso de arranque y permitir desactivar auto-regeneración para el siguiente inicio.

## 7. UI/UX

### 7.1 Ventana

- apertura por `/ffxives`, configuración de plugin y acción de notificación;
- tamaño inicial y mínimo definidos; scroll vertical solo donde corresponda;
- navegación superior: **Inicio**, **Categorías**, **Actualizaciones**, **Ajustes**;
- paleta, tipografía, tarjetas, radios, separadores e iconografía derivados de una maqueta aprobada;
- `WindowSystem`, draw lists, style scopes balanceados, fuentes embebidas mediante atlas administrado
  e imágenes con `ITextureProvider`;
- nada de lógica de filesystem/IPC dentro de métodos Draw.

### 7.2 Inicio

Estados:

- bienvenida con **Instalar traducción**;
- Penumbra ausente/deshabilitado/sin directorio;
- comprobando;
- generando con fase, progreso y resumen no bloqueante;
- listo, listo con omisiones o error;
- acciones: regenerar, abrir mod en Penumbra, copiar diagnóstico.

### 7.3 Categorías

- colección Base/Default visible;
- diez toggles con descripción y conteo real;
- cambios optimistas solo durante llamada; revertir si IPC falla;
- evento externo de Penumbra actualiza UI sin regenerar;
- estado heredado se explica y ofrece convertir en ajuste propio solo con confirmación.

### 7.4 Actualizaciones

- versión de plugin/corpus/juego;
- estado del fingerprint y última generación;
- hitos desde Markdown embebido;
- progreso editorial, no porcentaje inventado del blob;
- botón de comprobar/regenerar cuando proceda;
- Dalamud sigue siendo responsable de actualizar el binario del plugin.

### 7.5 Ajustes

- auto-regenerar: activado por defecto;
- best effort en mismatch: activado por defecto, con explicación;
- logging diagnóstico;
- colección administrada Base/Default, no configurable en este alcance;
- requisito “esperar a plugins” y estado detectable cuando sea posible;
- créditos/legal colapsable desde `dalamud/legal-and-credits.md`.

### 7.6 Notificaciones

Usar `INotificationManager` para:

- inicio/fin de regeneración cuando ventana cerrada;
- progreso largo actualizable;
- Penumbra ausente;
- omisiones y errores con acción para abrir ventana.

Nunca depender solo de `IToastGui`. Mantener historial visible en ventana y logs.

## 8. Etapas de implementación

### Etapa 0 — integrar v0.5.0 y cerrar decisiones

1. Partir del commit v0.5.0 aprobado, no del puntero antiguo de `v0.6.0`.
2. Confirmar las decisiones cerradas de `DECISIONES.md` y usar el nombre de trabajo aprobado; el
   nombre público definitivo queda como gate previo a la primera publicación.
3. Congelar `InternalName`, mod identity y nombres de opciones.
4. Actualizar `AGENTS.md` con estructura futura y gates.

Gate humano: aprobar contrato antes de crear instalaciones persistentes.

Commit: `docs(v0.6): freeze plugin runtime contract`.

### Etapa 1 — spike Dalamud, SDK y Lumina

Crear proyecto mínimo y prueba de carga, sin UI final ni instalación.

Debe demostrar:

- build locked con SDK 15;
- ZIP válido con DLL/manifiesto/recursos;
- ausencia de `Dalamud.dll`, Lumina y otras shared assemblies duplicadas;
- referencias Pipeline/vendor cargan sin `FileLoadException`/`MissingMethodException`;
- `IDataManager.GameData` permite leer un EXH/EXD conocido sin disponerlo;
- worker background no bloquea ticks;
- `DisposeAsync` cancela y termina.

Pruebas automáticas para backend no propietario. Smoke real Windows y Linux obligatorio.

Si falla compatibilidad Lumina, detenerse y documentar solución mínima antes de seguir.

Commit: `feat(plugin): prove Dalamud and shared engine compatibility`.

### Etapa 2 — configuración, estado y fakes

Implementar `PluginConfiguration` versionada, controller/state machine, cola de eventos, fingerprint y
abstracciones:

- `IPenumbraIpcClient`;
- `IModInstaller`;
- `IGameDataBackend`;
- `IPluginClock`/filesystem boundary si ayuda a tests.

Pruebas de todas las transiciones, exclusión mutua, cancelación y migración de config. Nada de IO real
en tests unitarios.

Commit: `feat(plugin): add state machine and build fingerprints`.

### Etapa 3 — Penumbra IPC y degradación

Implementar wrappers tipados, versión/feature gate, eventos y fake contract tests.

Casos:

- no instalado;
- deshabilitado;
- directorio vacío/inválido;
- breaking incompatible;
- feature insuficiente;
- `Initialized` tras carga tardía;
- `Disposed` y reconexión;
- excepción de cada wrapper convertida en resultado tipado/log.

Todas las suscripciones se disponen. Ningún catch vacío sin log diagnóstico y estado de usuario.

Smoke real: cargar plugin antes y después de Penumbra, desactivar/reactivar Penumbra.

Commit: `feat(plugin): integrate Penumbra API v5`.

### Etapa 4 — identidad, adopción e instalador transaccional

Implementar detector, staging/payload, promoción, rollback y fingerprint persistente.

Pruebas de filesystem sintético:

- primera instalación;
- update correcto;
- adopción v3/v4;
- dos candidatos ambiguos;
- path traversal/symlink;
- `AddMod`/`ReloadMod` fallan;
- caída simulada en cada paso;
- rollback conserva versión anterior;
- dispose durante generación/instalación;
- nunca se llama delete por unload.

Gate humano: instalar/adoptar/reiniciar en Windows y Linux; conservar settings y mod tras descargar
plugin.

Commit: `feat(plugin): install and adopt Penumbra mod safely`.

### Etapa 5 — sincronización bidireccional

Implementar colección elegida, lectura/escritura de grupo, herencia, anti-loop y reconciliación.

Pruebas:

- cambio desde plugin llega a Penumbra;
- cambio desde Penumbra llega al plugin;
- cambio propio no forma bucle;
- evento duplicado/out-of-order;
- cambio de colección;
- grupo/opción ausente;
- herencia;
- IPC falla y UI revierte;
- reload conserva settings.

Smoke real en Base/Default y prueba negativa que confirme que una colección individual no altera el
estado administrado.

Commit: `feat(plugin): synchronize category settings with Penumbra`.

### Etapa 6 — auto-regeneración y compatibilidad

Conectar corpus, versión recomendada, versión instalada y fingerprint.

Casos:

- primera instalación manual: instala y exige reinicio, sin declarar la sesión actual como lista;
- arranque sin cambios: cero regeneración;
- cambio de plugin/corpus/juego: una regeneración;
- mismatch best effort;
- omisiones visibles;
- structural failure siempre bloqueado;
- fallo de verificación siempre bloqueado, sin consentimiento ni override;
- auto desactivado;
- cambio de categorías no regenera;
- operación cancelada por unload/update.

Medir tiempo, memoria y ausencia de frame stalls. Registrar cifras de Windows/Linux, sin fijarlas como
garantía universal.

Commit: `feat(plugin): regenerate translations on real changes`.

### Etapa 7 — UI funcional

Primero implementar flujos y accesibilidad con componentes simples. Añadir pruebas del modelo de UI,
comando, apertura/cierre, acciones y estados. Crear una maqueta/prototipo visible y obtener aprobación
antes del pulido.

No mezclar aún release CI.

Commit: `feat(plugin): add translation manager workflows`.

### Etapa 8 — fidelidad visual, recursos y notificaciones

Aplicar diseño aprobado con fuentes, iconos, draw lists y notificaciones. Crear `icon.png` cuadrado y
capturas del installer.

Validación humana:

- 100 %, 125 %, 150 %, 200 %;
- resoluciones y tamaños mínimos;
- temas/contraste;
- navegación por teclado donde Dalamud lo permita;
- estado largo, errores y textos castellanos;
- Windows y XIVLauncher.Core.

No aceptar “funciona” como equivalencia de “se parece a la maqueta”.

Commit: `feat(plugin): match approved Dalamud visual design`.

### Etapa 9 — CI, release y pluginmaster

CI de PR/main:

1. job actual sin regresiones;
2. job Windows instala SDK .NET fijado y distribución dev de Dalamud;
3. restore locked del plugin;
4. build/test Release;
5. inspección ZIP y manifest;
6. prueba de recursos embebidos y tamaño razonable;
7. artifact efímero.

Release por tag:

1. reutilizar `validate-tag`;
2. construir patcher y plugin desde el mismo commit/version;
3. asset `FFXIVSpanishTranslationManager-{version}.zip` + SHA-256;
4. comprobar que assembly, manifest, meta del mod y tag coinciden;
5. adjuntar a la misma GitHub Release;
6. solo después, actualizar `dalamud/pluginmaster.json` en `main`;
7. enlaces install/update apuntan al asset versionado, no a `latest` mutable;
8. commit automático contiene solo pluginmaster y mensaje `[skip ci]`;
9. si branch protection impide push, fallar claramente; configurar bypass o token dedicado mediante
   decisión de repositorio, nunca desactivar protección silenciosamente.

Prueba de rollback: restaurar pluginmaster a asset anterior sin eliminar releases.

Commit: `ci(release): publish patcher and Dalamud plugin together`.

### Etapa 10 — documentación y release candidata

Actualizar:

- `README.md` breve con dos métodos: plugin recomendado y patcher clásico;
- `docs/PLUGIN.md` completa;
- `CHANGELOG.md` v0.6.0;
- `docs/DESIGN.md`, `COMPATIBILITY.md`, `RELEASE_CHECKLIST.md`;
- `CONTRIBUTING.md`, `AI_USAGE.md`, `NOTICE.md` según dependencias y política;
- `AGENTS.md` sin snapshots falsos;
- `dalamud/legal-and-credits.md`.

Prueba humana completa:

1. repo custom nuevo;
2. instalación desde `/xlplugins`;
3. bienvenida e instalación;
4. reinicio sin regeneración;
5. categorías ambos sentidos;
6. actualización de plugin y regeneración única;
7. mismatch de juego;
8. Penumbra descargado/reactivado;
9. adopción de mod del patcher;
10. descarga del plugin conserva mod;
11. Windows y Linux;
12. assets de GitHub y contador de descargas visibles.

Gate humano: aprobación expresa de funcionalidad, visual y convivencia.

## 9. Matriz mínima de pruebas

| Área | Unitarias | Integración sintética | Cliente real |
| --- | --- | --- | --- |
| Fingerprint | sí | filesystem | update real |
| Pipeline | suite existente | EXD sintético | FFXIV instalado |
| Árbol v4 | modelos/verificador | directorio/ZIP | Penumbra import/reload |
| IPC | fake por wrapper | secuencias de eventos | Penumbra actual |
| Instalación | plan transaccional | fallos inyectados | mod root real |
| Categorías | reducer/anti-loop | fake Base/Default | dos sentidos en Base/Default |
| Arranque | state machine | orden sync/prioridades | espera global + reinicio inicial |
| Lifecycle | cancel/dispose | worker bloqueado | disable/update/uninstall |
| UI | estados/acciones | draw smoke si viable | escalado/capturas |
| Release | scripts JSON | artifact inspection | repo custom install |

## 10. Observabilidad y soporte

- Ring buffer de eventos en memoria con límite explícito y exportación a texto.
- Log de Dalamud con correlación por operation ID.
- Estado final conserva fase, error corto y excepción técnica separada.
- Nunca registrar paths completos o datos personales en telemetría; no se añade telemetría en este
  plan.
- Diagnóstico copiable: versiones, API Penumbra, fingerprint sin hashes completos si no aportan,
  colección, outcome y estadísticas.

## 11. Criterio de terminado v0.6.0

v0.6.0 solo está terminada cuando:

1. v0.5.0 categorizada está publicada y adoptable;
2. plugin compila/carga con Dalamud 15 y Penumbra API compatible;
3. primera instalación, update, rollback y adopción están probados;
4. sincronización bidireccional funciona en Base/Default y no toca otras colecciones;
5. auto-regeneración solo ocurre por fingerprint real;
6. dispose/update/uninstall no borran mod ni dejan transacción corrupta;
7. UI funcional y visual ha sido aprobada en Windows/Linux;
8. suite completa y nuevos tests pasan sin avisos;
9. un tag produce patcher + plugin + checksums en una release;
10. pluginmaster instala exactamente ese asset;
11. tras la primera instalación se exige reinicio y, en arranques posteriores, Penumbra y el plugin
    terminan antes de que Dalamud reanude el juego;
12. docs permanentes coinciden con producto;
13. el usuario aprueba expresamente la release.

Solo después de cerrar v0.6.0 se puede decidir si esta carpeta temporal se elimina. La eliminación
requiere aprobación explícita y debe quedar en un commit separado de cierre.
