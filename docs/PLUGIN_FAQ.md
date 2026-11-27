# Preguntas frecuentes del plugin

Dudas habituales sobre **FFXIV Spanish Translation Manager**, el plugin de Dalamud. Cómo instalarlo
y usarlo está en [`PLUGIN.md`](PLUGIN.md); las preguntas propias del patcher de escritorio están en
[`PATCHER_FAQ.md`](PATCHER_FAQ.md). Su sección
[Problemas en el juego](PATCHER_FAQ.md#problemas-en-el-juego) (macros, cierres, errores de
traducción) vale también para quien usa el plugin.

## Sobre el plugin

### ¿Por qué necesita Penumbra para funcionar?

Es una decisión de diseño. Sería posible meter la traducción en un único plugin sin depender de
nadie, pero sustituir recursos del juego es un terreno delicado, complejo y muy especializado, y
Penumbra lleva años haciéndolo bien. Por eso el plugin delega esa responsabilidad por completo: él
genera la traducción y se la entrega a Penumbra como un mod; Penumbra es quien la aplica.

Además tiene ventajas para ti: la traducción aparece en Penumbra como cualquier otro mod, puedes
desactivarla o borrarla desde allí y sigue funcionando aunque desinstales el plugin.

### ¿Necesito el ejecutable (el patcher) si uso el plugin?

No. El plugin existe para distribuir la traducción con la menor fricción posible y de forma
automática. Una vez instalado, no tienes que volver a GitHub ni a Nexus Mods para ver si hay una
versión nueva: el propio plugin te avisa y, cuando se actualiza, regenera el mod en Penumbra por sí
solo.

### Entonces, ¿para qué sigue existiendo el patcher?

Como alternativa manual. Genera un archivo `.pmp` que importas tú en Penumbra, sin añadir ningún
repositorio a Dalamud. A cambio, hay que repetir el proceso con cada versión nueva. Los dos crean
el mismo mod.

### Ya tengo la traducción instalada con el patcher. ¿Tengo que quitarla?

No, si la generaste con la v0.5.0 o posterior: el plugin reconoce ese mod y lo adopta, conservando
su carpeta y tus categorías. Los paquetes de la v0.4 o anteriores sí hay que quitarlos de Penumbra
antes de instalar la traducción con el plugin.

### ¿En qué idioma tengo que tener el juego?

En inglés. La traducción sustituye los textos del cliente en inglés; con el juego en japonés,
alemán o francés no verás ningún cambio.

### ¿Modifica los archivos del juego?

No. El plugin lee los datos que necesita de tu instalación y genera un mod aparte; Penumbra lo
aplica mientras juegas. Los archivos originales no se tocan, así que no hay nada que reparar ni
restaurar si decides dejar de usarlo.

## Actualizaciones

### ¿Cómo me llegan las traducciones nuevas?

Las traducciones van dentro del plugin, así que llegan con cada versión nueva de este. Dalamud la
instala como la de cualquier otro plugin y, en el siguiente arranque, el gestor regenera el mod
automáticamente.

Si Dalamud no lo actualiza por la razón que sea, el plugin te avisa al arrancar y en su ventana, con
un botón que abre las actualizaciones de `/xlplugins`.

### ¿Qué pasa si no actualizo el plugin?

Sigue funcionando con la versión que tengas, pero con sus traducciones antiguas. Tras un parche de
FFXIV, una versión antigua también puede perder parte de la cobertura hasta que actualices.

### ¿Qué pasa cuando sale un parche de FFXIV?

El plugin lo detecta al arrancar y regenera la traducción para la versión nueva del juego, omitiendo
lo que ya no encaje. El estado pasa a **Traducción activa · best effort** y la cobertura completa
vuelve con la siguiente versión del plugin. Si tras el parche ves fallos, desmarca **Traducción
activa** hasta entonces.

### ¿Puedo evitar que regenere la traducción por su cuenta?

Sí. Desmarca **Mantener la traducción instalada y actualizada automáticamente**. El gestor te
avisará de que hay una actualización pendiente y esperará a que pulses **Regenerar traducción**.

## Arranque y rendimiento

### ¿Por qué tengo que reiniciar el juego?

El juego lee sus textos una sola vez, al arrancar. Después de instalar la traducción, regenerarla o
cambiar una categoría, los textos nuevos no se ven hasta el siguiente inicio.

### ¿Qué es la «carga temprana» y por qué la pide?

Es el ajuste de Dalamud **Wait for plugins before game loads**. Con él, el juego espera a que
Penumbra y el gestor estén listos antes de leer sus textos. Sin él, Penumbra puede cargar tarde y el
juego arranca en inglés aunque el mod esté instalado.

### ¿Hace más lento el arranque?

Normalmente no. Al cargar, el gestor solo comprueba si ha cambiado algo; si no, no hace ningún
trabajo. Solo regenera el mod cuando cambia el plugin, sus traducciones o la versión de FFXIV, y
suele tardar menos de un minuto.

### ¿Afecta al rendimiento mientras juego?

No. El trabajo se hace al arrancar. Durante la partida, quien sirve los textos traducidos es
Penumbra, igual que con cualquier otro mod.

## Convivencia con otros mods

### ¿Toca mis otros mods o colecciones de Penumbra?

No. El gestor administra únicamente su mod, **FFXIV en Español**, y solo en la colección
**Base/Default**, que es la que el juego usa para estos textos. No cambia tus otros mods ni tus
otras colecciones.

### ¿Puedo traducir solo una parte del juego?

Sí. Las diez categorías de la ventana (misiones, objetos, acciones, interfaz…) se activan y
desactivan por separado. Son las mismas opciones que muestra Penumbra en el mod, y se sincronizan en
los dos sentidos.

### He encontrado carpetas que empiezan por `.ffxivspanish` o `.FFXIVSpanish` en mi carpeta de mods

Son carpetas de trabajo del plugin: ahí prepara la traducción nueva antes de instalarla, y las borra
al terminar. Si cerraste el juego mientras generaba, puede quedar alguna; la siguiente generación la
limpia sola. No son la traducción instalada: esa vive en la carpeta `FFXIVSpanish`, sin punto
delante.

## Desinstalar

### ¿Qué pasa si desinstalo el plugin?

El mod se queda en Penumbra y sigue funcionando, pero deja de actualizarse. El plugin nunca borra el
mod, ni al desactivarlo ni al desinstalarlo.

### ¿Cómo quito la traducción del todo?

Borra el mod **FFXIV en Español** en Penumbra, desinstala el plugin desde `/xlplugins` y reinicia el
juego.

### Solo quiero volver al inglés un rato

Desmarca **Traducción activa** en la ventana del plugin, o la casilla del mod en Penumbra, y
reinicia el juego. Para recuperarla, vuelve a marcarla.

## ¿Es seguro usarlo?

El plugin no modifica los archivos originales del juego ni redistribuye archivos de Square Enix. Es
un proyecto no oficial hecho por fans, sin afiliación con Square Enix, y usar Dalamud, Penumbra y
mods en FFXIV queda bajo tu responsabilidad. El aviso legal completo está en
[`NOTICE.md`](../NOTICE.md).
