# Smoke tests v0.5.0 — categorías Penumbra

Estas pruebas humanas complementan la suite sintética. No usan ni adjuntan archivos originales de
FFXIV. Registrar versión exacta de FFXIV, Dalamud, Penumbra y patcher, además de capturas de
Penumbra cuando corresponda.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| ST-01 | Patcher recién abierto; corpus cargado; instalación de FFXIV válida. | Abrir panel avanzado. | Se muestran diez categorías, todas activadas, y el texto aclara que solo determina el estado inicial al importar. |
| ST-02 | Mismo estado; ninguna categoría desactivada. | Pulsar `Ninguna` e intentar generar. | Generar queda deshabilitado y aparece aviso de que debe seleccionarse al menos una categoría. |
| ST-03 | Mismo estado; hay salida `.pmp` previa conocida. | Activar solo dos categorías y generar. | Se publica un `.pmp` v4; no contiene `default_mod.json` ni `group_*.json`; conserva la salida previa si falla verificación. |
| ST-04 | `.pmp` generado con selección parcial. | Abrir `meta.json` del ZIP sin editarlo. | `FileVersion` es `4`; existe un único grupo `Multi` llamado `Categorías de traducción`, con diez opciones en orden estable; `DefaultSettings` activa solo las dos categorías elegidas. |
| ST-05 | Dos `.pmp` generados desde mismo juego/corpus: uno con todas categorías y otro con selección parcial. | Comparar número de redirects y payload EXD. | Ambos contienen mismas páginas y mismo payload por categoría; solo cambia `DefaultSettings` y metadatos derivados de selección. |
| ST-06 | Penumbra actual, colección Base/Default activa, mod aún no importado. | Importar `.pmp` de ST-03. | Penumbra acepta mod sin migrarlo a v3; muestra diez casillas; las dos elegidas aparecen activadas. |
| ST-07 | Mod de ST-06 importado. | Activar una categoría inicialmente desactivada; desactivar una activada; recargar interfaz/juego según pida Penumbra. | Cambia solo texto de páginas pertenecientes a esas categorías. No modifica archivos originales de FFXIV. |
| ST-08 | Mod importado con selección parcial. | Reimportar paquete equivalente con otra selección inicial. | Penumbra conserva la selección local existente; `DefaultSettings` solo se aplica en la primera importación, para no sobrescribir la elección del usuario. El paquete sigue ofreciendo las diez opciones. |
| ST-09 | Instalación de juego distinta de versión recomendada; best effort confirmado en patcher. | Generar e importar. | Se informan omisiones; paquete solo se publica si pasa integridad; categorías sin páginas aplicables permanecen visibles. |
| ST-10 | Windows y Linux con XIVLauncher.Core, Penumbra y mod importado. | Repetir ST-06 y ST-07 en ambos sistemas. | Importación, toggles y textos funcionan en ambos; registrar cualquier diferencia antes de aprobar release. |

## Criterio de aprobación humana

ST-01 a ST-05 deben completarse antes de validar formato. ST-06 a ST-10 requieren confirmación
humana explícita antes de publicar v0.5.0. Un ZIP válido o un import correcto sin comprobar texto
dentro de FFXIV no cierra las pruebas.
