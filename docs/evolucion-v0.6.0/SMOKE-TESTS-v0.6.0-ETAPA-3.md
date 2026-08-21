# Smoke tests v0.6.0 — etapa 3: disponibilidad de Penumbra

Esta etapa solo consulta el estado de Penumbra y reacciona a sus eventos. No genera, instala,
activa, recarga ni elimina mods. Mantener el `.pmp` clásico de FFXIV en Español desactivado en
Base/Default durante todas las pruebas.

## Estado inicial común

- Usar el ZIP Release más reciente de `FFXIVSpanishTranslationManager` como plugin de desarrollo.
- Registrar versión de Dalamud, Penumbra y FFXIV. El plugin actual exige API breaking 5 y feature
  15 o superior; Penumbra actual puede tener una feature posterior.
- Registrar el estado de **Wait for plugins before game loads**. Solo E3-ST-05 exige activarlo.
- No crear ni eliminar directorios de mod para esta etapa. E3-ST-04 puede usar una carpeta vacía ya
  existente; restaurar el directorio habitual exactamente al terminar.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E3-ST-01 | Penumbra habilitado, directorio de mods válido, FFXIV abierto, gestor deshabilitado. | Habilitar el gestor y revisar su registro. | Carga sin excepción; aparece `Penumbra: Ready. Penumbra está disponible.` No crea, instala, activa ni recarga ningún mod. |
| E3-ST-02 | Gestor habilitado tras E3-ST-01; Penumbra habilitado. | Deshabilitar Penumbra desde Dalamud, esperar registro del gestor, y volver a habilitar Penumbra. | Al descargar aparece `Penumbra: Unavailable. Penumbra se ha descargado; el gestor queda en espera.` Tras la carga tardía aparece `Penumbra: Ready. Penumbra está disponible.` El gestor sigue cargado y no se bloquea. |
| E3-ST-03 | Penumbra y gestor ya cargados; todos los mods de Penumbra habilitados. | En el chat, ejecutar `/penumbra disable`; esperar registro; ejecutar `/penumbra enable`. Alternativamente, abrir `/penumbra` y desmarcar/marcar **Enable Mods**, casilla superior de Settings. | Tras deshabilitar aparece `Penumbra: Disabled. Penumbra está desactivado.`; tras reactivar aparece `Penumbra: Ready. Penumbra está disponible.` Penumbra sigue cargado y ningún mod se crea, modifica ni elimina. Deshabilitar el plugin Penumbra completo se cubre en E3-ST-02 y debe dar `Unavailable`. |
| E3-ST-04 | Penumbra habilitado y una carpeta vacía ya existente disponible temporalmente. | Desde Settings de Penumbra, elegir esa carpeta vacía como directorio de mods; esperar el registro del gestor; restaurar después el directorio habitual. | En ambos cambios aparece `El directorio de mods de Penumbra ha cambiado; disponibilidad actualizada.` seguido de `Penumbra: Ready. Penumbra está disponible.` Una carpeta vacía existente es **válida**: será destino posible de primera instalación en etapa 4. Ruta ausente, vacía o corrupta produce `InvalidModDirectory`; ese caso queda cubierto de forma segura por los fakes automáticos. No modificar contenidos de ninguna carpeta. |
| E3-ST-05 | Plugin de desarrollo y Penumbra habilitados; **Wait for plugins before game loads** activado. | Cerrar FFXIV, iniciar una sesión nueva mediante XIVLauncher.Core y llegar a título/personaje. | Gestor carga antes de Penumbra sin fallo; al emitir `Initialized`, registra `Ready`. El juego continúa y no se crea ni toca mod alguno. |
| E3-ST-06 | Repetir E3-ST-01 y E3-ST-02 en el segundo sistema operativo. | Cargar gestor, descargar y recargar Penumbra. | Mismos estados `Ready`, `Unavailable` y nueva conexión `Ready`, sin excepciones. |

## Criterio de aprobación humana

E3-ST-01 a E3-ST-05 deben pasar en Windows y E3-ST-06 en Linux. Ruta ausente/vacía/corrupta se
acredita mediante pruebas automáticas porque la interfaz de Penumbra evita configurarla. Los tests no
prueban todavía instalación, categorías ni regeneración: esos alcances comienzan después de la etapa 3.
