# Decisiones, supuestos y preguntas

## 1. Decisiones de producto cerradas

Estas decisiones proceden de la entrevista del 20 de agosto de 2026 y son vinculantes para los
planes.

### Distribución y versionado

- El plugin se publica mediante un repositorio custom de Dalamud alojado en este mismo repositorio
  GitHub.
- `pluginmaster.json` se actualiza automáticamente mediante un commit de CI a `main`.
- Los ZIP del plugin son assets de GitHub Releases para conservar métricas de descarga.
- Patcher, plugin y corpus comparten versión: un tag `vX.Y.Z` produce una sola release.
- El corpus va embebido en el binario del plugin. No existe manifiesto remoto independiente del
  corpus.
- Una actualización de traducciones requiere una nueva versión del plugin.
- v0.4.1 solo contiene corpus/librerías y queda fuera de estos planes.
- v0.5.0 entrega categorías Penumbra; v0.6.0 entrega el MVP completo del plugin.

### Producto y experiencia

- Nombre provisional: **FFXIV Spanish Translation Manager**.
- Comando: `/ffxives`.
- Interfaz solo en castellano.
- Debe tener identidad visual propia, no apariencia de ventana ImGui genérica.
- Primera instalación: bienvenida y botón explícito para instalar.
- Regeneración automática activada por defecto, silenciosa y sin temporizador periódico.
- Solo se comprueba al cargar el plugin y tras una actualización real del plugin, corpus o juego.
- Pestaña **Actualizaciones**: estado de versiones e hitos de `translation-milestones.md`.
- Créditos y legal: bloque colapsable al final de **Ajustes**, alimentado por un archivo curado.
- La cobertura principal mostrada al usuario es el progreso editorial de la traducción; las
  estadísticas técnicas quedan disponibles como detalle de diagnóstico.

### Categorías y Penumbra

- El mod empaquetado contiene siempre todas las traducciones disponibles.
- Las categorías se representan mediante un único grupo `Multi` de Penumbra con diez casillas
  independientes. Diez grupos separados añadirían diez bloques y estados redundantes sin aportar
  independencia adicional.
- En el patcher clásico, la selección decide el estado inicial de esas opciones.
- El plugin sincroniza en ambos sentidos su estado de categorías y el de Penumbra.
- El mod se activa y administra en la colección **Base/Default**. Aunque parte del contenido se vea
  en la interfaz, todo el payload actual son recursos EXD y Penumbra resuelve `ResourceCategory.Exd`
  exclusivamente con esa colección.
- Penumbra es dependencia funcional dura; si no está disponible, el plugin carga y explica cómo
  resolverlo, pero no puede instalar ni activar el mod.
- Dalamud debe esperar a los plugins antes de reanudar el juego. Tras la primera instalación del
  plugin se exige reiniciar; desde el siguiente arranque Penumbra y el gestor deben terminar de
  cargar antes de que el juego continúe.

### Convivencia y propiedad del mod

- Plugin y patcher usan la misma identidad lógica de mod.
- El plugin detecta un mod generado por el patcher, lo adopta o reemplaza conservando ajustes.
- Una vez instalado, el plugin es el gestor canónico.
- El patcher avisa cuando detecta que el plugin gestiona el mod.
- Desinstalar o descargar el plugin nunca borra el mod. El usuario conserva su contenido en
  Penumbra y decide si eliminarlo.

### Compatibilidad y validación

- Si la versión del juego no coincide con el corpus, el modo automático intenta una regeneración
  best effort y muestra las omisiones.
- La verificación conserva exactamente el contrato actual: siempre es obligatoria; un fallo impide
  instalar o publicar el candidato y conserva el mod anterior. No existe confirmación para saltarla.
- `PackagedWithMisses` sigue siendo un resultado válido cuando solo hay omisiones best effort y el
  paquete supera íntegramente la verificación actual.
- El usuario puede desactivar la regeneración automática.
- Windows y Linux mediante XIVLauncher.Core son entornos humanos obligatorios.
- Cada etapa necesita pruebas automáticas, verificación humana y aprobación antes de continuar.
- Cada etapa actualiza `AGENTS.md`, `README.md` y los documentos permanentes que haya cambiado.
- `README.md` debe adelgazar: instrucciones breves y enlaces; arquitectura y operación detalladas
  pasan a `docs/`.

## 2. Decisiones técnicas adoptadas por este plan

### 2.1 Formato Penumbra v4

Se generará `meta.json` con `FileVersion: 4`, `DefaultData` y `Groups`. El formato v3 actual del
patcher (`meta.json` v3 + `default_mod.json`) solo se conserva en pruebas de lectura/migración. No
se crearán nuevos `group_*.json`.

Motivo: Penumbra actual declara v4 como formato canónico y, al migrar v3, elimina los manifiestos de
grupo separados. Atar v0.5.0 al formato legado añadiría trabajo que Penumbra desharía después.

### 2.2 Un grupo Multi

La representación adoptada es un grupo `Multi` estable llamado `Categorías de traducción` con diez
opciones estables, una por dominio actual:

1. Misiones
2. Nombres y lugares
3. Clases y jobs
4. Objetos
5. Objetos de evento
6. Coleccionables
7. Acciones
8. Logros
9. Registro
10. Interfaz

Cada opción contiene los redirects EXD de su categoría. `DefaultSettings` usa el bit correspondiente
a las categorías seleccionadas en el patcher.

La taxonomía asigna cada hoja a un único dominio, por lo que una página EXD solo pertenece a una
opción y no hay dos opciones declarando el mismo game path. Si aparece una colisión, la generación
debe fallar; no se resuelve por prioridad silenciosa.

### 2.3 Motor compartido y dos destinos

No se duplica el algoritmo de traducción. El pipeline se separará en:

- generación de un árbol de mod verificable;
- exportación opcional de ese árbol a `.pmp` para el patcher;
- instalación/promoción del árbol en Penumbra para el plugin.

El patcher seguirá usando el backend de cliente propio. El plugin tendrá un adaptador de datos de
Dalamud y no poseerá ni liberará `IDataManager.GameData`.

### 2.4 Trabajo pesado fuera del hilo del juego

Descompresión del corpus, lectura EXD, parcheo, hash, escritura, compresión y verificación se ejecutan
fuera del framework thread. Solo llamadas IPC que mutan o consultan estado sensible de Penumbra se
serializan mediante `IFramework`.

### 2.5 Instalación transaccional y mod persistente

- Primera instalación: árbol completo en staging, verificación, promoción al directorio canónico y
  `AddMod`.
- Actualización: payload versionado nuevo, `meta.json` temporal que apunta al payload nuevo,
  sustitución atómica, `ReloadMod` y limpieza del payload anterior solo tras éxito.
- Si falla `ReloadMod`, restaurar el manifiesto anterior y conservar el payload previo.
- `DisposeAsync` cancela trabajo, espera su cierre con límite y libera recursos; nunca elimina el
  mod.

### 2.6 Dependencias de referencia

- `Dalamud.NET.Sdk/15.0.0`.
- `Penumbra.Api/5.17.0`.
- Requisito runtime: API breaking `5`; comprobar feature mínimo para cada wrapper usado y rechazar
  una API incompatible con mensaje claro.

No se copiará código de Penumbra. Penumbra.Api se consume bajo MIT. Heliosphere solo se usa como
referencia de patrones; su código está bajo EUPL-1.2 y no se copiará literalmente.

## 3. Supuestos reversibles

- Nombre de trabajo aprobado para avanzar: **FFXIV Spanish Translation Manager**.
- `InternalName` provisional: `FFXIVSpanishTranslationManager`.
- Directorio canónico de mod provisional: `FFXIVSpanish`.
- Nombre lógico de mod: `FFXIV en Español`.
- El plugin usa `IAsyncDalamudPlugin`, `LoadSync: true`, `LoadRequiredState: 2` y una prioridad de
  carga inferior a la de Penumbra: cuando existe un cambio real, `LoadAsync` termina la regeneración
  y verificación antes de declararse cargado. El trabajo sigue ocurriendo fuera del framework thread.

## 4. Preguntas pendientes

Las tres preguntas funcionales de categorías, colección y validación quedan cerradas. No queda
ninguna pregunta funcional que bloquee el inicio de v0.5.0.

El nombre provisional queda ratificado para desarrollo. El nombre público definitivo puede
revisarse antes de publicar v0.6.0; no condiciona el diseño interno mientras `InternalName` se fije
antes de que existan instalaciones públicas.

## 5. Decisiones descartadas

- Descargar el corpus en runtime desde un manifiesto remoto.
- Reescribir DAT del juego.
- Ejecutar el pipeline en el hilo de render/framework.
- Copiar el código de Heliosphere o de Penumbra.
- Importar un `.pmp` en cada actualización cuando el plugin puede mantener directamente un árbol de
  mod transaccional.
- Borrar el mod al descargar o desinstalar el plugin.
- Crear hoy nuevos paquetes en el formato legado v3 con `group_*.json`.
