# Smoke tests — v0.6.0, etapa 7: flujos de interfaz

## Preparación común

- Instalar el ZIP actual del plugin y reiniciar XIV.
- Penumbra debe estar activo, con un mod FFXIV en Español generado por v0.5.0 o por el gestor y
  disponible en la colección Base/Default.
- Para pruebas de categorías, conservar anotado el estado de las diez casillas antes de empezar.
- Esta etapa valida comportamiento. Apariencia, escalado y recursos propios se validarán en etapa 8.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E7-ST-01 | Plugin cargado y ventana cerrada. | Ejecutar `/ffxives`. Cerrar y reabrir desde la configuración del plugin. Recorrer **Inicio**, **Categorías**, **Actualizaciones** y **Ajustes**. | Ambos accesos abren la misma ventana. Las cuatro pestañas responden. Inicio muestra estado de Penumbra y botón principal; Categorías nombra Base/Default; Actualizaciones muestra versiones e hitos; Ajustes muestra regeneración automática, diagnóstico e información legal. |
| E7-ST-02 | Mod gestionado reconocido, Penumbra `Ready` y categorías sincronizadas. | En **Categorías**, desmarcar una opción anotada y volverla a marcar. Abrir el mod en Penumbra y observar el grupo `Categorías de traducción`. | Las diez casillas reflejan el estado actual de Base/Default. Cada cambio se refleja en Penumbra sin regenerar EXD ni cambiar otras colecciones. Tras restaurar la casilla, el conjunto queda igual que antes. |
| E7-ST-03 | Plugin cargado, `AutoRegenerationEnabled` activado. | En **Ajustes**, desmarcar `Regenerar automáticamente tras una actualización real`; cerrar y volver a abrir la ventana. Volver a marcarlo. Pulsar **Copiar diagnóstico** y pegar el contenido en un editor. | El ajuste se conserva entre aperturas. Diagnóstico incluye estado, API de Penumbra, colección, versiones y huella de corpus abreviada; no muestra rutas locales ni la huella completa. |
| E7-ST-04 | Penumbra deshabilitado o descargado con ventana del gestor abierta. | Abrir **Inicio** y **Categorías**. Reactivar Penumbra y esperar a que termine de cargar. | Inicio explica que no puede instalar sin Penumbra y no ofrece acción peligrosa. Categorías no permite editar. Cuando Penumbra vuelve a estar disponible, ventana recupera estado y categorías sin reiniciar ni regenerar. |
