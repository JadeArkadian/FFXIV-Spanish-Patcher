# Prompt: continuar la investigación de rendimiento en el MacBook Air

> Archivo temporal de traspaso entre sesiones. No forma parte de la documentación del proyecto:
> bórralo cuando la investigación termine y no lo incluyas en una release.

Copia todo lo que hay bajo la línea como primer mensaje de una sesión nueva abierta en este repo,
en el Mac.

---

Vas a continuar una investigación de rendimiento que empezó en otra máquina (Linux, 20 núcleos).
El problema original: **generar el mod desde el plugin de Dalamud tarda mucho en equipos lentos**,
y el caso real es este MacBook Air M5 de 16 GB, donde el juego corre como binario Windows x64
traducido (Wine + Rosetta). Lee `AGENTS.md` antes de nada; sus reglas mandan sobre este prompt.

Tu objetivo es **medir en este Mac** dónde se va el tiempo de verdad y comprobar si lo que se
optimizó en Linux sirve aquí, antes de tocar más código. Mide primero, cambia después.

## Qué se hizo ya

Rama `feature/v0.6.1`. Comprueba con `git log` y `git status` que tienes las dos tandas; si la
segunda no está en el historial, avisa al usuario antes de medir, porque estarías midiendo otra cosa.

1. **Primera tanda** (commit `7fadd0c`):
   - `PatchPipeline`: el bucle de páginas EXD se partió en leer → parchear → recoger. La lectura
     del juego sigue en serie (Lumina nunca ve acceso concurrente), el parcheo se reparte entre
     hilos con PLINQ ordenado y los resultados se recogen en orden de página, así que eventos,
     contadores y archivos salen igual que antes.
   - Gate SeString y parseo JSON del corpus en paralelo.
   - `ClientExdSource` guarda layout y nombres de campo por hoja (418 hojas, 5677 páginas).
   - `PatchRequest.MaxDegreeOfParallelism`. El plugin pide núcleos − 1; el pipeline además limita
     a 8 hilos (`MaxUsefulWorkers` en `PatchPipeline.cs`).
   - Test `Run_PatchingOnManyThreads_MatchesTheSingleThreadedRunExactly`.
2. **Segunda tanda** (6 archivos en `vendor/XivSpanish.GameData`): recortes de memoria temporal
   en el parser y los tokenizadores SeString y en `ExdPatcher`, todos equivalentes por
   construcción (rangos contiguos en vez de listas byte a byte, `Serialize` de tamaño exacto,
   comparación de columnas ASCII sin crear cadenas).

## Qué se midió en Linux

Instalación real del juego, versión `2026.11.03.0000.0000`, salida en árbol como hace el plugin,
GC de estación de trabajo. Corpus: 659 008 entradas, 5677 páginas, 723 133 escrituras.

| | Primera ejecución del proceso | Ejecuciones siguientes |
|---|---|---|
| Antes de todo (`e9ac76a`) | 14,2 s | 8,1 s |
| Tras las dos tandas | 6,4–7,0 s | 4,1–4,6 s |

Reparto en caliente tras las dos tandas: carga del corpus ~1,3 s, gate ~0,3 s, abrir el juego
~0,2 s, agrupar + parchear + verificar ~2,2 s.

Hallazgos que condicionan lo que queda:

- **El límite es el recolector de basura, no la CPU.** Una generación reserva ~8,1 GB de memoria
  temporal (eran ~10,5 GB) y el GC suma ~1,6 s de pausa de los ~4,3 s.
- **Más hilos no ayuda sin límite.** Barrido de hilos en Linux, bucle de páginas en caliente:
  1 hilo 4,4 s, 2 hilos 3,3 s, 4–8 hilos 2,2–2,5 s, 20 hilos 3,0 s. De ahí el tope de 8. Ese
  tope se eligió en un x64 de 20 núcleos iguales: **en un M5 con núcleos de rendimiento y de
  eficiencia puede ser otro**.
- **La primera ejecución cuesta ~2,3 s más** que las siguientes por compilación JIT. En el plugin
  toda generación es una primera ejecución. Bajo Rosetta ese sobrecoste podría ser mucho mayor,
  pero es una hipótesis sin medir.
- La segunda tanda bajó la memoria un 23 % y **no dio mejora de tiempo medible** en Linux.
- Leer el corpus directamente en UTF-8 se probó y se descartó: mismo tiempo.
- Memoria pico del proceso: 1,1–1,7 GB, sin cambios respecto al original.

## Lo que no se sabe y hay que averiguar aquí

1. Cuánto tarda de verdad la generación **dentro del juego** en este Mac, y cuánto tardaba antes.
2. Qué parte de ese tiempo es JIT, cuál GC y cuál trabajo útil cuando el proceso es x64 traducido.
3. Cuál es el número de hilos óptimo en este procesador.
4. Si la presión de memoria (16 GB compartidos con el juego) añade paginación durante la generación.

## Cómo medir

### Arnés fuera del juego

Créalo **fuera del repo** (en tu directorio temporal de sesión), nunca dentro. Dos archivos más
una copia del `global.json` del repo.

`bench.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Dalamud aloja el plugin con GC de estación de trabajo: medir igual. -->
    <ServerGarbageCollector>false</ServerGarbageCollector>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(FFXIVSP_REPO)/src/FFXIVSpanishPatcher.Pipeline/FFXIVSpanishPatcher.Pipeline.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
// Uso: bench <rutaJuego> [ejecuciones=3] [hilos=núcleos]
// Requiere la variable de entorno FFXIVSP_REPO apuntando a la raíz del repo.
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FFXIVSpanishPatcher.Pipeline;

var repo = Environment.GetEnvironmentVariable("FFXIVSP_REPO") ?? throw new InvalidOperationException("Falta FFXIVSP_REPO");
var game = args[0];
var runs = args.Length > 1 ? int.Parse(args[1]) : 3;
var threads = args.Length > 2 ? int.Parse(args[2]) : Environment.ProcessorCount;
var work = Path.Combine(AppContext.BaseDirectory, "work");
if (Directory.Exists(work)) Directory.Delete(work, true);

Console.WriteLine($"proceso {RuntimeInformation.ProcessArchitecture} sobre SO {RuntimeInformation.OSArchitecture}, " +
    $"{RuntimeInformation.FrameworkDescription}, núcleos {Environment.ProcessorCount}, hilos pedidos {threads}, " +
    $"serverGC {GCSettings.IsServerGC}");

for (var run = 0; run < runs; run++)
{
    var alloc0 = GC.GetTotalAllocatedBytes();
    var pause0 = GC.GetTotalPauseDuration().TotalMilliseconds;
    var clock = Stopwatch.StartNew();
    long loaded = 0, gated = 0, opened = 0, patched = 0;
    var progress = new SyncProgress(e =>
    {
        var now = clock.ElapsedMilliseconds;
        if (e.Message.StartsWith("Cargando traducciones")) loaded = now;
        else if (e.Message.StartsWith("Verificando archivos base")) gated = now;
        else if (e.Message.StartsWith("Archivos base verificados")) opened = now;
        else if (e.Message.StartsWith("Integridad del árbol")) patched = now;
    });

    var tree = Path.Combine(work, "tree" + run);
    var result = PatchPipeline
        .ForClient(EmbeddedTranslationSource.FromFile(Path.Combine(repo, "data", "translations.dat")))
        .Run(new PatchRequest
        {
            GamePath = game,
            TreeOutputPath = tree, // igual que el plugin: árbol, sin .pmp
            StagingPath = Path.Combine(work, "staging"),
            CompatibilityMode = PatchCompatibilityMode.BestEffortVersionMismatch,
            MaxDegreeOfParallelism = threads,
        }, progress);

    var total = clock.ElapsedMilliseconds;
    Console.WriteLine(
        $"run {run}: {result.Outcome} total {total} ms | carga {loaded} | gate {gated - loaded} | abrir {opened - gated} | " +
        $"agrupar+parchear+verificar {patched - opened} | alloc {(GC.GetTotalAllocatedBytes() - alloc0) >> 20} MiB | " +
        $"pausa GC {GC.GetTotalPauseDuration().TotalMilliseconds - pause0:F0} ms | " +
        $"pico {Process.GetCurrentProcess().PeakWorkingSet64 >> 20} MiB | " +
        $"escrituras {result.Statistics?.AppliedWrites} misses {result.Statistics?.RowMisses} páginas {result.Statistics?.PatchedPages}");

    // Huella del árbol: cualquier cambio de código debe dejarla intacta.
    var listing = new StringBuilder();
    foreach (var file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
    {
        if (Path.GetFileName(file) != "meta.json" && !file.EndsWith(".exd")) continue;
        listing.Append(Path.GetRelativePath(tree, file).Replace('\\', '/'))
            .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))).Append('\n');
    }

    Console.WriteLine("  huella " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(listing.ToString()))));
    Directory.Delete(tree, true);
}

sealed class SyncProgress(Action<PipelineEvent> handler) : IProgress<PipelineEvent>
{
    public void Report(PipelineEvent value) => handler(value);
}
```

Ejecutar (sustituye las rutas; la del juego pregúntasela al usuario si no la encuentras):

```bash
FFXIVSP_REPO=/ruta/al/repo dotnet run -c Release -- "/ruta/al/juego" 3
```

La **run 0 es la que representa al plugin** (proceso recién arrancado); las siguientes son el
suelo sin JIT. Da siempre las dos cifras por separado y no las promedies. Pasa `1` como tercer
argumento para forzar un solo hilo.

**Huella de referencia**: con el juego en `2026.11.03.0000.0000` y el corpus de la rama debe
salir `581196BCD684AC4A97E91460573AA1E54388BC00EA2A43B5F9489BBD16E6C5CD`. Si la versión del
juego en el Mac es otra, la huella será distinta y no pasa nada: toma como referencia la que dé
esta rama sin tocar, y exige que no cambie con ningún cambio de código ni con el número de hilos.

### Dos arquitecturas, tres medidas

El arnés nativo corre como arm64 y **no representa al plugin**. Mide en este orden:

1. **arm64 nativo.** Techo de lo que da el hardware.
2. **x64 bajo Rosetta.** Lo más parecido al plugin que se puede medir fuera del juego. El IL es
   neutro, así que basta lanzar el mismo `bench.dll` con un host `dotnet` x64. Hace falta el
   runtime x64 de .NET 10: **pregunta al usuario antes de instalar nada**. Verifica en la primera
   línea de salida que pone `proceso X64 sobre SO Arm64`; si no, no estás midiendo lo que crees.
3. **Dentro del juego.** La única cifra que importa al usuario. Mira primero si
   `TranslationManagerSession` o el `dalamud.log` ya dejan marcas de tiempo de la generación; si
   no, añade un cronómetro temporal al log alrededor de la llamada al pipeline, mídelo y
   **retíralo** después. Compilar el plugin: sección «Compilar desde código» del `README.md`;
   cargarlo como dev plugin: `docs/RELEASE_CHECKLIST.md`. Esta
   medida necesita al usuario delante del juego: pídesela, no la des por hecha.

Ten presente que el paso 2 no es idéntico al 3 (runtime de macOS traducido frente a runtime de
Windows sobre Wine traducido, y con el juego ocupando CPU y memoria). Si 2 y 3 discrepan mucho,
el dato bueno es el 3.

### Si necesitas más detalle que el arnés

- Contadores temporales con `Stopwatch` y `GC.GetAllocatedBytesForCurrentThread()` dentro de
  `PatchPage` y `ExdPatcher.RebuildRow` funcionaron bien. Guarda copia limpia del archivo antes de
  instrumentar y restáurala después; comprueba con `grep` que no queda rastro.
- Un perfilador (`dotnet-trace`) daría mejor atribución. En Linux no se pudo instalar. Pregunta
  al usuario antes de instalarlo aquí.
- Variables de entorno útiles **solo para diagnóstico**, no para dejarlas en el producto (el
  plugin no controla el runtime de Dalamud): `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`,
  `DOTNET_ReadyToRun=0`, `DOTNET_gcServer=1`, `DOTNET_GCgen0size=<hex>`. Sirven para saber cuánto
  pesa el JIT y cuánto el GC comparando con y sin ellas.

## Hipótesis a comprobar, en este orden

1. **Línea base real.** Las tres medidas de arriba en esta rama, y la medida 2 también en
   `e9ac76a` (usa `git worktree`, no cambies de rama con trabajo sin guardar) para saber cuánto
   se ganó de verdad en este Mac.
2. **Número de hilos.** Barrido 1, 2, 3, 4, 6, 8, 10 en x64 bajo Rosetta. Si el óptimo no es el
   que sale de «núcleos − 1 con tope 8», propón el cambio con los datos; son dos líneas
   (`MaxUsefulWorkers` y `BuildPatchRequest` del plugin).
3. **Peso del JIT bajo Rosetta.** Diferencia entre run 0 y las siguientes, en arm64 y en x64. Si
   bajo Rosetta el JIT domina, la palanca que queda es precompilar (ReadyToRun) las DLL del
   plugin. Eso cambia el empaquetado del ZIP: **no lo implementes sin aprobación**; trae la
   medida y el coste en tamaño.
4. **Presión de memoria.** Con el juego abierto, mira si hay paginación o compresión de memoria
   durante la generación (`vm_stat`, `memory_pressure`). Si la hay, el objetivo pasa de tiempo de
   CPU a memoria pico.
5. Solo si 1–4 dejan claro que el trabajo útil sigue siendo el cuello: lo que queda por recortar
   es `SeStringTree.TryTranslate` (~0,65 GB en 33 500 filas) y la carga del corpus. El primero es
   el código que evita los cierres del Duty Finder: no lo toques sin una ganancia medida que lo
   justifique y sin avisar.

## Reglas

- La salida tiene que seguir siendo **idéntica byte a byte**: misma huella antes y después de
  cualquier cambio, con 1 hilo y con varios.
- `dotnet build -c Release` sin avisos y `dotnet test -c Release --no-build` en verde tras
  cualquier cambio.
- No hagas commit ni push sin aprobación expresa. No edites el corpus ni
  `dalamud/pluginmaster.json`. No añadas dependencias.
- No instales herramientas ni runtimes sin preguntar.
- Nada de `git stash` ni `git restore` sobre trabajo sin guardar.
- No dejes instrumentación, archivos de prueba ni salidas del arnés dentro del repo.
- Si una medida contradice lo que dice este documento, gana la medida: dilo tal cual.

## Qué entregar

Un informe breve con:

- la tabla de tiempos (run 0 y siguientes) para arm64, x64 bajo Rosetta y dentro del juego, antes
  y después;
- el barrido de hilos y el número que recomiendas;
- cuánto pesa el JIT y cuánto el GC en x64 bajo Rosetta;
- si hubo presión de memoria;
- qué propones hacer después, con la ganancia esperada medida, o que no merece la pena seguir;
- qué no pudiste medir y por qué.
