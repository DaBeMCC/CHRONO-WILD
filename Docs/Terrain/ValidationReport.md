# Informe de validación del terreno

- Fecha: 2026-10-31 19:55
- Semilla: 1337 · Terreno 600 × 600 m · heightmap 513² (1.17 m/muestra) · rango vertical 140 m
- Velocidades de referencia: caminar 4 m/s, correr 7 m/s (DebugFirstPersonController)

## Isla

- Superficie emergida: 14.4 ha (0.144 km²)
- Extensión: 437 m este-oeste × 422 m norte-sur
- Altura máxima sobre el mar: 105.5 m
- Transitable (≤ 40°): 87.6 % de la tierra · no escalable (> 45°): 10.1 %
- Cruzar la isla de lado a lado (línea recta, la mayor extensión): 109 s caminando / 62 s corriendo

## Caminos

| Camino | Longitud | Desnivel + / − | Pendiente máx. | Tramo > 15° | Tramo > 25° | Caminando | Corriendo |
|---|---|---|---|---|---|---|---|
| Ruta principal (playa → portal) | 572 m | 33 / 13 m | 27.3° | 23 m | 6 m | 2 min 23 s | 1 min 22 s |
| Ruta alternativa: costa oeste | 311 m | 5 / 6 m | 15.0° | 1 m | 0 m | 1 min 18 s | 44 s |
| Ruta alternativa: sendero del monte | 245 m | 23 / 15 m | 36.6° | 30 m | 3 m | 1 min 1 s | 35 s |

Pendiente medida en ventanas de 4 m sobre el eje del camino. El CharacterController de prueba sube hasta 45°.

## Puntos de interés

| Id | Nombre | Tipo | Posición (m) | Altura | Pendiente | Distancia al spawn | Aviso |
|---|---|---|---|---|---|---|---|
| spawn_beach | Playa del naufragio | Spawn | (300, 114) | 1.2 m | 0.1° | 0 m |  |
| res_beach_wood | Troncos a la deriva | Resource | (336, 120) | 1.5 m | 0.0° | 36 m |  |
| res_beach_stone | Rocas de la orilla | Resource | (252, 114) | 1.5 m | 0.0° | 48 m |  |
| res_fiber | Matorral de fibras | Resource | (288, 150) | 4.1 m | 9.1° | 38 m |  |
| camp | Claro del campamento | BuildZone | (252, 174) | 6.1 m | 0.4° | 77 m |  |
| forest_entrance | Entrada al bosque | Waypoint | (276, 216) | 7.7 m | 0.4° | 105 m |  |
| research_plant | Ginkgo ancestral | Research | (336, 300) | 13.3 m | 2.9° | 189 m |  |
| valley_overlook | Mirador del valle | Viewpoint | (252, 348) | 7.8 m | 3.1° | 239 m |  |
| dino_valley | Valle de los herbívoros | CreatureZone | (180, 378) | 4.7 m | 0.0° | 290 m |  |
| mount_cronos | Monte Cronos | Landmark | (396, 420) | 102.8 m | 38.9° | 321 m |  |
| portal | Portal temporal | Portal | (252, 456) | 21.1 m | 0.3° | 345 m |  |

## Revelación progresiva: % de tierra visible

Línea de visión desde la altura de los ojos contra el relieve (sin árboles ni niebla). Cuanto menor, más se revela al explorar.

| Desde | % de tierra visible | Portal visible | Monte Cronos visible |
|---|---|---|---|
| Playa del naufragio | 29.6 % | sí | sí |
| Claro del campamento | 29.8 % | no | sí |
| Entrada al bosque | 34.3 % | sí | sí |
| Ginkgo ancestral | 28.0 % | no | no |
| Mirador del valle | 17.9 % | sí | sí |
| Valle de los herbívoros | 20.5 % | sí | sí |
| Portal temporal | 12.0 % | — | sí |

## Vegetación

- Árboles: 1336 (prototipos: 3)
  - PF_Conifer_Placeholder: 669
  - PF_Ginkgo_Placeholder: 552
  - PF_Cycad_Placeholder: 115

## Pendiente de validar en Play / equipo objetivo

- FPS y memoria a 720p en calidad baja (usar el overlay de Island_Gameplay; F1 lo oculta).
- Colisiones reales del CharacterController en bordes de caminos y acantilados.
- Visibilidad real con vegetación (este informe ignora los árboles).
