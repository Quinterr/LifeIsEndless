# LifeIsEndless — Ecosphere

A low-poly artificial-life sandbox: a living planet where plants and animals grow from
genomes, follow their own needs, evolve, and are shaped by weather, wind, currents and
pressure across day/night cycles and 15-day seasonal quarters.

- 📄 **Project brief** (frozen vision): [`Docs/project-brief.md`](Docs/project-brief.md)
- 🏗 **Architecture & rules**: [`Docs/architecture.md`](Docs/architecture.md)
- 🧬 **Life system documentation**: [`Docs/life.md`](Docs/life.md)
- 🌱 **Ecology & evolution (Stage 06)**: [`Docs/evolution.md`](Docs/evolution.md)
- 📊 **Performance budgets and measurement status**: [`Docs/performance.md`](Docs/performance.md)
- 🤝 **Stage workflow & commit rules**: [`CONTRIBUTING.md`](CONTRIBUTING.md)

## Requirements

- Unity **6000.0 LTS** (pinned to `6000.0.75f1` in `ProjectSettings/ProjectVersion.txt`;
  any 6000.0.x can open it — accept the upgrade prompt).
- Modules: none beyond the default editor install (no platform SDKs needed yet).
- Packages are resolved automatically from `Packages/manifest.json`:
  Entities 1.3.14, Collections 2.5.7, Burst 1.8.19, Mathematics 1.3.2,
  URP 17.0.4, Test Framework 1.4.6 + Performance Testing 3.0.3.
  - Note: the brief lists `com.unity.jobs` — that package is deprecated (merged into
    Collections); the C# Job System is built into Unity 6.
  - UI: UI Toolkit ships with Unity 6 and will be used for stage-07 UI; TextMeshPro is
    not needed yet.

## Opening & running

1. Unity Hub → **Add** → select this repository root.
2. Open the project (first open resolves packages and imports assets).
3. Open `Assets/Scenes/Main.unity` (living planet with founder population) or
   `Assets/Scenes/StressWorld.unity` (10,000 organisms performance stress test) and press **Play**.

The only ProjectSettings files committed are `ProjectVersion.txt` and
`EditorBuildSettings.asset`; the editor regenerates the rest with defaults on first open.

### In-game controls

| Input | Action |
|---|---|
| `Space` | pause / resume simulation |
| `[` / `]` | time scale down / up (x0…x256) |
| `1`–`8` | switch climate/resource overlay modes on the planet surface |
| `0` | reset surface color to biome natural colors |
| HUD grid | select time scale directly |

The HUD shows: game date (Day X • Season • Year N), clock, tick counter, FPS, sim
ms/frame and per-tick, achieved ticks/s, entity count, world seed.

## Tests

`Window → General → Test Runner` (or CLI):

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults results.xml
```

Coverage:
- RNG determinism & stream independence (`SimRandom`, `RngState`).
- Calendar math, seasonal boundaries, southern hemisphere inversion.
- Climate simulation & weather event logging.
- Morphogenesis & genome development pipeline (`GenomeMath`, `OrganismMeshBuilder`).
- Creature life (Stage 05): needs drain/refills, utility AI, sphere locomotion, plant
  growth, resource exchange, predation, and death cleanup.
- Ecology & evolution (Stage 06): deterministic sexual/asexual inheritance, dominance,
  structural mutation caps, lineage/DAG/extinction bookkeeping, cold/drought/predator
  trait trends, controlled speciation, the 100-year Default harness, energy-ledger
  closure, and `EvolutionQuery` snapshots.
- Performance budgets are documented as targets only; see `Docs/performance.md` for
  the required Unity profiling protocol and current measurement status.

The portable Stage 06 batch harness links only the pure `Core/Simulation` sources and
can run without Unity or external NuGet packages:

```
dotnet run --project Tools/EvolutionCli -- --scenario default --seed 42 --years 100 --out artifacts/default
dotnet run --project Tools/EvolutionCli -- --all --out artifacts/all
```

It writes metrics, events, phylogeny, energy-audit CSVs and a JSON summary per scenario.

## Conventions (short version)

- Gameplay runs on **fixed sim ticks** (10 Hz; 1200 ticks = 1 game day = 120 s by
  default → 1 game hour = 50 ticks). Never `Time.deltaTime` in simulation.
- All randomness flows through **SimRandom/RngState** (PCG32, seeded from the world
  seed, sub-streams per subsystem). `UnityEngine.Random`/`System.Random` are banned in
  simulation code — enforced by a source-scan test.
- `Ecosphere.Core.Simulation` compiles with **no engine references** (`noEngineReferences:
  true`) — it is the pure, portable core.
- Input: legacy Input Manager (two keys + HUD); the Input System package arrives with
  the stage-07 camera/UX pass.
- HUD: IMGUI for now; UI Toolkit in stage 07.

## Stage status

| Stage | Scope | Status |
|---|---|---|
| 01 | Foundation — project, DOTS, time, RNG, architecture | ✅ done |
| 02 | Planet & time — icosphere from seed, surface cells, rotation/tilt | ✅ done |
| 03 | Climate — per-cell fields, weather events, `IClimateSampler` | ✅ done |
| 04 | Genome & morphogenesis — genome, development, low-poly meshes, viewer | ✅ done (`stage-04-genome`) |
| 05 | Creature life — needs/behavior/locomotion/ecology loop | ✅ done (`stage-05-life`) |
| 06 | Ecology & evolution | implemented; Unity compile/tests pending (`stage-06-evolution`) |
| 07 | Product UX & optimization | planned |

## Project layout

```
Assets/
├── Scenes/
│   ├── Main.unity              planet scene (camera + bootstrap + founder population)
│   ├── OrganismViewer.unity    stage-04 debug scene (specimen + gallery viewer)
│   └── StressWorld.unity       stage-05 stress benchmark scene (10,000 organisms + metrics)
├── Ecosphere/
│   ├── Core/Simulation/       pure C#: RNG, calendar, accumulator, SimLog,
│   │                          genetics math (catalog, development, L-system, JSON)
│   ├── Core/ECS/              DOTS: components, groups, TimeSystem, events, save seam,
│   │                          genome components + organism factory
│   ├── Genetics/              PhenotypeUpdateSystem (morphogenesis, tick-gated)
│   ├── Authoring/             bootstrap + ScriptableObject configs (incl. GeneCatalog)
│   ├── Presentation/          SimHud + input + organism mesh builder/pool/viewer
│   ├── Planet/                icosphere, cells, sun, climate
│   └── Life/                  needs, utility AI + genome MLP, great-circle locomotion,
│                              plant passives, resource pools, scent advection, death records
└── Tests/                     EditMode + PlayMode + performance tests
```
