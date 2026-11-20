# Repositorio de Dalamud

Esta carpeta es el repositorio personalizado de Dalamud desde el que se instala el plugin
**FFXIV Spanish Translation Manager**.

Dirección que hay que añadir en `/xlsettings` → **Experimental** → **Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/JadeArkadian/FFXIV-Spanish-Patcher/main/dalamud/pluginmaster.json
```

Las instrucciones de uso están en [`docs/PLUGIN.md`](../docs/PLUGIN.md).

## `pluginmaster.json`

Describe la versión publicada del plugin y de dónde descargarla. Lo escribe el workflow de release
al final de cada tag `vX.Y.Z`, cuando el ZIP del plugin ya está adjunto a la release de GitHub y su
checksum se ha comprobado. Apunta siempre al ZIP de esa versión concreta, nunca a `latest`.

No lo edites a mano. El archivo no existe hasta la primera release que incluye el plugin.

## Retirar una versión

Para volver a ofrecer la versión anterior, revierte el commit `chore(dalamud): publish plugin vX.Y.Z`
en `main`. El archivo anterior apunta al ZIP anterior, que sigue en su release. No hace falta borrar
ninguna release.

Dalamud no desinstala ni degrada una versión ya instalada: quien haya actualizado se queda con ella
hasta que se publique una versión superior.
