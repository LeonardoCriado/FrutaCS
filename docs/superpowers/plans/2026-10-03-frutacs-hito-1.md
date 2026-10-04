# FrutaCS Hito 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the playable offline core of FrutaCS: one original fy-style map, GoldSrc-faithful movement and recoil, 13 bots, 7v7 elimination rounds on a 10-minute match clock with map vote.

**Architecture:** Pure-C# simulation classes (engine-free, unit-tested via `dotnet test`) wrapped by thin Godot node/scene glue. Weapon feel is data (recoil tables), never randomness in the pattern.

**Tech Stack:** Godot .NET 4.7.2, C#, .NET 8 SDK, GdUnit4 (AssetLib, latest declaring 4.7 support) + GdUnit4Net (`gdUnit4.api` + VSTest adapter), Python 3 (reference tooling only).

**Spec:** `docs/superpowers/specs/2026-10-03-frutacs-hito-1-design.md`

## Global Constraints

- Godot .NET (mono) build 4.7.2, never the standard build.
- C# only for game code; Python only for `tools/`.
- Scale 1 unit Godot = 1 inch CS; run speed 250 u/s (spec §5).
- Recoil pattern is deterministic data; randomness lives only inside the spread cone (spec §5).
- No Valve/community files in the repo, ever: `FrutaCS-reference/` stays outside git (spec §3).
- `main` receives merges only with explicit PM approval; work lands via PR to `dev`.
- 60 fps floor on integrated-class GPUs; physics fixed at 60 Hz, input sampled per physics tick.

## Review Focus

- Spray diverges between 60 fps and low fps (variable-delta leak into recoil/spread accumulation). Expect identical bullet N offsets at any frame rate; pinned in Task 4.
- Crosshair shows less spread than the sim applies (view/HUD lying). Expect crosshair expansion derived from the same `SpreadDeg` value; pinned in Task 5.
- Thin map covers become wallbang highways or impenetrable walls (penetration constant mistuned). Expect AK to penetrate covers ≤40u and fail ≥120u; pinned in Task 4.
- Bots pile up on the same doorway/cover (navigation + state oscillation). Expect 13 bots to spread across ≥3 zones within 20 s of round start; pinned in Task 6.
- Round ends exactly as the 10-minute clock expires (score attribution race). Expect the in-flight round to count before the match closes; pinned in Task 7.

---

## File structure

New tree (Godot project root = repo root):

```text
project.godot
FrutaCS.sln
src/FrutaCS.csproj
src/player/MovementParams.cs      # plain struct, engine-free
src/player/MovementSim.cs         # pure sim, engine-free
src/player/MovementConfig.cs      # Resource -> ToParams()
src/player/PlayerBody.cs          # CharacterBody3D glue
src/player/PlayerBody.tscn
src/weapons/WeaponData.cs         # Resource: damage, cadence, spread, RecoilTable
src/weapons/WeaponSim.cs          # pure sim, engine-free
src/weapons/WeaponSystem.cs       # Node glue: fire, reload, AWP scope, knife
src/weapons/WeaponView.tscn
data/weapons/{ak47,m4a1,awp,deagle,knife}.tres
src/bots/BotBrain.cs              # pure state machine, engine-free
src/bots/BotParams.cs             # plain struct: reaction, aim error, burst
src/bots/Bot.cs                   # Node glue + NavigationAgent3D
src/bots/Bot.tscn
src/game/RoundManager.cs          # pure match/round logic, engine-free
src/game/VoteManager.cs           # pure vote logic, engine-free
src/game/MatchManager.cs          # Node glue: timers, spawns, scene flow
src/game/Game.tscn
src/maps/MapConfig.cs             # Resource: spawns, pickups, buy zones
src/maps/fy_pileta.tscn
src/ui/Hud.cs + Hud.tscn          # health/armor/ammo/timer/score, TAB scoreboard
src/ui/MainMenu.tscn (+ .cs)      # play, sensitivity, quit
tests/FrutaCS.Tests/FrutaCS.Tests.csproj
tests/FrutaCS.Tests/{MovementSimTests,WeaponSimTests,BotBrainTests,RoundManagerTests,VoteManagerTests}.cs
tools/parse_bsp.py
docs/reference/mediciones.md
```

`.gitignore` gains: `.godot/`, `*.tmp`, `export_presets.cfg` stays tracked.

---

### Task 1: Toolchain + project scaffold

**Files:**
- Create: `project.godot`, `FrutaCS.sln`, `src/FrutaCS.csproj` (net8.0, Godot.NET.Sdk), `tests/FrutaCS.Tests/FrutaCS.Tests.csproj` (gdUnit4.api + gdunit4.test.adapter), folder tree above (empty `.gdkeep` where needed)
- Modify: `.gitignore` (append Godot entries)
- Test: none (environment verification instead)

**Interfaces:**
- Consumes: nothing
- Produces: buildable solution; every later task assumes `dotnet build` green

- [ ] **Step 1: Install Godot .NET 4.7.2 + .NET 8 SDK (Linux)**

Run: `dotnet --list-sdks` Expected: an `8.x` entry. Godot: download the `- .NET -` Linux x86_64 build from godotengine.org, extract, verify with `godot --version` Expected: `4.7.2.stable.mono.official`.
- [ ] **Step 2: Scaffold project + solution**

Create `project.godot` (renderer gl_compatibility for integrated GPUs), `src/FrutaCS.csproj` referencing `Godot.NET.Sdk/4.7.2`, solution file, test project with GdUnit4Net packages, folder tree.
- [ ] **Step 3: Install GdUnit4 from the AssetLib tab (latest version declaring 4.7 support), enable plugin**
- [ ] **Step 4: Verify headless import + build**

Run: `dotnet build FrutaCS.sln` Expected: `Build succeeded, 0 errors`. Run: `godot --headless --quit` Expected: exit 0, `.godot/` created.
- [ ] **Step 5: Commit**

```bash
git add project.godot FrutaCS.sln src tests .gitignore
git commit -m "chore: scaffold Godot .NET 4.7.2 + C# + GdUnit4"
```

### Task 2: BSP reference measurements

**Files:**
- Create: `tools/parse_bsp.py`, `docs/reference/mediciones.md`
- Test: script self-checks (assert entity counts > 0, bounds finite)

**Interfaces:**
- Consumes: `/home/leo/Documentos/FrutaCS-reference/maps/{iceworld,poolday,tennis}/*/*.bsp` (read-only, never copied into repo)
- Produces: `mediciones.md` table that Task 8 builds the map against

- [ ] **Step 1: Write `tools/parse_bsp.py`**

Parses BSP30 entity lump (spawns, weapons) + derives world bounds, cover heights, spawn-to-spawn distances per map. Prints a markdown table.
- [ ] **Step 2: Run and review**

Run: `python3 tools/parse_bsp.py` Expected: `docs/reference/mediciones.md` with one section per map: bounds (u), spawn counts CT/T, mean cover height, longest sightline. Eyeball against in-game feel; fix parser if numbers are absurd.
- [ ] **Step 3: Commit**

```bash
git add tools/parse_bsp.py docs/reference/mediciones.md
git commit -m "docs: mediciones de referencia iceworld/poolday/tennis"
```

### Task 3: Movement sim (pure) + tests

**Files:**
- Create: `src/player/MovementParams.cs`, `src/player/MovementSim.cs`
- Test: `tests/FrutaCS.Tests/MovementSimTests.cs`

**Interfaces:**
- Consumes: `MovementParams` (RunSpeed=250, WalkSpeed, AirAccelerate, Friction, Gravity, JumpVelocity, from spec §5 constants)
- Produces: `MovementSim.Tick(in MovementInput, in MovementParams, float delta)`; `readonly record struct MovementInput(Vector3 WishDir, bool JumpPressed)`; exposes `Velocity`, `IsOnFloor`

- [ ] **Step 1: Write failing tests**

```csharp
[TestCase] public void RunReaches250Ups() // hold forward 2 s -> Velocity.Length() ~= 250
[TestCase] public void AirStrafeGainsSpeedWithoutCap() // bhop chain keeps accelerating
[TestCase] public void FrictionStopsInPlace() // release keys -> stops, no slide
[TestCase] public void JumpLeavesFloor() // JumpPressed -> IsOnFloor false, positive Y
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/FrutaCS.Tests/ --filter MovementSim` Expected: FAIL (types missing)
- [ ] **Step 3: Implement `MovementSim` (Quake-style: ground friction + accelerate/wishdir, air-accelerate, gravity/jump; fixed-step, no `delta` scaling bugs — multiply accelerations by `delta` exactly once)**
- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/FrutaCS.Tests/ --filter MovementSim` Expected: PASS, 4/4
- [ ] **Step 5: Commit**

```bash
git add src/player tests/FrutaCS.Tests/MovementSimTests.cs
git commit -m "feat: simulacion de movimiento GoldSrc"
```

### Task 4: Weapon data + WeaponSim (pure) + tests

**Files:**
- Create: `src/weapons/WeaponData.cs`, `src/weapons/WeaponSim.cs`, `data/weapons/*.tres` (5 files)
- Test: `tests/FrutaCS.Tests/WeaponSimTests.cs`

**Interfaces:**
- Consumes: per-weapon tables researched from 1.6 reference (damage, headshot ×, armor pen, RPM, mag/reserve, base spread per stance, `Vector2[] RecoilTable` length == mag)
- Produces: `RecoilOffset(int bulletIndex) -> Vector2`, `SpreadDeg(Stance) -> float`, `DamageAt(float distU, bool wall, float wallU) -> int`; `enum Stance { Stand, Move, Air, Duck }`

- [ ] **Step 1: Encode the five `WeaponData` tables from 1.6 reference values; commit the research source (URL/book) as a comment atop each `.tres`**
- [ ] **Step 2: Write failing tests**

```csharp
[TestCase] public void RecoilTableLengthMatchesMag() // AK table has 30 entries
[TestCase] public void PatternIsDeterministic() // same index -> identical offset, twice
[TestCase] public void FirstBulletAccurateStanding() // SpreadDeg(Stand) < SpreadDeg(Move)
[TestCase] public void DamageFallsWithDistance() // DamageAt(100) > DamageAt(2000)
[TestCase] public void WallbangThinVsThick() // AK penetrates 40u wall, fails 120u wall
[TestCase] public void FpsIndependent() // 60 ticks of 1/60 == 120 ticks of 1/120 for bullet 10 offset
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/FrutaCS.Tests/ --filter WeaponSim` Expected: FAIL
- [ ] **Step 4: Implement `WeaponSim` (table lookup + stance spread + distance falloff + wall penetration by thickness; fixed-step accumulation)**
- [ ] **Step 5: Verify pass + spray-dump review**

Run: `dotnet test tests/FrutaCS.Tests/ --filter WeaponSim` Expected: PASS. Then dump the AK 30-bullet pattern and eyeball against the 1.6 reference (left-down-long-right shape); fix table, not code, on mismatch.
- [ ] **Step 6: Commit**

```bash
git add src/weapons data/weapons tests/FrutaCS.Tests/WeaponSimTests.cs
git commit -m "feat: datos y simulacion de armas con recoil por tablas"
```

### Task 5: Player body + weapon view glue

**Files:**
- Create: `src/player/MovementConfig.cs` (`Resource`, `ToParams()`), `src/player/PlayerBody.cs`, `src/player/PlayerBody.tscn`, `src/weapons/WeaponSystem.cs`, `src/weapons/WeaponView.tscn`
- Test: GdUnit4 scene check + manual test scene

**Interfaces:**
- Consumes: Task 3 (`MovementSim`, `ToParams()`), Task 4 (`WeaponSim`, `WeaponData`)
- Produces: playable first-person body (mouse look, `MovementSim`-driven), weapon that fires/reloads/scopes (AWP), knife оба attack, crosshair driven by `SpreadDeg`

- [ ] **Step 1: Implement `MovementConfig`, `PlayerBody` (capsule 72u tall, camera at 64u, mouse look with sensitivity setting hook)**
- [ ] **Step 2: Implement `WeaponSystem` (hitscan raycast, view punch recoverable, crosshair expansion = `SpreadDeg` of current stance, per-weapon placeholder sound + low-poly placeholder viewmodel sharing one material)**
- [ ] **Step 3: Verify in test scene**

Open a flat test scene, run: strafe + bhop feels 1.6; empty a full AK mag into a wall — pattern matches Task 4 dump. Fix glue, never the sim, on mismatch.
- [ ] **Step 4: Commit**

```bash
git add src/player/MovementConfig.cs src/player/PlayerBody.* src/weapons/WeaponSystem.cs src/weapons/WeaponView.tscn
git commit -m "feat: cuerpo FPS + sistema de armas jugable"
```

### Task 6: Bot brain (pure) + tests, then node glue

**Files:**
- Create: `src/bots/BotBrain.cs`, `src/bots/BotParams.cs`, `src/bots/Bot.cs`, `src/bots/Bot.tscn`
- Test: `tests/FrutaCS.Tests/BotBrainTests.cs`

**Interfaces:**
- Consumes: Task 3 movement speeds (bots move at run speed), Task 4 damage (bots die the same way)
- Produces: `enum BotState { Patrol, Chase, Attack, Pickup }`; `BotDecision Update(BotPerception, BotParams, delta)` with `MoveTarget`, `WantFire`, `WantPickup`; `BotParams` (ReactionSec, AimErrorDeg, BurstLen)

- [ ] **Step 1: Write failing tests**

```csharp
[TestCase] public void SeesEnemyTransitionsToChase() // enemy in cone+range -> Chase
[TestCase] public void FiresOnlyAfterReaction() // Attack reached but WantFire false until ReactionSec elapsed
[TestCase] public void PicksBetterWeaponNearby() // AK on floor + Deagle in hand -> WantPickup
[TestCase] public void LosesEnemyReturnsToPatrol() // no stimulus for 5 s -> Patrol
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/FrutaCS.Tests/ --filter BotBrain` Expected: FAIL
- [ ] **Step 3: Implement `BotBrain` + `Bot` glue (`NavigationAgent3D`, hearing radius, aim error applied at fire time)**
- [ ] **Step 4: Run tests + spread check in test scene**

Run: `dotnet test tests/FrutaCS.Tests/ --filter BotBrain` Expected: PASS. Spawn 13 bots on a flat arena: within 20 s they occupy ≥3 zones, none stuck >5 s.
- [ ] **Step 5: Commit**

```bash
git add src/bots tests/FrutaCS.Tests/BotBrainTests.cs
git commit -m "feat: bots con maquina de estados y dificultad"
```

### Task 7: Rounds, match clock, votes (pure) + tests

**Files:**
- Create: `src/game/RoundManager.cs`, `src/game/VoteManager.cs`, `src/game/MatchManager.cs`, `src/game/Game.tscn`
- Test: `tests/FrutaCS.Tests/{RoundManagerTests,VoteManagerTests}.cs`

**Interfaces:**
- Consumes: scoring rules (spec §6: 10-min match, most rounds wins, golden-round on tie, 2-min round timer with most-alive tiebreak, no buy)
- Produces: `RoundManager` (StartMatch/OnKill/Update → Phase, ScoreCT/T, TimeLeft; in-flight round counts if clock expires mid-round); `VoteManager` (StartVote/CastVote/Tally with majority + timeout fallback)

- [ ] **Step 1: Write failing tests**

```csharp
[TestCase] public void MostRoundsWinsAtClock() // 6-4 at 10:00 -> CT wins
[TestCase] public void TieTriggersGoldenRound() // 5-5 -> Phase == GoldenRound
[TestCase] public void RoundTimerMostAliveWins() // timeout 4v2 -> team of 4 wins round
[TestCase] public void InFlightRoundCounts() // kill at 9:59.9 lands before close
[TestCase] public void VoteMajorityAndTimeout() // 4/7 for dust -> dust; timeout -> plurality wins
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/FrutaCS.Tests/ --filter "RoundManager|VoteManager"` Expected: FAIL
- [ ] **Step 3: Implement both classes (engine-free, C# events for phase changes)**
- [ ] **Step 4: Run to verify pass**

Run: same filter Expected: PASS, 5/5
- [ ] **Step 5: Commit**

```bash
git add src/game tests/FrutaCS.Tests/RoundManagerTests.cs tests/FrutaCS.Tests/VoteManagerTests.cs
git commit -m "feat: rondas, partido 10min y votacion de mapa"
```

### Task 8: Map 1 — pileta (scene + config)

**Files:**
- Create: `src/maps/MapConfig.cs`, `src/maps/fy_pileta.tscn`, pickup/buy-zone markers
- Test: measurement cross-check + walkthrough

**Interfaces:**
- Consumes: Task 2 `mediciones.md` (arena bounds, spawn spacing, cover heights), Task 5/6 (player + bots spawn through `MapConfig`)
- Produces: `fy_pileta.tscn` (~2000×2000u symmetric, sloped pool walls walkable, 7 spawns/team, mid + side pickups: AK×2/M4×2/AWP×1 per spec §7, base loadout knife + Deagle, no buy zones on map 1)

- [ ] **Step 1: Build geometry (bright lighting, no dark corners, 64u jumpable covers, 128u+ walls)**
- [ ] **Step 2: Place markers from `MapConfig`; verify longest sightline and spawn distances against `mediciones.md` (±10%)**
- [ ] **Step 3: Full round walkthrough: spawn, grab mid AK as human, bots contest pickups, round ends, next round re-arms per config**
- [ ] **Step 4: Commit**

```bash
git add src/maps
git commit -m "feat: mapa fy_pileta con pickups y spawns"
```

### Task 9: HUD, menu, scoreboard

**Files:**
- Create: `src/ui/Hud.cs`, `src/ui/Hud.tscn`, `src/ui/MainMenu.tscn` (+`.cs`)
- Test: manual checklist

**Interfaces:**
- Consumes: Task 5 (ammo/health), Task 7 (timer/score/vote UI hooks)
- Produces: HUD (health/armor/ammo/timer/round score, fruta tone, no Valve assets), TAB scoreboard (frags/deaths, bots show "BOT"), end-of-match vote panel, main menu (play / sensitivity / quit)

- [ ] **Step 1: Implement HUD + scoreboard bound to live game state (no mocked numbers in final scene)**
- [ ] **Step 2: Implement menu + vote panel wired to `MatchManager`/`VoteManager`**
- [ ] **Step 3: Click-through verify: menu → match → TAB → vote → map change**
- [ ] **Step 4: Commit**

```bash
git add src/ui
git commit -m "feat: HUD, menu y votacion"
```

### Task 10: Acceptance pass + PR

**Files:** fixes only; no new systems.

- [ ] **Step 1: Telemetry run — record run speed, jump distance, AK spray dump; attach to PR as evidence vs spec §9**
- [ ] **Step 2: Soak — full 10-minute match bot-only, zero errors, vote + map change fires**
- [ ] **Step 3: Perf — 60 fps with 14 players on integrated-class GPU (gl_compatibility); note GPU + fps in PR**
- [ ] **Step 4: Blind playtest with a 1.6 player (5 min); record verdict verbatim in PR**
- [ ] **Step 5: Push branch + open PR to `dev` (never `main`)**

```bash
git push -u origin feature/hito-1-core
gh pr create --base dev --title "Hito 1: core offline con bots" --body "Evidencia de aceptacion: ..."
```

---

## Self-review

- **Spec coverage:** §3 visual → Tasks 5/8/9 (materials shared, bright map, fruta HUD). §4 architecture → Tasks 1/3–7 (pure sims + thin glue, GdUnit4Net). §5 movement/recoil → Tasks 3/4/5. §6 bots/rounds/votes → Tasks 6/7. §7 map → Task 8. §8 scope IN/OUT respected (no grenades/multi/skins tasks). §9 acceptance → Task 10. No gaps.
- **Step scan:** each step yields one artifact; table values (Task 4 Step 1) are research + review, flagged as such rather than invented.
- **Type consistency:** `MovementInput`/`MovementParams`/`ToParams()`, `WeaponData`/`WeaponSim`/`Stance`, `BotBrain`/`BotParams`/`BotPerception`/`BotDecision`, `RoundManager`/`VoteManager` phases — defined once at first use, reused verbatim later.
- **Review Focus:** all five lines pinned to owning tasks (4, 5, 4, 6, 7).
- **Proportion:** plan decides interfaces/values/commands; bodies left to implementers. No transcripts.
