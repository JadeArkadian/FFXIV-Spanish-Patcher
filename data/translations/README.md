# data/translations

La traducción que se embebe en el ejecutable.

- **`data/translations.dat`** (versionado): blob gzip-JSONL con las filas **empaquetables**
  (`status ∈ {approved, gold}` + target no vacío + sourceKey completo); las demás no se aplican y se
  excluyen. Además cada fila está **proyectada** a los campos que el runtime lee (`source`, `target`,
  `status`, `sourceKey`); los metadatos de procedencia (`hash`, `id`, `category`, `notes`…) se omiten
  para comprimir ~65 %. Es la **fuente de registro** compacta que este repo distribuye y que la App
  embebe como recurso. ~7 MB.
- **`data/translations/jsonl/`** (NO versionado, en `.gitignore`): el corpus crudo (~60 MB, un
  fichero por sheet). Se sincroniza localmente desde el repo upstream **FFXIV-Spanish** solo para
  poder regenerar el blob. Su historial línea-a-línea vive en upstream.

Flujo:

```
upstream FFXIV-Spanish/data/translations/jsonl
  -> build/sync-translations.py    (copia local del corpus crudo, git-ignored)
  -> build/build-translations.py   (filtra approved+gold y compacta a data/translations.dat, versionado)
  -> EmbeddedResource en FFXIVSpanishPatcher.App (F3)
  -> publish
```

Atajo: `python build/sync-translations.py --build` hace ambos pasos. Actualizar la traducción = re-correr
esto + re-publicar (la App vuelve a embeber el blob nuevo). CI (F7) no reconstruye el blob: usa el
`data/translations.dat` ya versionado.
