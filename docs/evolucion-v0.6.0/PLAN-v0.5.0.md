# Plan de implementación v0.5.0 — categorías Penumbra

> Prerrequisito obligatorio de v0.6.0. Este plan cambia el formato del mod, no crea todavía el
> plugin Dalamud.

## 1. Objetivo de release

El patcher de escritorio genera un `.pmp` Penumbra v4 que contiene todas las traducciones
packageable y ofrece diez categorías configurables. La selección de la UI define únicamente qué
opciones quedan activadas al importar; nunca elimina categorías del archivo.

El paquete sigue sin modificar archivos del juego, mantiene SeString gate, contamination guard,
best effort, verificación obligatoria y publicación atómica.

## 2. Contrato observable

### Contenido

- `meta.json` en raíz, `FileVersion: 4`.
- `DefaultData` vacío salvo que aparezcan datos comunes reales.
- Un grupo `Multi` `Categorías de traducción`.
- Diez opciones en orden estable, con nombre y descripción castellanos.
- Cada EXD parcheado aparece exactamente en una opción.
- El `.pmp` contiene todas las opciones aunque el usuario desmarque alguna.
- No contiene `default_mod.json` ni `group_*.json` nuevos.
- No contiene `.exh`, DAT, dumps ni archivos reales sin parchear.

### Estado inicial

- `DefaultSettings` es un bitfield sobre el orden estable de opciones.
- Las categorías seleccionadas en Avalonia tienen su bit a 1.
- Debe haber al menos una categoría seleccionada.
- `Todas` activa todos los bits; `Ninguna` deja la UI inválida y no permite generar.

### Resultado y estadísticas

- Candidatas, omisiones y contamination guard se calculan sobre el mod completo.
- Se añaden estadísticas por dominio: candidatas, escrituras, misses y páginas.
- Una categoría sin páginas aplicables por incompatibilidad queda presente pero puede estar vacía;
  se informa como omisión.
- Cualquier game path declarado por más de una categoría es error de validación.

## 3. Arquitectura destino

### Modelo

Crear modelos internos/source-generated para:

```text
PenumbraModV4
  FileVersion = 4
  Name/Author/Description/Version/Website/ModTags
  DefaultData: ModContainer
  Groups: [ModGroupMulti]

ModGroupMulti
  Version = 0
  Name = "Categorías de traducción"
  Type = "Multi"
  Priority = 0
  DefaultSettings: int
  Options: [ModOption]

ModOption
  Name/Description/Priority
  Files: gamePath -> modRelativePath
  FileSwaps: {}
  Manipulations: []
```

Los nombres de grupo/opción son identidad persistente para settings de Penumbra: no se cambian sin
migración. Los dominios internos siguen siendo claves técnicas estables.

### Separación de responsabilidades

```text
PatchPipeline
  produce páginas parcheadas + categoría + estadísticas
        |
        v
PenumbraModTreeWriter
  escribe árbol v4 en staging
        |
        +--> ModTreeVerifier
        |
        +--> PmpArchiveWriter --> ZipVerifier --> promoción atómica
```

`PenumbraModTreeWriter` y `ModTreeVerifier` deben poder reutilizarse en v0.6.0 sin crear un ZIP.

### Catálogo compartido

Extraer del boundary Avalonia una definición compartida mínima:

```csharp
TranslationCategoryDefinition(
    string Domain,
    string DisplayName,
    string Description,
    int Order)
```

`CategoryCatalog` puede seguir añadiendo tooltips/presentación, pero debe consumir la misma lista y
no duplicar nombres ni orden.

## 4. Etapas

Cada etapa termina con pruebas, diff revisado, documentación y aprobación humana.

### Etapa 0 — higiene y contrato congelado

Objetivo: eliminar ambigüedad antes de tocar el formato.

Acciones:

1. Comprobar rama/commit y árbol limpio o preservar cambios ajenos.
2. Confirmar que `DECISIONES.md` mantiene el grupo `Multi` único y la verificación actual
   obligatoria, ya ratificados.
3. Elegir estrategia de ramas para evitar colisión branch/tag.
4. Añadir fixtures JSON escritos a mano, mínimos y sin bytes de FFXIV:
   - meta v4 válido con dos opciones;
   - bitfield parcial;
   - redirect duplicado entre opciones, inválido;
   - ruta con traversal, inválida.
5. Actualizar `AGENTS.md`: retirar `@RTK.md`, snapshot v0.3.0 y cifras antiguas; describir estos docs.

Pruebas:

- deserialización/serialización determinista del fixture;
- JSON producido validado contra las invariantes locales;
- no iniciar todavía cambios de pipeline.

Gate humano: aprobar forma de grupos y nombres persistentes.

Commit sugerido: `docs(v0.5): freeze categorized mod contract`.

### Etapa 1 — modelos Penumbra v4 y verificador de árbol

Objetivo: representar y validar el formato sin integrarlo aún con el pipeline.

Archivos previstos:

- `src/FFXIVSpanishPatcher.Pipeline/PenumbraModV4.cs`;
- `src/FFXIVSpanishPatcher.Pipeline/PenumbraModJsonContext.cs`;
- `src/FFXIVSpanishPatcher.Pipeline/ModTreeVerifier.cs`;
- pruebas nuevas en `tests/FFXIVSpanishPatcher.Tests/`.

Invariantes del verificador:

1. `FileVersion == 4`.
2. nombre no vacío, un grupo Multi con nombre estable.
3. opciones con nombres únicos y orden estable.
4. `DefaultSettings` no contiene bits fuera del número de opciones.
5. cada redirect tiene game path relativo normalizado.
6. cada destino permanece bajo el root del árbol.
7. el destino existe y no es symlink que escape del root.
8. ningún game path aparece en dos containers.
9. los `.exd` declarados empiezan por `EXDF`.
10. no existen archivos huérfanos de payload salvo lista explícita de recursos permitidos.

No validar solo el modelo en memoria: reabrir JSON y archivos desde disco.

Pruebas negativas para cada invariante. Ejecutar suite completa.

Gate humano: inspeccionar un `meta.json` sintético generado.

Commit sugerido: `feat(packaging): model and verify Penumbra v4 trees`.

### Etapa 2 — escritor de árbol categorizado

Objetivo: producir un árbol v4 completo y determinista.

Acciones:

1. Sustituir `PackageWriter` monolítico por `PenumbraModTreeWriter` + `PmpArchiveWriter`.
2. `AddPatchedExd` recibe dominio y rechaza dominios desconocidos.
3. Guardar payload por opción, por ejemplo
   `files/categories/{order:D2}-{domain}/exd/...`.
4. Construir `DefaultSettings` desde categorías elegidas.
5. Escribir `meta.json` al final mediante temporal hermano y reemplazo/move.
6. Ordenar mapas, grupos, opciones y archivos para builds reproducibles.
7. Comprimir el árbol ya verificado; verificar de nuevo el ZIP reabierto.

No reutilizar `PackageDefaultMod` ni emitir formato v3.

Pruebas:

- todas seleccionadas, selección parcial y una sola;
- siempre diez opciones y el mismo número de archivos total;
- solo cambia `DefaultSettings` entre selecciones;
- cada categoría recibe las hojas correctas;
- orden estable y JSON byte-idéntico con misma entrada;
- cleanup de staging/temporales;
- salida anterior conservada si falla tree o ZIP verification.

Gate humano: abrir el ZIP sintético e inspeccionar estructura.

Commit sugerido: `feat(packaging): emit categorized Penumbra v4 mods`.

### Etapa 3 — pipeline completo y estadísticas por categoría

Objetivo: cambiar la semántica de selección sin degradar seguridad.

Acciones:

1. Separar:
   - categorías que se **generan**: siempre todas;
   - categorías **enabled by default**: selección de `PatchRequest`.
2. Mantener statuses `approved`/`gold`, SeString gate y broadcast sobre todas las candidatas.
3. Asociar cada `PagePatch` a `TranslationCategories.DomainOf(entry)`.
4. Fallar si una misma página recibe dominios distintos; la taxonomía debe corregirse, no elegir
   uno arbitrariamente.
5. Añadir `CategoryPatchStatistics` a `PatchStatistics` sin romper propiedades usadas por App.
6. Mantener `Ok`/`PackagedWithMisses` según cobertura completa.
7. Propagar cancelación hasta writers/verifiers cuando sea posible.

Pruebas de regresión obligatorias:

- todos los tests actuales de pipeline;
- una selección parcial sigue empaquetando páginas de categorías desmarcadas;
- `DefaultSettings` refleja selección;
- contamination guard no cambia al desmarcar UI;
- SeString insegura solo vacía/recorta su opción y queda contabilizada;
- missing sheet/page por categoría;
- broadcast no cruza categorías;
- fallo por colisión de dominio.

Gate humano: revisar estadísticas y mensajes; confirmar que “desactivada” no parece “ausente”.

Commit sugerido: `refactor(pipeline): build full mod with category defaults`.

### Etapa 4 — adaptación de Avalonia

Objetivo: explicar el nuevo contrato al usuario.

Cambios:

- etiquetas: “Categorías activadas al importar en Penumbra”.
- texto claro: el paquete siempre incluye la traducción completa.
- conservar Todas/Ninguna y validación de al menos una.
- resumen final con número de categorías incluidas y activadas.
- detalle por categoría cuando existan omisiones.
- no añadir lógica de JSON/packaging al ViewModel.

Pruebas:

- visual-tree assertion de los textos nuevos y ausencia de textos engañosos antiguos;
- petición construida con selección correcta;
- cero seleccionadas deshabilita generar;
- smoke Avalonia completo.

Gate humano:

- revisar ventana normal y avanzadas a `1240x820` y tamaño mínimo;
- generar un `.pmp` sintético/local y confirmar el resumen.

Commit sugerido: `feat(app): present Penumbra category defaults`.

### Etapa 5 — integración real Penumbra

Objetivo: demostrar el formato contra Penumbra, no solo contra nuestro parser.

Automático:

- restore/build/test locked;
- `git diff --check`;
- inspección del `.pmp`: v4, sin legacy manifests, sin archivos prohibidos;
- si es viable, test de schema fijando una copia permitida o reglas equivalentes; no descargar
  `master` mutable durante tests normales.

Humano Windows y Linux:

1. generar con todas las categorías;
2. importar en Penumbra actual;
3. comprobar diez opciones y estado inicial;
4. cambiar opciones y verificar texto dentro del juego;
5. repetir con selección parcial;
6. actualizar/reimportar y confirmar preservación esperada de settings;
7. probar juego igual y distinto a la versión del corpus;
8. comprobar que archivos originales no cambian.

Registrar versiones exactas de Dalamud/Penumbra/FFXIV y capturas. Un import correcto sin prueba en
juego no cierra esta etapa.

Commit sugerido: `test(packaging): validate categorized mod workflow`.

### Etapa 6 — documentación y release v0.5.0

Actualizar:

- `README.md`: uso breve y enlace a guía de categorías;
- `CHANGELOG.md`: v0.5.0;
- `docs/DESIGN.md`: árbol v4 y separación tree/archive;
- `docs/COMPATIBILITY.md`: omisiones por categoría;
- `docs/RELEASE_CHECKLIST.md`: inspección v4 y prueba Penumbra;
- `CONTRIBUTING.md`: tests de formato y nombres estables;
- `NOTICE.md`: solo si cambian dependencias/licencias;
- `AGENTS.md`: estructura y comandos reales.

Release:

1. merge/rebase de la rama aprobada según política elegida;
2. confirmar que `refs/heads/v0.5.0` no vuelve ambiguo el tag;
3. tag anotado `v0.5.0` sobre commit aprobado;
4. CI/release completa;
5. descargar assets, verificar hashes y smoke nativo;
6. repetir import real con el asset publicado.

No borrar esta carpeta: v0.6.0 todavía depende de ella.

## 5. Puertas globales

Antes de cada aprobación:

```bash
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
git diff --check
```

Además:

- no cambios en `data/translations.dat` salvo que la etapa sea explícitamente de corpus;
- no fixtures reales de FFXIV;
- no cambios masivos en `vendor/`;
- no pérdida de pruebas actuales;
- no warnings nuevos;
- no archivos temporales o `.pmp` versionados.

## 6. Criterio de terminado v0.5.0

v0.5.0 solo está terminada cuando:

1. el `.pmp` publicado contiene siempre las diez categorías;
2. la selección solo cambia defaults;
3. el árbol y ZIP pasan verificación obligatoria;
4. las 245 pruebas base y las nuevas pasan;
5. Penumbra real importa y aplica opciones en Windows y Linux;
6. documentación permanente coincide con código;
7. release y hashes están publicados;
8. el usuario aprueba expresamente el resultado.
