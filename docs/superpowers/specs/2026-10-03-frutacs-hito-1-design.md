# FrutaCS — Hito 1: Core offline con bots — Spec de diseño

- Fecha: 2026-10-03
- Estado: pendiente de revisión del PM
- Rama: `feature/spec-hito-1` → PR contra `dev`

## 1. Intención (acordada)

Clon de CS 1.6 con estilo "fruta" (concepto, no literal): servidores estilo
"fua" de NostalgiaGamers — mapas chicos tipo aim_/fy_ (iceworld, poolday,
tennis), rondas 7v7 donde el único objetivo es eliminar al equipo rival.
Free to play monetizado por skins (hito 3). El gameplay debe sentirse
exactamente igual al 1.6 (recoil, spray, movimiento).

Supuestos explícitos (corregir si no): PC Windows + Linux, primera persona,
C# (coincide con el nombre del repo; GDScript queda descartado salvo aviso).

## 2. Descomposición en hitos

1. **Core offline con bots** (este spec): movimiento, armas, recoil, 1 mapa.
2. Multiplayer con netcode (servidor autoritativo).
3. Cuentas + skins + pagos.

## 3. Dirección visual (Sección 1, aprobada)

Low-poly estilizado con PBR (ni realista ni retro). Motivos: producible por
equipo chico, 60 fps en integradas, y materiales con slots de textura por
parte → cada skin futura es un set de texturas sin remodelar. Sin
vertex-colors como única técnica.

- Personajes: humanoides estilizados, silueta y paleta distinta por equipo
  (la legibilidad es parte del feel; ninguna skin toca hitboxes jamás).
- "Fruta" = concepto y tono (marca, nombres, HUD), no utilería literal.
  Mapas = lugares lúdicos convertidos en arena (pileta, hielo, cancha),
  paleta alegre pero fondos que no camuflan rivales.
- Mapas de referencia (iceworld, poolday, tennis): originales inspirados,
  nunca copias ni archivos redistribuidos. Los `.bsp` viven solo como
  referencia privada en `FrutaCS-reference/` (fuera del repo, gitignored).
- Pipeline: `.blend` → `.glb` → Godot, `StandardMaterial3D`, viewmodel y
  worldmodel compartiendo materiales.

## 4. Arquitectura técnica (Sección 2, aprobada)

- Motor: Godot **.NET 4.7.2** + C# + .NET 8 SDK. Física fija 60 Hz, input
  muestreado por tick.
- Repo: `src/` código, `assets/` (originales), `maps/` escenas,
  `data/` tablas de armas como recursos, `docs/superpowers/specs/` specs.
- Componentes (una responsabilidad cada uno):
  - `PlayerController`: movimiento GoldSrc (fricción, accelerate/wishdir,
    air-accelerate, bunny hop sin cap en aire), valores en `MovementConfig`.
  - `WeaponSystem`: hitscan, dispersión y recoil como datos; viewmodel
    separado compartiendo materiales.
  - `BotAI`: máquina de estados (patrullar, perseguir por visión/oído,
    ráfagas, recoger arma del piso) sobre `NavigationAgent3D`.
  - `RoundManager`: rondas a eliminación, timer de partido, ronda de oro,
    reseteo por ronda. Sin economía.
  - `VoteManager`: genérico (propuesta + opciones + timer + mayoría); hito 1
    = voto de mapa, reutilizable para kick/restart en hito 2.
  - `HUD`: salud, armadura, munición, marcador, scoreboard TAB, tono fruta.
- Aislamiento futuro: simulación (movimiento, daño, recoil) en clases C#
  puras, separadas de nodos — testeables sin motor, reutilizables por el
  servidor autoritativo del hito 2.
- Git: `feature/*` → PR a `dev` (pruebas) → PR a `main` solo con aprobación
  del PM. Ningún merge a `main` sin OK explícito.

## 5. Movimiento + recoil (Sección 3, aprobada)

- Escala 1:1 con unidades CS. Correr 250 u/s se siente 1.6 solo si el mundo
  mide lo mismo.
- `WeaponData` por arma: daño, multiplicador de cabeza, penetración de
  armadura, cadencia, cargador/reserva, dispersión base por estado
  (quieto/moviéndose/aire/agachado) y **tabla de ~30 balas con offsets
  pitch/yaw** (patrón aprendible y compensable; el azar solo vive dentro del
  cono de dispersión).
- Feedback = feel: crosshair dinámico ligado al spread real, view punch
  recuperable, sonido por arma.
- Armas hito 1: AK-47, M4A1, AWP (mira + penalidad de movimiento), Deagle,
  cuchillo (dos ataques). Penetración de paredes simple por material
  (el wallbang en mapas con cajas es core).
- Validación: telemetría interna (u/s reales), spray del AK comparado bala
  por bala contra 1.6, test de distancia de salto con strafe.

## 6. Bots + reglas de ronda (Sección 4, aprobada con correcciones)

- 13 bots, dificultad parametrizable (reacción, error, ráfaga); el mapa
  define la mezcla. Bots votan al azar (ejercitan el sistema).
- **Sin compra ni economía.** Adquisición según el mapa:
  - loadout fijo (ej. mapa awp_: AWP + cuchillo),
  - pickup del piso (esparcidas + dropeo de muertos),
  - puntos de compra en zonas específicas (lista del mapa, sin dinero
    global; costo/disponibilidad por mapa).
- Partido de **10 minutos por mapa** (configurable): gana quien tenga más
  rondas; empate → ronda de oro súbita. Timer de ronda corto anti-camper
  (2 min: gana el de más vivos, empate si igualan). Mejor de N rondas
  queda reemplazado por este formato.
- Fin de partido → votación de próximo mapa → cambio. Muerto espera el fin
  de ronda; reset rearma según el mapa.

## 7. El mapa (Sección 5, aprobada)

- Arena cerrada simétrica ~2000×2000u, spawns opuestos (7 puntos por equipo),
  centro disputado con coberturas bajas saltables (64u), dos rutas laterales.
  Sin posiciones dominantes permanentes ni rincones oscuros.
- Temática mapa 1: **pileta vacía** (espíritu poolday, identidad propia).
- Adquisición mapa 1: base cuchillo + Deagle; AK-47, M4A1 y una AWP como
  pickups en centro y laterales. Sin zonas de compra en mapa 1.
- Data-driven: spawns, pickups y zonas como markers + recurso `MapConfig`.

## 8. Alcance hito 1 — IN / OUT (Sección 6, aprobada)

IN: lo descripto arriba + menú mínimo (jugar, sensibilidad, salir) +
sonidos y modelos originales placeholder pero propios.

OUT explícito: granadas, multiplayer, cuentas, skins, tienda, anti-cheat,
más mapas, matchmaking.

## 9. Criterios de aceptación

- Correr 250 u/s y salto con strafe iguales a 1.6 (telemetría).
- Spray del AK coincide bala por bala contra referencia.
- Ronda 7v7 completa sin errores ni bots trabados.
- 60 fps estables en integrada (piso: GTX 1650 / Vega).
- Partido de 10 min termina, se vota y cambia de mapa.
- Playtest ciego: jugador de 1.6 no distingue el feel en 5 minutos.
