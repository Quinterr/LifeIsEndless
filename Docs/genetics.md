# Ecosphere — Genetics & Morphogenesis (stage 04)

This document is the **contract** for the genome format, the developmental program,
and the procedural mesh pipeline. Later stages (05 life, 06 evolution) build on
exactly this. Every simplification taken in stage 04 is documented in
[Simplifications](#simplifications).

## Status and ownership

`PhenotypeUpdateSystem` (assembly `Ecosphere.Genetics`) is the **only** writer of
organism developmental state: `PhenotypeData`, `LifeStageData`, `OrganismSize`,
`OrganismDevAge`, `OrganismAge`, `EnvironmentEMAData`. It runs in
`SimulationSystemGroup` after `ClimateSystem` (stage 03), is tick-gated (advances
once per sim tick, not per frame), and reads climate only through
`IClimateSampler` at the organism's cell.

Mesh synthesis lives in `Ecosphere.Presentation` (`Presentation/Genetics`): the
descriptor builder is pure math (deterministic, Burst-friendly), and the
`MeshDataArray` upload happens once per pooled archetype, never per frame.

```
Genome (DynamicBuffer<Gene>) ──► GenomeMath.Compute ──► Phenotype (flat struct)
   GenomeHeader (kingdom/seed)        │  ▲                        │
   GeneCatalogData (baked SO)         │  └── EnvironmentEMAData ◄── IClimateSampler
                                       ▼
                              LifeStageData + OrganismSize + MeshDirty
                                       │ (stage or 10% size-bucket change)
                                       ▼
              OrganismMeshBuilder (LOD 0..3) ──► MeshDescriptor (hash)
                                       │
                                       ▼
              OrganismMeshPool (key = hash|bucket|kingdom|lod) ──► shared Mesh
```

## 1. Genome data model

### 1.1 Components

| Component | Type | Contents |
|---|---|---|
| `GenomeHeader` | `IComponentData` | `Version` (ushort, =1), `Kingdom` (`GeneKingdom`: Plant/Animal), `GenomeSeed` (uint — symmetry-breaking RNG seed), `ChromosomeCount` (byte=4), chromosome start offsets `Chr0Start..Chr7Start` |
| `GeneElement` | `IBufferElementData` | `Gene Value` |
| `Gene` (element) | struct (12 B) | `ushort TypeId; float Value (0..1); float Dominance; float MutationRate;` |
| `GeneCatalogData` | `IComponentData` | `BlobAssetReference<GeneCatalogBlob>` — baked from `GeneCatalogAsset` (ScriptableObject) by `GeneticsBootstrap` |

`DynamicBuffer<GeneElement>` holds the organism's genes. **Buffer layout is
compact per kingdom, in TypeId order:**

* **Animal (100 genes):** regulatory 0–4 · animal body plan 5–34 · metabolism 65–79 ·
  growth 80–89 · appearance 90–99 · behavior 100–119 · reproduction 120–129.
* **Plant (80 genes):** regulatory 0–4 · plant body plan 35–64 · metabolism 65–79 ·
  growth 80–89 · appearance 90–99 · reproduction 120–129.

Cross-kingdom genes are not duplicated: an animal genome never contains plant body
genes (and vice versa), and plants carry no behavior genes. Shared gene groups
(regulatory, metabolism, growth, appearance, reproduction) are the *same TypeIds*
in both kingdoms, so the common catalog entries are literally shared — the strict
separation required by the brief is at the body-plan and behavior level.

**Chromosomes** (identical layout in both kingdoms) for stage-06 crossover:

| Chromosome | Buffer start | Contents |
|---|---|---|
| 0 | 0 | regulatory (5) |
| 1 | 5 | body plan (30) |
| 2 | 35 | metabolism + growth (25) |
| 3 | 60 | appearance + behavior (animals) / appearance + reproduction (plants) |

Crossover = swap chromosome ranges between parents (1–2 points per chromosome);
point mutation = nudge one gene's `Value`; duplication/deletion and dominance
blending are stage-06 work (the `Dominance` field is already carried per gene).

### 1.2 Gene catalog

`GeneCatalog` (pure C# in `Core.Simulation`) is the registry: one
`GeneDefinition` per TypeId with **name, valid range [Min,Max], default, kingdom
applicability, phenotype field, environment-sensitivity (axis + polarity)**.
It is created from `GeneCatalog.Create()` (built-ins) and made extensible through
`GeneCatalogAsset` (ScriptableObject → baked `BlobAsset<GeneCatalogBlob>`);
`GeneticsBootstrap` installs the `GeneCatalogData` singleton, and
`PhenotypeUpdateSystem` falls back to the built-in catalog when absent (tests).

`Gene.Value` is always 0..1; systems map it to the gene's range via
`catalog.MapToRange(TypeId, Value)` = `Min + Value·(Max−Min)`.

**Versioning:** `GenomeHeader.Version` = 1. Bump on any breaking change to gene
ranges/meaning; `GeneCatalogAsset.CatalogVersion` mirrors it for authors.

### 1.3 Full gene catalog (TypeId, name, range, default, kingdom, group, env)

Regulatory (Both) — expression gates & master knobs:

| ID | Gene | Range | Def | Notes |
|---|---|---|---|---|
| 0 | ExpressionSensitivity | 0.5–2 | 0.5 | Scales epigenetic modulation strength |
| 1 | StageThresholdShift | −0.3–0.3 | 0.5 | Shifts every stage-gate onset (fraction of lifespan) |
| 2 | TemperatureResponseKnob | 0–2 | 0.5 | (reserved: scales temperature-driven responses; stage 05/06) |
| 3 | SizeMultiplier | 0.2–3 | 0.5 | Master body-scale scalar (applied by the mesh builder) |
| 4 | DevelopmentRate | 0.3–2 | 0.5 | Scales the ontogenetic clock (dev age advance per tick) |

Animal body plan (Animal):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 5 | Symmetry | 0–1 | 0 | 0=bilateral, 1=radial-style (wing/fin wings) | | 19 | EyeSize | 0–1 | 0.3 |
| 6 | TorsoSegments | 1–8 | 0.25 | | 20 | EyeForwardAngle | 0–1 | 0.3 |
| 7 | TorsoLength | 0.3–3 | 0.5 | | 21 | EarSize | 0–1 | 0.2 | *env: cold ↓* |
| 8 | TorsoGirth | 0.2–2 | 0.4 | | 22 | AntennaLength | 0–1.5 | 0 | *env: cold ↓* |
| 9 | LimbCount | 0–4 | 0.5 | limb pairs | | 23 | IntegumentType | 0–1 | 0 | *env: cold ↑* |
| 10 | LimbLength | 0.2–2 | 0.5 | *env: cold ↓* | | 24 | IntegumentDensity | 0–1 | 0.3 | *env: cold ↑* |
| 11 | LimbThickness | 0.05–0.5 | 0.3 | | 25 | BodyLengthScale | 0.3–3 | 0.5 |
| 12 | LimbJointness | 0–1 | 0.5 | | 26 | FinSize | 0–1.5 | 0 |
| 13 | NeckLength | 0–1.5 | 0.3 | | 27 | FinCount | 0–4 | 0 |
| 14 | HeadSize | 0.2–1.5 | 0.4 | | 28 | ShellThickness | 0–1 | 0 |
| 15 | JawType | 0–1 | 0.5 | 0 none / <0.4 beak / <0.6 mammal / else mandibles | | 29 | SpineLength | 0–1 | 0 |
| 16 | MouthSize | 0–1 | 0.3 | | 30 | HeadFlatten | 0–1 | 0 |
| 17 | TailLength | 0–2 | 0.4 | *env: cold ↓* | | 31 | TorsoFlatten | 0–1 | 0 |
| 18 | EyeCount | 0–8 | 0.25 | | 32 | LimbWebbing | 0–1 | 0 |
| | | | | | 33 | TongueLength | 0–1.5 | 0 |
| | | | | | 34 | ClawLength | 0–1 | 0.2 |

IntegumentType bands: `<0.16` skin · `<0.34` scales · `<0.5` feathers ·
`<0.66` fur · `<0.84` shell · else spines. (Scales are a color treatment — see
Simplifications.)

Plant body plan (Plant):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 35 | RootDepth | 0–3 | 0.4 | | 49 | FlowerPetalCount | 3–12 | 0.35 |
| 36 | RootSpread | 0–2 | 0.4 | | 50 | FruitSize | 0–1 | 0.2 |
| 37 | StemHeight | 0.2–5 | 0.4 | | 51 | FruitCount | 0–20 | 0.2 |
| 38 | StemThickness | 0.05–1 | 0.3 | *env: wind ↑ (thigmomorphogenesis)* | | 52 | SeedCount | 1–100 | 0.3 |
| 39 | Woodiness | 0–1 | 0.3 | | 53 | SeedSize | 0.01–1 | 0.3 |
| 40 | BranchingDepth | 0–5 | 0.4 | L-system recursion cap | | 54 | GrowthHabit | 0–1 | 0.5 |
| 41 | BranchingAngle | 10–80 | 0.45 | tilt of side branches (degrees) | | 55 | CrownShape | 0–1 | 0.3 |
| 42 | BranchLengthRatio | 0.3–0.9 | 0.6 | child/parent segment length | | 56 | BarkTexture | 0–1 | 0.3 |
| 43 | LeafSize | 0.1–2 | 0.5 | *env: low light ↑* | | 57 | Thorns | 0–0.5 | 0 |
| 44 | LeafCount | 1–8 | 0.5 | *env: low light ↑* | | 58 | VineCurl | 0–1 | 0 |
| 45 | LeafThickness | 0.02–0.3 | 0.4 | | 59 | NodeSpacing | 0.1–1 | 0.5 |
| 46 | LeafAngle | 0–90 | 0.4 | droop from the stem | | 60 | AerialRoots | 0–1 | 0 |
| 47 | FlowerSize | 0–1 | 0.3 | | 61 | TendrilLength | 0–1 | 0 |
| 48 | FlowerCount | 0–12 | 0.3 | | 62 | RootNodules | 0–1 | 0 |
| | | | | | 63 | BulbSize | 0–1 | 0 |
| | | | | | 64 | RosetteSpread | 0–2 | 0 |

Metabolism (Both — the shared common genes):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 65 | MetabolicRate | 0.1–3 | 0.5 | | 74 | TemperatureOptimum | −10–40 | 0.5 |
| 66 | Endothermy | 0–1 | 0 | | 75 | TemperatureTolerance | 5–40 | 0.4 |
| 67 | WaterNeed | 0.05–1 | 0.4 | | 76 | OsmoregulationCost | 0–1 | 0.2 |
| 68 | DietPhotosynthesis | 0–1 | 0.8 | diet vector (normalized to sum 1) | | 77 | DigestiveEfficiency | 0.3–1 | 0.5 |
| 69 | DietHerbivory | 0–1 | 0 | | 78 | StorageCapacity | 0.1–2 | 0.4 |
| 70 | DietCarnivory | 0–1 | 0 | | 79 | AnaerobicCapacity | 0–1 | 0.1 |
| 71 | DietScavenging | 0–1 | 0 | | | | | |
| 72 | DietFilterFeeding | 0–1 | 0 | | | | | |
| 73 | ToxinDefense | 0–1 | 0.1 | | | | | |

Growth & life history (Both):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 80 | GrowthCurveType | 0–1 | 0.25 | <0.25 logistic / <0.75 Gompertz / else linear | | 85 | SenescentOnset | 0.5–0.95 | 0.8 |
| 81 | GrowthRate | 0.1–2 | 0.5 | | 86 | MaturityAge | 0.05–0.5 | 0.3 |
| 82 | EmbryoDuration | 0.01–0.15 | 0.5 | fraction of lifespan | | 87 | Lifespan | 100–10000 | 0.3 | ticks |
| 83 | JuvenileDuration | 0.1–0.4 | 0.5 | | 88 | SeasonalGrowthGate | 0–1 | 0.3 |
| 84 | AdultDuration | 0.3–0.7 | 0.5 | | 89 | GrowthBurstAge | 0.05–0.4 | 0.25 |

Appearance (Both):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 90 | PigmentRed | 0–1 | 0.5 | primary pigment channels (ramps §4.1) | | 96 | SecondaryRed | 0–1 | 0.5 |
| 91 | PigmentGreen | 0–1 | 0.5 | | 97 | SecondaryGreen | 0–1 | 0.5 |
| 92 | PigmentBlue | 0–1 | 0.5 | | 98 | SecondaryBlue | 0–1 | 0.5 |
| 93 | PatternType | 0–1 | 0 | <0.18 solid / <0.5 stripes / <0.85 spots / else gradient | | 99 | Bioluminescence | 0–1 | 0 |
| 94 | PatternScale | 0.1–2 | 0.5 | | | | | |
| 95 | PatternSymmetry | 0–1 | 0.5 | >0.5 = mirror-symmetric about the body axis | | | | |

Behavior (Animal; **stage 05 consumes — not interpreted in stage 04**):

* 100–115 `BehaviorWeight00..15`: 16 floats = the small MLP weight blob
  (12–24 → 4–8 network in the brief; the input/output slicing is stage 05's job).
* 116 `InstinctAggression`, 117 `InstinctCuriosity`, 118 `InstinctFear`,
  119 `InstinctSociability` (0–1 instinct priors).

All 20 values are copied **verbatim** into the phenotype blob (no gating beyond
the behavior group's adult-onset stage gate, so embryos carry a zero blob).

Reproduction (Both):

| ID | Gene | Range | Def | | ID | Gene | Range | Def |
|---|---|---|---|---|---|---|---|---|
| 120 | ReproductionMode | 0–1 | 0.75 | 0 asexual / 1 sexual | | 125 | MutationRateMod | 0.01–0.2 | 0.5 | stage 06 |
| 121 | ClutchSize | 1–20 | 0.3 | | 126 | ReproFrequency | 0.1–4 | 0.25 |
| 122 | EggSeedSize | 0.05–1 | 0.3 | | 127 | GestationDuration | 0.01–0.3 | 0.3 |
| 123 | ParentalCare | 0–1 | 0.2 | | 128 | SexualDimorphism | 0–1 | 0.2 |
| 124 | BreedingSeason | 0–1 | 0 | 0 spring … 0.75 winter | | 129 | PheromoneStrength | 0–1 | 0.2 |

## 2. Phenotype struct

`Phenotype` (`Core.Simulation`, ~100 floats, blittable, ~420 B) is a **flat
struct** — one field per consumed gene plus derived values:

* header: `Size` (0..1 maturity), `SizeMultiplier` (regulatory gene 3);
* animal body plan block, plant body plan block, metabolism block, growth block,
  appearance block, behavior blob (20 floats), reproduction block — field names
  mirror the gene names.

It is written only by `PhenotypeUpdateSystem`. `GenomeMath.HashPhenotype` (FNV-1a
over values **quantized to 2 decimals**) is the pool/determinism key: tiny
environment-EMA drift does not create new archetypes, while distinct genomes do.

## 3. Development rules

### 3.1 The development function

`GenomeMath.Compute(catalog, input)` with
`input = {genes, kingdom, devAgeTicks, ema}` is **the** deterministic function:

```
1. regulatory   sensitivity = clamp(gene0, 0.25, 2), stageShift = gene1
                sizeMult = gene3, devRate = gene4
2. growth       lifespan, stage fractions, curve type/rate, burst age
                (clamped: embryo∈[0.02,0.15], juvenile∈[0.1,0.3], senescence∈[0.7,0.95])
3. stage        = DetermineStage(age/lifespan, fractions)
                  ∈ Embryo/Seed → Juvenile → Adult → Senescent
   progress     = StageProgress(...) in [0,1]
   ageFraction  = clamp(devAge/lifespan, 0, 1)
4. size         = GrowthSize(curve, rate, ageFraction, burstAge)   // §3.4
                  senescent: size *= 1 − 0.25·progress
5. per gene     mapped  = Min + Value·(Max−Min)
                  gate   = StageGate(group, ageFraction, stageShift)   // §3.2
                  envMod = sensitive ? ComputeEnvModulation(ema, axis,
                             polarity, optTemp, tolTemp, sensitivity) : 0  // §3.5
                  effective = mapped · gate · (1 + envMod)
                  → ApplyToPhenotype (one switch, one field per TypeId)
6. diet         normalized to sum 1
```

**Determinism contract:** phenotype = f(genome, devAge, env history). No
unseeded randomness anywhere in development. Identical inputs ⇒ byte-identical
`Phenotype` (proven by `DevelopmentTests.Determinism_*`) and identical mesh
vertex data (proven by `DevelopmentTests` + `MeshBuilderTests`).

### 3.2 Staged expression (stage gates)

Each gene group has an **onset** (fraction of lifespan; shifted by gene 1):

| Group | Onset | Meaning |
|---|---|---|
| Regulatory / Growth | 0 | always expressed |
| Metabolism | 0.05 | early |
| Plant body plan | 0.10 | from germination |
| Animal body plan | 0.15 | from mid-embryo |
| Appearance | 0.25 | juvenile coloration |
| Behavior | 0.60 | adult only |
| Reproduction | 0.65 | at maturity |

The gate ramps linearly from 0→1 over the next 10% of lifespan
(`StageGate = 0` before onset, `1` after onset+0.1). The clamps in step 2
guarantee the adult window always spans [≤0.45, ≥0.7], so the behavior and
reproduction onsets always land inside the Adult stage — the gates and the
gene-driven stage boundaries can never disagree.

### 3.3 Allometry (per-part growth exponents)

Part maturity at organism size `s`: `partScale = s^exponent`.

| Part | Animal exponent | Part | Plant exponent |
|---|---|---|---|
| Head | 1.4 | Stem | 1.0 |
| Sensors (eyes/ears) | 1.3 | Leaves | 1.5 |
| Cover | 1.1 | Roots | 1.2 |
| Torso | 1.0 | Branches | 0.7 |
| Neck | 0.9 | Flowers | 0.5 |
| Limbs | 0.7 | Fruit | 0.4 |
| Tail | 0.65 | | |

Head/leaves mature early, limbs/branches/flower/fruit lag — the "real organism"
growth asymmetry. The mesh builder multiplies every part's dimensions by its
`partScale`; the tests pin the ordering (`DevelopmentTests.Allometry_*`).

### 3.4 Growth curves

`GrowthSize` over normalized age t = age/lifespan (all normalized to size(1)=1):

* **logistic** (curve < 0.25): `σ(k(t−t0)) / σ(k(1−t0))`
* **Gompertz** (< 0.75): `G(t) / G(1)`, `G = exp(−exp(−k(t−t0)))`
* **linear** (else): `min(1, t · max(1, r))`

with `k = 4 + 8·GrowthRate`, `t0 = clamp(GrowthBurstAge, 0.01, 0.99)`.

**Seasonal growth gating** (gene 88): outside spring/summer the system slows
development — plants ×(1 − 0.65·gate) (dormancy), animals ×(1 − 0.25·gate)
(torpor). Deterministic (calendar + gene only).

### 3.5 Epigenetics-lite (environment-modulated expression)

Per-organism `EnvironmentEMA` (temp, light, wind, moisture) decays toward the
`IClimateSampler` sample at the organism's cell every tick
(`EMA' = EMA·decay + value·(1−decay)`, decays 0.995–0.998/tick). The viewer's
climate sliders feed the same EMA through `ManualEnvironment` (documented
debug-tool exception, §6).

For each environmentally sensitive gene:

```
raw  = axisFactor(EMA)                      // clamped to ±1
mod  = clamp(raw · polarity · sensitivity, −0.4, +0.4)     // HARD CAP ±40%
effective = mapped · gate · (1 + mod)
```

Axis factors:

| Axis | Factor (neutral point) |
|---|---|
| Temperature | clamp((optimum − envTemp) / max(1, tol/2), −1, 1) — positive when cold |
| Light | clamp((light − 0.5) · 2, −1, 1) — negative in shade |
| Wind | clamp((wind − 2) · 0.25, −1, 1) — positive in high wind |
| Moisture | clamp((moisture − 0.5) · 2, −1, 1) |

Sensitive genes (catalog flags; all others unmodulated):

| Gene | Axis | Polarity | Response |
|---|---|---|---|
| IntegumentType, IntegumentDensity | temp | +1 | cold → denser/more cover |
| LimbLength, TailLength, EarSize, AntennaLength | temp | −1 | cold → shorter extremities |
| LeafSize, LeafCount | light | −1 | low light → bigger/more leaves |
| StemThickness | wind | +1 | wind → thicker stems (thigmomorphogenesis) |
| TemperatureOptimum/Tolerance | temp | +1 | (self-describing; consumed by stage 05) |

**Cap: ±40% of the gene value, always** (`MorphogenMath.EpigeneticCap = 0.4`),
pinned by `DevelopmentTests.Epigenetics_ModulationCappedAt40Percent`.

### 3.6 Morphogen gradients (simple, honest)

Two gradients drive placement, both pure functions of geometry + genes:

* **Body-axis gradient** — `BodyAxisGradient(i, n) = i/(n−1)` along the torso
  chain / stem nodes;
* **Segment size gradient** — `SegmentSizeFactor(g) = 0.4 + 0.6·e^(−8(g−0.3)²)`:
  limbs attach largest slightly-anterior-of-center, smaller toward the ends
  (applied per limb pair).

No diffusion equations, no positional info fields — documented simplification.

### 3.7 L-systems (plants)

`LSystemExpander.Expand(...)` (pure, capped at 2000 commands, push/pop always
balanced) turns the plant body-plan genes into a command list:

* axiom: a straight main stem of `3 + 4·GrowthHabit` segments (segment length =
  StemHeight · NodeSpacing · size, radius taper 1 → 0.5);
* at interior nodes and while `depth < BranchingDepth`: push, `Branch` (tilt
  `BranchingAngle`° toward a deterministic yaw: 60°±20° left / 300°±20° right),
  recurse with `BranchLengthRatio` segment/radius scaling, pop;
* leaves: `LeafCount` (1–4) per node once size > 0.3, drooping by `LeafAngle`;
* flowers at terminal branches once size > 0.6 (up to 4 positions,
  `FlowerPetalCount` petals in the mesh); fruit at size > 0.8.

Jitter (leaf yaw ±15°, branch yaw ±20°) draws from
`RngState.Create(GenomeHeader.GenomeSeed, "Organism.Mesh")` — symmetry-breaking
is deterministic per genome, distinct between genomes.

## 4. Procedural mesh synthesis

### 4.1 Grammar

`OrganismMeshBuilder.Build(phenotype, kingdom, lod, genomeSeed)` →
`MeshDescriptor` (flat triangle list, **split normals per face** = flat shading,
per-vertex RGB colors). Primitive vocabulary: box (12t), n-prism (2n t),
n-cone (n t + cap), icosphere (20 t), octahedron (8 t), double-sided quad (4 t),
crossed quad (8 t). All closed solids orient their faces outward from the solid
center (no back-face culling holes).

**Animals** (facing +Z, ground y=0): torso prism chain (6 sides LOD0, posterior
taper, `TorsoFlatten` elliptical cross-section) → head icosphere (LOD1:
octahedron) with `HeadFlatten` → jaw per `JawType` (beak cone / mammal box /
mandible pair) → `EyeCount` eye cones placed by `EyeForwardAngle` → ears /
two-segment antennae (bend from `LimbJointness`) → `LimbCount` limb pairs
(upper+lower prism segments, knee bend from jointness, box or webbed fan foot)
placed by the segment-size gradient → two-segment drooping tail → `FinCount`
crossed-quad fins (raised to wings when `Symmetry > 0.6`) → cover details
(shell dome / spine cones / feather tufts / fur tufts; density-gated, cut first
when the budget runs out). **Embryo (size < 0.06): egg icosphere.**

**Plants**: interpreted L-system — prism stem segments along the local axis,
drooping crossed-quad leaves, octahedron flowers with petal quads, octahedron
fruit. **Seed (size < 0.05): seed ellipsoid.** Roots are a **separate
descriptor** (`BuildRoots`, downward prisms from `RootDepth`/`RootSpread`),
shown only in x-ray debug mode.

**Embryo/seed stages are distinct shapes** — the age slider visibly walks
egg → juvenile body → adult → shrinking senescent (and seed → sprout → tree).

### 4.2 Color: pigment ramps + patterns (vertex math, no textures)

* Primary color = the three pigment genes mixed through organism ramps
  (dark desaturated base → saturated tone per channel; neutral tan when all
  pigments ≈ 0 so nothing renders black).
* Secondary = same for the secondary pigments, pushed 18% brighter.
* Pattern evaluated **per vertex in mesh space** (y-up):
  * solid; stripes = horizontal Y bands (`sin` of y·scale); spots = hashed mesh
    cells (deterministic `Hash3` of floor-scaled coords, ~35% coverage, cell-
    hashed spot center); gradient = primary→secondary over mesh height.
  * `PatternSymmetry > 0.5` folds x (|x|) → mirror-symmetric patterns.
* Bioluminescence: lerp toward cyan-white (no emissive channel — documented).
* Part accents: head +12% brightness; eyes near-black or cyan when bioluminescent.

### 4.3 LODs, budgets, impostor

| LOD | Animal | Plant | Strategy |
|---|---|---|---|
| 0 | ≤ **300** | ≤ **400** | full grammar |
| 1 | ≤ **60** | ≤ **60** | 4-sided prisms, octahedron head, sparse leaves, no cover/sensors |
| 2 | ≤ **12** | ≤ **12** | one octahedron + a 4-tri silhouette accent |
| 3 | **2** | **2** | billboard quad |

* Budgets are **hard**: the builder carries a tri-count guard and cuts detail
  (cover → fins → sensors → segments), never the character parts (torso/head or
  stem). `MeshValidator` re-checks every descriptor (no NaN/inf, no degenerate
  tris < 1e-8 area, ≤ budget, ≥ 1 tri) — run over 120 random genomes × 4 LODs.
* **LOD3 impostor** = a billboard quad from the generated-mesh *snapshot*:
  dominant (area-weighted) color + bounds aspect of the LOD0 descriptor
  (simplification of "render-target capture" — documented).
* Beauty test: LOD2 reads as a silhouette at ≤ 12 tris (pinned by
  `MeshBuilderTests.SilhouetteBudget_LOD2ReadsAt30Tris`).

### 4.4 Growth rebuilds & pooling

* The simulation marks `MeshDirty` **only on stage transition or 10% size-bucket
  change** (bucket = round(size·10)) — never per tick. Between buckets the
  presentation scales the visual object by size (smooth growth, zero geometry
  cost).
* `OrganismMeshPool` (presentation, static) is a per-kingdom archetype cache
  keyed by `Key(phenotypeHash, sizeBucket, kingdom, lod)` (64-bit packed key).
  The quantized phenotype hash (§2) makes asexual clones and near-identical
  phenotypes **share one Mesh** — the instancing seam for stage 07.
* Meshes upload once via `Mesh.MeshDataArray` (Position/Normal F32, Color
  UNorm8), triangle-list indexed; `RecalculateBounds` once. Steady state is
  allocation-free (pinned by `OrganismPlayModeTests`).
* Pool resets on scene load (`RuntimeInitializeOnLoadMethod`) so no destroyed
  references survive reloads.

### 4.5 Locomotion params (derived, consumed by stage 05)

Derived from morphology, no animation assets:

* **stride length** ≈ `2 · LimbLength · bodyScale` (0 for fins → flap),
* **stride frequency** ∝ `MetabolicRate · (1 − 0.3·Endothermy-cost)` (stage 05
  scales),
* **limb phase offsets**: diagonal pairs antiphase for 4+ limbs
  (offset = 0.5 · (pairIndex mod 2) + 0.25 · (side mod 2));
* **body bob** amplitude ∝ `TorsoGirth`, frequency = stride frequency,
* **swim/fly flap** ∝ `FinSize`/`FinCount` (fins become wings at high Symmetry).

The parameters are documented here as the contract; the movement system
implements them in stage 05.

## 5. Determinism contract (summary)

1. Same world seed + spawn order ⇒ same genomes (`OrganismFactory.SpawnRandom`
   streams from `SimRandom.ForStream(worldSeed, "Organism.Spawn" ⊕ index)`).
2. Same genome + devAge + environment history ⇒ byte-identical `Phenotype`
   and `LifeStageData` (`GenomeMath.Compute` is pure; the system is the only
   writer and is tick-gated).
3. Same phenotype + size + `GenomeSeed` + LOD ⇒ byte-identical `MeshDescriptor`
   (all jitter from `RngState.Create(GenomeSeed, …)`; no unseeded RNG).
4. `PhenotypeUpdateSystem` never calls Unity's random; the project-wide
   `BannedRandomApiTests` scan still guards `Core.Simulation`.
5. Frame rate affects only how many ticks run per frame, never tick contents.

## 6. JSON genome export format

`GenomeJson.Export/Import` (pure C#):

```json
{
  "version": 1,
  "kingdom": 2,
  "genomeSeed": 4321,
  "genes": [
    { "id": 0, "v": 0.5, "d": 1.0, "m": 0.002 },
    { "id": 5, "v": 0.314, "d": 1.0, "m": 0.002 }
  ]
}
```

* `kingdom`: 1 = Plant, 2 = Animal (the `GeneKingdom` flag bits);
* `v` = `Gene.Value` (0..1, G7 float format), `d` = Dominance, `m` = MutationRate;
* genes in buffer order; **round-trips exactly** (pinned by
  `GenomeFactoryAndJsonTests.Json_ExportImport_RoundTripsExactly`, including a
  double round-trip). The viewer's "Export genome JSON" writes
  `StreamingAssets/organism_genome.json` and prints the path.

## 7. Viewer & debugging (`Assets/Scenes/OrganismViewer.unity`)

`OrganismViewerController` drives one scene that demonstrates every acceptance
criterion:

* **Specimen**: orbit camera (LMB rotate, RMB/MMB pan, wheel zoom) around one
  genome-grown organism at the origin (lab space; the planet is in the background
  via `PlanetBootstrap`).
* **Controls (IMGUI panel):** kingdom toggle, Randomize, Mutate ± (3 genes,
  deterministic per genome seed), Regenerate, **age slider** (pins the
  ontogenetic clock — watch egg → juvenile → adult), climate sliders (temp /
  light / wind / moisture) with Cold/Hot/Dry/Windy presets feeding
  `ManualEnvironment`, LOD 0–3, X-ray roots, **gallery 2×24** (24 random
  animals + 24 random plants, built in < 1 s — the "every body is different"
  proof, with the measured time shown in the panel), readable gene list
  (name = value → mapped range), per-LOD mesh stats + pool stats, and
  **Export genome JSON**.
* **Debug-tool exception (documented):** like the stage-07 god tools, the
  viewer writes organism debug state (genome, pinned age, manual environment)
  to the simulation world. Normal gameplay code never does.

## 8. Test map

| Acceptance | Test |
|---|---|
| Gene registry integrity (ranges, kingdom flags, groups, env axes) | `GenomeCatalogTests` |
| Genome determinism + mutation + JSON round-trip | `GenomeFactoryAndJsonTests` |
| Same genome+env ⇒ identical phenotype **and** mesh vertex hash | `DevelopmentTests.Determinism_*` |
| Stage gates (behavior/repro silent pre-adult; stage sequence) | `DevelopmentTests.StageGates_*` |
| Allometry (head > torso > limbs; leaves > branches > fruit) | `DevelopmentTests.Allometry_*` |
| Epigenetic ±40% cap + cold/light/wind response signs | `DevelopmentTests.Epigenetics_*` |
| Growth curves (monotonic, bounded, normalize to 1) | `DevelopmentTests.GrowthCurves_*` |
| L-system bounds/determinism/depth/flowering | `LSystemTests` |
| 120 random genomes × 4 LODs: no NaN, no degenerate tris, budgets | `MeshBuilderTests.RandomGenomes_AllLods_ValidateClean` |
| Egg/seed stages, growth visibility, x-ray roots, LOD2 silhouette, gallery diversity (≥20/24 unique) | `MeshBuilderTests` |
| Same key ⇒ same Mesh instance; distinct keys distinct; ClearAll | `MeshPoolTests` |
| 100 organisms / 5000 ticks / no exceptions / pool stability / steady-state ≈0 alloc | `OrganismPlayModeTests` |

## Simplifications

Every shortcut taken in stage 04, and what later stages should replace it with:

1. **LOD3 impostor** is a color+aspect billboard from the LOD0 descriptor
   snapshot, not a render-texture capture (RenderTexture capture added with
   stage 07's planet LOD rendering).
2. **Scales** (integument band 0.16–0.34) are a color treatment only — no scale
   geometry (budget); fur/feathers use sparse tufts, capped by the budget guard.
3. **TongueLength / ClawLength / BarkTexture / VineCurl / AerialRoots /
   TendrilLength / RootNodules / BulbSize / RosetteSpread / FlowerCount /
   SeedCount / FruitCount / PheromoneStrength / SexualDimorphism /
   TemperatureResponseKnob / OsmoregulationCost / DigestiveEfficiency /
   StorageCapacity / AnaerobicCapacity** are carried in genome + phenotype (the
   contract) but only partially or not at all shape the LOD0 mesh — stage 05/07
   consume them (needs, damage, rendering detail).
4. **L-System**: single straight axiom, fixed two-branch nodes, no tropism
   (stage 05 phototropism/gravitropism), roll and taper are no-ops, vine curl is
   expressed through branch tilt only, flowers/fruit only at terminal branches.
5. **Eyes/ears** are small cones (no pupils/irises — vertex-color budget).
6. **Radial symmetry** (`Symmetry` gene) manifests as wings/fin layout, not true
   n-fold radial bodies (the brief's "symmetry" gene is interpreted as
   bilateral↔winged continuum for low-poly readability).
7. **Sex**: no sex field in the genome yet (stage 06); `SexualDimorphism` is
   carried and will gate display traits when sex exists.
8. **Epigenetics** uses a 4-axis EMA only (no photoperiod, no humidity-driven
   cuticle); the ±40% cap is a fixed contract value.
9. **Seasonal gating** scales development speed (dormancy/torpor), not
   metabolism (stage 05).
10. **Organism placement on the planet**: stage 04 spawns take a cell index and
    sample climate there, but rendering on the planet surface is stage 07 —
    the viewer shows lab-space full meshes (per the brief: "LOD rendering on
    the planet" is out of scope).
11. **Behavior blob** is stored and adult-gated but not interpreted (stage 05).
12. **Mesh budget guard** cuts detail in a fixed order (cover → fins →
    sensors → segments); there is no per-part quality slider.
13. **Crossover** is not implemented (stage 06) — only the chromosome layout
    that makes it tractable.
