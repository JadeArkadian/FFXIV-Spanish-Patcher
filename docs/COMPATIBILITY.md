# Compatibilidad entre versiones de FFXIV

## Principio

El patcher compara la versión instalada (`ffxivgame.ver`) con la versión para la que se preparó el
corpus (`data/recommended-game-version.txt`). La comparación es una igualdad exacta de cadenas; el
programa no intenta decidir cuál es más antigua.

Una diferencia no bloquea la generación. La interfaz avisa, explica la pérdida de cobertura posible
y exige una confirmación explícita. Tras confirmarla, el pipeline aplica *best effort*: conserva todas
las páginas válidas y omite de forma auditable el contenido que no existe en esa instalación.

Esto permite probar deliberadamente con una versión antigua del juego sin esconder el riesgo ni
convertir la prueba en un falso error.

## Decisiones recuperables y fatales

| Situación | Acción | Resultado posible |
| --- | --- | --- |
| Hoja EXH inexistente | Omitir hoja y contar entradas | Parcial |
| Fila fuera del rango de páginas | Omitir fila y agrupar aviso por hoja | Parcial |
| Página EXD inexistente o ilegible de forma aislada | Omitir página y contar entradas | Parcial |
| Fuente esperada no encontrada (`miss`) | Omitir reemplazo y registrar `rowId` | Parcial |
| SeString incompatible | Omitir fila salvo diagnóstico forzado | Parcial |
| Variante EXD no soportada | Omitir página y avisar | Parcial |
| Coincidencia baja con versiones iguales/desconocidas | Abortar por contaminación | Fatal |
| Coincidencia baja después de confirmar mismatch | Conservar páginas válidas | Parcial |
| Ninguna traducción aplicada | No crear paquete vacío | Fatal |
| Ninguna página legible | Abortar por datos del juego | Fatal |
| Error de escritura | No reemplazar la salida | Fatal |
| Fallo de integridad | No reemplazar la salida | Fatal |

`Ok` indica un paquete verificado sin omisiones. `PackagedWithMisses` indica un paquete verificado y
utilizable con menos cobertura. Cualquier otro estado significa que no se ha publicado un `.pmp`
nuevo.

## Estadísticas

`PatchStatistics` separa:

- entradas candidatas;
- escrituras aplicadas;
- reemplazos fallidos;
- hojas y páginas ausentes;
- entradas afectadas por esas ausencias;
- filas no resolubles en la versión instalada;
- filas SeString omitidas;
- páginas no soportadas;
- páginas parcheadas y omitidas.

La tasa del guard de contaminación usa únicamente filas de páginas que pudieron leerse. Una hoja o
página ausente no se interpreta como una base contaminada.

## Integridad

La verificación es siempre obligatoria. El pipeline construye un `.pmp` temporal junto a la salida,
lo verifica y solo entonces lo promueve atómicamente. Si la comprobación falla, conserva el paquete
anterior, informa en consola y limpia staging y temporales.

## Prueba manual con una instalación antigua

1. Mantener intacta la instalación de prueba y su `ffxivgame.ver`.
2. Abrir el patcher y confirmar que aparece **Versión diferente**.
3. Pulsar **Crear traducción para Penumbra**.
4. Leer el modal y elegir **Generar de todos modos**.
5. Comprobar que la consola avisa del modo *best effort*.
6. Verificar que hojas, páginas y `misses` aparecen como avisos agrupados.
7. Si se crea un paquete, confirmar el estado **Mod verificado con omisiones**.
8. Si no existe ninguna coincidencia útil, confirmar que no se crea un paquete vacío.

Esta prueba no autoriza a modificar ni actualizar automáticamente la instalación.
