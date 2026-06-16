# FFXIVSpanish Patcher

Aplicación de escritorio (un solo ejecutable, sin instalación) que extrae los `.exd`
necesarios de una instalación local de Final Fantasy XIV, los parchea con traducciones al
castellano embebidas y genera un `.pmp` instalable con Penumbra.

GUI en **.NET 10 + Avalonia UI**. La lógica de extracción / parcheo / empaquetado se reutiliza
del proyecto **FFXIV-Spanish** mediante código vendorizado (ver `vendor/`).

## Estructura

- `src/FFXIVSpanishPatcher.App` — GUI Avalonia (MVVM). *(F3)*
- `src/FFXIVSpanishPatcher.Pipeline` — orquestación extract→patch→package con eventos de progreso. *(F1)*
- `vendor/` — librerías core copiadas de FFXIV-Spanish. **No editar a mano** (ver `vendor/VENDORED.md`).
- `data/translations/` — manifest aprobado en JSONL (fuente del blob embebido).
- `tests/FFXIVSpanishPatcher.Tests` — unit + integración con EXD sintético.
- `build/` — scripts de sincronización y empaquetado de traducciones.

## Build

```powershell
dotnet build
```

## Publicar (single-file, self-contained)

```powershell
dotnet publish src/FFXIVSpanishPatcher.App -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

El ejecutable resultante es un único `.exe` self-contained (~56 MB comprimido), sin instalación.

## Releases (CI/CD)

- `.github/workflows/ci.yml`: en cada push/PR a `main` compila y pasa los tests (incluido el smoke
  headless de la GUI).
- `.github/workflows/release.yml`: al empujar un tag `vX.Y.Z` publica los ejecutables single-file
  para `win-x64`, `linux-x64`, `osx-x64` y `osx-arm64` y los adjunta a un GitHub Release.

Ver `docs/DESIGN.md` para el diseño completo y `AGENTS.md` para el contexto de agentes IA.
