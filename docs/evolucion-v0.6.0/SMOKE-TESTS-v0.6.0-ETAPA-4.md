# Smoke tests v0.6.0 — etapa 4: instalación transaccional

Esta etapa implementa el núcleo que adopta, instala, actualiza y revierte un árbol v4 ya generado.
Todavía no hay una acción del plugin que produzca ese árbol desde el corpus: se conectará en la etapa
6, junto con el fingerprint de juego/corpus y la instalación real de traducciones. Por tanto, no se
debe crear un mod vacío, una instalación de prueba ni modificar una instalación de FFXIV para probar
esta etapa de forma manual.

## Estado inicial común

- No instalar ni desinstalar mods manualmente para esta etapa.
- Mantener el plugin de desarrollo y Penumbra en el estado usado en E3.
- El `.pmp` clásico existente puede permanecer instalado o activado: esta versión del plugin aún no
  invoca el instalador por sí sola.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E4-ST-01 | Plugin y Penumbra cargados; cualquier mod existente conservado. | Cerrar y volver a abrir FFXIV; descargar y volver a cargar el gestor desde Dalamud. | No aparece ningún mod nuevo, no se recarga ninguno y no se elimina nada. El mod previo y sus ajustes siguen intactos. |
| E4-ST-02 | Igual que E4-ST-01, en el segundo sistema operativo. | Repetir el ciclo de descarga/carga del gestor. | Mismo resultado: el unload no llama a ninguna operación de borrado ni altera el árbol de mods. |

## Cobertura automática obligatoria

La suite del plugin construye árboles v3/v4 sintéticos y acredita: primera instalación, adopción,
ambigüedad, path traversal, enlaces simbólicos, fallos de `AddMod`/`ReloadMod`, interrupción en cada
paso, rollback, cancelación y persistencia del fingerprint solo tras éxito. Los árboles se crean en
directorios temporales y nunca proceden de FFXIV ni de la instalación real de Penumbra.

## Gate completo diferido a etapa 6

Cuando exista el generador real, el smoke de etapa 6 sustituirá esta prueba de núcleo por instalación,
adopción, actualización y reinicio reales en Windows y Linux. No se considerará completado ese gate
por estos smoke mínimos: únicamente confirman que la etapa 4 aún no tiene efectos de instalación al
cargar o descargar el plugin.
