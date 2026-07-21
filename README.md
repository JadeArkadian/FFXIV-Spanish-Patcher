# FFXIVSpanish Patcher

<p align="center">
  <img src="docs/assets/logo-git.png" alt="FFXIV en español" width="360">
</p>

Parcheador de español para **Final Fantasy XIV**.

Web del proyecto: <https://ffxivspanish.carrd.co/>

Esta aplicación genera un mod `.pmp` para Penumbra usando los archivos de tu propia instalación del
juego. No modifica los archivos originales de FFXIV y no incluye archivos del juego: extrae solo los
datos necesarios, aplica las traducciones incluidas en el programa y crea un paquete listo para
instalar.

La traducción todavía está en progreso. Gran parte de la interfaz, nombres de personajes, NPC,
monstruos, objetos y textos del sistema ya está traducida, pero parte del guion, conversaciones y
prosa narrativa puede seguir en inglés.

## Avance del proyecto

Datos del seguimiento EXD actual. El porcentaje principal mide líneas traducidas reales sobre líneas
traducibles exactas.

| Métrica | Valor | Avance |
| --- | ---: | --- |
| Avance total por líneas | 473.962/802.280 (59,1%) | ![59,1%](https://geps.dev/progress/59.1?barColor=f1c232) |
| Hojas OK | 2.152/6.956 (30,9%) | ![30,9%](https://geps.dev/progress/30.9?barColor=f0883e) |

| Área | Líneas traducidas | Avance |
| --- | ---: | --- |
| UI / menús / sistema visible | 25.749/25.749 (100,0%) | ![100,0%](https://geps.dev/progress/100?barColor=2ea043) |
| Objetos / economía / tiendas | 176.572/176.572 (100,0%) | ![100,0%](https://geps.dev/progress/100?barColor=2ea043) |
| Combate / acciones / duties | 34.911/34.927 (99,9%) | ![99,9%](https://geps.dev/progress/99.9?barColor=2ea043) |
| Mundo / NPCs / localizaciones | 88.327/88.327 (100,0%) | ![100,0%](https://geps.dev/progress/100?barColor=2ea043) |
| Crafting / recolección / progreso | 5.463/7.367 (74,2%) | ![74,2%](https://geps.dev/progress/74.2?barColor=f1c232) |
| Lore / diarios / colecciones | 20.745/20.745 (100,0%) | ![100,0%](https://geps.dev/progress/100?barColor=2ea043) |
| Minijuegos / eventos / perfil | 7.384/7.400 (99,8%) | ![99,8%](https://geps.dev/progress/99.8?barColor=2ea043) |
| Guion - quests | 58.710/261.923 (22,4%) | ![22,4%](https://geps.dev/progress/22.4?barColor=f0883e) |
| Guion - cinemáticas | 1.518/26.426 (5,7%) | ![5,7%](https://geps.dev/progress/5.7?barColor=da3633) |
| Guion - custom talk/NPC | 12.443/28.367 (43,9%) | ![43,9%](https://geps.dev/progress/43.9?barColor=f0883e) |
| Guion - eventos explícitos | 1.469/1.469 (100,0%) | ![100,0%](https://geps.dev/progress/100?barColor=2ea043) |
| Otros / revisión | 40.671/123.008 (33,1%) | ![33,1%](https://geps.dev/progress/33.1?barColor=f0883e) |

## Descargar

Ve a la página de **Releases** del proyecto y descarga el ZIP de tu sistema:

- `FFXIVSpanishPatcher-...-win-x64.zip` para Windows.
- `FFXIVSpanishPatcher-...-linux-x64.zip` para Linux.
- `FFXIVSpanishPatcher-...-osx-arm64.zip` para macOS (Apple Silicon).

Descomprime el ZIP y ejecuta `FFXIVSpanishPatcher`.

No hace falta instalar .NET ni ningún runtime aparte: el programa viene empaquetado como ejecutable
autónomo.

## Requisitos

- Final Fantasy XIV instalado.
- Penumbra instalado y funcionando en Dalamud.
- Una versión del juego compatible con la release descargada.

Cada release del parcheador se construye para una versión concreta de FFXIV. La aplicación muestra
esa versión recomendada al arrancar y la compara con la versión de tu instalación; si no coinciden,
puede generar textos rotos o cierres del juego aunque el mod llegue a crearse.

Si el juego acaba de actualizarse, lo recomendable es quitar el mod de Penumbra y esperar una release
nueva del parcheador, o en su defecto, recrear el mod a partir de los nuevos ficheros postparche. 
FFXIV cambia archivos internos en cada parche y un paquete generado para una versión antigua puede provocar 
textos rotos o cierres del juego.

## Crear el mod

1. Abre `FFXIVSpanishPatcher`.
2. Si detecta la instalación de FFXIV, la ruta aparecerá automáticamente.
3. Si no la detecta, pulsa **Examinar** y selecciona la carpeta del juego.
4. Elige las categorías que quieras traducir. Puedes dejar todo marcado si no tienes una preferencia
   concreta.
5. Pulsa **Generar mod**.
6. Cuando termine, abre la carpeta de salida desde la propia aplicación.

El archivo generado tendrá un nombre parecido a:

```text
FFXIVSpanish-2026-06-24_18-30-00.pmp
```

Por defecto se guarda en:

```text
Documentos/FFXIVSpanish Patcher/Output
```

## Instalar en Penumbra

1. Abre Penumbra dentro del juego.
2. Importa el `.pmp` generado por el parcheador.
3. Activa el mod.
4. En los ajustes de Dalamud, marca **Wait for plugins before game loads**. Si no se hace este paso los textos no se cargarán adecuadamente.
5. Reinicia el juego.

Ese ajuste es importante: si Dalamud carga tarde, Penumbra puede no aplicar el mod a tiempo y verás
el juego sin traducir.

## Actualizar o quitar el mod

Cuando salga una release nueva:

1. Descarga el nuevo parcheador.
2. Genera un `.pmp` nuevo.
3. Quita o desactiva el paquete anterior en Penumbra.
4. Importa y activa el paquete nuevo.

Después de un parche oficial de FFXIV, desactiva el mod antiguo hasta que haya una versión nueva de
este proyecto.

## Solución de problemas (Troubleshooting)

### El juego se cierra en ciertos momentos

Asegúrate de haber creado el mod con la última versión disponible del parcheador y con una versión
compatible de Final Fantasy XIV. Si ya tenías un paquete anterior instalado, quítalo de Penumbra,
genera un `.pmp` nuevo e instala ese paquete nuevo.

Si sigue pasando, desinstala o desactiva el mod y envía una incidencia desde este formulario:

https://tally.so/r/1ARKzp

Incluye, si puedes, cuándo ocurre el cierre, qué estabas haciendo, la versión del juego, la versión
del parcheador y si el problema desaparece al desactivar el mod.

### Hay errores de traducción, textos solapados o partes en inglés

La traducción sigue en progreso y puede haber textos con errores, mala colocación, mezcla de español
e inglés o calidad irregular. Puedes ayudar enviando feedback desde este formulario:

https://tally.so/r/1ARKzp

Una captura y el lugar exacto donde aparece el texto ayudan mucho.

### He cargado el mod en Penumbra pero no veo cambios

En los ajustes de Dalamud, marca **Wait for plugins before game loads** y reinicia el juego. En
Penumbra, comprueba también que el paquete esté instalado, activo y habilitado para el personaje o
colección que estás usando.

Si no se hacen estos pasos, Penumbra puede cargar tarde y los textos no se aplicarán.

### Mis macros han dejado de funcionar

Es un fallo conocido. Suele pasar cuando una macro invoca acciones por su nombre en inglés. Al
aplicar la traducción, los nombres de esas acciones pasan a estar en español.

Puedes traducir los nombres de las acciones dentro de tus macros o generar el mod sin aplicar la
categoría de acciones, habilidades, rasgos y estados.

## Qué se traduce

La aplicación permite activar o desactivar bloques de traducción:

- Misiones.
- Nombres de NPC, enemigos, lugares y términos del mundo.
- Clases y jobs.
- Objetos.
- Objetos de evento.
- Coleccionables.
- Acciones, habilidades, rasgos y estados.
- Logros.
- Registro de combate y mensajes del sistema.
- Interfaz.

Algunos textos pueden seguir en inglés aunque la categoría esté marcada. Eso suele significar que esa
parte aún no está traducida o que el juego cambió el dato en un parche reciente.

## Avisar de errores

Si encuentras bugs, textos mal colocados, traducciones raras o inconsistencias, puedes enviarlo aquí:

https://tally.so/r/1ARKzp

Lo más útil es incluir:

- Captura del texto.
- Zona, misión, NPC, objeto o menú donde aparece.
- Qué esperabas ver.
- Versión del juego y versión del parcheador.

## Seguridad y límites

- El parcheador no toca los archivos originales del juego.
- El `.pmp` se instala como cualquier otro mod de Penumbra.
- No se redistribuyen archivos de Square Enix.
- El proyecto no está afiliado a Square Enix.
- Usar mods en FFXIV depende de herramientas externas y queda bajo tu responsabilidad.

Lee también `NOTICE.md` para los avisos legales completos. Si quieres contribuir código,
documentación o traducciones, revisa `CONTRIBUTING.md`; las contribuciones asistidas por IA se rigen
además por `AI_USAGE.md`.

## Compilar desde código

Para desarrollo necesitas el SDK indicado en `global.json`.

```powershell
dotnet build
dotnet test
```

Para publicar un ejecutable autónomo de Windows:

```powershell
dotnet publish src/FFXIVSpanishPatcher.App/FFXIVSpanishPatcher.App.csproj -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

El diseño técnico y las decisiones internas están en `docs/DESIGN.md`.
