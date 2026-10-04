# Life — Stages 05–06

The Life assembly runs needs, utility behavior, locomotion, plant growth, resource
exchange, death cleanup, reproduction and evolution on fixed simulation ticks.

Stage 05 supplies the ecological mechanics: animal energy/hydration needs, movement,
feeding, plant photosynthesis, resource pools and death attribution. Stage 06 adds
inheritance-driven reproduction, egg/gestation and seed-bank life cycles, stable
lineage/species bookkeeping, habitat-level speciation, extinction records and annual
species metrics. See [`Docs/life.md`](../../../Docs/life.md) and
[`Docs/evolution.md`](../../../Docs/evolution.md) for the contracts and known
simplifications.

The ECS state uses plain components and dynamic buffers. Census bins change through
birth/death/movement deltas; the annual habitat-speciation pass is the only routine
full scan of live organisms. Rare extinction-safety checks query transient uncounted
births when stored genomes are lost. `GameBalance.Evolution` controls mutation,
compatibility, speciation and per-tick work budgets.

The current implementation has not yet been compiled or exercised in the pinned Unity
editor in this environment. Do not interpret the documented target budgets as measured
results.
