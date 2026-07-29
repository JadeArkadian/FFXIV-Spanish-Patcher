# Editar los hitos de traducción

La ventana **Ver hitos de traducción** se alimenta de:

```text
data/translation-milestones.md
```

No hay que modificar XAML ni C# para actualizar su contenido.

## Sintaxis admitida

- encabezados con `#`;
- párrafos;
- `**negrita**`, `*cursiva*` y `~~tachado~~`;
- listas ordenadas y sin ordenar;
- citas con `>`;
- código en línea y bloques de código;
- separadores `---`;
- tablas Markdown;
- enlaces absolutos `https://` o `http://`.

Por seguridad y para mantener un diseño idéntico en todos los sistemas, no se admiten HTML, imágenes
Markdown, rutas locales ni otros esquemas de enlace. El contenido se convierte en controles Avalonia
nativos; no se usa un navegador incrustado.

## Procedimiento

1. Editar `data/translation-milestones.md`.
2. Ejecutar:

   ```bash
   dotnet test tests/FFXIVSpanishPatcher.App.Tests/FFXIVSpanishPatcher.App.Tests.csproj
   ```

3. Abrir la aplicación y revisar el modal a `1240 × 820` y `1080 × 720`.
4. Comprobar tablas, listas, enlaces y scroll.
5. Publicar al menos un RID recortado para comprobar que el recurso embebido sobrevive al trimming.

El build falla si el fichero falta o está vacío. Una construcción ya distribuida muestra un texto de
reserva si, por una anomalía de runtime, el recurso no puede cargarse.
