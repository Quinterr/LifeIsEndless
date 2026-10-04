# Stage 06 — Ecology & Evolution

Stage 06 adds heritable variation and population history on top of the Stage 05 needs,
behavior, movement, plant-growth and resource systems. Trait trends are produced by
inheritance plus differential survival/reproduction; no system edits a gene toward a
scenario-specific target.

## Inheritance contract

`Ecosphere.Core.Simulation.GenomeEvolutionMath` is the deterministic, engine-free
inheritance implementation used by the ECS and the batch harness:

- Sexual children alternate parental chromosome segments at one or two seeded cut
  points per chromosome. Matching alleles are blended using dominance; phenotype
  expression uses the catalog reference allele for recessives.
- Asexual animal offspring and plant clones skip crossover but use the same mutation
  pass. Child streams are keyed by world seed, parental genome seeds and a stable
  conception ordinal.
- Per-gene point mutation uses the gene's mutation rate, the configured multiplier,
  an environmental mutagen and `RngState`; a subset of point mutations also changes
  the heritable dominance coefficient. Structural duplication/deletion is bounded per
  chromosome and by a two-times default genome-length cap.
- Parent arrays are never changed. Chromosome starts travel with stored embryos and
  offspring so structural changes remain in the inherited layout.

No simulation code uses `UnityEngine.Random` or `System.Random`.

## Runtime life cycle

`ReproductionSystem` pairs eligible adults in the same or adjacent cells, requires
same-species genome compatibility and a seasonal breeding window, then applies a
courtship delay and parental energy/hydration costs. The inherited `GestationDuration`
selects live gestation or eggs; `ClutchSize` limits egg count. Egg incubation is
climate-sensitive and exposed eggs can be predated. Gestation can fail when the mother
dies. Parental care is an energy-transfer mechanic, not a scripted survival bonus.

`PlantReproductionSystem` makes seeds during spring/summer flowering. Nearby/downwind
compatible flowering plants and forager activity can provide pollination; otherwise the
plant can clone. Seeds move over neighboring surface cells according to wind/current,
wait through a seed-bank delay, lose viability, and germinate only when season, light,
soil moisture and temperature permit. Germination creates an ordinary plant organism,
which then enters the existing phenotype, life and resource loop.

System tuning is copied from `GameBalance.Evolution` into serializable
`EvolutionStateData` when `PlanetBootstrap` creates the planet. Defaults are also
available to tests/minimal worlds.

## Population, lineage and species records

Every organism receives a stable `OrganismIdentity`, `SpeciesIdentity` and
`LineageData`. Births are mirrored to the append-only `LineageRecord` buffer before
ordinary death cleanup destroys the entity. `DeathRecord` records stable organism and
species ids and the death cause. Egg/seed deaths are evolution events because those
entities are not yet counted organisms.

`CellSpeciesPopulation` bins are updated by birth/death deltas and cell transitions
emitted by `SphereLocomotionSystem`; they are not rebuilt from all organisms each tick.
Species use online trait moments and retain extinction tombstones. Once per year the
runtime groups organisms by species and biome/continent, compares local trait drift and
cross-region genome compatibility, and may fork one geographically structured
subpopulation. A fork appends a parent-child edge and a speciation event. Extinction
records preserve the last cause and tick. The phylogeny is a DAG; species ids are never
reused.

Annual per-species metrics include population, mean/variance, a diversity measure,
effective population size, mutation load, births, deaths and the mean trait difference
between reproductive parents and the census. The portable runner estimates diversity
from sampled pairwise genome distance; the runtime ECS uses a normalized trait-variance
proxy to keep updates incremental. Its effective-size field is also a proxy: it counts
distinct breeders observed in that year, capped by census population, rather than
estimating a full Wright–Fisher effective size. Each organism stores the last
year/species in which it contributed, preventing repeated matings from inflating the
count. Runtime event and metric buffers are bounded by
`GameBalance.Evolution.MaxTrackedEvents/MaxTrackedMetrics`; lineage and phylogeny
history remain append-only.

`Ecosphere.Core.ECS.EvolutionQuery` provides read-only snapshots for callers that have
the planet entity. It copies buffer values into managed lists so callers do not keep
buffer handles across structural changes. Use it on the main thread; it is not a Burst
or job API.

## Portable scenarios and CLI

The pure harness is in `HeadlessEvolutionRunner`. Its five deterministic scenarios are:

| CLI name | Controlled pressure | Expected measured direction |
|---|---|---|
| `default` | Regional temperature and carrying capacity | Stable nonzero population; no forced trait trend |
| `ice-age` | Cold survival/reproductive weighting | Higher cold-tolerance/endothermy/cover alleles |
| `predator-pressure` | Predator-prey population proxy and predation fitness | Higher speed, vigilance and herdiness; prey/predator series exported |
| `drought` | Low-moisture plant fitness | Higher root depth and water efficiency |
| `mass-extinction-recovery` | Configured multi-year carrying-capacity bottleneck | Mass-die-off, recovery and extinction events where lineages disappear |

Run one scenario or all five with the standalone .NET CLI (the project links only the
pure `Core/Simulation` sources; it does not require Unity):

```sh
dotnet run --project Tools/EvolutionCli -- --scenario default --seed 42 --years 100 --out artifacts/default
# or
dotnet run --project Tools/EvolutionCli -- --all --out artifacts/all
```

Each output directory contains `metrics.csv`, `events.csv`, `phylogeny.csv`,
`energy.csv` and `summary.json`. CSV numbers use invariant round-trip formatting. The
CLI's energy audit is a cohort-scaled normalized ledger for the portable harness; it is
not a claim that the current Unity world has a fully measured physical-energy budget.

## Simplifications and validation status

- The portable harness is a controlled selection/regression model, not a second ECS
  implementation. It uses scenario fitness only to change parent sampling, not to mutate
  traits in a preferred direction. Its population size is scenario carrying capacity;
  yearly deaths/births describe cohort replacement. Predator-prey values are a compact
  Lotka–Volterra proxy.
- The Unity mate selector has no explicit sex chromosome or age-structured population
  genetics model. The initiator supplies the maternal role; compatibility is based on
  normalized genome distance. The plant pollination neighborhood is intentionally local.
- Runtime speciation samples annual habitat clusters and may be tuned through
  `GameBalance`; it does not model reproductive isolation with a full mating network.
- Egg/seed state is serializable ECS data, but the repository's full world save/load
  implementation remains a later-stage seam.
- No Unity compile, EditMode run, or profiler measurement is represented by this
  document. Run the tests and profile on the pinned Unity editor before treating target
  budgets or default-world outcomes as measured results. A lack of default-world
  speciation is not hidden as a pass; controlled speciation tests exercise the detector
  and DAG invariants separately.

## Tests

EditMode tests in `Assets/Tests/EditMode` cover deterministic sexual/asexual inheritance,
dominance, mutation caps, trait moments, species DAG/extinction, cell-ledger deltas,
controlled cold/drought trends, the 100-year Default harness and `EvolutionQuery`.
Run through Unity Test Runner or the repository's batchmode commands in the README.
