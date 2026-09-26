# Ecosphere — Project Brief (shared context)

> **Purpose:** keep this file in the repository at `Docs/project-brief.md`. Every
> implementation prompt references it so each agent session has full project context.
> **Do not modify it during staged development without a design review.**

## One-liner

Ecosphere is a low-poly artificial-life sandbox: a living planet (sphere) where plants and
animals grow from genomes, follow their own needs, evolve, and are shaped by weather, wind,
currents, and pressure across day/night cycles and 15-day seasonal quarters.

## Genre & audience

God-game / digital terrarium / artificial life. PC (Windows/macOS/Linux).
The player observes and gently intervenes; there are no levels. The simulation is the story.

## Design pillars

- **Life from a genome** — every organism is developed from its DNA-like genome (body plan,
  metabolism, appearance, behavior). Real ontogenesis: embryo/seed → juvenile → adult → old.
- **Climate is a character** — weather, wind, ocean currents and pressure actively shape
  survival, behavior and long-term evolution.
- **Observation as gameplay** — camera from orbit down to one insect; UI narrates events.
- **Low-poly honesty** — flat shading, vertex colors, strong silhouettes; no fake realism.
- **Scale through abstraction** — believable models at every LOD (individual vs population).

## World

- Icosphere planet from a seed: continents, mountains, oceans, polar ice. 10k–50k surface
  cells (elevation, water/land, biome, soil moisture, fertility, biomass, detritus).
- Rotation + axial tilt → day/night sides (visible terminator), polar day/night, seasons.
- Ocean layer with currents; atmosphere with clouds and precipitation.
- Time: day = one rotation. Year = 60 days = 4 seasons × 15 days (spring, summer,
  autumn, winter; southern hemisphere inverted). Time controls: pause, ×1…×256, jump to date.
- Simulation runs on a fixed tick (10 Hz); rendering is decoupled.

## Climate

Per-cell fields: temperature, pressure, humidity, wind vector, cloud cover, precipitation,
soil moisture, snow; ocean: currents + water temperature.

Mechanics: insolation (latitude/season/time-of-day/albedo/altitude), land-vs-water heat
capacity, pressure gradients + Coriolis → wind, advection of heat/humidity, wind-driven +
thermohaline (simplified) currents, weather events (fronts, storms, fog, hail, blizzards,
droughts). Every organism samples weather at its cell (altitude-adjusted for fliers).

Wind: chill, flight cost, scent dispersal, seed dispersal, waves. Pressure gradients drive
wind; sharp pressure drops warn animals of storms. Currents move heat, nutrients, larvae.

## Genome & morphogenesis (core system)

- Chromosomes → genes: `TypeId`, `Value(0..1)`, `Dominance`, `MutationRate`.
- Gene groups: regulatory; body plan (animals: segments, limbs, head, tail, sensors, mouth,
  integument; plants: roots, stem, L-system branching, leaves, flowers/fruit, seeds);
  metabolism (rate, endo/ectothermy, water need, diet: photosynthesis/herbivory/carnivory/
  scavenging/filter-feeding); growth & life history (growth curves, maturity, stages,
  lifespan); appearance (pigments, patterns, cover, bioluminescence); behavior (small MLP
  weights, instincts, sociability, aggression, curiosity, sensor ranges); reproduction
  (sexual/asexual, clutch/seed count, egg/seed size, parental care, breeding season,
  mutation rate).
- Inheritance: crossover (1–2 points per chromosome), point mutations, duplications/
  deletions, dominance blending.
- Development: staged gene expression, allometry, simple morphogen gradients along body
  axis/segments, environment-modulated expression (epigenetics-lite: cold → denser cover,
  low light → larger leaves). Deterministic: same genome + same environment history → same
  phenotype.
- Output: parametric low-poly mesh (30–300 tris, LOD chain 300→60→12→impostor) + procedural
  locomotion derived from proportions (no hand-made animations) + behavior MLP (12–24 → 4–8).

## Two kingdoms (kept strictly separate in rules)

### Plants
Light (PAR), water, minerals, temperature window, space; photosynthesis =
insolation × leaf mass × water × temperature factor; biomass allocation (root/stem/leaf/
fruit) by genes+season; woodiness; seasonal cycle (flowering, leaf fall, dormancy);
reproduction by seeds (wind/animals/water), pollination, vegetative spread; phototropism,
thigmomorphogenesis, frost/drought/hail/fire damage, chemical defense.

### Animals
Body types walk/swim/fly, insect→megafauna; senses (sight, smell with wind-borne scent
trails, hearing, touch, thermo); needs: energy, hydration, thermal comfort, rest, safety,
social, reproduction, curiosity; utility AI + genome MLP; morphology-bound locomotion
(snow/depth/headwind cost); roles: herbivore, carnivore, omnivore, scavenger, detritivore,
filter-feeder.

## Ecology & evolution

Food web: sun → plants → herbivores → carnivores → carrion/detritus → soil → plants.
Predator-prey feedbacks, competition (light/water/grazing pressure), seasonal bottlenecks.

Reproduction with crossover+mutation; natural + sexual selection; reproductive isolation
(season, habitat, signals); speciation detected by genome clustering + interbreeding
failure; phylogenetic tree; metrics (trait trends per species per year, diversity index,
speciation/extinction counters). 100-year headless regressions must show adaptation and
no total collapse.

## Rendering & performance (Unity 6 + DOTS)

- ECS + Burst + Jobs; `DynamicBuffer<Gene>`, `BlobAssetReference` for static topology.
- GPU instancing (Entities Graphics), LOD chains, spatial hash per planet cell.
- Simulation LOD: distant/tiny → population-level aggregates; near → full individual loop.
- Budgets: sim tick ≤ 8 ms for 10k organisms at 10 Hz; 60 FPS with 20k+ instances; <2 GB RAM.
- Deterministic RNG (PCG/Xorshift, seeded) for genetics/development; snapshots for saves.

## Product surface

Orbit→region→follow camera; HUD (date, season, weather, time controls); overlays (temperature,
wind, pressure, humidity, biomes, population density, insolation, currents, fertility);
creature inspector (needs, readable genome, action + why, lineage); species browser +
evolution tree; event feed (storms, extinctions, speciations); god tools (seed life, nudge
climate, trigger storm/drought/winter, meteorite, terrain edit); saves (seed worlds,
snapshot rewind, share); photo mode; generative ambient audio.

## Tech stack & repo

Unity 6 (6000.x LTS), C#, Entities 1.3+, Jobs, Burst, Collections, Mathematics; URP +
custom flat-shading shaders; Entities Graphics; ScriptableObjects for config; JSON/binary
saves (GZip/LZ4); Unity Test Framework + Performance Testing; asmdefs:
`Core.Simulation`, `Core.ECS`, `Presentation`, `Authoring`, `Tests`.

## Stage sequence (each prompt = one reviewable stage)

1. **Foundation** — project, DOTS, time, RNG
2. **Planet & time**
3. **Climate**
4. **Genome & morphogenesis**
5. **Creature life** — needs/behavior
6. **Ecology & evolution**
7. **Product UX & optimization**

Each stage ends with: compiles, tests pass, acceptance criteria met, performance budget
respected, README updated, tagged commit.

## Non-goals (until post-v1)

Multiplayer, narrative campaign, combat-focused gameplay, full biochemistry/fluid dynamics,
real genetics of real species, photorealism, mobile/web ports, mod scripting language.
