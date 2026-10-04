# Genetics (stage 04)

`PhenotypeUpdateSystem` — the morphogenesis engine. Reads `GenomeHeader` +
`DynamicBuffer<GeneElement>` per organism, advances age/environment EMA, and
writes `PhenotypeData` + `LifeStageData` + `OrganismSize`, marking `MeshDirty`
on stage or 10%-size-bucket transitions (never per tick).

Runs in `SimulationSystemGroup` after `ClimateSystem`; tick-gated (advances per
sim tick); reads climate only via `IClimateSampler` at the organism's cell;
manual override via `ManualEnvironment` (Organism Viewer debug tool).

The full contract — gene catalog, development rules, mesh grammar, LOD/pooling,
determinism, JSON format, and every simplification — is
[`Docs/genetics.md`](../../../Docs/genetics.md).

Pure development math lives in `Core.Simulation/Genetics` (engine-free);
mesh synthesis lives in `Presentation/Genetics`.
