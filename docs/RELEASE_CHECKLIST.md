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
- [ ] Tests de pipeline, aplicación, Markdown, Dalamud, consola y plugin pasan.
- [ ] El escenario de versión deliberadamente antigua pasa.
- [ ] Un fallo de integridad conserva una salida anterior.

## Validación visual

- [ ] Preparación a `1280 × 845`, con las opciones avanzadas abiertas de inicio.
- [ ] Preparación sin juego detectado: ninguna comprobación pendiente muestra ✓.
- [ ] Opciones avanzadas cerradas y vueltas a abrir.
- [ ] Cero categorías.
- [ ] Modal de mismatch.
- [ ] Modal de Dalamud.
- [ ] Botón y modal de invitación al plugin; el destello del botón es discreto.
- [ ] Generando: la barra avanza suave y su destello se detiene al terminar.
- [ ] Resultado correcto, parcial y error.
- [ ] Tamaño mínimo `1200 × 680`: sin solapes ni textos cortados sin tooltip.
- [ ] En una pantalla de `1366 × 768` la ventana se abre entera dentro del área de trabajo.
- [ ] `Esc` cierra hitos y modales; el foco entra en ellos al abrirse.
- [ ] Escalas 100 %, 125 %, 150 % y 200 %.
- [ ] Logo centrado y sin recortes.
- [ ] Consola grande, coloreada y con selección continua de varias líneas.
- [ ] `Ctrl+A`, `Ctrl+C`, copiar log, limpiar y autoscroll.

## Publicaciones reales

Publicar `win-x64`, `linux-x64` y `osx-arm64` con las mismas propiedades que el workflow.

- [ ] Trimming completo.
- [ ] Sin PDB sueltos.
- [ ] Windows: publish nativo en `windows-latest`, EXE self-contained single-file con Zstandard embebido.
- [ ] Windows: bundle single-file sin compresión interna (`EnableCompressionInSingleFile=false`).
- [ ] Windows: analizar el EXE ya firmado con Microsoft Defender antes de distribuirlo.
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
- [ ] **Pásate al plugin de Dalamud** con el juego cerrado: añade el repositorio y Dalamud lo
  muestra en `/xlsettings` en el siguiente arranque, con el resto de repositorios intactos.
- [ ] El botón no aparece sin Dalamud, con el juego abierto, ni con el repositorio o el plugin ya
  presentes.
- [ ] Sin `pluginmaster.json` publicado o sin red: no cambia nada en Dalamud y el botón sigue
  disponible.
- [ ] Integridad se ejecuta siempre.
- [ ] Los archivos originales del juego permanecen intactos.

## Plugin de Dalamud

Antes del tag, con el plugin cargado como dev plugin o desde el artifact `dalamud-plugin` de CI:

- [ ] Primera instalación: **Instalar traducción**, reinicio y textos en castellano.
- [ ] Arranque sin cambios: no regenera.
- [ ] Categorías y activación: un cambio en el plugin llega a Penumbra y al revés.
- [ ] Cancelar una regeneración conserva la traducción instalada.
- [ ] Penumbra desactivado y reactivado: el plugin se recupera sin recargar.
- [ ] Adopta un mod creado con el patcher conservando sus categorías.
- [ ] Desinstalar el plugin conserva el mod en Penumbra.
- [ ] Windows y Linux (XIVLauncher.Core).

Después del tag:

- [ ] La release incluye `FFXIVSpanishTranslationManager-X.Y.Z.zip` y su `.sha256`.
- [ ] `dalamud/pluginmaster.json` en `main` apunta a ese ZIP y declara la versión `X.Y.Z.0`.
- [ ] Instalar desde el repositorio personalizado en `/xlplugins` funciona en una instalación limpia.
- [ ] **Pásate al plugin de Dalamud** del patcher publicado, en una instalación sin el plugin: lo
  descarga, Dalamud lo carga activado en el siguiente arranque y después le ofrece actualizaciones.
- [ ] Windows: Microsoft Defender no marca el patcher al descargar e instalar el plugin.
- [ ] Con la versión anterior instalada, el plugin avisa de la nueva al arrancar y en su ventana.
- [ ] Actualizar desde la versión anterior regenera la traducción una sola vez.

## Cierre

- [ ] Revisar `README.md`, `docs/DESIGN.md`, `docs/PLUGIN.md`, `docs/PLUGIN_FAQ.md`,
  `docs/PATCHER_FAQ.md`, `docs/COMPATIBILITY.md`, `docs/TRANSLATION_MILESTONES.md`,
  `CONTRIBUTING.md`, `NOTICE.md` y `AGENTS.md`.
- [ ] Obtener aprobación explícita de la versión.
- [ ] Crear el commit/tag final y verificar los assets publicados.
