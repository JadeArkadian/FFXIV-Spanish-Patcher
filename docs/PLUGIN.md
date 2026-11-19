# FFXIV Spanish Translation Manager — plugin de Dalamud

Guía de uso y soporte del plugin. Para el patcher de escritorio, consulta el
[`README.md`](../README.md).

## Qué hace

El plugin genera la traducción al castellano dentro del propio juego, la instala como un mod de
Penumbra y la mantiene al día. Sustituye los pasos manuales del patcher: no hay que crear un `.pmp`,
importarlo ni repetir el proceso tras cada actualización.

- Lee los datos necesarios de tu instalación de FFXIV; no modifica ningún archivo del juego.
- Las traducciones van dentro del plugin. No descarga nada mientras juegas.
- El mod que instala es el mismo que crea el patcher y se administra desde Penumbra como cualquier
  otro.
- Desinstalar o desactivar el plugin **nunca** borra el mod: se queda en Penumbra hasta que tú lo
  quites.

## Requisitos

- Final Fantasy XIV iniciado con XIVLauncher y Dalamud (en Linux, XIVLauncher.Core).
- **Penumbra** instalado y cargado. Si falta, el plugin te indica cómo instalarlo.
- El ajuste de Dalamud **Wait for plugins before game loads** activado. El plugin lo comprueba y
  puede activarlo por ti; lo llama *carga temprana*.

## Instalación

1. En el juego, escribe `/xlsettings` y abre la pestaña **Experimental**.
2. En **Custom Plugin Repositories**, pega esta dirección, pulsa **+** y guarda:

   ```text
   https://raw.githubusercontent.com/JadeArkadian/FFXIV-Spanish-Patcher/main/dalamud/pluginmaster.json
   ```

3. Escribe `/xlplugins`, busca **FFXIV Spanish Translation Manager** y pulsa **Install**.
4. Abre el gestor con `/ffxives`.
5. Si el gestor pide **Activar carga temprana**, acéptalo y reinicia el juego.
6. Pulsa **Instalar traducción**. Suele tardar menos de un minuto.
7. Cuando el estado cambie a **Reinicio necesario**, pulsa **Reiniciar juego**.

El reinicio es obligatorio la primera vez: el juego ya ha cargado sus textos en inglés y solo lee
los traducidos al arrancar. Desde el siguiente inicio, Dalamud espera a que Penumbra y el gestor
terminen antes de dejar continuar al juego.

### Si ya usabas el patcher

El plugin reconoce el mod creado por el patcher desde la v0.5.0 y lo adopta: conserva su carpeta y
tus categorías. Los paquetes de la v0.4 o anteriores no se reconocen; quítalos de Penumbra antes de
instalar la traducción con el plugin. Si hay varias copias del mod, la primera regeneración las
consolida.

Una vez instalado el plugin, no hace falta volver a usar el patcher.

## La ventana

Se abre con `/ffxives`, desde el botón de ajustes del plugin en `/xlplugins` o al pulsar una de sus
notificaciones.

### Estado

La tarjeta superior resume la situación y ofrece la acción que toca:

| Estado | Significado | Qué hacer |
| --- | --- | --- |
| Traducción no instalada | Aún no hay mod en Penumbra | **Instalar traducción** |
| Actualizando la traducción | Se está generando e instalando | Esperar, o **Cancelar** |
| Reinicio necesario | Hay cambios que el juego solo carga al arrancar | **Reiniciar juego** |
| Traducción activa | Instalada y cargada | Nada |
| Traducción activa · best effort | Activa, pero tu versión de FFXIV no es la recomendada | Ver [Compatibilidad](#compatibilidad) |
| Traducción desactivada | El mod está instalado pero apagado | Marcar **Traducción activa** |
| Traducción bloqueada | El gestor no ha podido validar el mod instalado | **Regenerar traducción** |

La casilla **Traducción activa** enciende o apaga el mod. Hace lo mismo que la casilla del mod en
Penumbra: si cambias una, la otra se actualiza.

Si cancelas una generación o esta falla, la traducción que ya tenías instalada no cambia.

### Categorías de traducción

Diez casillas deciden qué partes del juego se traducen:

- Misiones
- Nombres y lugares
- Clases y jobs
- Objetos
- Objetos de evento
- Coleccionables
- Acciones
- Logros
- Registro
- Interfaz

El mod contiene siempre la traducción completa; las casillas solo activan o desactivan cada bloque.
Son las mismas opciones que muestra Penumbra en el mod, y se sincronizan en los dos sentidos. Pasa
el cursor sobre una categoría para ver qué textos incluye.

Cambiar una categoría no regenera nada, pero requiere reiniciar el juego para verse. Si deshaces el
cambio antes de reiniciar, el aviso de reinicio desaparece.

El gestor administra únicamente la colección **Base/Default** de Penumbra, que es la que el juego
usa para estos textos. No toca tus otras colecciones. Si en esa colección el mod hereda sus ajustes
de otra, el gestor pide confirmación antes de crear un ajuste propio.

### Automatización

**Mantener la traducción instalada y actualizada automáticamente** está activado por defecto. Con
él, el gestor comprueba al cargar si ha cambiado el plugin, sus traducciones o la versión de FFXIV.
Solo entonces regenera el mod, durante el arranque y antes de que el juego continúe. Si no ha
cambiado nada, no hace ningún trabajo.

Con la opción desactivada, el gestor avisa de que hay una actualización pendiente y espera a que
pulses **Regenerar traducción**.

Las traducciones nuevas llegan con las actualizaciones del plugin, que instala Dalamud como las de
cualquier otro.

### Compatibilidad

Cada versión del plugin se prepara para una versión concreta de FFXIV. La tarjeta muestra la
versión del plugin, la del juego instalado y la recomendada.

Si no coinciden —lo habitual justo después de un parche del juego— el gestor no se bloquea: genera
la traducción en modo *best effort*, omitiendo lo que ya no encaje, y lo indica en el estado. La
cobertura completa vuelve con la siguiente versión del plugin. Los detalles de qué se omite están en
[`COMPATIBILITY.md`](COMPATIBILITY.md).

La verificación del mod es siempre obligatoria. Si falla, no se instala nada y se conserva la
versión anterior.

### Botones inferiores

- **Hitos de traducción**: historial de lo que se ha traducido en cada versión.
- **Historial de sesión**: lo que ha hecho el gestor desde que arrancaste el juego.
- **Aviso legal**.
- **Copiar diagnóstico**: copia al portapapeles un resumen técnico para adjuntarlo a una incidencia.

## Actualizar y desinstalar

- **Actualizar**: instala la nueva versión desde `/xlplugins`. En el siguiente arranque el gestor
  regenera la traducción por sí solo.
- **Desactivar la traducción sin desinstalar**: desmarca **Traducción activa** y reinicia.
- **Desinstalar el plugin**: quítalo desde `/xlplugins`. El mod sigue en Penumbra y seguirá
  funcionando, pero ya no se actualizará.
- **Quitar también el mod**: bórralo en Penumbra; se llama **FFXIV en Español**.

## Solución de problemas

Las dudas comunes al plugin y al patcher (macros, cierres del juego, parches de FFXIV, errores de
traducción) están en [`FAQ.md`](FAQ.md). Aquí se tratan las propias del plugin.

### El gestor pide instalar Penumbra

Sigue los dos pasos que muestra: añadir el repositorio oficial de Penumbra en los ajustes de Dalamud
e instalarlo desde `/xlplugins`. La pantalla se actualiza sola en cuanto Penumbra carga.

### «Penumbra no está cargado» o «no es compatible»

Penumbra está instalado pero desactivado, o su versión es demasiado antigua. Actívalo o actualízalo
desde `/xlplugins`; el gestor no puede hacerlo por ti.

### Pide reiniciar una y otra vez

Comprueba en `/xlsettings` → **General** que **Wait for plugins before game loads** sigue activado.
Sin ese ajuste, Penumbra puede cargar después de que el juego haya leído sus textos.

### He instalado la traducción y el juego sigue en inglés

1. Reinicia el juego: es obligatorio tras la primera instalación.
2. Comprueba que el estado es **Traducción activa**.
3. Comprueba que las categorías que esperas están marcadas.
4. En Penumbra, confirma que el mod **FFXIV en Español** está activo en la colección
   **Base/Default**.

Algunos textos siguen en inglés porque todavía no están traducidos; el avance está en el
[`README.md`](../README.md).

### Otros errores

Pulsa **Copiar diagnóstico** y envíalo junto con una descripción desde este formulario:

<https://tally.so/r/1ARKzp>

## Seguridad y límites

- El plugin no modifica los archivos originales del juego ni redistribuye archivos de Square Enix.
- Proyecto no oficial hecho por fans, sin afiliación con Square Enix.
- Usar Dalamud, Penumbra y mods en FFXIV queda bajo tu responsabilidad.

El aviso legal completo está en [`NOTICE.md`](../NOTICE.md).
