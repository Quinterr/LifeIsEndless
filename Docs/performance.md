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
