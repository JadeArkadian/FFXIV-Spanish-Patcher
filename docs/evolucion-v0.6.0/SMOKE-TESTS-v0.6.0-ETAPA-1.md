# Smoke tests v0.6.0 — etapa 1: carga Dalamud y Lumina

Estas pruebas no instalan ni modifican un mod de Penumbra. Ejecutar en Windows y Linux con una
instalación real de FFXIV, XIVLauncher.Core y Dalamud actuales. Registrar versión de Dalamud, juego y
plugin. Si el plugin no llega a cargar, adjuntar únicamente el mensaje de error y diagnóstico, nunca
archivos del juego.

## Estado inicial común

- Penumbra puede estar instalado, habilitado o deshabilitado: esta etapa no usa su API ni debe
  modificar su estado. Mantener el estado habitual y registrarlo.
- Si existe un `.pmp` clásico de FFXIV en Español (v0.4.0 o v0.5.0), conservarlo instalado pero
  **desactivado** en Base/Default. No desinstalarlo ni eliminarlo: así sus textos no confunden esta
  prueba de carga.
- E1-ST-02 a E1-ST-05 se ejecutan con FFXIV ya abierto; el ajuste **Wait for plugins before game
  loads** no afecta a esas pruebas. Dejarlo como estaba y registrar si estaba activo.
- E1-ST-06 prueba ese ajuste de forma separada: requiere activarlo antes de iniciar una sesión nueva
  de XIVLauncher.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E1-ST-01 | Build Release recién generado. | Abrir `FFXIVSpanishTranslationManager/latest.zip` y revisar contenido. | Contiene DLL y manifiesto del plugin; no contiene `Dalamud.dll`, `Lumina.dll`, `Lumina.Excel.dll` ni `Microsoft.Extensions.ObjectPool.dll`. |
| E1-ST-02 | Estado común; FFXIV abierto mediante XIVLauncher.Core; Dalamud cargado; plugin aún no habilitado. | Añadir el directorio Release del plugin como ubicación de desarrollo, escanear y habilitar **FFXIV Spanish Translation Manager**. | Dalamud reconoce manifiesto API 15, carga plugin y no informa `FileLoadException`, `MissingMethodException` ni ensamblado ausente. Penumbra y el `.pmp` clásico permanecen sin cambios. Los avisos de callbacks de UI ausentes son esperados mientras etapa 1 no tiene ventana; etapa UI los sustituirá por acciones reales. |
| E1-ST-03 | Plugin habilitado tras E1-ST-02. | Abrir registro de Dalamud y esperar fin de carga. | Aparece `Compatibilidad con Dalamud y motor compartido verificada.`: `IDataManager.GameData` leyó `exd/Addon.exh` y `exd/Addon_0_en.exd`. No se crea mod, no se modifica Penumbra y no se abre UI final. |
| E1-ST-04 | Plugin cargado correctamente. | Deshabilitarlo desde Dalamud y volver a habilitarlo. | Descarga y nueva carga terminan sin bloqueo, excepción ni residuos de operación. |
| E1-ST-05 | Mismo procedimiento repetido en segundo sistema operativo. | Repetir E1-ST-02 a E1-ST-04. | Resultado idéntico en Windows y Linux; registrar cualquier diferencia de carga o ruta. |
| E1-ST-06 | Plugin de desarrollo habilitado; `.pmp` clásico sigue desactivado; **Wait for plugins before game loads** activado antes de arrancar. | Cerrar FFXIV por completo, iniciarlo otra vez mediante XIVLauncher.Core y llegar a la pantalla de título o personaje. | XIVLauncher/Dalamud espera la carga del plugin sin bloqueo ni error; aparece mensaje de compatibilidad y el juego continúa. Penumbra y el `.pmp` clásico siguen sin cambios. |

## Criterio de aprobación humana

E1-ST-01 a E1-ST-04 y E1-ST-06 deben pasar en Windows; E1-ST-05 repite E1-ST-02 a E1-ST-04 en Linux.
Esta etapa no queda aprobada por compilación o ZIP sintético: se necesita confirmación expresa de
carga real en ambos sistemas. E1-ST-06 solo es requisito Windows en esta etapa; Linux con el ajuste
activo queda cubierto antes de aprobar la etapa de arranque completa.
