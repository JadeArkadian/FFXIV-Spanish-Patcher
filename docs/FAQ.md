# Preguntas frecuentes y solución de problemas

Dudas comunes al plugin de Dalamud y al patcher de escritorio. Los problemas propios de la
instalación del plugin (Penumbra ausente, carga temprana, reinicios) están en
[`PLUGIN.md`](PLUGIN.md#solución-de-problemas), y las preguntas sobre el plugin en sí (por qué
necesita Penumbra, si hace falta el patcher, cómo se actualiza) en [`PLUGIN_FAQ.md`](PLUGIN_FAQ.md).

## El juego sigue en inglés

Comprueba, por este orden:

1. Has reiniciado el juego después de instalar o importar la traducción. El juego solo lee sus
   textos al arrancar.
2. En los ajustes de Dalamud, **Wait for plugins before game loads** está marcado. Sin él, Penumbra
   puede cargar tarde y la traducción no se aplica.
3. En Penumbra, el mod **FFXIV en Español** está activo en la colección **Base/Default**. Activarlo
   solo en otra colección no sirve para estos textos.
4. Las categorías que esperas están marcadas, en el plugin o en las opciones del mod en Penumbra.

Si solo algunos textos siguen en inglés, lo más probable es que aún no estén traducidos; consulta
el avance en el [`README.md`](../README.md#avance-del-proyecto).

## Hay errores de traducción, textos solapados o mezcla de idiomas

La traducción sigue en progreso y puede tener errores, textos mal colocados o calidad irregular.
Puedes avisar desde este formulario:

<https://tally.so/r/1ARKzp>

Una captura y el lugar exacto donde aparece el texto (zona, misión, NPC, objeto o menú) ayudan
mucho.

## El juego se cierra en ciertos momentos

Asegúrate de usar la última versión del proyecto y una versión de FFXIV compatible con ella:

- **Plugin**: actualízalo desde `/xlplugins`; regenera la traducción al arrancar.
- **Patcher**: descarga la última versión, genera un `.pmp` nuevo, quita de Penumbra el paquete
  anterior e importa el nuevo.

Si sigue pasando, desactiva el mod —la casilla **Traducción activa** del plugin o la del mod en
Penumbra— y envía una incidencia desde el [formulario](https://tally.so/r/1ARKzp). Indica cuándo
ocurre el cierre, qué estabas haciendo, la versión del juego, la del plugin o patcher y si el
problema desaparece al desactivar el mod.

## Mis macros han dejado de funcionar

Pasa cuando una macro invoca acciones por su nombre en inglés: con la traducción aplicada, esos
nombres están en español y el juego ya no los encuentra.

Tienes dos soluciones:

- Traducir los nombres de las acciones dentro de tus macros.
- Desmarcar la categoría **Acciones**, en el plugin o en las opciones del mod en Penumbra, y
  reiniciar el juego. Las acciones, rasgos y estados volverán a verse en inglés.

## Acaba de salir un parche de FFXIV

Cada versión del proyecto se prepara para una versión concreta del juego. Tras un parche oficial,
parte de la traducción puede dejar de encajar hasta que se publique una versión nueva.

- **Plugin**: regenera la traducción por sí solo y omite lo que ya no encaja. El estado pasa a
  **Traducción activa · best effort**.
- **Patcher**: avisa de que la versión es distinta y pide confirmación antes de generar. Conviene
  crear un `.pmp` nuevo y sustituir el anterior.

Si tras el parche ves fallos, desactiva el mod hasta que haya una versión nueva. Qué se omite
exactamente está explicado en [`COMPATIBILITY.md`](COMPATIBILITY.md).

## El patcher dice «Mod verificado con omisiones»

El paquete se ha comprobado y es utilizable, pero algunas traducciones no existían o no pudieron
aplicarse a la versión de FFXIV instalada. La consola indica cuántas hojas, páginas y filas se han
omitido. Generar con la versión recomendada del juego ofrece la cobertura más completa.

## Cómo copiar el registro del patcher

Arrastra el ratón sobre las líneas de la consola que necesites, o pulsa **Seleccionar todo**
(`Ctrl+A`), y copia con `Ctrl+C` o con el botón **Copiar log**.

La consola conserva todo lo que emite el proceso. Si te piden más detalle para una incidencia,
inicia el patcher con el argumento `--debug`.

En el plugin, el equivalente es el botón **Copiar diagnóstico**.

## Quiero quitar la traducción

Borra el mod **FFXIV en Español** en Penumbra y reinicia el juego. Ni el plugin ni el patcher
modifican los archivos originales del juego, así que no hay nada más que restaurar.

Desinstalar el plugin no borra el mod: hay que quitarlo en Penumbra.
