# data/translations

Manifest aprobado en formato JSONL. Es la **fuente** desde la que se genera el blob de
traducciones que se embebe en el ejecutable.

- Se versiona en git (es texto curado humano, no son assets de SquareEnix ni `.exd`).
- Se sincroniza desde el repo upstream **FFXIV-Spanish** con `build/sync-translations.ps1` *(F2)*.
- `build/build-translations.ps1` *(F2)* lo compacta a `artifacts/translations.dat` (ignorado por git)
  y ese artefacto se embebe como recurso en `FFXIVSpanishPatcher.App`.

Flujo: `data/translations/*.jsonl → build-translations.ps1 → artifacts/translations.dat → EmbeddedResource → publish`.
