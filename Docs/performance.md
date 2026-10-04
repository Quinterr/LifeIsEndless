# Performance Measurement Plan & Budgets

## Measurement status

No Unity profiler or performance-test run is recorded for this stage in the current
workspace. Budgets below are **targets**, not measured or achieved results. Earlier
Stage 05 benchmark values have not been independently verified here and should not be
quoted as measurements.

## Stage 05 target

| Scope | Target | Measurement status |
|---|---:|---|
| Life simulation tick at 10,000 organisms | ≤ 8 ms | Not measured in this workspace |
| Render rate in `StressWorld.unity` | 60 FPS target | Not measured in this workspace |

## Stage 06 work budgets — targets only

| Work | Target | Current upper bound / scaling |
|---|---:|---|
| Incremental population bookkeeping | ≤ 1.0 ms per sim tick | Buffer lookups are linear: O(deltas × (tracked cell/species bins + species records)); annual reporting aggregates bins once and prunes empty bins |
| Animal conception search | ≤ 1.5 ms per sim tick | At most `MaxConceptionsPerTick` successful conceptions; currently scans eligible adults and checks local/neighbor candidates |
| Egg/seed incubation | ≤ 1.0 ms per sim tick | At most `MaxEggUpdatesPerTick` stored genomes are advanced; predator counts are rebuilt only while stored genomes exist |
| Annual speciation | ≤ 20 ms per year boundary | O(organisms + habitat clusters² × sampled genome-distance work), once per year; this is the only Stage 06 full organism scan |
| All Stage 06 additions | ≤ 3.0 ms ordinary tick; ≤ 20 ms annual boundary | Must be profiled; current implementation uses managed collections and temporary NativeArray snapshots |

`EvolutionStateData` stores the per-tick work caps; `GameBalance.Evolution` is their
asset-facing tuning surface. The budgets should be evaluated in both the ordinary
10,000-organism stress world and a high-reproduction/large-seed-bank case. A rare
extinction-safety check also queries the transient `PopulationUncounted` cohort when a
stored genome is discarded; include a high-egg/seed-loss case in that profile.

## Profiling protocol

1. Open the pinned Unity editor, let Burst warm up, and use a release-like player/editor
   configuration consistent across runs.
2. Measure steady-state tick time separately from annual speciation-boundary time.
3. Record organism count, species count, cell count, stored egg/seed count, hardware,
   Unity version, build configuration and sample count alongside every reported value.
4. Use the Unity Performance Testing package for repeatable timing and allocation
   samples; inspect the Unity Profiler for structural-change, GC and job-wait costs.
5. Do not infer 10k-world timings from empty-world smoke tests or from algorithmic
   complexity alone.

The climate hot pass is a Burst `IJobParallelFor` with neighbor traversal and event
processing; its reference architecture is documented in `Docs/climate.md`. Its timing is
also not measured by this Stage 06 change.


## Stage 07 — product budgets (targets, capture recipe)

Stage 07 turns the stage-06 developer build into a shippable slice, so the budgets below are
the acceptance numbers the product layer is built against. **They are still targets in this
workspace**: the environment that produced stage 07 has no Unity/GPU, so no number here has
been measured. Every number is produced by one code path (`PerfBudget.Report`) so the capture
recipe is the same for the reference machine, a QA pass and CI runners.

| Budget | Target | Where it is enforced / reported |
|---|---:|---|
| Frame rate @ 2560×1440 | 60 FPS (frame p99 ≤ 16.7 ms) | `QualityController` auto-degrade; `PerfBudget.MeetsFps` |
| Sim tick @ 10,000 organisms | ≤ 8 ms | `PerfBudget.SimTickBudgetMs`; `SimMetricsData.PerTickMsEma` |
| Rendered instances | ≥ 20,000 | `QualityRuntime.ReportRenderedInstances` + `PerfBudget.MeetsInstances` |
| Warm memory | < 2 GB | `PerfBudget.MeetsMemory` |
| World load | < 30 s | `QualityController.RecordWorldLoad` + checksum-verified load path |
| GC in the play loop | 0 B/frame | `ProfilerRecorder` on "GC Allocated In Frame" in the perf smoke test |

### Capture recipe

1. Launch a **release** player (IL2CPP) at 2560×1440, or the editor with Burst warmed up and
   the same tier; note CPU/GPU, Unity version and `Docs/manual.md` build stamp.
2. Load a world with ≥ 10,000 organisms (the headless stress scenario, or `StressWorld`).
3. Let the sim reach steady state at ×1 for 60 s, then at ×32 for 60 s; take the report from
   the performance chip's long form (`PerfBudget.Report`).
4. Repeat on Ultra/High/Medium/Low and record the tier curve; the tier that meets the budgets
   becomes the default in `ProductSettings`.
5. Record the load time from world file open to first frame, and the warm memory after
   5 minutes of play.

### Structural notes that influence the numbers

- Overlays recompute once per game tick on `mesh.colors32` and cross-fade colours; only the
  population-density mode touches the stage-06 buffers (one pass over `CellSpeciesPopulation`).
- Terrain edits rebuild affected chunk geometry once per revision via `TerrainVersion`.
- Rewind/date scrubbing uses catch-up tick batches with a frame budget (`ScrubMath.Plan`), so a
  long jump degrades frame rate gracefully instead of locking up.
- At the organism cap, distant creatures fall back to aggregates
  (`QualityRuntime.PopulationAggregated` / `InstanceFraction`) instead of hard-culling.
- The beauty pass adds one 48x24 cloud sphere with a generated 256x128 texture, two aurora
  ribbons (Ultra/High winter only) and per-frame light/ambient tinting; all three are
  quality-gated and disabled in safe mode after a crash.
