# Mediciones de referencia: iceworld / poolday / tennis

Tabla derivada de los BSP GoldSrc (v30) de referencia con
`tools/parse_bsp.py`. Los .bsp son SOLO lectura y no viven en el repo.
Unidades: unidades GoldSrc (u). Escala aprox: 1u ~= 2.54cm
(jugador 72u ~= 183cm); el ojo en pie esta a 64u de los pies.

## Comparativa rapida

| mapa | CT | T | area XY (u) | dist. centroides (u) | spawn-enemigo min/med/max (u) | cobertura media (u) | sightline max despejada (u) |
|---|---|---|---|---|---|---|---|
| fy_iceworld (iceworld) | 12 | 12 | 1536 x 1792 | 1280 | 1152 / 1337 / 1605 | 88 (n=36, mediana 80) | 0 (0/144 pares con LOS) |
| fy_pool_day (poolday) | 16 | 16 | 1536 x 2048 | 962 | 635 / 1047 / 1436 | 78 (n=395, mediana 80) | 1249 (9/256 pares con LOS) |
| he_tennis (tennis) | 10 | 10 | 1088 x 896 | 359 | 231 / 382 / 554 | 57 (n=225, mediana 40) | 554 (100/100 pares con LOS) |

## fy_iceworld (iceworld)

- Bounds (model 0 worldspawn): min (-384, -1536, -258), max (1152, 256, 128).
- Tamano: 1536 x 1792 x 386 u; diagonal 2392 u.
- Spawns: CT (info_player_start) x12 en x[-2,768] y[-64,64]; T (info_player_deathmatch) x12 en x[0,768] y[-1344,-1216]; 64 entidades en total.
- Distancia entre centroides CT-T: 1280 u.
- Spawn enemigo-enemigo: min 1152 u, media 1337 u, max 1605 u.
- Spawn mismo equipo mas cercano: 126 u; sightline max intra-equipo despejada: 781 u.
- Coberturas (caras verticales de worldspawn, 32-128u de alto): n=36, media 88.0 u, mediana 80.0 u.
- Sightline maxima con LOS despejado entre enemigos (ojo a ojo, ojo = spawn+28u): 0 u (0/144 pares enemigos con LOS).
- Armoury (24 entidades): weapon_mp5navy x2, weapon_tmp x2, weapon_p90 x2, weapon_mac10 x2, weapon_ak47 x2, weapon_sg552 x2, weapon_m4a1 x2, weapon_aug x2, weapon_awp x2, weapon_m3 x2, weapon_xm1014 x2, weapon_m249 x2. De ellos, 2 asumidos como weapon_mp5navy: entidades sin clave `item` (default FGD 0).
- Otras entidades: func_buyzone.

## fy_pool_day (poolday)

- Bounds (model 0 worldspawn): min (-896, -512, -224), max (640, 1536, 320).
- Tamano: 1536 x 2048 x 544 u; diagonal 2617 u.
- Spawns: CT (info_player_start) x16 en x[-564,299] y[-116,227]; T (info_player_deathmatch) x16 en x[-590,300] y[845,1137]; 93 entidades en total.
- Distancia entre centroides CT-T: 962 u.
- Spawn enemigo-enemigo: min 635 u, media 1047 u, max 1436 u.
- Spawn mismo equipo mas cercano: 102 u; sightline max intra-equipo despejada: 759 u.
- Coberturas (caras verticales de worldspawn, 32-128u de alto): n=395, media 77.8 u, mediana 80.0 u.
- Sightline maxima con LOS despejado entre enemigos (ojo a ojo, ojo = spawn+28u): 1249 u (9/256 pares enemigos con LOS).
- Armoury (34 entidades): weapon_mp5navy x2, weapon_tmp x2, weapon_p90 x2, weapon_mac10 x2, weapon_ak47 x2, weapon_sg552 x2, weapon_m4a1 x2, weapon_aug x2, weapon_scout x2, weapon_g3sg1 x2, weapon_awp x2, weapon_m3 x2, weapon_xm1014 x2, weapon_m249 x2, weapon_hegrenade x6. De ellos, 2 asumidos como weapon_mp5navy: entidades sin clave `item` (default FGD 0).
- Otras entidades: ambient_generic, func_bomb_target, func_button, func_buyzone, func_door_rotating, func_illusionary, func_wall, func_water.

## he_tennis (tennis)

- Bounds (model 0 worldspawn): min (64, -1152, -576), max (1152, -256, 64).
- Tamano: 1088 x 896 x 640 u; diagonal 1548 u.
- Spawns: CT (info_player_start) x10 en x[205,333] y[-1006,-749]; T (info_player_deathmatch) x10 en x[564,692] y[-1014,-757]; 82 entidades en total.
- Distancia entre centroides CT-T: 359 u.
- Spawn enemigo-enemigo: min 231 u, media 382 u, max 554 u.
- Spawn mismo equipo mas cercano: 64 u; sightline max intra-equipo despejada: 287 u.
- Coberturas (caras verticales de worldspawn, 32-128u de alto): n=225, media 57.5 u, mediana 40.0 u.
- Sightline maxima con LOS despejado entre enemigos (ojo a ojo, ojo = spawn+28u): 554 u (100/100 pares enemigos con LOS).
- Armoury (40 entidades): weapon_hegrenade x40.
- Otras entidades: func_buyzone, func_wall, game_player_equip, info_target, trigger_camera.

## Metodo y supuestos

- Parser BSP30 propio: lumps de entidades, planos, vertices, aristas, surfaristas, caras y models; sin dependencias.
- Bounds = bbox del model 0 (worldspawn). Incluye cielo/caja del mapa; el area jugable es menor o igual.
- Cobertura = cara de worldspawn con plano near-vertical (|nz| <= 0.2) cuya altura cae en [32, 128]u: excluye escalones/cordones (<32u) y muros perimetrales/rascacielos (>128u). Heuristica, no semantica de gameplay.
- LOS: segmento ojo-a-ojo entre cada par CT-T contra worldspawn + brush models visibles en pose base (func_wall, func_door_rotating, func_water, func_illusionary, func_button) como bloqueadores de doble cara. Solo se excluyen volumenes invisibles: func_buyzone, func_bomb_target y trigger_*/info_*/game_*. Las puertas se consideran cerradas: si abren en juego, los pares despejados son cota inferior. Se ignoran rozamientos coplanares. Verificado con un segundo metodo 2D independiente.
- Armoury: entidades sin clave `item` se asumen item=0 (default FGD = weapon_mp5navy); la linea de armoury de cada mapa indica cuantos fueron asumidos.
- Ojo = origin del spawn + 28u (= 64u sobre los pies en pie). Verificado: los spawns flotan 1-33u sobre el suelo y origin = pies+36u.
