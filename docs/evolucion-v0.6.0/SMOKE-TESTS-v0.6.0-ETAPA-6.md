# Smoke tests v0.6.0 — etapa 6: generación e instalación gestionada

Esta etapa prueba el recorrido completo que usa el plugin: corpus embebido, motor compartido,
árbol Penumbra v4 verificado, registro/recarga por IPC y persistencia del fingerprint. La ventana
se abre con `/ffxives`, desde la configuración del plugin o desde su entrada principal en Dalamud.

## Estado inicial común

- Usar el ZIP de desarrollo actualizado de `FFXIVSpanishTranslationManager`.
- Penumbra debe estar **habilitado**, haber terminado de cargar y tener un directorio de mods válido.
  Mantener marcada **Wait for plugins before game loads** para la comprobación de carga temprana.
- El plugin FFXIV Spanish Translation Manager debe estar habilitado. Para una instalación inicial,
  el juego ya puede estar abierto: el resultado se aplicará completamente tras el siguiente reinicio.
- No debe haber otra operación de generación activa. No cerrar Dalamud ni deshabilitar Penumbra mientras
  se genera o instala.
- Estas pruebas no modifican archivos originales de FFXIV. El plugin no importa ningún `.pmp`:
  E6-ST-00 y E6-ST-01 usan una importación clásica hecha manualmente solo como estado inicial.
- Para E6-ST-00 y E6-ST-01, importar antes el `.pmp` indicado en Penumbra y comprobar que aparece
  **una sola** instalación de `FFXIV en Español`. El plugin adopta un mod anterior únicamente si su
  árbol conserva el marcador con huella SHA-256 emitido por el patcher; no depende del nombre, Website o
  directorio que el usuario haya cambiado, ni del tag nuevo `ffxiv-spanish-managed`. Para paquetes
  v0.5 que aún no incluyan esta huella se usa la firma estructural de su formato como migración.
  v0.4 y anteriores no se adoptan automáticamente.
- Normalmente el directorio de mods contiene una sola copia identificable de la traducción. Un respaldo
  se guarda fuera de esa raíz y nunca se toca. Si hubiera varias copias identificables dentro de la raíz,
  una regeneración las consolida en una sola instalación; sus ajustes de categorías pueden restablecerse.
- Penumbra 1.6.x abre los `meta.json` v4 pero no permite guardar desde su UI cambios de presentación
  (nombre, Website o descripción): muestra `Can not save changes to a V4 mod`. Para estas pruebas no
  usar esos campos de la UI. Con Penumbra deshabilitado o XIV cerrado, renombrar directamente el
  directorio del mod. Penumbra seguirá mostrando el `Name` guardado en `meta.json`; no confundir ese
  nombre visible con el directorio físico que debe adoptar el gestor.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E6-ST-00 | Plugin sin fingerprint previo. Importar un `.pmp` real de **v0.5.0 generado tras ST-11**. Con Penumbra deshabilitado o XIV cerrado, renombrar el directorio del mod; volver a abrir Penumbra y dejar sus categorías de Base/Default en el estado que se quiera conservar. | Abrir `/ffxives` y pulsar el botón principal: **Instalar traducción** si no hay fingerprint guardado o **Regenerar traducción** si lo hay. Abrir el `meta.json` resultante. | El gestor reconoce el marcador `ffxivspanish.identity.json` aunque el directorio físico sea distinto; conserva ese directorio y finaliza con `Traducción existente actualizada de forma segura.` Solo se llama a recargarlo: no se crea `FFXIVSpanish` en paralelo ni se pierde el ajuste de categorías de Base/Default. Penumbra puede seguir mostrando `FFXIV en Español`, porque ese es el nombre contenido en `meta.json`. Tras actualizar, `meta.json` usa el contrato del patcher clásico: `Version` es `v<versión del plugin>-<versión instalada de FFXIV>` y `Description` contiene descripción base, ambas versiones y las secciones de categorías incluidas y activadas por defecto. Para el gestor las diez categorías figuran en ambas secciones. No quedan en la raíz de Penumbra carpetas `.ffxivspanish-pipeline-staging` ni `.FFXIVSpanish.ffxivspanish-candidate-*`. |
| E6-ST-01 | Plugin sin fingerprint previo. Importar un `.pmp` real de **v0.5.0 anterior a ST-11**. Con Penumbra deshabilitado o XIV cerrado, renombrar directamente su directorio y volver a abrir Penumbra. | Abrir `/ffxives` y pulsar el botón principal de instalación o regeneración. | Se adopta mediante la firma estructural de v0.5.0 y se conserva el directorio existente, sin duplicarlo. Esta prueba acredita la migración de paquetes ya publicados que no pueden llevar el marcador retrospectivamente. |
| E6-ST-03 | No hay fingerprint guardado del gestor y no existe un mod gestionado. | Abrir `/ffxives` y pulsar **Instalar traducción**. Esperar a que termine. | Se crea `FFXIVSpanish` bajo el directorio de mods de Penumbra, Penumbra lo registra **y lo activa en Base/Default**; la ventana termina en `Traducción instalada en Penumbra.` No se crea ningún `.pmp`; el mod tiene `meta.json` v4, `ffxivspanish.identity.json` y el árbol `files/categories/`. El log registra una regeneración correcta. Reiniciar el juego antes de comprobar textos, pues EXD puede estar cacheado. |
| E6-ST-04 | E6-ST-00, E6-ST-01 o E6-ST-03 completado, con el mod activo. | Deshabilitar y volver a habilitar solo el gestor, o reiniciar XIV con **Wait for plugins before game loads** marcado. | El log indica que la traducción coincide con el juego, corpus y plugin. No se genera un candidato, no se recarga el mod y no cambian las categorías de Base/Default. |
| E6-ST-05 | Mod gestionado procedente de E6-ST-00, E6-ST-01 o E6-ST-03; realizar una copia manual del directorio de mod fuera de la carpeta de Penumbra como respaldo visual. Para probar la consolidación, se puede dejar además una copia identificable dentro de la raíz de mods. | Pulsar **Regenerar traducción**. | El directorio gestionado se actualiza mediante staging y Penumbra lo recarga. Si había varias copias identificables, el gestor conserva una de forma determinista y retira las demás, por lo que al terminar queda una sola instalación activa; las categorías pueden volver a su estado por defecto. No se borra el respaldo manual ni se crea un `.pmp`. |
| E6-ST-06 | E6-ST-03 completado. | Durante una regeneración, pulsar **Cancelar** antes de que termine. | La ventana informa de cancelación o permite reintentar. Si había una versión anterior, sigue intacta y cargable por Penumbra. No queda un mod parcialmente promovido; los candidatos fallidos, si los hubiera, quedan ocultos bajo un nombre `.FFXIVSpanish.ffxivspanish-*` para diagnóstico y no se registran como mod. |
| E6-ST-07 | E6-ST-03 completado y `AutoRegenerationEnabled` conservado en `true` (valor por defecto). | Simular una actualización editando temporalmente, con Dalamud cerrado, el fingerprint guardado del plugin para que `CorpusSha256` tenga un valor distinto; volver a abrir Dalamud con Penumbra listo. Restaurar el archivo al terminar si no se completara la prueba. | El log anuncia `RegenerationRequired` y que inicia la regeneración automática. El plugin actualiza el mod antes de que el juego continúe; tras éxito persiste un fingerprint nuevo. Si se desactiva la regeneración automática, el mismo desfase solo se anuncia y queda disponible el botón manual. |

## Evidencia mínima a adjuntar

- Registro desde `Penumbra: Ready` hasta la línea final de cada prueba.
- Para E6-ST-00 a E6-ST-05, listado de primer nivel del mod gestionado y captura de la ventana final.
- Para E6-ST-07, las líneas `Regeneración: RegenerationRequired` y `Se inicia la regeneración automática`.

No ejecutar E6-ST-07 si no se localiza con seguridad el archivo de configuración de Dalamud: es una
simulación opcional y no justifica editar una configuración incierta.
