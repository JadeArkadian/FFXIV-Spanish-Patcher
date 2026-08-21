# Smoke tests v0.6.0 — etapa 5: categorías Base/Default

La etapa 5 verifica que el gestor lee y reconcilia el grupo estable **Categorías de traducción**
solo en la colección **Base/Default**. Aún no hay ventana: modificar una casilla desde el plugin
llegará en la etapa 7. No se genera ni instala ningún mod durante estas pruebas.

## Estado inicial común

- Usar el ZIP de desarrollo actualizado de `FFXIVSpanishTranslationManager`.
- Penumbra habilitado y con directorio de mods válido; registrar sus versiones.
- Instalar en Penumbra un mod **v0.5.0 o posterior** de FFXIV en Español, generado después de la
  migración a categorías v4. Debe contener el grupo de diez categorías; un `.pmp` v0.4.0 o anterior
  es adoptable, pero no sirve para E5-ST-01 porque no tiene ese grupo.
- Para que el gestor lo identifique, su `meta.json` debe tener el tag `ffxiv-spanish-managed` o la
  combinación de nombre `FFXIV en Español` y Website del proyecto.
- Anotar qué colección está asignada a **Base/Default**. No modificar Interface, Current ni una
  colección individual salvo E5-ST-03.

| ID | Estado inicial | Prueba | Resultado esperado |
| --- | --- | --- | --- |
| E5-ST-00 | Mod v0.4.0 o anterior identificado; plugin deshabilitado. | Habilitar el plugin y revisar el registro cuando Penumbra esté `Ready`. | Aparece una sola vez `Categorías: CategoryGroupMissing. El grupo de categorías no existe en el mod instalado; regenera la traducción.` No se recarga, modifica ni elimina el mod. Este resultado acredita la detección segura de la migración pendiente; después instalar un `.pmp` v0.5.0+ para E5-ST-01. |
| E5-ST-01 | Mod v0.5.0+ con el grupo de categorías identificado; plugin deshabilitado. | Habilitar el plugin y revisar el registro cuando Penumbra esté `Ready`. | Aparece una sola vez `Categorías: Ready` o `Categorías: Inherited`, nombrando Base/Default. No se recarga el mod, no cambia ninguna casilla y no se generan archivos. |
| E5-ST-02 | Plugin y mod cargados tras E5-ST-01. | En la pestaña de ajustes del mod dentro de Penumbra, cambiar una casilla del grupo **Categorías de traducción** de Base/Default. | El gestor registra de nuevo `Categorías: Ready` con Base/Default. El valor elegido en Penumbra se conserva; no aparece una segunda escritura ni bucle de eventos. |
| E5-ST-03 | Igual que E5-ST-02, con una colección individual activa para un personaje. | Cambiar la misma casilla únicamente en esa colección individual. | El gestor no anuncia ni aplica cambios sobre Base/Default. El ajuste individual queda limitado a esa colección. |
| E5-ST-04 | Repetir E5-ST-01 y E5-ST-02 en el segundo sistema operativo. | Cargar el gestor y cambiar una categoría en Base/Default. | Mismo estado y conservación del ajuste, sin excepción de IPC. |

## Cobertura automática de la escritura del plugin

La suite invoca la escritura del servicio directamente y acredita que envía las diez opciones como
un único ajuste de grupo, vuelve a leer el resultado, exige confirmación antes de romper herencia,
revierte el snapshot ante error, ignora otros mods y no forma bucles. La interfaz que expone esa
acción al usuario queda fuera de esta etapa y no debe simularse con comandos ocultos.
