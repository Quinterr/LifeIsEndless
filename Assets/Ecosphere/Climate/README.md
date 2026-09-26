# Climate (stage 03)

Climate simulation and its read-only `IClimateSampler` live in the Planet runtime assembly.
`ClimateSystem` is the sole writer of dynamic climate fields; it updates after the stage-02
`SunSystem`, uses a Burst double-buffered cell job, and logs bounded weather events. The demo
provides 0–8 climate/terrain overlays and a selected-cell weather station. Field units, causal
rules, API and known simplifications are documented in [`Docs/climate.md`](../../../Docs/climate.md).

EditMode unit tests are in `Assets/Tests/EditMode/ClimateMathTests.cs`. The seed-42,
level-4, 60-day deterministic regression emits `TestResults/climate-seed42-60days.csv` when run
from Unity Test Runner. Runtime/performance measurements remain unverified in this workspace.
