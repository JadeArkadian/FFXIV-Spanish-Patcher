# Preguntas frecuentes del patcher

Dudas habituales sobre **FFXIVSpanish Patcher**, la aplicación de escritorio. Cómo descargarlo y
usarlo está en el [`README.md`](../README.md#patcher-de-escritorio); las preguntas propias del
plugin de Dalamud están en [`PLUGIN_FAQ.md`](PLUGIN_FAQ.md). La sección
[Problemas en el juego](#problemas-en-el-juego) vale también para quien usa el plugin.

## Sobre el patcher

### ¿Qué hace exactamente el patcher?

Lee de tu instalación de FFXIV los textos que hay que traducir, les aplica la traducción que lleva
dentro y genera un archivo `.pmp`: un mod que importas tú en Penumbra. No instala nada en el juego
ni se queda en marcha; cuando tienes el `.pmp`, puedes cerrarlo.

### ¿Me conviene el patcher o el plugin?

El plugin es mas conveniente, salvo que prefieras hacerlo a mano. Los dos crean el mismo mod, 
pero el plugin lo instala en Penumbra y lo mantiene al día por sí solo. El patcher sigue disponible
como alternativa manual.

### Uso el patcher y quiero pasarme al plugin. ¿Tengo que quitar el mod?

Depende de la versión. Si lo generaste con la v0.5.0 o posterior, no es necesario: el plugin
reconoce ese mod y lo adopta, conservando su carpeta y tus categorías. Los paquetes de la v0.4 o
anteriores sí hay que quitarlos de Penumbra antes. Una vez instalado el plugin, no hace falta volver
a usar el patcher.

### ¿Tengo que instalar .NET o algo más?

No. El patcher es un ejecutable autónomo: se descomprime y se ejecuta. Lo que sí necesitas es Final
Fantasy XIV con Dalamud y Penumbra funcionando, porque el mod se importa con Penumbra.

### Windows desconfía del ejecutable. ¿Es normal?

Puede pasar. Las versiones de Windows llevan una firma autofirmada, no un certificado emitido por
una autoridad reconocida, así que Windows puede mostrar una advertencia al abrirlo. Descárgalo solo
desde la página de **Releases** del proyecto y comprueba su SHA-256 como explica el
[`README.md`](../README.md#verificar-descarga).

### ¿En qué idioma tengo que tener el juego?

En inglés. La traducción sustituye los textos del cliente en inglés; con el juego en japonés,
alemán o francés no verás ningún cambio.

### ¿Modifica los archivos del juego?

No. El patcher solo lee los datos que necesita de tu instalación y escribe el `.pmp` en tu carpeta
de documentos. Los archivos originales no se tocan, así que no hay nada que reparar ni restaurar si
decides dejar de usarlo.

## Actualizaciones

### ¿Cómo me llegan las traducciones nuevas?

Las traducciones van dentro del patcher, así que llegan con cada versión nueva de este. Al abrirse,
el patcher comprueba si hay una versión más reciente y lo indica en la consola con el enlace de
descarga.

No se actualiza solo: descarga la versión nueva, genera otro `.pmp` y sustituye el anterior en
Penumbra.

### ¿Tengo que quitar el mod anterior al actualizar?

Sí. Quita o desactiva el paquete anterior en Penumbra y después importa y activa el nuevo, para que
no convivan dos copias de la traducción.

### ¿Qué pasa si no actualizo el patcher?

El mod que ya tienes sigue funcionando, pero con sus traducciones antiguas. Tras un parche de FFXIV
puede dejar de encajar con el juego (lo cual puede ocasionar problemas, incluyendo crasheos del juego) hasta que generes uno nuevo.

### ¿Qué pasa cuando sale un parche de FFXIV?

Cada versión del patcher se prepara para una versión concreta del juego, y el mod que ya tienes no
se regenera solo. Deberias desactívarlo en Penumbra hasta que haya una versión nueva del patcher
para evitar problemas.

Si generas un mod antes de que la haya, el patcher avisa de que la versión del juego es distinta y
pide confirmación. Con ella trabaja en modo *best effort*, es decir, omite lo que ya no encaja y lo
contabiliza en la consola. Qué se omite exactamente está explicado en
[`COMPATIBILITY.md`](COMPATIBILITY.md).

## Crear el mod

### El patcher no encuentra mi instalación de FFXIV. ¿Qué hago?

Pulsa **Selecciona FFXIV** y elige la carpeta del juego. La detección automática cubre las
ubicaciones habituales, pero no todas.

### ¿Puedo traducir solo una parte del juego?

Sí. En **Opciones avanzadas y categorías** eliges qué categorías quedarán activadas al importar el
mod; tiene que quedar al menos una. El paquete incluye siempre la traducción completa, así que
después puedes cambiarlas en las opciones del mod en Penumbra sin generar otro.

### ¿Dónde se guarda el archivo generado?

Por defecto en `Documentos/FFXIVSpanish Patcher/Output`, con un nombre parecido a
`FFXIVSpanish-2026-06-24_18-30-00.pmp`. Al terminar, la propia aplicación ofrece abrir esa carpeta.

### ¿Qué significa «Mod verificado con omisiones»?

El paquete se ha comprobado y es utilizable, pero algunas traducciones no existían o no pudieron
aplicarse a la versión de FFXIV instalada. La consola indica cuántas hojas, páginas y filas se han
omitido. Generar con la versión recomendada del juego ofrece la cobertura más completa.

### ¿Cómo copio el registro para enviar una incidencia?

Arrastra el ratón sobre las líneas de la consola que necesites, o pulsa **Seleccionar todo**
(`Ctrl+A`), y copia con `Ctrl+C` o con el botón **Copiar log**. Si te piden más detalle, inicia el
patcher con el argumento `--debug`.

En el plugin, el equivalente es el botón **Copiar diagnóstico**.

## Problemas en el juego

Estas respuestas valen igual para el patcher y para el plugin: los dos instalan el mismo mod.

### ¿Por qué el juego sigue en inglés?

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

### He visto errores de traducción, textos solapados o mezcla de idiomas. ¿Cómo aviso?

La traducción sigue en progreso y puede tener errores, textos mal colocados o calidad irregular.
Puedes avisar desde este formulario:

<https://tally.so/r/1ARKzp>

Una captura y el lugar exacto donde aparece el texto (zona, misión, NPC, objeto o menú) ayudan
mucho.

### ¿Qué hago si el juego se cierra?

Asegúrate de usar la última versión del proyecto y una versión de FFXIV compatible con ella:

- **Patcher**: descarga la última versión, genera un `.pmp` nuevo, quita de Penumbra el paquete
  anterior e importa el nuevo.
- **Plugin**: actualízalo desde `/xlplugins`; regenera la traducción al arrancar.

Si sigue pasando, desactiva el mod —su casilla en Penumbra o **Traducción activa** en el plugin— y
envía una incidencia desde el [formulario](https://tally.so/r/1ARKzp). Indica cuándo ocurre el
cierre, qué estabas haciendo, la versión del juego, la del patcher o plugin y si el problema
desaparece al desactivar el mod.

### ¿Por qué mis macros han dejado de funcionar?

Pasa cuando una macro invoca acciones por su nombre en inglés: con la traducción aplicada, esos
nombres están en español y el juego ya no los encuentra.

Tienes dos soluciones:

- Traducir los nombres de las acciones dentro de tus macros.
- Desmarcar la categoría **Acciones**, en las opciones del mod en Penumbra o en el plugin, y
  reiniciar el juego. Las acciones, rasgos y estados volverán a verse en inglés.

## Quitar la traducción

### ¿Cómo quito la traducción del todo?

Borra el mod **FFXIV en Español** en Penumbra y reinicia el juego. El patcher no instala nada: para
quitarlo basta con borrar su carpeta y, si quieres, la de salida.

### Solo quiero volver al inglés un rato

Desactiva el mod en Penumbra y reinicia el juego. Para recuperar la traducción, vuelve a activarlo.

## ¿Es seguro usarlo?

El patcher no modifica los archivos originales del juego ni redistribuye archivos de Square Enix. Es
un proyecto no oficial hecho por fans, sin afiliación con Square Enix, y usar Dalamud, Penumbra y
mods en FFXIV queda bajo tu responsabilidad. El aviso legal completo está en
[`NOTICE.md`](../NOTICE.md).
