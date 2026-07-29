# Evolución del patcher v0.3.0

> Documento temporal de implementación y validación. Esta carpeta, este plan y la maqueta se
> eliminarán únicamente cuando la versión esté terminada, validada y aprobada expresamente.

## 1. Punto de partida y regla de ramas

- Rama de desarrollo: `feature/v0.3.0`.
- Base obligatoria: último commit disponible de `feature/v0.2.6`.
- Base comprobada al comenzar: `20b03598e88e69ec91743b2065628f53e885daaa`.
- No mezclar cambios de traducción cruda en este repositorio. El corpus canónico sigue viviendo en
  `FFXIV-Spanish`.
- No borrar esta carpeta durante el desarrollo ni en una limpieza automática.
- Cierre: después de probar, revisar y recibir aprobación explícita, borrar
  `docs/evolucion-v0.3.0/` en el commit final de cierre.

## 2. Objetivo de producto

Publicar una v0.3.0 que celebre la traducción de *A Realm Reborn*, conserve el uso directo del
patcher y haga más segura y comprensible la generación del mod:

1. Una sola interfaz Avalonia compartida por Windows, Linux y macOS.
2. Aspecto visual calcado a `maqueta-v0.3.0.html`, sin implementaciones específicas por sistema.
3. Historial de traducción editable mediante Markdown.
4. Compatibilidad *best effort* cuando la versión instalada de FFXIV no coincide.
5. Detección silenciosa de Dalamud y Penumbra, con corrección opcional del ajuste necesario.
6. Verificación de integridad obligatoria y publicación transaccional del `.pmp`.
7. Consola grande, coloreada y seleccionable como un único texto.
8. Dependencias y acciones de CI actualizadas a la última versión compatible verificada.

## 3. Reglas no negociables

- Nunca modificar archivos de FFXIV.
- Nunca distribuir EXD/EXH/DAT/PMP o snapshots reales del juego.
- Nunca generar un paquete vacío ni publicar un paquete que no pase la verificación.
- Un desfase de versión no es por sí solo un error fatal.
- Una ausencia localizada —hoja, página o fila— no debe impedir empaquetar las partes válidas.
- Un fallo total de lectura, cero traducciones aplicadas, contaminación no confirmada, error de
  escritura o fallo de integridad sí es fatal.
- Si la inspección o modificación de la configuración de Dalamud falla, no escribir en la consola,
  no mostrar aviso y continuar con el patcher.
- Si no hay ninguna categoría seleccionada, el botón de generación queda deshabilitado y se explica
  el motivo en línea.
- La integridad no es una opción del usuario: se comprueba siempre.

## 4. Contrato de compatibilidad de versiones

### 4.1 Comparación

Comparar la cadena de `ffxivgame.ver` con `data/recommended-game-version.txt` por igualdad exacta,
ignorando mayúsculas/minúsculas y espacios exteriores. No interpretar qué versión es anterior o
posterior.

Estados:

- `Match`: cadenas iguales.
- `Different`: ambas conocidas y diferentes.
- `Unknown`: falta alguna de las dos.

La instalación antigua mantenida deliberadamente para pruebas es un caso soportado y no debe ser
actualizada, rechazada ni «arreglada» automáticamente.

### 4.2 Confirmación

Con estado `Different`, mantener un aviso visible y, al generar, mostrar un modal:

- Título: **La versión del juego no coincide con esta traducción**.
- Exponer versión del corpus y versión instalada.
- Explicar que se usarán los archivos instalados.
- Explicar que hojas, páginas y líneas inexistentes se omitirán.
- Aclarar que el resultado puede tener menos traducciones y que no se tocan los originales.
- Acciones: **Volver** y **Generar de todos modos**.

Solo la segunda activa `BestEffortVersionMismatch`. No recordar la aceptación entre ejecuciones de
la aplicación.

### 4.3 Clasificación de omisiones

Registrar por separado:

- entradas candidatas;
- escrituras aplicadas;
- `misses` de reemplazo;
- hojas ausentes y entradas afectadas;
- páginas ausentes o ilegibles y entradas afectadas;
- filas que no pertenecen a ninguna página de esa versión;
- filas SeString inseguras;
- páginas/formas EXD todavía no soportadas;
- páginas parcheadas y páginas omitidas.

Agrupar avisos por hoja/página para no inundar la consola. Los `rowId` de un `miss` se muestran
ordenados y con un máximo de 20 por línea.

### 4.4 Resultado

- Hay escrituras y ninguna omisión: `Ok`.
- Hay escrituras, omisiones y paquete verificado: `PackagedWithMisses`.
- No se aplica ninguna traducción: `NothingToPackage`, sin `.pmp` nuevo.
- La tasa de coincidencia baja en modo estricto: `Contaminated`.
- La misma tasa baja, tras confirmación de mismatch: continuar con avisos.
- Ninguna página puede leerse: `GameDataError`.

El guard de contaminación solo usa filas que sí pudieron leerse. Una hoja o página inexistente no
debe reducir artificialmente su tasa.

## 5. Integridad y publicación transaccional

Algoritmo obligatorio:

1. Crear un staging aislado con GUID para esa ejecución.
2. Generar el `.pmp` en un temporal hermano de la salida final.
3. Ejecutar siempre `IIntegrityVerifier`.
4. Si falla, mostrar los problemas en consola, borrar temporal/staging y conservar una salida
   anterior si existía.
5. Si pasa, reemplazar atómicamente la salida anterior o mover el temporal si todavía no existe.
6. Limpiar temporales en `finally` sin ocultar el resultado real.

No debe existir `VerifyIntegrity` en la petición ni un toggle equivalente en la GUI.

## 6. Dalamud y Penumbra

### 6.1 Detección acotada

Inspeccionar solo ubicaciones conocidas:

- Windows: `%AppData%/XIVLauncher`.
- Linux: `~/.xlcore`.
- Linux Flatpak: `~/.var/app/dev.goats.xivlauncher/.xlcore`.
- macOS: `~/Library/Application Support/XIV on Mac`.

Aceptar `dalamudConfig.json` y `dalamudconfig.json`. Considerar Penumbra detectado únicamente si
existe un manifiesto real `installedPlugins/Penumbra[/versión]/Penumbra.json` cuyo `InternalName` o
`Name` sea `Penumbra`.

### 6.2 Comportamiento

- Si ambos existen y `IsResumeGameAfterPluginLoad` es `true`: estado preparado, sin modal.
- Si ambos existen y no es `true`: estado revisable y modal una vez por sesión.
- Si falta algo, el JSON no es válido o no se puede leer: tratar como no detectado y callar.

Texto del modal:

- Título: **Haz que Penumbra termine de cargar antes de iniciar FFXIV**.
- Explicar que Dalamud y Penumbra están detectados, pero Dalamud no espera a los plugins.
- Consecuencia: la traducción puede no activarse a tiempo.
- Explicar que la corrección activa la opción de esperar a los plugins.
- Aclarar que solo cambia esa opción y no toca Penumbra ni el juego.
- Acciones: **Ahora no** y **Activar opción**.

### 6.3 Escritura segura

1. Leer bytes originales.
2. Parsear un objeto JSON.
3. Cambiar solo la propiedad lógica `IsResumeGameAfterPluginLoad`.
4. Escribir un temporal en el mismo directorio y forzar `Flush`.
5. Comprobar mediante SHA-256 que el original no cambió concurrentemente.
6. Reemplazar atómicamente.
7. Releer y comprobar `true`.
8. Ante cualquier fallo, devolver `false`, limpiar el temporal y no emitir traza.

## 7. Historial de traducción Markdown

- Fuente editable: `data/translation-milestones.md`.
- Recurso embebido: `FFXIVSpanishPatcher.App.translation-milestones.md`.
- Renderizado Avalonia nativo mediante Markdig; no usar WebView.
- Sintaxis permitida: encabezados, párrafos, negrita, cursiva, tachado, enlaces HTTP(S), listas,
  citas, código, separadores y tablas.
- Rechazar HTML, imágenes y enlaces con esquemas locales o inseguros.
- Fallar el build si el fichero falta o está vacío.
- Si el recurso no puede cargarse en runtime, mostrar contenido de reserva sin cerrar la app.

La edición habitual de los hitos solo debe requerir cambiar ese `.md`, compilar y revisar visualmente
el modal.

## 8. Interfaz y paridad visual

Una sola `MainWindow.axaml` y un solo tema para los tres sistemas. No crear XAML condicional,
ventanas nativas ni CSS/HTML de runtime.

### 8.1 Geometría de referencia

- Ventana: `1240 × 820`; mínimo `1080 × 720`.
- Fondo azul noche con degradado base desde `#07101E`.
- Paneles con degradados azul noche, borde `#29405F`, acento azul `#3C8DFF` y profundidad moderada.
- Cabecera de 92 px con logo correctamente encuadrado en caja recortada de 66 px.
- Inter para la interfaz y Noto Serif embebida para titulares editoriales; no depender de fuentes del
  sistema operativo.
- Hito ARR con iluminación radial ámbar/azul y botón principal con degradado vertical.
- Panel de preparación a la izquierda, hito y estado a la derecha.
- Consola de ancho completo y claramente mayor que la de v0.2.x.
- Pie fijo con ruta de salida, versión del corpus y estado.

### 8.2 Preparación

- Los tres rótulos `Preparación`, `Generando` y `Resultado` son indicadores, no botones.
- Cuatro comprobaciones: juego, versión, corpus y entorno Penumbra.
- `Opciones avanzadas y categorías` es un despliegue real.
- Panel abierto: 10 categorías en cinco columnas y dos filas, contadores y tooltips.
- `Todas` selecciona todas las disponibles; `Ninguna` las desmarca.
- La ausencia de selección se muestra como error en línea y deshabilita generar.
- No hay botón de cancelación.

### 8.3 Estados

- Preparación: resumen y llamada a crear.
- Generando: progreso por componentes del pipeline; no ofrecer cancelación.
- Resultado correcto: resumen y estadísticas; reutilizar las acciones generales de la izquierda.
- Resultado parcial: ámbar, estadísticas y paquete utilizable, sin duplicar botones de acción.
- Error: rojo, explicar que no se publicó un paquete nuevo y remitir a consola.

### 8.4 Consola

- Una sola superficie `AvaloniaEdit.TextEditor` de solo lectura; no un control por línea.
- Documento completo en memoria y líneas visuales virtualizadas al viewport.
- Colores independientes para hora, componente, nivel y mensaje.
- Selección continua de varias líneas, `Ctrl+A`, `Ctrl+C` y botón **Seleccionar todo**.
- No romper la selección al añadir eventos.
- Autoscroll solo si el usuario estaba al final y no tiene selección.
- Limitar el autoscroll al eje vertical, conservar el desplazamiento horizontal y no mostrar espacio
  vacío bajo la última línea.
- Añadir eventos por lotes en el dispatcher.
- No truncar silenciosamente el historial.
- Prueba de 10.000 líneas con selección conservada.
- Conservar el resultado de cada página y todos los avisos normales; no resolver rendimiento
  suprimiendo líneas.
- Usar `--debug` solo para trazas internas adicionales de broadcast y `miss`.
- Probar interacción y desplazamiento con la consola conectada a una ventana visible, no solo el
  coste de añadir datos a un control sin maquetar.

## 9. Dependencias, reproducibilidad y CI

Versiones verificadas para esta rama:

| Componente | Versión |
| --- | --- |
| SDK .NET | `10.0.302` (`latestFeature`) |
| Avalonia | `12.1.1` |
| CommunityToolkit.Mvvm | `8.4.2` |
| Markdig | `1.3.2` |
| Tmds.DBus.Protocol | `0.94.2` |
| Lumina | `7.6.1` |
| Lumina.Excel | `7.5.0` |
| Microsoft.NET.Test.Sdk | `18.8.1` |
| xUnit v3 | `3.2.2` |
| xunit.runner.visualstudio | `3.1.5` |

- Versionar `packages.lock.json` para todos los proyectos.
- Incluir `win-x64`, `linux-x64` y `osx-arm64` en el grafo común de runtime para que un único lock
  reproducible sirva a toda la matriz.
- Restaurar CI y release con `--locked-mode`.
- Fijar acciones por SHA y dejar la versión legible en comentario.
- Ejecutar auditoría NuGet y no aceptar vulnerabilidades conocidas.
- Mantener el smoke nativo automático de los RIDs publicados.

## 10. Pruebas obligatorias

### Pipeline

- hoja inexistente + hoja válida;
- página inexistente + página válida;
- `misses` masivos con strict y con confirmación;
- ninguna escritura;
- fallo de integridad con salida anterior;
- reemplazo correcto de salida anterior;
- estadísticas y resultado parcial;
- SeString inseguro omitido;
- staging y temporales limpios.

### Aplicación

- instalación deliberadamente antigua conserva estado `Different`;
- modal de mismatch: volver y confirmar;
- cero categorías deshabilita generar;
- Todas/Ninguna;
- Dalamud/Penumbra ausentes, preparados, ajuste falso, JSON inválido y edición;
- Markdown válido e inválido;
- consola de 10.000 líneas y selección;
- carga XAML headless.

### Visual

- capturas de preparación, avanzadas, modal, generando, resultado y cero categorías;
- revisar a `1240 × 820` y mínimo `1080 × 720`;
- revisar escalas 100 %, 125 %, 150 % y 200 %;
- comprobar que no hay texto cortado, superposición, scroll horizontal ni controles fuera de vista;
- comparar con `maqueta-v0.3.0.html`.

### Publicación

- `dotnet restore --locked-mode`;
- build Release con cero avisos;
- suite completa;
- `git diff --check`;
- publish real recortado para `win-x64`, `linux-x64` y `osx-arm64`;
- inspeccionar contenido: sin PDB sueltos y corpus externo únicamente donde corresponda;
- ejecutar el binario Linux publicado;
- confirmar que Markdown y JSON source-generated sobreviven al trimming.

## 11. Documentación permanente

Actualizar antes de entregar:

- `README.md`: flujo nuevo, mismatch, partial success, integridad, Dalamud y consola.
- `CHANGELOG.md`: sección v0.3.0.
- `docs/DESIGN.md`: arquitectura y UX actuales.
- `docs/COMPATIBILITY.md`: matriz recoverable/fatal y estadísticas.
- `docs/TRANSLATION_MILESTONES.md`: cómo editar/validar el `.md`.
- `docs/RELEASE_CHECKLIST.md`: checklist reproducible.
- `CONTRIBUTING.md`: lockfiles, hitos y validaciones.
- `NOTICE.md`: nueva dependencia Markdown y licencias de terceros.
- `AGENTS.md`: estado y comandos coherentes si procede.

Los porcentajes del README se toman en vivo de `FFXIV-Spanish/Remaining_EXD.md`; no reutilizar cifras
redondeadas de releases anteriores.

## 12. Criterio de terminado

La implementación solo está terminada cuando:

1. todos los requisitos anteriores están implementados;
2. build, pruebas y publicaciones reales pasan;
3. se ha inspeccionado visualmente la app;
4. la documentación permanente coincide con el código;
5. no quedan temporales ni avisos;
6. el usuario ha validado la maqueta implementada y ha aprobado la versión.

Solo después del punto 6 se elimina esta carpeta y el plan.
