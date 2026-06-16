# data/translations

La traducción que se embebe en el ejecutable.

- **`data/translations.dat`** (versionado): blob gzip-JSONL con todo el corpus aprobado. Es la
  **fuente de registro** compacta que este repo distribuye y que la App embebe como recurso. ~9 MB.
- **`data/translations/jsonl/`** (NO versionado, en `.gitignore`): el corpus crudo (~60 MB, un
  fichero por sheet). Se sincroniza localmente desde el repo upstream **FFXIV-Spanish** solo para
  poder regenerar el blob. Su historial línea-a-línea vive en upstream.

Flujo:

```
upstream FFXIV-Spanish/data/translations/jsonl
  -> build/sync-translations.ps1   (copia local del corpus crudo, git-ignored)
  -> build/build-translations.ps1  (compacta a data/translations.dat, versionado)
  -> EmbeddedResource en FFXIVSpanishPatcher.App (F3)
  -> publish
```

Atajo: `build/sync-translations.ps1 -Build` hace ambos pasos. Actualizar la traducción = re-correr
esto + re-publicar (la App vuelve a embeber el blob nuevo). CI (F7) no reconstruye el blob: usa el
`data/translations.dat` ya versionado.
