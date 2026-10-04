# LifeIsEndless — Ecosphere

A low-poly artificial-life sandbox: a living planet where plants and animals grow from
genomes, follow their own needs, evolve, and are shaped by weather, wind, currents and
pressure across day/night cycles and 15-day seasonal quarters.

- 📄 **Project brief** (frozen vision): [`Docs/project-brief.md`](Docs/project-brief.md)
- 🏗 **Architecture & rules**: [`Docs/architecture.md`](Docs/architecture.md)
- 🤝 **Stage workflow & commit rules**: [`CONTRIBUTING.md`](CONTRIBUTING.md)

## Requirements

- Unity **6000.0 LTS** (pinned to `6000.0.75f1` in `ProjectSettings/ProjectVersion.txt`;
  any 6000.0.x can open it — accept the upgrade prompt).
- Modules: none beyond the default editor install (no platform SDKs needed yet).
- Packages are resolved automatically from `Packages/manifest.json`:
  Entities 1.3.14, Collections 2.5.7, Burst 1.8.19, Mathematics 1.3.2,
  URP 17.0.4, Test Framework 1.4.6 + Performance Testing 3.0.3.
  - Note: the brief lists `com.unity.jobs` — that package is deprecated (merged into
    Collections); the C# Job System is built into Unity 6. No asmdef references
    `Unity.Jobs` in stage 01 because no code uses job types yet; stages that schedule
    jobs add the reference when needed.
  - UI: UI Toolkit ships with Unity 6 and will be used for stage-07 UI; TextMeshPro is
    not needed yet.

## Opening & running

1. Unity Hub → **Add** → select this repository root.
2. Open the project (first open resolves packages and imports assets).
3. Open `Assets/Scenes/Main.unity` and press **Play**.

The only ProjectSettings files committed are `ProjectVersion.txt` and
`EditorBuildSettings.asset`; the editor regenerates the rest with defaults on first open.

### In-game controls (stage 01)

| Input | Action |
|---|---|
| `Space` | pause / resume simulation |
| `[` / `]` | time scale down / up (x0…x256) |
| HUD grid | select time scale directly |

The HUD shows: game date (Day X • Season • Year N), clock, tick counter, FPS, sim
ms/frame and per-tick, achieved ticks/s, entity count, world seed.

## Tests

`Window → General → Test Runner` (or CLI):

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults results.xml
```

Coverage today: RNG determinism & stream independence (seed 42, 10k numbers per stream),
calendar math (day 15→16 flips season, year wraps at day 60, southern inversion),
time-scale/accumulator math, settings load, banned-RNG source scan, SimLog ring buffer,
scene boot + 500 ticks consistency, day/season/year event lifecycle, and the
allocation-free 1000-tick performance smoke test.

## Conventions (short version)

- Gameplay runs on **fixed sim ticks** (10 Hz; 1200 ticks = 1 game day = 120 s by
  default → 1 game hour = 50 ticks). Never `Time.deltaTime` in simulation.
- All randomness flows through **SimRandom/RngState** (PCG32, seeded from the world
  seed, sub-streams per subsystem). `UnityEngine.Random`/`System.Random` are banned in
  simulation code — enforced by a source-scan test.
- `Ecosphere.Core.Simulation` compiles with **no engine references** (`noEngineReferences:
  true`) — it is the pure, portable core.
- Input: stage 01 uses the **legacy Input Manager** (two keys + HUD); the Input System
  package arrives with the stage-07 camera/UX pass.
- HUD: IMGUI for now; UI Toolkit in stage 07.

## Stage status

| Stage | Scope | Status |
|---|---|---|
| 01 | Foundation — project, DOTS, time, RNG, architecture | ✅ done |
| 02 | Planet & time — icosphere from seed, surface cells, rotation/tilt | ✅ done |
| 03 | Climate — per-cell fields, weather events, `IClimateSampler` | ✅ done |
| 04 | Genome & morphogenesis — genome, development, low-poly meshes, viewer | ✅ done (`stage-04-genome`) |
| 05 | Creature life — needs/behavior | next |
| 06 | Ecology & evolution | planned |
| 07 | Product UX & optimization | planned |

### What stage 05 must do

Consume the stage-04 contracts: the behavior-weight blob + instinct priors
(MLP utility AI), the diet vector, metabolism params, and the derived
locomotion params (§4.5 of `Docs/genetics.md`). Needs: energy, hydration,
thermal comfort, rest, safety, social, reproduction, curiosity — all sampled
through `IClimateSampler` and the per-organism `EnvironmentEMAData`.

## Project layout

```
Assets/
├── Scenes/Main.unity            planet scene (camera + bootstrap + overlays)
├── Scenes/OrganismViewer.unity  stage-04 debug scene (specimen + gallery viewer)
├── Ecosphere/
│   ├── Core/Simulation/         pure C#: RNG, calendar, accumulator, SimLog,
│   │                            genetics math (catalog, development, L-system, JSON)
│   ├── Core/ECS/                DOTS: components, groups, TimeSystem, events, save seam,
│   │                            genome components + organism factory
│   ├── Genetics/                PhenotypeUpdateSystem (morphogenesis, tick-gated)
│   ├── Authoring/               bootstrap + ScriptableObject configs (incl. GeneCatalog)
│   ├── Presentation/            SimHud + input + organism mesh builder/pool/viewer
│   ├── Config/                  WorldSettings.asset, GameBalance.asset, GeneCatalog.asset
│   ├── Planet/                  icosphere, cells, sun, climate (stages 02–03)
│   └── Life/ Ecology/ UX/       ← stage stubs
└── Tests/                       EditMode + PlayMode + performance tests
```

## Planet work in progress

Main now starts seed-driven primal icosphere generation (default 10,242 cells) with
vertex-coloured chunk meshes, a queryable per-cell sunlight buffer and orbit camera.
See [Docs/planet.md](Docs/planet.md) for the grid, ownership and outstanding stage-02 gaps.
