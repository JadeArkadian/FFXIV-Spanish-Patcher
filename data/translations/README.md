# data/translations

La traducción que se embebe en el ejecutable.

- **`data/translations.dat`** (versionado): blob Brotli-JSONL con las filas **empaquetables**
  (`status ∈ {approved, gold}` + target no vacío + sourceKey completo); las demás no se aplican y se
  excluyen. Además cada fila está **proyectada** a los campos que el runtime lee (`source`, `target`,
  `status`, `sourceKey`); los metadatos de procedencia (`hash`, `id`, `category`, `notes`…) se omiten.
  Se comprime con **Brotli** (q11; .NET lo lee con `BrotliStream` nativo). Es la **fuente de registro**
  compacta que este repo distribuye y que la App embebe como recurso. Tamaño actual: ~8.5 MiB
  (`8928043` bytes el 2026-07-03).
  Este fichero vive en **Git LFS** (`*.dat filter=lfs`) porque se regenera al menos una vez por
  release y su historial binario crecería rápido dentro del repo normal. Cualquier workflow o clon
  que vaya a compilar/publicar debe traer el objeto LFS real (`actions/checkout` con `lfs: true`, o
  `git lfs pull` localmente). Si se embebe el puntero LFS de texto en vez del blob real,
  `BrotliStream` fallará con datos inválidos al cargar traducciones.
- **`data/recommended-game-version.txt`** (versionado): versión de FFXIV asociada al blob/release.
  La App la embebe como recurso y avisa si la instalación local (`ffxivgame.ver`) no coincide. Valor
  actual: `2026.06.18.0000.0000`.
- **`data/translations/jsonl/`** (NO versionado, en `.gitignore`): el corpus crudo (~60 MB, un
  fichero por sheet). Se sincroniza localmente desde el repo upstream **FFXIV-Spanish** solo para
  poder regenerar el blob. Su historial línea-a-línea vive en upstream.

Flujo:

```
upstream FFXIV-Spanish/data/translations/jsonl
  -> XivSpanish.BlobBuilder sync   (copia local del corpus crudo, git-ignored)
  -> XivSpanish.BlobBuilder build  (filtra approved+gold, proyecta y comprime brotli)
  -> data/translations.dat + data/recommended-game-version.txt
  -> EmbeddedResource en FFXIVSpanishPatcher.App (F3)
  -> publish
```

Atajo: `dotnet run --project tools/XivSpanish.BlobBuilder -- sync --build` hace ambos pasos. Actualizar
la traducción = re-correr esto + re-publicar (la App vuelve a embeber el blob nuevo). CI (F7) no
reconstruye el blob: usa `data/translations.dat` y `data/recommended-game-version.txt` ya versionados.
Por eso CI/release deben hacer checkout con LFS habilitado.
