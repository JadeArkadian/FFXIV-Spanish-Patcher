# Smoke tests v0.6.0 — etapa 2: configuración y ciclo interno

Esta etapa sigue sin comunicarse con Penumbra, generar paquetes ni modificar mods. Confirma que el
plugin conserva su configuración versionada y que su ciclo interno se descarga limpiamente tras
añadir el controller.

## Estado inicial común

- Usar el ZIP Release más reciente de `FFXIVSpanishTranslationManager`; sustituir el directorio de
  desarrollo de la etapa 1 por este build.
- Penumbra puede estar instalado, habilitado o deshabilitado: registrar el estado, pero no cambiarlo
  para la prueba. Esta etapa no usa su API.
- Mantener cualquier `.pmp` clásico de FFXIV en Español instalado pero **desactivado** en
  Base/Default. No se crea, instala, activa ni recarga ningún mod en esta etapa.
- Si el plugin ya se probó en etapa 1, conservar su configuración existente: la carga debe aceptar
  tanto una configuración ausente como una ya creada.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E2-ST-01 | Estado común; FFXIV abierto mediante XIVLauncher.Core; Dalamud cargado; plugin deshabilitado. | Escanear y habilitar el plugin de desarrollo. Revisar el registro de Dalamud al terminar. | Carga sin excepción. Aparecen `Configuración v1 cargada; regeneración automática: True.` y `Compatibilidad con Dalamud y motor compartido verificada.` (el valor puede reflejar una futura preferencia guardada). No se muestra UI, no se consulta Penumbra y el `.pmp` clásico queda igual. Los dos avisos de callbacks de UI de etapa 1 siguen siendo esperados. |
| E2-ST-02 | Plugin cargado tras E2-ST-01. | Deshabilitarlo, esperar a que Dalamud confirme la descarga y volver a habilitarlo dos veces seguidas. | Cada descarga y recarga concluye sin bloqueo, error ni operaciones residuales. La segunda y tercera carga vuelven a reconocer configuración v1. No se crea ni modifica una carpeta de mod. |
| E2-ST-03 | Plugin de desarrollo habilitado; ajuste **Wait for plugins before game loads** activado antes de arrancar. | Cerrar FFXIV por completo, iniciarlo de nuevo con XIVLauncher.Core y llegar a la pantalla de título o personaje. | La carga previa del plugin termina y el juego continúa. El registro contiene ambos mensajes de E2-ST-01, sin error de configuración ni de descarga. Penumbra y el `.pmp` clásico no cambian. |
| E2-ST-04 | Repetir E2-ST-01 y E2-ST-02 en el segundo sistema operativo. | Habilitar, descargar y recargar el plugin con el mismo ZIP Release. | Mismo resultado en Windows y Linux. Registrar cualquier diferencia de versión/ruta, nunca archivos del juego. |

## Criterio de aprobación humana

E2-ST-01 a E2-ST-03 deben pasar en Windows y E2-ST-04 en Linux. Esta aprobación cubre solo la
configuración y el ciclo de vida interno: no acredita integración con Penumbra ni generación de
traducciones, que comienzan en etapas posteriores.
