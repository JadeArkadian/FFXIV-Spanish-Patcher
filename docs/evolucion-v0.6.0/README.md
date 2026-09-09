# Evolución v0.5.0 y v0.6.0

> Documentación temporal de auditoría, implementación y validación. No borrar esta carpeta hasta
> que v0.5.0 y v0.6.0 estén implementadas, probadas y aprobadas expresamente.

## Propósito

Esta carpeta convierte las decisiones de producto tomadas el 20 de agosto de 2026 en un plan que
otros agentes pueden ejecutar sin depender de la conversación original.

- **v0.5.0**: el `.pmp` clásico pasa a contener siempre el mod completo y categorías configurables
  dentro de Penumbra. La selección del patcher decide qué categorías quedan activadas inicialmente,
  no qué datos se incluyen en el paquete.
- **v0.6.0**: se añade al mismo repositorio un plugin Dalamud que genera, instala, actualiza y
  configura el mismo mod mediante Penumbra, conservando el patcher de escritorio.

## Orden de lectura

1. [`DECISIONES.md`](DECISIONES.md): decisiones cerradas, supuestos y preguntas pendientes.
2. [`AUDITORIA.md`](AUDITORIA.md): estado real del repositorio y referencias externas verificadas.
3. [`PLAN-v0.5.0.md`](PLAN-v0.5.0.md): prerrequisito de empaquetado por categorías.
4. [`PLAN-v0.6.0.md`](PLAN-v0.6.0.md): plugin Dalamud completo, distribución y convivencia.
5. [`SMOKE-TESTS-v0.5.0.md`](SMOKE-TESTS-v0.5.0.md): pruebas humanas de formato, importación y texto.

## Punto de partida verificado

- Fecha de auditoría: `2026-08-20`.
- Rama activa: `v0.5.0`.
- Commit: `1cfbac6` (`v0.4.0 (#36)`).
- `main`, `v0.4.1`, `v0.5.0` y `v0.6.0` apuntaban al mismo commit al comenzar.
- Último tag real: `v0.4.0`.
- SDK fijado: `.NET 10.0.400`, `rollForward: disable`.
- Corpus embebido: `22.807.014` bytes.
- Versión recomendada del juego: `2026.08.11.0000.0000`.
- Validación ejecutada en Linux:
  - `dotnet restore --locked-mode`: correcto;
  - `dotnet build -c Release --no-restore`: 0 avisos, 0 errores;
  - `dotnet test -c Release --no-build`: 217 + 28 = 245 pruebas aprobadas.

No se ejecutaron en esta auditoría publicaciones self-contained, Dalamud, Penumbra ni un cliente
real de FFXIV. Esas validaciones siguen siendo obligatorias en sus etapas correspondientes.

## Corrección respecto al análisis interrumpido

El análisis anterior alcanzó el límite de uso cuando todavía empleaba Penumbra.Api `5.15.1` y el
formato histórico `group_*.json`. Al repetir la investigación se comprobó:

- plantilla oficial actual: `Dalamud.NET.Sdk/15.0.0`;
- Penumbra.Api actual en el árbol de Penumbra: `5.17.0`;
- versión IPC actual: `Breaking=5`, `Feature=17`;
- formato canónico actual del mod: `meta.json` con `FileVersion: 4`, `DefaultData` y `Groups`;
- Penumbra mantiene lectura y migración del formato v3, pero elimina `default_mod.json` y
  `group_*.json` al migrarlo a v4.

Por ello el requisito funcional de categorías se conserva, pero los planes generan v4. No se debe
implementar hoy un nuevo escritor basado en el formato legado.

## Regla de ejecución

Cada etapa de ambos planes:

1. parte de una rama y un árbol de trabajo comprobados;
2. modifica solo su alcance;
3. añade o actualiza pruebas;
4. ejecuta sus puertas automáticas;
5. actualiza la documentación afectada;
6. deja un commit autocontenido;
7. se detiene para revisión y aprobación humana.

No se avanza a la etapa siguiente por el mero hecho de compilar. Las pruebas con FFXIV y Penumbra
reales se marcan como pendientes hasta que una persona confirme el resultado.
