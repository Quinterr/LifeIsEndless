# Performance measurements

No Unity Editor/runtime is installed in this workspace. The requested 20k-cell orbit frame-rate,
≤5 s terrain generation benchmark, and stage-03 ≤4 ms climate-step target remain **unmeasured**.
Generation currently runs synchronously on scene start and should be converted to scheduled jobs
before claiming the stage-02 terrain budget. Level 5 generates 10,242 primal cells; level 6
generates 40,962.

## Climate cost model (not a benchmark)

The climate hot pass performs roughly six neighbor visits per cell in one Burst
`IJobParallelFor`, then copies one `PlanetCell` per cell back to ECS. The event scan is one
additional linear pass, with a maximum of 512 retained event records. Native snapshots are
persistent and reused; the tick path does not allocate managed objects. Actual milliseconds,
Burst warm-up, and 20k-cell player-build timing must be captured in Unity before the ≤4 ms budget
can be claimed. Run the `Seed42Level4RunsAStableSeasonalClimateYear` EditMode regression
to verify climate invariants and write its per-cell CSV to `TestResults/`.
