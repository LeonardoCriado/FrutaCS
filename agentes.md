# FrutaCS — Guía de contexto para agentes

> Documento vivo para retomar el proyecto sin releer todo el repo.
> Última actualización: **2026-10-27**.
> Regla de oro: **el código manda**. Si este documento y el código difieren,
> investigá el código, corregí el documento y avisá. No inventes sistemas que
> no existen.

## 0. Primeros 5 minutos de una sesión nueva

1. Leer este archivo completo.
2. `git status --short` y `git log --oneline -5`.
3. `dotnet build FrutaCS.sln` y `dotnet test FrutaCS.sln`.
4. Si vas a tocar escenas o ver el juego: usar **`godot-mono`**, nunca `godot`.
5. **No commitear ni revertir cambios sin pedido explícito.** Hay trabajo
   importante sin commitear (ver §2).
6. Antes de implementar skins u otro sistema grande, confirmar alcance con el
   usuario. El hito 1 no incluye skins, multiplayer, economía ni granadas.

## 1. Qué es FrutaCS

Clon de **Counter-Strike 1.6** con identidad propia "fruta" (marca y tono, no
frutas literales en el mapa). El objetivo es que el *feel* de movimiento,
recoil y spray sea el de 1.6, en mapas chicos estilo `fy_`/`aim_`.

- **Hito 1 (actual):** core offline con bots. Un mapa, 7v7, movimiento y armas
  GoldSrc, rondas a eliminación, partido de 10 minutos, votación de mapa.
- **Hito 2 (futuro):** multiplayer con servidor autoritativo.
- **Hito 3 (futuro):** cuentas, skins y pagos.
- **Dirección visual:** low-poly estilizado con PBR, legible, 60 fps en GPUs
  integradas. Sin assets de Valve ni de la comunidad. Todo el arte actual es
  procedural/placeholder propio.

El repositorio está en la rama `dev` (HEAD `16ec845`, merge del PR #2).
La escena principal es el menú; el mapa jugable es `fy_pileta`.

## 2. Estado actual del árbol de trabajo

**Importante:** el árbol tiene cambios sin commitear. No hacer `git checkout`,
`git restore`, `git clean` ni revertir archivos sin consultar.

Archivos modificados/nuevos al escribir este documento:

| Archivo | Qué aporta |
|---|---|
| `src/weapons/WeaponViewmodel.cs` **(nuevo)** | Modelos low-poly procedurales por arma, poses, luces locales, geometría del cuchillo, `AddArms` (brazos first-person con guantes oscuros y mangas color de equipo; anclas `Grip`/`Support` por arma). |
| `src/weapons/WeaponSystem.cs` | Construcción del viewmodel, animaciones (equipar, retroceso, reload, bob, sway, cuchillo), fogonazo, trazadoras finas, hitmarker, sonidos de impacto, números de daño, headshots, ocultar AWP al apuntar; pasa el equipo a `Build` para el color de manga de las manos. |
| `src/weapons/WeaponView.tscn` | Se redujo al host: `Viewmodel`, `Muzzle`, `FireSound`; la geometría se construye en runtime. |
| `src/player/PlayerBody.cs` | Inventario de 3 slots, cambio 1/2/3 y Q, loadout base, evento `Damaged`, re-captura del mouse, pausa. |
| `src/ui/Hud.cs` | Slots, pausa, flash de daño, flecha de amenaza, textos con outline. |
| `src/bots/Bot.cs` | Nameplates de equipo, daño al jugador con posición del atacante. |
| `src/game/MatchManager.cs` | Respawn con Deagle + cuchillo vía `ResetBaseLoadout`. |
| `src/maps/WeaponPickup.cs` | Pickup del jugador usa `GiveWeapon` (slot correcto). |
| `src/weapons/WeaponSim.cs` | Sobrecarga de daño con headshot. |
| `tests/FrutaCS.Tests/WeaponSimTests.cs` | Test de multiplicador de cabeza. |
| `tests/FrutaCS.Tests/PlayerWeaponGlueTests.cs` | Tests de viewmodel custom y escena. |
| `project.godot` | MSAA 3D 2x. |

Agregado después (entorno de prueba estilo workshop):

| Archivo | Qué aporta |
|---|---|
| `src/ranges/WeaponRange.cs` **(nuevo)** | Glue del rango: picker de las 5 armas (teclas 1-5 y botones), reserva infinita con recarga real, 7 dummies (bot congelado) que respawnean, toggle de strafe, HUD overlay construido en código. |
| `scenes/WeaponRange.tscn` **(nuevo)** | Escena del rango: piso 3000×3000, muros perimetrales, pared fina y gruesa para wallbang, `PlayerBody`. |
| `src/player/PlayerBody.cs` | Flag `SceneOwnsWeaponSwitching` (default off): el rango silencia el manejo de slots 1/2/3/Q para que no compita con su picker. |
| `src/ui/MainMenu.cs` / `MainMenu.tscn` | Botón **RANGO DE PRUEBAS** → carga `res://scenes/WeaponRange.tscn`. |
| `tests/FrutaCS.Tests/WeaponRangeTests.cs` **(nuevo)** | 5 tests de forma: escena, glue, cobertura de armas, flag del player, botón del menú. |

Validación de esa pasada: `dotnet build` con **0 errores**, `dotnet test` con
**52/52 tests**, y smoke test headless con exit 0. Las capturas de verificación
quedaron en `/tmp/opencode/weapon_*.png` (pueden no persistir entre sesiones).

Validación del rango: `dotnet build` 0 errores / 1 warning (`CS8632` conocido),
`dotnet test` **57/57**, y smoke test headless de `WeaponRange.tscn`
(`--quit-after 600`) con **exit 0**.

Agregado después (humanoides de bots + manos first-person):

| Archivo | Qué aporta |
|---|---|
| `src/bots/BotModel.cs` **(nuevo)** | Humanoide low-poly procedural (cabeza con gorra y anteojos, torso, brazos con guantes, piernas, botas) + `BotRig` con el ciclo de caminada puro (`NextPhase`/`SwingFor`). Colores por equipo: CT azul, T naranja. |
| `src/bots/Bot.tscn` | `Body` deja de ser la cápsula mesh: ahora es contenedor `Node3D` donde `BotModel` construye las piezas. La cápsula de colisión (72 u) no cambia. |
| `src/bots/Bot.cs` | `_Ready` construye el humanoide (`BotModel.Build`), `Steer` anima el rig con la velocidad horizontal, `Respawn`/`Die` muestran/ocultan el contenedor. Elimina `PaintTeamColor`. |
| `tests/FrutaCS.Tests/CharacterModelTests.cs` **(nuevo)** | 4 tests: piezas y colores del humanoide, escena/tests del contenedor, matemática pura del Swing, manos del viewmodel en todas las armas. |

Validación de los humanoides: `dotnet build` 0 errores, `dotnet test`
**61/61**, smoke headless del rango y de `fy_pileta` (13 bots) con exit 0, y
capturas de verificación en `/tmp/opencode/range_*.png` y `map_bots*.png`
(fuera del repo; pueden no persistir).

## 3. Stack y requisitos

| Pieza | Versión / decisión |
|---|---|
| Motor | **Godot .NET (mono) 4.7.2** |
| Lenguaje de juego | **C#**, `net8.0` |
| SDK | .NET 8 |
| Renderer | `gl_compatibility` (pensado para GPUs integradas) |
| Física | 60 Hz fijos (`physics_ticks_per_second=60`) |
| Antialiasing | MSAA 3D 2x |
| Tests | GdUnit4 + `gdunit4.test.adapter` + VSTest |
| Escalado | 1 unidad Godot = 1 pulgada CS/GoldSrc |
| Dependencias de arte | Ninguna; primitivas y sonidos generados en C# |

Además: el proyecto usa `Directory.Build.props` para que la raíz Godot sea la
raíz del repo y para mandar los binarios a `.godot/mono/temp/bin/`.

### Gotcha crítico: mono vs estándar

El binario `godot` instalado en este entorno es la build **estándar** y no puede
cargar scripts `.cs`: falla con `No loader found for resource ... .cs`.
Usar siempre:

```bash
godot-mono --version   # 4.7.2.stable.mono.official...
```

## 4. Comandos

```bash
# Compilar
dotnet build FrutaCS.sln

# Correr toda la suite (61 tests)
dotnet test FrutaCS.sln

# Smoke test sin ventana (importa, arranca y sale)
godot-mono --headless --path . --quit-after 10

# Jugar el menú principal
godot-mono --path .

# Abrir el editor
godot-mono --path . --editor

# Jugar directo el mapa
godot-mono --path . res://src/maps/fy_pileta.tscn

# Campo de pruebas de armas/movimiento (sin MatchManager)
godot-mono --path . res://scenes/TestRange.tscn

# Rango de armas estilo workshop: picker 1-5, dummies, reserva infinita
godot-mono --path . res://scenes/WeaponRange.tscn

# Rehornear el navmesh del mapa (obligatorio tras cambiar layout)
godot-mono --headless --path . res://src/maps/makenav.tscn

# Si hace falta forzar renderer/display en Linux
godot-mono --path . --display-driver x11 --rendering-driver opengl3
```

Notas:

- `dotnet test` no necesita el motor; las simulaciones puras corren en .NET.
- Los tests de escenas **parsean texto** de `.tscn`/`.tres`; no instancian nodos
  C# porque no hay runtime de Godot bajo `dotnet test`.
- El build emite warnings `CS8632` (anotaciones nullable sin `#nullable` en
  `RoundManager.cs` y `VoteManager.cs`). Son inocuos, pero se pueden limpiar.
- Para capturas visuales se puede usar una escena temporal, pero no dejarla en
  el repo; en la última pasada se usó `.review/` y se borró.

## 5. Cómo se juega

### Flujo

1. `MainMenu` → **JUGAR** carga `res://src/maps/fy_pileta.tscn`.
2. `fy_pileta.tscn` contiene geometría, `Match` (`MatchManager`), `Hud`,
   pickups, spawns y `NavigationRegion3D`.
3. `MatchManager` crea al jugador (`Vos`, equipo CT) y **13 bots**:
   6 CT + 7 T. Total: **7 vs 7**.
4. Empieza la ronda 1 inmediatamente. No hay fase de compra ni economía.
5. La ronda termina cuando un equipo queda sin vivos, o cuando expira el timer
   de ronda. La siguiente ronda arranca enseguida.
6. A los 10 minutos de partido se cierra la ronda en curso (si estaba viva) y
   gana quien tenga más rondas. Empate → **ronda de oro** súbita.
7. Al terminar el partido se abre una votación de mapa (30 s). El único mapa
   del pool es `fy_pileta`, así que en la práctica es una revancha.
8. Al cerrar la votación, el HUD recarga la escena actual tras 3 s.

### Reglas exactas

- **Partido:** 600 s (10 min).
- **Ronda:** 120 s (2 min), anti-camper.
- **Victoria de ronda:** eliminar a todo el equipo rival.
- **Si expira la ronda:** gana el equipo con más vivos; si hay empate, la ronda
  es tablas y no da punto.
- **Si expira el partido a mitad de ronda:** la ronda en curso se juega hasta
  resolverse y cuenta. Después se cierra el partido.
- **Empate de partido:** ronda de oro; si la ronda de oro empata, se repite.
- **Sin economía:** el loadout base es **Deagle + cuchillo**; los rifles y la
  AWP están en el piso y se reponen al inicio de cada ronda.
- **Sin friendly fire:** los compañeros bloquean la bala pero no reciben daño.

### Nombres de bots

CT (6): `Naranja`, `Limon`, `Manzana`, `Pera`, `Uva`, `Sandia`.
T (7): `Melon`, `Kiwi`, `Mango`, `Frutilla`, `Banana`, `Cereza`, `Durazno`.
El jugador se llama `Vos`.

## 6. Controles

| Acción | Tecla / botón |
|---|---|
| Mover | `WASD` o flechas |
| Saltar | `Espacio` |
| Caminar (velocidad reducida) | `Shift` |
| Agacharse | `Ctrl` o `C` |
| Disparar | Clic izquierdo |
| Secundario | Clic derecho: AWP apunta; cuchillo apuñala |
| Recargar | `R` |
| Slot 1 (primario: rifle/AWP) | `1` |
| Slot 2 (secundario: Deagle) | `2` |
| Slot 3 (cuchillo) | `3` |
| Última arma | `Q` |
| Scoreboard | `TAB` (abre/cierra) |
| Pausa | `ESC` |
| Votar | `1`..`9` o botones del panel |
| Recapturar mouse | Clic izquierdo cuando el mouse está visible y no hay pausa |

El mouse empieza capturado en la partida. `ESC` pausa y libera el mouse;
`ESC` de nuevo vuelve. En `TestRange` (sin HUD) `ESC` solo libera el mouse.
Al abrir la votación el mouse se libera y la partida se despausa.

### Rango de pruebas (`WeaponRange`)

Sin MatchManager, sin rondas, sin HUD de partido. Entrada: botón
**RANGO DE PRUEBAS** del menú, o
`godot-mono --path . res://scenes/WeaponRange.tscn`.

| Acción | Tecla / botón |
|---|---|
| Elegir arma (las 5) | `1` AK-47 · `2` M4A1 · `3` AWP · `4` DEAGLE · `5` CUCHILLO, o botones del panel |
| Recargar | `R` (recarga real de 2.5 s; la reserva es infinita) |
| Strafe de dummies | `G` (toggle; quietos por defecto) |
| Reponer dummies | `F` |
| Liberar / recapturar mouse | `ESC` / clic izquierdo |

- 7 dummies ("Blanco 1..7"): bots reales con la IA congelada, a 350/700/1050/1400 u
  con offsets laterales; respawnean a los ~1.5 s de morir. Comparten todas las
  reglas del match (daño por distancia, headshot ≥54 u, hitmarker, números de daño).
- El picker usa `PlayerBody.SceneOwnsWeaponSwitching` para que las teclas 1-5 no
  activen también los slots 1/2/3 del inventario.
- Pared fina (40 u) y gruesa (120 u) a la derecha para probar wallbang.

## 7. Arquitectura

### Principio central

**Simulación pura en C# + glue fino de Godot.** La lógica de movimiento,
daño, recoil, bots, rondas y votos no toca `Node`/`SceneTree`; los nodos solo
leen input, mueven cuerpos, reproducen efectos y conectan escenas. Esto permite
testear sin motor y reutilizar la simulación en el servidor autoritativo del
hito 2.

```text
Input / escena
    │
    ├─ PlayerBody (CharacterBody3D)
    │     ├─ MovementSim.Tick → Velocity → MoveAndSlide → SyncFromEngine
    │     └─ WeaponSystem (hijo de cámara)
    │           ├─ WeaponSim (daño, spread, recoil)
    │           └─ WeaponViewmodel (malla, luces, animación)
    │
    ├─ Bot (CharacterBody3D)
    │     ├─ BotBrain.Update(percepción) → decisión
    │     └─ NavigationServer3D + MovementSim
    │
    └─ MatchManager
          ├─ RoundManager (rondas/partido)
          ├─ VoteManager (votación)
          └─ spawns, pickups, scoreboard
```

### Contratos públicos importantes

- `MovementSim.Tick(in MovementInput, in MovementParams, float delta)` y
  `SyncFromEngine(Velocity, IsOnFloor)`.
- `WeaponSim.DamageAt(...)`, `RecoilOffset(bulletIndex)`,
  `SpreadDeg(Stance)`, `Tick`.
- `BotBrain.Update(in BotPerception, in BotParams, float delta) → BotDecision`.
- `RoundManager.StartMatch/OnKill/Update`, eventos `RoundStarted` y
  `PhaseChanged`.
- `VoteManager.StartVote/CastVote/Update`, evento `VoteClosed`.
- `MatchManager.Rounds`, `MatchManager.Vote`, `OnFighterDown`,
  `OnFighterFrag`, `ResetRound`.

### Escenas

- `src/ui/MainMenu.tscn`: entrada del juego.
- `src/maps/fy_pileta.tscn`: **escena jugable real** (mapa + Match + Hud).
- `src/player/PlayerBody.tscn`: jugador, cámara y `WeaponView`.
- `src/bots/Bot.tscn`: bot, cápsula, `NavigationAgent3D` (ver §11).
- `src/weapons/WeaponView.tscn`: host del arma, sin geometría estática.
- `src/game/Game.tscn`: escena mínima con `MatchManager`; existe por el test de
  forma, pero el flujo real es Menú → `fy_pileta`. No asumir que se usa.
- `scenes/TestRange.tscn`: campo de pruebas con paredes fina/gruesa y jugador
  con AK por defecto; sin MatchManager/HUD.

## 8. Mapa de archivos

```text
project.godot                  Configuración Godot (main scene, física, renderer)
FrutaCS.sln / Directory.Build.props
data/weapons/{ak47,m4a1,awp,deagle,knife}.tres   Tablas de armas
src/player/
  MovementParams.cs            Constantes GoldSrc
  MovementSim.cs               Simulación pura de movimiento
  MovementConfig.cs            Resource editable → ToParams()
  PlayerBody.cs                Glue: input, cámara, inventario, daño
  PlayerBody.tscn
  StepUp.cs                    Ayuda de escalón para joints de cajas
src/weapons/
  WeaponData.cs                Resource de stats + cache/PreloadAll
  WeaponSim.cs                 Simulación pura de daño/spread/recoil
  WeaponSystem.cs              Glue: disparo, recarga, mira, FX, viewmodel
  WeaponViewmodel.cs           Modelos low-poly y poses por arma
  WeaponView.tscn
src/bots/
  BotBrain.cs                  FSM pura
  BotParams.cs                 Dificultad/percepción
  BotModel.cs                  Humanoide low-poly + rig de caminata
  Bot.cs                       Glue: nav, percepción, disparo, pickups, modelo
  Bot.tscn
src/game/
  RoundManager.cs              Rondas, relojes, oro (puro)
  VoteManager.cs               Votación genérica (puro)
  MatchManager.cs              Spawns, timers, score, votos (glue)
  Game.tscn
src/maps/
  MapConfig.cs                 Spawns, pickups, covers, layout (fuente de verdad)
  BotDifficulty.cs             Mix de dificultad por spawn
  PickupEntry.cs               Entrada de pickup
  WeaponPickup.cs              Area3D de pickup
  fy_pileta.tscn               Mapa jugable
  fy_pileta_nav.tres           Navmesh horneado
  MakeNav.cs / makenav.tscn    Herramienta de bake
src/ui/
  MainMenu.cs / MainMenu.tscn
  Hud.cs / Hud.tscn
  GameSettings.cs              Sensibilidad estática (sin save)
src/ranges/
  WeaponRange.cs               Glue del rango workshop (picker, dummies, HUD)
scenes/
  TestRange.tscn               Campo de pruebas de armas/movimiento
  WeaponRange.tscn             Rango workshop: todas las armas + dummies
tests/FrutaCS.Tests/           Suite GdUnit4/Net (57 tests)
docs/superpowers/specs/...     Spec del hito 1
docs/superpowers/plans/...     Plan de implementación
docs/reference/mediciones.md   Medidas de mapas CS de referencia
tools/parse_bsp.py             Parser de BSP de referencia (solo herramienta)
```

## 9. Movimiento

Implementación GoldSrc/Quake en `MovementSim`:

| Constante | Valor |
|---|---|
| Run speed | 250 u/s |
| Walk speed | 130 u/s |
| Ground accelerate | 5 |
| Air accelerate | 10 |
| Friction | 4 |
| Gravity | 800 u/s² |
| Jump velocity | 268.328157 (45 u de altura) |
| Stop speed | 100 |
| Tick | 1/60 s |

- Cápsula del jugador/bot: radio 16 u, altura 72 u, centro en y=36.
- Ojo en pie: y=64. Agachado: y=44. Pitch máximo: ±89°.
- Agacharse **no** cambia la velocidad; solo la postura, el ojo y el spread.
- Aire: no hay cap de velocidad; el air-strafe puede ganar velocidad (bhop).
- El salto también aplica air-accelerate en el tick de despegue (no congela
  el movimiento horizontal).
- `SyncFromEngine` adopta el resultado de `MoveAndSlide`: piso y velocidad
  recortada por paredes son autoritativos. Si se toca el movimiento, mantener
  ese write-back.
- `StepUp.TryStep` ayuda a subir joints de 40 u cuando el cuerpo se traba.
- El mapa usa rampas de ~40° hacia la pileta; la navegación de bots está
  horneada con altura real (deck 0, pileta −96).

## 10. Armas y combate

### 10.1 Loadout, slots y pickups

- Slots del jugador: **0 primario**, **1 secundario**, **2 cuchillo**.
- Loadout base por ronda: **Deagle + cuchillo**, arranca con Deagle.
- Pickups del mapa: AK-47 ×2, M4A1 ×2, AWP ×1. Reemplazan el slot primario y
  auto-equipan. Volver a agarrar la misma arma la recarga (arma nueva).
- La munición por slot se guarda/restaura al cambiar; cambiar no recarga.
- Los bots no usan slots: tienen un arma actual y solo agarran pickups que
  sean **mejora** de tier (`knife 0 < deagle 1 < rifle 2 < awp 3`). No
  dropean armas al morir.
- `WeaponPickup` se reclama una sola vez; el que llega primero (humano o bot)
  se la queda. `Grab()` es idempotente y oculta el pickup hasta el reset.

### 10.2 Tabla de stats

| Arma | Daño | RPM | Cargador/Reserva | Spread stand / move / air / duck (°) | RangeMod | Penetración (etapas / grosor) | Melee |
|---|---:|---:|---|---|---|---|---|
| AK-47 | 36 | 628 | 30 / 90 | 0.3151 / 3.091 / 6.8428 / 0.3151 | 0.98 | 2 / 64 u | — |
| M4A1 | 32 | 686 | 30 / 90 | 0.2292 / 2.8052 / 6.5602 / 0.2292 | 0.97 | 2 / 48 u | — |
| AWP | 115 | 41 | 10 / 30 | 0.0573 / 14.0362 / 40.3645 / 0.0 | 0.99 | 3 / 96 u | — |
| Deagle | 54 | 267 | 7 / 35 | 0.7448 / 1.4321 / 8.5308 / 0.6589 | 0.81 | 2 / 32 u | — |
| Cuchillo | 65 (primario) / 20 (estocada) | — | — / infinito | 0 | 1.0 | 0 | 48 u |

- Headshot: **×4** en armas de fuego; **×1** en cuchillo.
- Los valores salen de referencias de CS 1.6 y cada `.tres` lleva las fuentes
  en comentarios. No cambiar números "a ojo": actualizar tabla y tests.
- `ArmorPenetration` está en los datos pero **no se usa**: el hito 1 no tiene
  modelo de chaleco. El HUD muestra `CHALECO --` honestamente.
- `RecoilTable` tiene una entrada por bala (AK 30, M4 30, AWP 10, Deagle 7,
  cuchillo 0). Es determinística: el patrón se aprende; el azar vive solo
  dentro del cono de dispersión.

### 10.3 Spread, recoil y puntería

- La postura se elige en este orden: **aire** (no está en piso) → **agachado**
  → **moviéndose** (velocidad horizontal > 10 u/s) → **quieto**.
- El spread efectivo es exactamente el que usa el disparo; la cruz del
  crosshair se abre con `6 px + spread * 3 px`. No debe haber discrepancia
  entre crosshair y bala.
- El recoil es acumulativo por tabla: cada disparo suma el offset de la bala
  N; el view punch se recupera exponencialmente (`PunchRecoverRate = 10`).
- La balística usa `PlayerBody.GetAimBasis()` (yaw + pitch **sin** view punch)
  para que el retroceso visual no realimente el patrón.
- AWP sin apuntar: penalidad extra de **4.5749°** (atan 0.08) al spread.
- AWP apuntada: FOV 25°, sensibilidad ×0.4, viewmodel oculto.
- Al recargar, cambiar de arma o apuntar, el burst/spray se corta.

### 10.4 Disparo, daño y paredes

- Hitscan desde la cámara; rango 4000 u.
- Primer impacto visible = primera superficie golpeada.
- Si la primera superficie es un cuerpo vivo del equipo rival, se factura con
  `WeaponSim.DamageAt`.
- **Headshot:** no hay hitboxes; se considera cabeza si el punto de impacto
  está a **≥54 u** sobre el origen (pies) del rival, y el ataque no es melee ni
  secundario.
- **Paredes:** solo `StaticBody3D` cuenta como pared. Se miden grosores y se
  permiten hasta `PenetrationStages` paredes, cada una con retención ×0.6; si
  el grosor supera `MaxWallThicknessU`, el daño es 0. Un cuerpo no estático
  corta la penetración.
- El jugador puede wallbang; **los bots no** (simplificación declarada).
- **Sin friendly fire:** un compañero corta la bala pero no recibe daño.
- Falloff: `daño * pow(RangeModifier, distancia / 500)`, truncado a entero.
- Munición: recarga 2.5 s; disparar con cargador vacío inicia recarga si hay
  reserva. La reserva del jugador es finita; la de los bots es infinita
  (placeholder).
- Cuchillo: primario 65 con cadencia 0.4 s; secundario/estocada 20 con
  cadencia 1.1 s; alcance 48 u; no penetra paredes.

### 10.5 Feedback de disparo

- Crosshair dinámico de 4 líneas.
- View punch recuperable.
- Hitmarker blanco (impacto) / rojo (kill), sonido de click.
- Número de daño flotante (blanco cuerpo, naranja cabeza, rojo kill con `!`).
- Chispa/puff en pared o carne.
- Fogonazo procedural con textura generada por código + luz temporal.
- Trazadora fina (caja de 0.03 u) desde el muzzle hasta el impacto.
- Sonido de disparo generado proceduralmente, distinto por arma; sonido de
  golpe y de kill.
- El daño recibido dispara flash rojo, flecha de amenaza y un golpe grave en
  el HUD.

## 11. Bots

### Composición

- 13 bots: 6 CT + 7 T; el jugador ocupa el séptimo lugar CT.
- Nombre visible con `Label3D` (color CT azul, T naranja) desde el trabajo sin
  commitear. Es billboard y tiene depth test: las paredes lo tapan.
- **Modelo:** humanoide low-poly procedural (`BotModel.Build`): cabeza con
  gorra y anteojos, torso, brazos con guantes oscuros, piernas y botas.
  Colores por equipo (CT azul, T naranja); la cabeza abarca 54..72 u, así que
  la zona visual de cabeza coincide con el umbral de headshot (54 u). Sigue
  siendo una sola malla visual sobre la **misma cápsula de colisión de 72 u**
  (la física, LOS y el headshot no cambiaron).
- **Caminata:** `BotRig.Animate` (llamado desde `Steer`) swingea piernas y
  brazos con cadencia proporcional a la velocidad y vuelve a la pose neutra
  al frenar. La matemática pura está en `BotRig.NextPhase`/`SwingFor`.
- No tienen modelo de arma visible (las manos van vacías).

### FSM (`BotBrain`)

| Estado | Comportamiento |
|---|---|
| `Patrol` | Camina a un punto aleatorio. Si hay pickup mejor → `Pickup`; si ve enemigo → `Chase`/`Attack`; si oye un disparo → `Chase` al sonido. |
| `Chase` | Persigue la última posición conocida. Si llega a rango de ataque con LOS → `Attack`. Si pasan 5 s sin estímulo → `Patrol`. |
| `Attack` | Se queda a distancia de tiro; dispara recién después de `ReactionSec` de compromiso continuo. Si pierde LOS/rango → `Chase`; a los 5 s → `Patrol`. |
| `Pickup` | Va a un arma mejor. Si aparece un enemigo dentro de `ThreatRadiusU` (400 u), interrumpe y pelea; si el arma deja de ser mejora, vuelve a `Patrol`. |

### Percepción y dificultad

- Escaneo de enemigos/picks a **10 Hz** (no cada tick) para bajar presión de
  allocations; el resto de timers y disparo corre a 60 Hz.
- Visión: 1500 u, FOV 90°, LOS por raycast ojo→pecho. El más cercano visible.
- Audición: disparo dentro de 800 u; el glue lo latcha 1 s.
- Ataque: 1000 u; pierde estímulo a los 5 s.
- Dificultad por spawn (mapa):
  - flancos: reacción 0.55 s, error 4.5°, ráfaga 2.
  - medio: reacción 0.40 s, error 3.0°, ráfaga 3.
  - centro CT/T (índice 3): reacción 0.30 s, error 2.0°, ráfaga 4.
- El error de puntería se muestrea en un disco uniforme de `AimErrorDeg`; el
  daño usa el mismo `WeaponSim` que el jugador.
- Ráfagas: `BurstLen` disparos y pausa de 0.6 s. Recarga 2.5 s con reserva
  infinita (placeholder). No usan cuchillo, no saltan, no se agachan.

### Navegación

- `Bot.tscn` incluye un `NavigationAgent3D`, pero **no se usa para steering**.
  El bot consulta `NavigationServer3D.MapGetPath` de forma directa.
- Path cacheado: refresh cada ~10 ticks (~6 Hz), si el goal se movió >24 u o
  si el bot fue teletransportado. Los endpoints se proyectan a y=0; la altura
  la resuelve gravedad/snap.
- Si el path está vacío o agotado, va en línea recta. Si se traba, hace un
  detour lateral verificado por raycast; si sigue trabado, repickea patrol.
- No hay avoidance entre agentes: los cuerpos se empujan/slide.
- El navmesh tiene huecos alrededor de la isla y de cada cobertura (margen
  40 u) para que los bots ruteen alrededor, no contra las caras.

## 12. Rondas, partido y votación

### `RoundManager` (puro)

- `StartMatch(playersPerTeam=7)`: resetea scores, relojes, fase `Live`, y abre
  la primera ronda (`RoundStarted`).
- `OnKill(Team victimTeam)`: resta vivo del equipo; si llega a 0, da la ronda
  al rival.
- `Update(dt)`: avanza reloj de ronda y de partido de forma time-sliced.
- Fases: `Idle → Live → GoldenRound → Finished`.
- No hay fase de "fin de ronda": `AwardRound` encadena `BeginRound`
  inmediatamente. Esto evita carreras de atribución de kills.
- Al expirar el partido, la ronda viva se juega; después se cierra.
- Empate de partido → `GoldenRound`; un empate en oro se repite.

### `VoteManager` (puro)

- Opciones en orden; el orden es el desempate determinístico.
- Un voto por `voterId`; revotar mueve el voto sin duplicar.
- Mayoría estricta (>50% de votantes elegibles) cierra al instante.
- Al expirar el timer: gana la pluralidad; empate exacto → primera opción.
- Sin votos → `null`. Opciones inválidas o votos post-cierre se rechazan.

### `MatchManager` (glue)

- `_Ready`: precarga armas en el hilo principal (`WeaponData.PreloadAll`),
  se suscribe a `RoundStarted`/`PhaseChanged` **antes** de `StartMatch`, crea
  la roster y spawnea.
- Spawns: jugador en `SpawnsCT[0]`; bots CT en `[1..6]`, bots T en `[0..6]`.
- `ResetRound`: todos a su spawn, vida completa, Deagle + cuchillo, pickups
  reseteados. El jugador vuelve a mirar al centro.
- `OnFighterDown`: cuenta muerte y llama `RoundManager.OnKill`.
- `OnFighterFrag`: cuenta frag del killer para el scoreboard.
- Al terminar: abre voto con el mapa actual, 30 s; los bots votan al azar
  (uno cada uno) tras 6 s de gracia. El jugador vota desde el HUD.
- Al cerrar el voto, el HUD recarga `fy_pileta` a los 3 s.

## 13. Mapa `fy_pileta`

### Layout

- Arena cerrada de **2000 × 2000 u**; deck superior en **y=0**.
- Paredes perimetrales de **256 u** de alto y 32 u de espesor.
- Pileta central vacía: hueco **x ∈ [−320, 320], z ∈ [−160, 160]**.
- Fondo de pileta: **y = −96**, con rampas de ~40° en los cuatro lados.
- Isla central: **x ∈ [−120, 120], z ∈ [−100, 100]**, altura 96 (top en y=0).
- Puente sobre la pileta: **x ∈ [−80, 80], z ∈ [−240, 240]**, top y=0.
- 7 coberturas bajas de **40 u** de alto: una 80×80 en la isla y seis 128×128
  en los laterales. Son saltables con el salto de 45 u.
- Spawns: 7 CT en arco sur, 7 T en el espejo exacto del norte.
  Distancias de referencia: centroides ~962 u; enemigo mín/med/máx
  ~635/1047/1436 u; sightline despejada ~1249 u (cruzado con poolday ±10%).
- Materiales: deck gris claro, pileta azul, coberturas naranjas, paredes
  blancas; ambiente celeste. Sin texturas externas.

### Pickups

| Arma | Posición | Zona |
|---|---|---|
| AK-47 | `(0, 4, −160)` | puente, lado sur |
| AK-47 | `(0, 4, 160)` | puente, lado norte |
| M4A1 | `(560, 4, −400)` | lateral este |
| M4A1 | `(−560, 4, 400)` | lateral oeste |
| AWP | `(170, −92, 0)` | piso de la pileta, este de la isla |

Todos los pickups están fuera de los huecos del navmesh y se reponen por ronda.
`MapConfig.Pickups` es la fuente de verdad; la escena duplica los markers.

### Navmesh y edición del mapa

- `fy_pileta_nav.tres` se hornea desde `MapConfig` con `MakeNav` (heightfield
  real: deck 0, pileta −96, rampas interpoladas).
- **Después de cambiar** coberturas, isla, pileta, rampas o pickups:
  1. editar `MapConfig.cs`;
  2. editar `fy_pileta.tscn` para que los markers/geometría coincidan;
  3. correr `godot-mono --headless --path . res://src/maps/makenav.tscn`;
  4. correr tests (`MapConfigTests` verifica spawns, huecos, pickups y escena).
- Los markers de spawn de la escena están a `y=2`; `MapConfig` los define a
  `y=0`. El spawn real usa `MapConfig`. Los tests comparan solo X/Z.
- `BaseLoadoutIds` y `BuyZones` están en `MapConfig`, pero `MatchManager`
  hardcodea Deagle + cuchillo y no hay compra. No asumir que editarlos cambia
  el juego.

## 14. UI

### Menú principal

- Botones **JUGAR**, slider de **sensibilidad**, **SALIR**.
- Sensibilidad: default `0.0022`, rango `0.0005..0.006`.
- `GameSettings` es estático: **no hay guardado**; al reiniciar vuelve al
  default.
- `PlayerBody` lee `GameSettings.MouseSensitivity` al spawnear.

### HUD

- Arriba: ronda, score CT/T, timer de ronda, reloj de partido.
- Abajo izquierda: vida; abajo derecha: arma y munición.
- Línea de slots: `1 ARMA · 2 DEAGLE · 3 CUCHILLO`, con el activo entre
  corchetes.
- Textos con outline negro para leerse sobre el mapa claro.
- `TAB`: scoreboard CT primero, frags, muertes, ping (`BOT` para bots, `--`
  para el jugador), tag `(afuera)` en muertos.
- Mensajes centrales de ronda/oro/ganador.
- Panel de pausa con **SEGUIR JUGANDO** y **SALIR AL MENÚ**.
- Panel de votación con opciones, conteos y timer; acepta teclas 1..9.
- Al morir el jugador: mensaje "¡Te exprimieron! La ronda sigue sin vos...";
  no hay modo espectador.
- El scoreboard se reconstruye solo cuando cambia la firma de datos
  (roster/frags/deaths), no cada frame.

## 15. Renderizado y viewmodels

- `gl_compatibility`, `StandardMaterial3D`, primitivas de Godot; sin shaders
  custom ni assets importados.
- El mapa es brillante (deck/paredes claras, pileta azul) para que los rivales
  se lean; los bots usan color de equipo y nameplate.
- **Viewmodel procedural:** `WeaponViewmodel.Build(root, muzzle, weaponId)`
  borra los hijos del `Viewmodel` (salvo el `Muzzle`) y construye:
  - AK-47: acero + madera, cargador curvo, mira, freno de boca.
  - M4A1: polímero negro, raíles, culata, miras.
  - AWP: chasis oliva, cañón largo, mira telescópica con lentes.
  - Deagle: corredera plateada, empuñadura negra, detalles dorados.
  - Cuchillo: hoja extruida con perfil/filo, guarda, mango con vendas.
- Todas las piezas usan `WeaponViewmodel.ViewmodelLayer` (capa 2), sin
  sombras. Hay dos `OmniLight3D` locales (fill cálido + rim azulado) con
  `LightCullMask` de esa capa: iluminan el arma, no el mapa.
- Animaciones: subida al equipar, kick de retroceso, dip de recarga, bob de
  caminata, sway con el mouse, slash/stab del cuchillo. La AWP oculta el
  viewmodel al apuntar.
- Fogonazo: quad billboard additive, textura de estrella generada en C#,
  `NoDepthTest`, luz temporal; trazadora fina; chispas; números de daño.
- MSAA 2x configurado en `project.godot`.
- `WeaponView.tscn` **no** debe volver a tener la caja oscura compartida: el
  test `WeaponViewSceneHostsScriptSoundAndMuzzle` y
  `EveryWeaponHasACustomViewmodelPose` lo impiden.
- No hay worldmodels de armas para bots, ni animaciones de personajes.

## 16. Datos y tablas

- Armas: `data/weapons/*.tres`, script `WeaponData.cs`. Cada archivo lleva
  fuentes y notas de CS 1.6 en comentarios.
- `WeaponData.Get(id)` cachea recursos; `PreloadAll()` se llama al inicio del
  match en el hilo principal para evitar cargas scripted dentro de física.
- `WeaponData.AllIds` es la lista canónica. **Ojo:** el test
  `EveryWeaponHasACustomViewmodelPose` recorre `AllIds` y exige que cada arma
  tenga modelo custom; si agregás un id, agregá su modelo/pose o el test falla.
- `MapConfig` es `[GlobalClass] Resource`; sus arrays se editan en C# y se
  duplican en la escena. Los tests antidesvío comparan escena vs config.
- `BotDifficulty` define reacción/error/ráfaga por spawn; `MapConfig.MixCT` y
  `MixT` son espejos.
- `MovementConfig` permite editar constantes; `PlayerBody` cae a
  `MovementParams.Default` si no está asignado.

## 17. Tests

Comando: `dotnet test FrutaCS.sln`. Estado actual: **61/61**.

| Archivo | Tests | Cubre |
|---|---:|---|
| `MovementSimTests.cs` | 5 | 250 u/s, walk, air-strafe, fricción, salto |
| `WeaponSimTests.cs` | 8 | tablas, determinismo, spread, falloff, wallbang, headshot, fps-independence |
| `BotBrainTests.cs` | 13 | FSM, reacción, pickups, oído, LOS/FOV, escena, dispersión |
| `RoundManagerTests.cs` | 7 | rondas, reloj, oro, ronda en vuelo, eventos, escena |
| `VoteManagerTests.cs` | 3 | mayoría, pluralidad, votos inválidos |
| `MapConfigTests.cs` | 7 | spawns, simetría, distancias, pickups, navmesh, escena |
| `PlayerWeaponGlueTests.cs` | 9 | escena del jugador, viewmodel, salto, sync, spray AK, cuchillo, penetración |
| `WeaponRangeTests.cs` | 5 | escena del rango, glue con dummies, cobertura de armas, flag del player, botón del menú |
| `CharacterModelTests.cs` | 4 | piezas/colores del humanoide, contenedor `Body` en escena/glue, matemática pura del swing, manos del viewmodel en todas las armas |

Patrones importantes:

- Las simulaciones puras se testean directo en .NET.
- Los tests de `.tscn`/`.tres` leen el archivo como texto. No se pueden
  instanciar nodos C# sin motor.
- `WeaponSimTests` fija valores de referencia (AK 36, M4 32, AWP 115, Deagle
  54, cuchillo 65; AWP mag 10; Deagle mag 7/RPM 267). Si cambiás datos a
  propósito, actualizá el test en el mismo cambio.
- `MapConfigTests` valida contra `docs/reference/mediciones.md` con tolerancia
  ±10% y verifica que el navmesh tenga huecos y que los pickups sean ruteables.
- No hay tests de integración runtime (disparo real, navegación real, UI). Para
  eso: smoke test headless + prueba manual con `godot-mono`.

## 18. Convenciones de trabajo

- **C# solamente** para código de juego. Python solo en `tools/`.
- Namespaces file-scoped, XML docs en clases/métodos importantes, records y
  `readonly struct` para contratos puros.
- La lógica engine-free no debe importar `Node`, `SceneTree`, `Input`, etc.
  Solo math de Godot (`Vector3`, `Vector2`, `Mathf`) cuando haga falta.
- Los nodos son glue delgado: input, física, escena, FX. No meter reglas de
  juego en ellos si pueden vivir en una simulación pura.
- Datos de gameplay en Resources (`WeaponData`, `MapConfig`,
  `MovementConfig`, `BotDifficulty`), no hardcodeados en nodos.
- Input actions se registran en runtime con `EnsureInputActions()` y son
  idempotentes. No agregar duplicados al `InputMap` del proyecto sin revisar.
- Viewmodel: usar `WeaponViewmodel.ViewmodelLayer`, sombras off, y no volver a
  poner geometría estática en `WeaponView.tscn`.
- **Manos first-person:** `WeaponViewmodel.AddArms` agrega dos brazos por
  arma (manga color de equipo + guante oscuro): mano derecha en la empuñadura
  (`Spec.Grip`) e izquierda sosteniendo (`Spec.Support`). Van como hijas del
  viewmodel, así que heredan bob, sway, recarga y swing del cuchillo. El
  equipo lo pasa `WeaponSystem` desde `PlayerBody.Team`.
- UI en español; identificadores/comentarios de código en inglés (el código
  existente mezcla, mantener el estilo local).
- Sin assets de Valve/comunidad. Todo arte nuevo debe ser original.
- Git: rama `dev`; features por `feature/*` y PR a `dev`; `main` solo con
  aprobación explícita del PM. **No commitear sin que el usuario lo pida.**
- No borrar ni revertir trabajo sin commitear. Si un archivo parece ajeno,
  inspeccionar el diff antes de tocarlo.
- Usar `godot-mono`; el `godot` estándar no carga C#.

## 19. Deuda técnica, límites y cosas que NO existen

### No implementado

- **Skins**: no hay recurso `WeaponSkin`, inventario, persistencia, UI ni swap
  de modelo completo. Solo existen los modelos procedurales con colores
  hardcodeados.
- **Multiplayer/netcode**: nada. Todo es offline.
- **Cuentas, pagos, tienda, anti-cheat**.
- **Economía/compra**: no hay dinero ni buy zones activas.
- **Granadas**, **más mapas**, **matchmaking**.
- **Guardado**: `GameSettings` es estático; no hay archivo de config.
- **Chaleco/armadura**: no hay modelo de armor; `ArmorPenetration` no se usa.
- **Hitboxes reales**: el headshot es un umbral de altura, no una hitbox.
- **Espectador**: el muerto espera el reset; no hay cámara libre.
- **Worldmodels**: los bots no muestran el arma en la mano (tienen humanoide
  y ciclo de caminada, pero las manos van vacías).
- **Animaciones de bots**: solo swing de caminata; no hay correr, saltar,
  agacharse, disparar ni muerte animada (el cuerpo se oculta al morir).
- **Friendly fire**: desactivado.
- **Wallbang de bots**: desactivado.
- **Dropeo de armas al morir**: no existe.
- **Reserva finita de bots**: infinita (placeholder).
- **Audio/arte final**: sonidos procedurales y geometría placeholder.
- **Tests runtime**: la suite es de simulación + texto de escenas.

### Mismatches / trampas conocidas

- El spec menciona coberturas de 64 u, pero el código y los tests usan **40 u**
  (saltables con el salto de 45 u). El código manda.
- `MapConfig.BaseLoadoutIds` y `BuyZones` existen pero no se usan; el loadout
  está hardcodeado en `MatchManager` como Deagle + cuchillo.
- `Game.tscn` es mínima y no es la escena que se juega; el flujo real es
  `MainMenu.tscn` → `fy_pileta.tscn`.
- `PlayerBody.tscn` tiene un AK asignado por defecto para `TestRange`; el match
  lo reemplaza por el loadout base.
- `NavigationAgent3D` está en `Bot.tscn` pero el steering usa
  `NavigationServer3D` directo.
- Los spawn markers de la escena están a y=2; `MapConfig` a y=0. El spawn real
  usa `MapConfig`.
- El build puede mostrar warnings `CS8632`; no son errores.
- La suite no detecta regresiones visuales ni de navegación real.

## 20. Próximo paso natural: skins (sin implementar todavía)

La arquitectura actual **facilita** skins cosméticas, pero no las tiene.

### Lo que ya sirve

- `WeaponData` solo tiene stats; el aspecto vive en `WeaponViewmodel`.
- Todo el modelo se reconstruye desde un único punto:
  `WeaponViewmodel.Build(root, muzzle, weaponId)`.
- `WeaponSystem.Equip` ya reconstruye al cambiar de arma.
- Las piezas tienen nombres (`Receiver`, `Slide`, `ScopeTube`, etc.), lo que
  permite recolorear o reemplazar materiales por parte.

### Lo que falta para un sistema real

1. **Recurso de skin**, por ejemplo `WeaponSkin : Resource` con
   `SkinId`, `WeaponId`, colores primario/secundario/acento y, opcionalmente,
   `Texture2D` de albedo.
2. **Paleta centralizada**: sacar los colores hardcodeados de cada
   `BuildAk47/BuildM4a1/...` y pasar la skin a `Build(...)`.
3. **Persistencia e inventario**: hoy no hay save; decidir dónde se guarda la
   skin equipada por arma.
4. **UI**: selector y preview en el menú.
5. **Skins de geometría completa** (segunda fase): cargar un `PackedScene` por
   skin y exigir un `Muzzle` en el modelo; el pipeline actual solo hace
   material/color.

Recomendación: empezar por skins de **color/material** sobre los modelos
actuales. Es un cambio acotado; las skins con modelos GLB/texturas complejas
son otra fase.

## 21. Guías rápidas

### Agregar/tunear un arma

1. Crear/editar `data/weapons/<id>.tres` con `WeaponData` y una `RecoilTable`
   cuya longitud sea igual a `MagSize`.
2. Agregar el id a `WeaponData.AllIds`.
3. Agregar un caso en `WeaponViewmodel` (spec + build) y, si corresponde,
   `Bot.TierOf`, `PlayerBody.SlotFor` y color de pickup.
4. Si es pickup, agregarlo a `MapConfig.Pickups` y a `fy_pileta.tscn`; rehornear
   navmesh si cambia el layout.
5. Actualizar tests de datos/escena y correr build + tests.

### Cambiar movimiento

1. Editar `MovementParams.Default` o el `MovementConfig` de la escena.
2. Mantener el tick fijo y el write-back `SyncFromEngine`.
3. Correr `MovementSimTests` y probar salto/strafe en `TestRange`.

### Cambiar el mapa

1. Editar `MapConfig.cs` (spawns, pickups, covers, isla, pileta).
2. Editar `fy_pileta.tscn` para que la geometría y markers coincidan.
3. Rehornear `fy_pileta_nav.tres`.
4. Correr `MapConfigTests` y jugar el mapa.

### Agregar una skin (fase futura)

1. Crear `WeaponSkin` y una paleta por arma.
2. Cambiar `Build` para recibir la skin y aplicar materiales.
3. Persistir la skin equipada.
4. Agregar selector/preview en el menú.
5. Recién después evaluar modelos GLB completos.

## 22. Referencias

- Spec: `docs/superpowers/specs/2026-10-03-frutacs-hito-1-design.md`
- Plan: `docs/superpowers/plans/2026-10-03-frutacs-hito-1.md`
- Mediciones CS: `docs/reference/mediciones.md`
- Fuentes de armas: comentarios al inicio de cada `data/weapons/*.tres`
- Parser de BSP: `tools/parse_bsp.py`

Si algo de este documento quedó desactualizado después de un cambio, actualizalo
en el mismo commit/PR. Este archivo es el punto de entrada para la próxima
sesión de agente.
