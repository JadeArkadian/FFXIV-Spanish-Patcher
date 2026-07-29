# Checklist de release

## Preparación

- [ ] Trabajar en la rama de la versión, creada desde la base acordada.
- [ ] Confirmar que `git status` no contiene cambios ajenos.
- [ ] Ejecutar `git lfs pull` y comprobar que `data/translations.dat` no es un puntero LFS.
- [ ] Regenerar el blob y la versión recomendada cuando corresponda.
- [ ] Actualizar `data/translation-milestones.md`.
- [ ] Sincronizar la tabla completa del README con `FFXIV-Spanish/Remaining_EXD.md`.
- [ ] Actualizar `CHANGELOG.md`.

## Dependencias

- [ ] Revisar paquetes directos y mantener solo versiones compatibles.
- [ ] Actualizar todos los `packages.lock.json`.
- [ ] Ejecutar `dotnet restore --locked-mode`.
- [ ] Comprobar que la auditoría NuGet no informa de vulnerabilidades conocidas.
- [ ] Mantener acciones de GitHub fijadas por SHA con versión legible en comentario.

## Validación automática

```bash
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
git diff --check
```

- [ ] Cero errores.
- [ ] Cero avisos de compilación.
- [ ] Tests de pipeline, aplicación, Markdown, Dalamud y consola pasan.
- [ ] El escenario de versión deliberadamente antigua pasa.
- [ ] Un fallo de integridad conserva una salida anterior.

## Validación visual

- [ ] Preparación a `1240 × 820`.
- [ ] Opciones avanzadas abiertas.
- [ ] Cero categorías.
- [ ] Modal de mismatch.
- [ ] Modal de Dalamud.
- [ ] Generando.
- [ ] Resultado correcto, parcial y error.
- [ ] Tamaño mínimo `1080 × 720`.
- [ ] Escalas 100 %, 125 %, 150 % y 200 %.
- [ ] Logo centrado y sin recortes.
- [ ] Consola grande, coloreada y con selección continua de varias líneas.
- [ ] `Ctrl+A`, `Ctrl+C`, copiar log, limpiar y autoscroll.

## Publicaciones reales

Publicar `win-x64`, `linux-x64` y `osx-arm64` con las mismas propiedades que el workflow.

- [ ] Trimming completo.
- [ ] Sin PDB sueltos.
- [ ] Windows: un EXE self-contained y `translations.dat` adyacente.
- [ ] Linux: single-file self-contained.
- [ ] macOS: `.app`, icono y firma ad-hoc.
- [ ] Markdown embebido funciona en el binario publicado.
- [ ] Binario Linux arranca realmente, no solo compila.
- [ ] ZIPs tienen el prefijo/layout correcto.
- [ ] Generar y publicar SHA-256 de los tres ZIPs.

## Prueba funcional

- [ ] Instalación compatible: resultado `Ok` o parcial explicado.
- [ ] Instalación antigua deliberada: modal, *best effort*, avisos y métricas.
- [ ] Dalamud + Penumbra + ajuste falso: modal comprensible.
- [ ] **Ahora no** no modifica configuración.
- [ ] **Activar opción** cambia únicamente la propiedad lógica y se relee como `true`.
- [ ] Fallos de detección/configuración de Dalamud permanecen silenciosos.
- [ ] Integridad se ejecuta siempre.
- [ ] Los archivos originales del juego permanecen intactos.

## Cierre

- [ ] Revisar `README.md`, `docs/DESIGN.md`, `docs/COMPATIBILITY.md`,
  `docs/TRANSLATION_MILESTONES.md`, `CONTRIBUTING.md`, `NOTICE.md` y `AGENTS.md`.
- [ ] Obtener aprobación explícita de la versión.
- [ ] Solo después de esa aprobación, borrar `docs/evolucion-v0.3.0/`.
- [ ] Crear el commit/tag final y verificar los assets publicados.
