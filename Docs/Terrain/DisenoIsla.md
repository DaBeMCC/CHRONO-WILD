# CHRONO WILD: diseño de la isla y generación del terreno

Documento vivo del equipo (David y Gerardo). Recoge las decisiones técnicas de la isla jugable,
su estado y lo que falta por validar. Las cifras medidas salen de
[`ValidationReport.md`](ValidationReport.md), que regenera la herramienta (botón **Validar**).

---

## 1. Diagnóstico técnico

| Aspecto | Estado |
|---|---|
| Motor | Unity **6000.6.3f1**, Universal Render Pipeline **17.6** |
| Plantilla | URP con dos perfiles de render (`PC_RPAsset`, `Mobile_RPAsset`) y dos niveles de calidad |
| Entrada | Input System 1.20 (manejo activo: solo el sistema nuevo) |
| Módulos de build instalados | Windows, Linux, **WebGL** |
| Contenido previo | Solo la plantilla (`SampleScene`, `TutorialInfo`), sin código propio |
| Control de versiones | Git, un commit inicial |

Observaciones:

- **Una sola Terrain tile es suficiente.** 600 × 600 m con un heightmap de 513² da 1,17 m por
  muestra, suficiente para caminos de 2,5–3,5 m y acantilados legibles. El terreno de Unity ya
  divide internamente su malla en parches con LOD, así que partirlo en varias tiles no reduce el
  coste de dibujo a este tamaño. Solo complicaría el trabajo (costuras, vecinos, más assets).
  Habría que dividirlo solo si el perfilado lo pide o si la isla crece más allá de ~1 km.
- **WebGL condiciona la tecnología.** WebGL 2 no tiene compute shaders, así que quedan fuera
  GPU Resident Drawer, GPU occlusion culling y la vegetación por compute. La isla usa instancing
  clásico, LOD Groups y culling por distancia, que funcionan en Windows y en WebGL.
- **`.gitignore`**: contenía `manifest.json` y `packages-lock.json`. Como ya estaban versionados,
  git los sigue rastreando, pero conviene quitar esas dos líneas para que nadie los pierda en un
  clon nuevo. Se añadieron `Backups/` y `Exports/` (copias locales y exportaciones regenerables).

## 2. Layout de la isla

Coordenadas en metros sobre el terreno (x = oeste→este, z = sur→norte; el origen es la esquina
suroeste). El terreno empieza en y = −20 m y el nivel del mar está en **y = 0**.

```
                N
        ┌────────────────────────┐
        │   ~~~ mar ~~~          │
        │      ╭─acantilado──╮   │
        │  Portal●  ▲ Monte   │  │
        │  (252,456) Cronos   │  │
        │    ╲   ╲ (396,420)  │  │
        │ Valle●  ╲sendero    │  │
        │ (180,378) ╲ del monte  │
        │  │  Mirador● ●Ginkgo   │
O       │  │  (252,348) (336,300)│      E
        │  costa       │         │
        │  oeste   Entrada●      │
        │   ╲     (276,216)      │
        │    Campamento●         │
        │     (252,174)          │
        │        ●Spawn (300,114)│
        │  ~~~ playa sur ~~~     │
        └────────────────────────┘
                S
```

| Región | Identidad | Función |
|---|---|---|
| **Playa sur** (spawn) | Arena ancha y casi plana (0–3 m), troncos y rocas | Primeros recursos, tutorial |
| **Campamento** | Claro llano a 7 m, al borde del bosque | Construir el refugio |
| **Bosque central** | Coníferas y ginkgos densos con claros y caminos estrechos | Exploración, investigar la planta |
| **Ginkgo ancestral** | Claro con un ginkgo monumental | Objetivo de investigación y referencia visual |
| **Cresta del valle** | Loma de ~15 m entre el bosque y el valle | Oculta el valle hasta el mirador |
| **Valle interior** | Cuenca abierta a ~5 m, poca vegetación | Zona de dinosaurios, escaneo |
| **Monte Cronos** | Pico de ~103 m con crestas, al noreste | Hito principal, visible desde casi toda la isla |
| **Acantilados** | Costa norte y este, caída de ~22 m al agua | Límite natural del mapa |
| **Meseta del portal** | Plataforma a ~22 m al noroeste | Final de la misión |
| **Río / arroyo** | *Pendiente (fase 2)*: del flanco del monte al valle y al mar por el oeste | Orillas, barro, sonido de agua |

## 3. Dimensiones

| Parámetro | Valor | Motivo |
|---|---|---|
| Terreno | 600 × 600 m | Propuesta inicial; deja ~80 m de mar alrededor |
| Tierra emergida | ~437 × 422 m, **14,4 ha** | Transitable concentrado en el centro |
| Rango vertical | 140 m (−20 a +120) | Lecho marino de 20 m y pico de ~103 m |
| Heightmap | 513 × 513 | 1,17 m por muestra |
| Alphamap | 512 × 512 | 8 capas = 2 splatmaps RGBA |
| Cruzar la isla en línea recta | 109 s caminando / 62 s corriendo | Medido en la extensión mayor |
| Ruta principal (playa → portal) | **572 m: 2 min 23 s caminando / 1 min 22 s corriendo** | Validador |

Una isla de ~1 minuto de punta a punta corriendo es deliberadamente compacta. La sensación de
tamaño viene del diseño (sección 7) y de la densidad de actividades, no de la distancia. Si las
pruebas de juego la notan pequeña, primero conviene bajar la velocidad de carrera (7 → 6 m/s) y
añadir actividades por el camino. Ampliar a 800–1000 m (heightmap 1025²) sería el último recurso.

## 4. Generación: capas y parámetros

El generador trabaja por capas, sin apilar técnicas sin necesidad:

1. **Forma de isla**: máscara radial con *domain warping* moderado (fBm de 3 octavas,
   fuerza 0,07) para que la costa sea irregular sin desmembrarse.
2. **Perfil costero por sectores angulares**: al sur, playa (sube 1,8 m en 40 m); al norte y
   al este, acantilado (22 m en 7 m) y agua profunda casi inmediata; en el resto, una rampa normal.
3. **Relieve interior**: fBm de detalle (±5 m) más **rasgos diseñados** con posición, radio y
   altura editables en el Inspector (monte con ruido *ridged*, colinas, cresta, meseta y la cuenca
   del valle). El relieve grande viene de decisiones de diseño, no del azar.
4. **Áreas planas**: spawn, campamento, claro del Ginkgo, fondo del valle y plataforma del portal.
   Se aplanan a la altura generada en su centro, con una transición suave.
5. **Suavizado selectivo**: un pase 3×3 que excluye la franja de acantilado, para que siga
   siendo vertical.
6. **Caminos**: splines Catmull-Rom tallados en el heightmap. El perfil longitudinal se suaviza
   (radio de 25 m) para evitar escalones, y los extremos se fijan a la altura existente para que
   los caminos empalmen entre sí.
7. **Superficies**: reglas por altitud, pendiente, máscara de playa, humedad y caminos, con
   umbrales perturbados por ruido.
8. **Vegetación**: rejilla con jitter, filtrada por densidad de bosque (núcleo central × racimos de
   ruido), pendiente, altura sobre el mar, caminos y claros.
9. **Ajustes manuales**: ver más abajo.

Se descartaron por ahora la erosión hidráulica (cara y poco visible a 1,17 m por muestra; queda
como opción preprocesada si el relieve se ve artificial) y las curvas de biomas por temperatura
(con un solo ecosistema basta con humedad y altitud).

**Parámetros iniciales** (asset `Assets/_Game/Settings/World/IslandTerrainSettings.asset`):

| Grupo | Parámetro | Valor |
|---|---|---|
| Semilla | `seed` | 1337 |
| Costa | `islandRadius` / `coastWarpStrength` / `coastWarpFrequency` | 0,36 / 0,07 / 3 |
| Playa | `beachDirection` / `beachArc` / `beachWidth` / `beachHeight` | 270° / 110° / 40 m / 1,8 m |
| Acantilado | `cliffDirection` / `cliffArc` / `cliffHeight` / `cliffWidth` | 50° / 130° / 22 m / 7 m |
| Relieve | `inlandBaseHeight` / `detailAmplitude` / `detailFrequency` | 6 m / 5 m / 6 |
| Monte Cronos | centro / radio / altura / ridged | (0,66, 0,70) / 0,14 / 66 m / 0,55 |
| Vegetación | `treeSpacing` / `maxTrees` / `treeSlopeLimit` | 4,5 m / 2500 / 30° |

**Conservar retoques manuales.** Cada generación guarda la altura base en
`Island_HeightBaseline.bytes`. Si David esculpe a mano y luego cambia la semilla o un parámetro,
la herramienta calcula *terreno actual − base anterior* (lo esculpido) y lo suma al nuevo relieve.
El pintado manual de superficies y árboles sí se sobrescribe si se marcan *Repintar superficies*
o *Recolocar vegetación*; para conservarlos, se desmarcan esas casillas.

**Respaldo.** Antes de sobrescribir el TerrainData o una escena se copia el archivo en
`Backups/` (fuera de `Assets`, ignorado por git), con fecha y hora.

## 5. Biomas y superficies

8 Terrain Layers (en esta fase, texturas provisionales de color):

| Capa | Regla inicial |
|---|---|
| Arena seca | Altura < ~3 m en el sector de playa (< 0,8 m en otras costas), pendiente < 18° |
| Arena húmeda | Altura < ~1 m (línea de agua) |
| Tierra de bosque | Base de todo el interior |
| Hojarasca | Donde la densidad de bosque es alta |
| Barro | Humedad alta, zonas bajas (< 7 m) y llanas; en la fase 2 también junto al río |
| Roca | Pendiente > ~30° |
| Roca de acantilado | Pendiente > ~44° |
| Tierra de camino | Máscara de los caminos (no se pinta sobre la arena) |

**Coste.** URP Terrain Lit dibuja 4 capas por pasada, así que 8 capas son 2 pasadas sobre todo el
terreno. Para calidad baja se propone reducir a 4 capas (arena, tierra, roca y camino) o usar
el *basemap* a menor distancia. Hay que medirlo antes de decidir.

**Materiales finales (fase 5):**

- Albedo + normal + mask map en todas las capas.
- *Height blending* solo en calidad alta.
- *Triplanar* solo en las rocas de acantilado, preferiblemente con mallas de roca encima en
  lugar de un shader triplanar para todo el terreno.
- POM y parallax solo en primer plano y calidad alta, si el perfilado lo permite. Nunca en todo
  el terreno.
- Calidad baja: texturas comprimidas de 512², normal maps y el shader estándar.

## 6. Rutas

| Ruta | Recorrido | Longitud | Caminando | Corriendo | Pendiente máx. |
|---|---|---|---|---|---|
| **Principal** | Playa → campamento → bosque → Ginkgo → mirador → valle → zigzag → portal | 572 m | 2 min 23 s | 1 min 22 s | 27° (6 m por encima de 25°) |
| **Alternativa: costa oeste** | Campamento → orilla oeste → valle (evita el bosque) | 311 m | 1 min 18 s | 44 s | 15° |
| **Alternativa: sendero del monte** | Ginkgo → falda del Monte Cronos → portal (evita el valle y los dinosaurios) | 245 m | 1 min 1 s | 35 s | 37° (3 m por encima de 25°) |

Así, el valle y el portal tienen al menos dos accesos cada uno. Todas las pendientes quedan por
debajo del límite de 45° del controlador de prueba. Los dos tramos cortos de más de 25° se
revisarán en la prueba jugable.

**Misión y POIs** (editables en el asset de configuración o moviendo los marcadores en la escena):

| # | Paso | POI | Posición (m) | Distancia al spawn |
|---|---|---|---|---|
| 1 | Aparecer | `spawn_beach` | (300, 114) | 0 m |
| 2 | Madera, piedras, fibras | `res_beach_wood`, `res_beach_stone`, `res_fiber` | (336, 120), (252, 114), (288, 150) | 36–48 m |
| 3–4 | Herramienta y refugio | `camp` | (252, 174) | 77 m |
| 5 | Entrar al bosque | `forest_entrance` | (276, 216) | 105 m |
| 6 | Investigar la planta | `research_plant` | (336, 300) | 189 m |
| 7 | Ver la zona de dinosaurios | `valley_overlook` | (252, 348) | 239 m |
| 8–9 | Escanear y tomar muestras | `dino_valley` | (180, 378), radio 30 m | 290 m |
| 10 | Volver al portal | `portal` | (252, 456) | 345 m |
| — | Hito | `mount_cronos` | (396, 420) | 321 m |

Las actividades quedan a 40–90 m unas de otras, entre 10 y 25 s caminando.

## 7. Sensación de escala

| Técnica | Cómo se aplica |
|---|---|
| **A. Revelación progresiva** | Sin árboles, desde ningún punto de la ruta se ve más del 35 % de la tierra (playa 30 %, mirador 18 %); con vegetación, bastante menos. La cresta oculta el valle hasta el mirador |
| **B. Caminos sinuosos** | Splines con curvas, cambios de altura y un zigzag hasta la meseta |
| **C. Hitos visuales** | El Monte Cronos se ve desde 6 de 7 puntos clave; el Ginkgo monumental y el portal son hitos secundarios |
| **D. Regiones contrastantes** | Playa abierta → bosque cerrado → valle abierto → meseta y acantilado, todo dentro del mismo ecosistema jurásico |
| **E. Vistas controladas** | El mirador está en la salida de la cresta; los claros del bosque dejan ver el monte |
| **F. Rutas alternativas** | Tres rutas, con dos accesos al valle y dos al portal |
| **G. Distancia simulada** | Niebla lineal (70–520 m) y océano de 3 km. Pendiente: siluetas de islas lejanas de bajo detalle |
| **H. Densidad de contenido** | POIs cada 40–90 m |
| **I. Sonido ambiental** | Fase 5: zonas de audio (oleaje en la costa, viento en la meseta, agua en el río) y llamadas lejanas de dinosaurios |
| **J. Escala humana** | Coníferas de ~10–13 m, ginkgos de ~10 m, cícadas de ~2 m, caminos de 2,5–3,5 m y escalones de 0,4 m como máximo |

## 8. Presupuesto de rendimiento (objetivos, aún sin medir)

Objetivo: **720p, 30 FPS (33 ms por frame) en calidad baja con 4 GB de RAM**. Ninguna cifra de
esta tabla está medida todavía: son límites de trabajo hasta perfilar en el equipo de Gerardo y
en WebGL.

| Recurso | Calidad baja | Calidad alta |
|---|---|---|
| Batches / SetPass | ≤ 300 / ≤ 80 | ≤ 1000 / ≤ 200 |
| Triángulos visibles | ≤ 400 k | ≤ 2 M |
| Terreno | `pixelError` 10–15, `basemapDistance` 80 m, 4 capas | `pixelError` 5, 8 capas |
| Árboles | `treeDistance` 150 m, LOD0 ≤ 1,5 k tris, impostor desde 50 m | `treeDistance` 400 m |
| Detalles (helechos) | densidad 0,3, distancia 40 m, sin sombras | densidad 1, distancia 80 m |
| Sombras | 1 cascada, 35–40 m, mapa de 1024 | 2–4 cascadas, 80 m, mapa de 2048 |
| Texturas | 512², compresión de plataforma (DXT/BC en Windows; ASTC/ETC2 o DXT en WebGL según destino) | 1024–2048² |
| Memoria del juego | ≤ 1,2 GB en total; texturas ≤ 300 MB; mallas ≤ 100 MB | — |
| Colliders | Terreno y cápsulas de tronco; rocas con colliders primitivos; vegetación pequeña sin collider | Igual |
| Generación en runtime | **Ninguna**: todo se genera en el editor | Igual |

Datos actuales del terreno: heightmap ~0,5 MB, splatmaps ~2 MB, asset de TerrainData 3,3 MB y
1336 árboles provisionales.

**Occlusion culling.** Por ahora no. El terreno no actúa como oclusor en el bake de Umbra, y un
bosque de muchos objetos pequeños apenas se beneficia. Se medirá con el Frame Debugger y el
Profiler cuando haya rocas grandes y estructuras. GPU Resident Drawer y GPU occlusion quedan
**desactivados** por WebGL.

## 9. Estructura de archivos

```
Assets/_Game/
  Editor/                          ChronoWild.Editor (solo editor)
    ChronoWildTerrainGenerator.cs  Ventana: CHRONO WILD ▸ Generador de terreno
    Terrain/                       Ruido, heightmap, superficies, escenas, validación, placeholders
  Scripts/                         ChronoWild.Runtime
    World/                         IslandTerrainSettings, PointOfInterest, WorldSceneLoader
    Player/                        DebugFirstPersonController (solo pruebas)
    Debug/                         PerformanceOverlay (FPS, memoria, recorrido)
  Settings/World/                  IslandTerrainSettings.asset
  Art/Terrain/Generated/           TerrainData, base de alturas, Terrain Layers, texturas
  Art/Materials|Meshes/Placeholder/
  Prefabs/Placeholder/             Árboles provisionales con LOD Group
  Scenes/World/
    Island_Terrain.unity           Entorno: terreno, océano, sol, niebla        (David)
    Island_Gameplay.unity          POIs, jugador, cargador del entorno          (Gerardo)
Docs/Terrain/                      Este documento e informe de validación
Backups/   Exports/                Locales, ignorados por git
```

**Trabajo en paralelo:**

- Gerardo abre `Island_Gameplay`: al darle a Play carga `Island_Terrain` de forma aditiva. Puede
  mover POIs, añadir recursos e interacción sin tocar la escena del terreno.
- David regenera o esculpe en `Island_Terrain`. Como la escena referencia el TerrainData como
  asset, regenerarlo no modifica la escena de gameplay.
- Las fuentes de alta resolución (PSD, texturas de 4K, escaneos) deben quedar **fuera de `Assets`**
  (por ejemplo, en una carpeta `ArtSource/` con Git LFS o en una unidad compartida). En `Assets`
  solo entran versiones importables a la resolución final.
- Más adelante, cuando haya prefabs de recursos, una escena ligera `Scenes/Tests/Sandbox.unity`
  servirá para probar interacción sin cargar la isla.

## 10. Plan de implementación

| Fase | Contenido | Estado |
|---|---|---|
| **1** | Forma de isla, relieve, playa, colinas, acantilados, bosque provisional, 3 caminos, POIs, escenas separadas, controlador y overlay de prueba, validación estática | **Hecho** |
| 2 | Río o arroyo (spline tallado + capa de barro + malla de agua), rocas y formaciones, detalles (helechos, equisetos) | Pendiente |
| 3 | Perfil de calidad baja (nivel de calidad + asset URP reducido + componente que ajusta el terreno por nivel). **Medir FPS y memoria** en el equipo de Gerardo y en WebGL | Pendiente |
| 4 | Agua: shader barato (color por profundidad, ondas en vértice, espuma costera) con variante baja | Pendiente |
| 5 | Arte final: capas PBR, vegetación jurásica con LOD e impostores, audio ambiental | Pendiente |
| 6 | Gameplay de la misión sobre los POIs | Pendiente |

## Cómo usar la herramienta

1. **CHRONO WILD ▸ Generador de terreno**.
2. Ajustar parámetros en la ventana (es el mismo asset de configuración) y pulsar **Previsualizar**:
   genera un mapa 2D sin tocar el terreno.
3. **Generar / actualizar TerrainData** (deja respaldo en `Backups/`).
4. **Construir / actualizar escenas**: crea solo lo que falta y nunca borra objetos añadidos a mano.
5. **Validar** escribe `Docs/Terrain/ValidationReport.md`.
6. POIs: **escena ← configuración** recoloca los marcadores; **configuración ← escena** guarda en
   el asset las posiciones de los marcadores movidos a mano.

Atajo: **CHRONO WILD ▸ Terreno ▸ Generar todo** ejecuta los pasos 3–5.

Controles de prueba en `Island_Gameplay`: WASD, Shift para correr, Espacio para saltar,
T para reiniciar el cronómetro de recorrido, F1 para ocultar el overlay y Esc para liberar el ratón.

## Validación

| Comprobación | Estado |
|---|---|
| Terreno navegable (87,6 % de la tierra a ≤ 40°) | Medido (estático) |
| Pendientes de los caminos | Medido; dos tramos cortos de más de 25° por revisar |
| Rutas y distancias | Medido |
| Jugador apoyado en el terreno y escena aditiva sin errores | Probado en Play (equipo de David) |
| Colisiones del controlador en bordes y acantilados | **Pendiente** de recorrer a mano |
| FPS y memoria a 720p en calidad baja | **Pendiente**: requiere la fase 3 y el equipo de Gerardo |
| Carga del proyecto en un equipo modesto | **Pendiente** |
| Build WebGL | **Pendiente** |
