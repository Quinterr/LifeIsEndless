# Ecosphere — Architecture (stage 01 foundation)

This document is the contract for every later stage. If a stage needs to break a rule
here, that is a design review, not a commit.

## 1. Layers

```
                 ┌────────────────────────┐
   scene/assets  │    Ecosphere.Authoring │  MonoBehaviours, Bakers, ScriptableObjects
                 │  (WorldBootstrap, SOs) │  WorldSettings / GameBalance
                 └───────────┬────────────┘
                             │ creates world, injects singletons
                             ▼
                 ┌────────────────────────┐
                 │    Ecosphere.Core.ECS  │  components, systems, groups, events
                 │ (TimeSystem, groups,   │  references: Core.Simulation +
                 │  events, metrics)      │  Entities/Collections/Mathematics/Burst/Jobs
                 └───────────┬────────────┘
                             │ uses pure logic
                             ▼
                 ┌────────────────────────┐
                 │ Ecosphere.Core.Simulation │  PURE C# — no engine references at all
                 │ (RngState, CalendarMath,  │  (asmdef: noEngineReferences = true)
                 │  TimeAccumulator, SimLog) │
                 └───────────▲────────────┘
                             │ reads state for display only
                 ┌───────────┴────────────┐
                 │  Ecosphere.Presentation│  HUD, input, camera (stage 07)
                 │   (SimHud, input)      │  NEVER writes gameplay state except
                 └────────────────────────┘  TimeControl (player intent)
```

Dependency rule: arrows only point downward. `Core.Simulation` depends on nothing.
`Core.ECS` depends only on `Core.Simulation` + DOTS packages. `Presentation` renders
and forwards input; it never mutates simulation state other than `TimeControl`.

## 2. Assemblies

| asmdef | Contents | References |
|---|---|---|
| `Ecosphere.Core.Simulation` | RngState/SimRandom, CalendarMath, TimeAccumulator, SimLog | **none** (`noEngineReferences: true`) |
| `Ecosphere.Core.ECS` | components, groups, systems, events, save seam | Core.Simulation, Unity.Entities, Collections, Mathematics, Burst |
| `Ecosphere.Authoring` | WorldBootstrap, WorldSettings/GameBalance SOs, WorldSettingsAuthoring baker | Core.*, Entities, Entities.Hybrid, Transforms |
| `Ecosphere.Presentation` | SimHud (IMGUI), TimeControlInput (legacy Input) | Core.*, Entities |
| `Ecosphere.Tests` | EditMode + PlayMode tests, perf tests | all of the above + TestRunner + PerformanceTesting |

## 3. System update order (explicit slots)

All simulation systems run inside Unity's `SimulationSystemGroup` (player loop). Stage
01 installs two named slots; later stages plug in via attributes only.

```
SimulationSystemGroup                        (Unity root, player loop)
├── TimeSystemGroup                [OrderFirst]
│   ├── BeginSimulationEcbSystem   [OrderFirst]   ← ECB frontier; plays back last frame
│   └── TimeSystem                                ← advances ticks, records boundary events
│        (TimeSystemGroup.OnUpdate also maintains SimMetricsData)
├── EventSystemGroup
│   ├── TimeEventLoggerSystem                     ← reference consumer → SimLog
│   ├── <stage 03..06 consumers go here, [UpdateBefore(EventPurgeSystem)]>
│   └── EventPurgeSystem               [OrderLast] ← destroys SimEventTag entities
└── <future groups: ClimateSystemGroup (03), LifeSystemGroup (05), EcologySystemGroup (06)>
```

Ordering is declarative (`[UpdateInGroup]`, `[UpdateBefore]`, `OrderFirst/OrderLast`).
`WorldBootstrap` additionally force-creates the two groups so ordering is settled before
the first update.

## 4. Simulation clock

* Fixed tick: `simTicksPerSecond` (default 10 Hz). Rendering is decoupled.
* One game day = `secondsPerGameDay` (default 120 s) = **1200 sim ticks**.
  → 1 game hour = 50 ticks at defaults. The brief's "10 ticks = 1 game hour" holds when
  `secondsPerGameDay = 24` (both cases are pinned by `CalendarMathTests`).
* Year = 60 days = 4 × 15-day seasons (Spring/Summer/Autumn/Winter, south inverted via
  `CalendarMath.SouthernSeason` / `SeasonAtLatitude`).
* `TimeAccumulator` converts scaled wall-clock time into whole ticks:
  `backlog += dt × timeScale; ticks = backlog / tickDuration` with a catch-up cap of
  **8 ticks/frame**; at the cap the remaining backlog is dropped (no death spirals).
  Pause/scale-0 clears the backlog.
* Time scales: index → {0 (pause), 0.5, 1, 4, 16, 64, 256} (`CalendarMath.TimeScaleForIndex`).
* **Every gameplay system runs on sim ticks** — read `GameTime`, never `Time.deltaTime`.
  The only wall-clock read is `HostFrameData.DeltaTime`, written by `WorldBootstrap`
  (or by tests driving a world manually).

### Event lifecycle (day/season/year)

1. Frame N — `TimeSystem` detects boundary crossings (before/after `CalendarMath.FromTicks`)
   and records `DayChangedEvent` / `SeasonChangedEvent` / `YearChangedEvent` (each with
   `SimEventTag`) into the `BeginSimulationEcbSystem` buffer.
2. Frame N+1 — the ECB plays back at the top of `TimeSystemGroup`; events are visible to
   every `EventSystemGroup` consumer for exactly this frame.
3. End of frame N+1 — `EventPurgeSystem` destroys all remaining tagged event entities.

`TimeEventLoggerSystem` is the reference consumer (mirrors events into `SimLog`).

## 5. Determinism & RNG

* `RngState` = PCG-XSH-RR 64/32. Pure struct, integer core, platform-independent.
* `SimRandom.ForStream(worldSeed, streamId)` derives independent sub-streams.
  Convention: `streamId = StreamIds.Combine(StreamIds.Fnv1a("SystemName"), itemIndex)`;
  hot paths combine integers only (no string hashing).
* **Only SimRandom** in simulation code. `UnityEngine.Random` / `System.Random` are banned;
  `BannedRandomApiTests` scans the Core.Simulation sources and fails the build test run
  on violations. (Comment lines are exempt so this ban may be documented in code.)
* Determinism contract: same world seed + same tick input sequence ⇒ same world.
  Frame-rate deltas affect only *how many* ticks a frame advances, never tick contents.
  `RngState.NextGaussian` uses `System.Math` (fine off-job today; port to
  Unity.Mathematics if a Burst job needs it).

## 6. Hard rules

1. **Fixed ticks only** — no gameplay logic on `Time.deltaTime` / frame callbacks.
2. **SimRandom only** — enforced by test scan.
3. **SoA data** — `IComponentData` / `DynamicBuffer`, no per-entity class instances.
   (Genomes will use `DynamicBuffer<Gene>` in stage 04.)
4. **No allocations in hot paths** — struct log entries, ring buffer, ECB reuse;
   `PerformanceSmokeTests.ThousandEmptyTicks_AllocateApproximatelyZero` guards the floor.
5. **Core.Simulation stays engine-free** — the asmdef has `noEngineReferences: true`;
   any UnityEngine reference simply fails to compile.
6. Managed-code isolation — if a Unity API forces managed code, hide it behind an
   interface in Core.ECS (pattern: `Save/IWorldSerializer`).

## 7. Configuration

* `WorldSettings` (ScriptableObject, committed at `Assets/Ecosphere/Config/WorldSettings.asset`)
  → world seed, secondsPerGameDay, simTicksPerSecond, daysPerSeason, seasonsPerYear,
  maxOrganisms placeholder, locale (en/ru). Runtime-injected as `WorldSettingsData`
  singleton by `WorldBootstrap`; `WorldSettingsAuthoring.Baker` is the subscene-baking
  seam for stage 02+.
* `GameBalance` (ScriptableObject, committed) → placeholder sections Climate / Plants /
  Animals / Evolution, filled by stages 03–06. Loadable in EditMode tests.

## 8. Logging & metrics

* `SimLog`: static 1024-entry ring buffer of struct entries `{Tick, Category, Code, A, B,
  Payload}` — zero allocation per push; text formatting is lazy (`SimLog.CodeName`,
  `TextWriterLogSink`). Categories: Time, Climate, Life, Evolution, Performance.
* `SimMetricsData` singleton (maintained by `TimeSystemGroup`): EMA of sim wall time per
  frame and per tick, achieved ticks/second, entity count, total ticks. HUD + tests read it.

## 9. Folder map & where stages plug in

```
Assets/Ecosphere/
├── Core/Simulation     pure logic (RNG, calendar, accumulator, log)      [stage 01 ✓]
├── Core/ECS            components/systems/groups/events/save seam         [stage 01 ✓]
├── Authoring           bootstrap + ScriptableObject configs               [stage 01 ✓]
├── Presentation        HUD + input (camera, overlays, UI Toolkit in 07)   [stage 01 ✓]
├── Config/             committed WorldSettings.asset, GameBalance.asset   [stage 01 ✓]
├── Planet/             icosphere, cells, rotation/tilt                    [stage 02]
├── Climate/            per-cell climate fields, weather events            [stage 03]
├── Genetics/           genome, inheritance, morphogenesis                 [stage 04]
├── Life/               needs, behavior, locomotion, plant growth          [stage 05]
├── Ecology/            food web, reproduction, speciation, phylogeny      [stage 06]
└── UX/                 camera rig, UI Toolkit, inspectors, god tools      [stage 07]
```

Stage 02 inserts planet generation systems into a new group after `EventSystemGroup`
(e.g. `PlanetSystemGroup`), consumes `DayChangedEvent`/`SeasonChangedEvent` for
insolation, and reads `WorldSettingsData.WorldSeed` via `SimRandom.ForStream`.

## 10. Input & HUD decisions (stage 01)

* Input: **legacy Input Manager** (`UnityEngine.Input`) — Space = pause/resume,
  `[` / `]` = time scale down/up, HUD grid selector. The Input System package arrives
  with the stage-07 camera/UX pass.
* HUD: **IMGUI (OnGUI)** — zero asset dependencies for a debug HUD; UI Toolkit replaces
  it in stage 07.

## 11. Save seam

`Ecosphere.Core.ECS.Save.IWorldSerializer` + `WorldSerializerStub` (throws
`NotSupportedException`). The real binary/GZip format ships in stage 07; all component
data is plain blittable structs on purpose so snapshots stay trivial later.
