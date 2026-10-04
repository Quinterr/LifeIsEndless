# Stage 05 — Creature Life & Ecosystem Dynamics

## 1. Overview & Objective
Stage 05 breathes life into the ecosphere:
- **Organisms live on sim ticks (10 Hz)**: Hungry animals seek vegetation or prey, thirsty animals seek fresh water, tired organisms rest/sleep, ectotherms bask in the sun, and animals take shelter during storms and blizzards.
- **Plants photosynthesize and grow**: Insulated by daylight, cloud cover, and seasonal warmth, plants accumulate biomass, allocate resources to roots, stems, leaves, and fruits, drop leaves during winter dormancy, and sustain damage from frost, drought, and hail.
- **Closed Ecosystem Loop**:
  $$\text{Sun} \longrightarrow \text{Plants} \longrightarrow \text{Herbivores} \longrightarrow \text{Predators} \longrightarrow \text{Detritus} \longrightarrow \text{Soil Fertility} \longrightarrow \text{Plants}$$
- **Zero GC Allocations**: All simulation systems utilize Burst-compatible structs, NativeArrays, and IJobEntity/IJobParallelFor patterns.

---

## 2. Needs Model (`NeedsData`)
Both plants and animals use the inverted urgency convention:
- **1.0 = Fully satisfied** (no urgency)
- **0.0 = Completely depleted** (critical emergency / lethal risk)
- **Urgency** $= 1.0 - \text{Value}$

### Animal Needs:
1. **Energy**: Depleted by basal metabolic rate $\times$ temperature thermoregulation curve $\times$ activity/locomotion $\times$ size. Restored by grazing, foraging, hunting, or scavenging.
2. **Hydration**: Depleted by basal water need, locomotion, and heat evaporation. Restored by drinking fresh water or eating juicy plants.
3. **ThermalComfort**: Computed from environmental effective temperature relative to the genome's `TemperatureOptimum` and `TemperatureTolerance`.
4. **Rest**: Depleted by locomotion and continuous activity. Restored by `Rest` and `Sleep` actions.
5. **Safety**: Depleted by nearby predators or violent storms ($>0.6$). Restored over time when unthreatened.
6. **Social**: Urgency for interacting with conspecifics.
7. **Reproduction**: Mating urge that accumulates in adult organisms.
8. **Exploration**: Curiosity and restlessness.

### Plant Passives (mapped onto same struct):
- `Energy` $\rightarrow$ **Light** (Insolation modulated by cloud cover)
- `Hydration` $\rightarrow$ **Water** (Soil moisture and precipitation)
- `ThermalComfort` $\rightarrow$ **Temperature** (Optimum curve vs effective temp)
- `Rest` $\rightarrow$ **Nutrients** (Local soil fertility)

### Status Effects:
Crossed critical thresholds enable status effect components:
- `Starving` ($\text{Energy} \le 0.15$)
- `Dehydrated` ($\text{Hydration} \le 0.15$)
- `Exhausted` ($\text{Rest} \le 0.15$)
- `Freezing` ($T_{\text{eff}} < T_{\text{opt}} - T_{\text{tol}}$)
- `Overheating` ($T_{\text{eff}} > T_{\text{opt}} + T_{\text{tol}}$)
- `Panicked` ($\text{Safety} \le 0.25$)
- `PlantDormant` (Winter season or low temperature)

---

## 3. Utility AI & Behavior MLP
Decisions are re-evaluated every 8 ticks or immediately upon interrupt (threats, blizzards, starving).

$$\text{Score}(\text{action}) = \sum (\text{NeedUrgency} \times \text{Weight}) \times \text{ActionEfficacy} \times \text{Feasibility} \times \text{MLP Multiplier}$$

### Action Catalog:
- `Wander`: Baseline exploratory roaming.
- `Forage`: Searching for seeds/fallen forage on vegetation pools.
- `Graze`: Herbivorous feeding on live biomass.
- `Hunt`: Carnivorous attack roll against nearby prey.
- `Scavenge`: Detritivore/carnivore consumption of carcasses.
- `Drink`: Rehydrating at water cells.
- `Rest`: Halting movement to recover stamina.
- `Sleep`: Deep rest at night or severe exhaustion.
- `Flee`: Great-circle escape from predators and extreme hazards.
- `Explore`: Traveling toward novel or curiosity-driven regions.
- `Migrate`: Long-range seasonal comfort gradient seeking.
- `Socialize`: Grouping with nearby herd members.
- `SeekMate`: Stage 06 courtship stub.
- `Bask`: Ectotherm sunbathing to raise body temperature.
- `TakeShelter`: Seeking calm neighboring cells during severe weather.

### Fixed-Topology Genome MLP:
Burst-evaluated fixed net ($\le 24 \rightarrow 16 \rightarrow 15$):
- Inputs: 8 needs, 6 sensory signals, 4 environment/time factors, 4 instincts.
- Hidden Layer: 16 units parameterized by genome `BehaviorWeight00`–`BehaviorWeight15` with `tanh` activation.
- Outputs: Per-action bias in $[-1, 1]$ mapped to scoring multipliers.

---

## 4. Locomotion on the Sphere (`LocomotionMath`)
- Movement occurs in tangent space along great-circle geodesics:
  $$\vec{v}_{\text{tangent}} = \text{normalize}\left(\vec{p}_{\text{target}} - \vec{p}_{\text{current}} - \hat{n}(\hat{n} \cdot (\vec{p}_{\text{target}} - \vec{p}_{\text{current}}))\right)$$
- Speeds derived from body plan:
  - Stride length $\approx 2 \times \text{LimbLength} \times \text{BodyScale}$
  - Stride frequency $\propto \text{MetabolicRate} \times (1 - 0.3 \times \text{EndothermyCost})$
- Environmental modifiers:
  - Uphill slopes penalize speed; downhill gives mild boost.
  - Tailwinds assist fliers; headwinds penalize ground movers.
  - Snow cover adds surface friction ($0.7\times$).
  - Fatigue ($\text{Rest}$) scales velocity down to $25\%$.
- Obstacle avoidance: Impassable cliffs ($\Delta \text{elevation} > 0.45$) cause tangent deflection.

---

## 5. Plant Life Model
- **Photosynthesis**:
  $$\text{Gross} = I_{\text{sun}} \times (1 - 0.7 C_{\text{cloud}}) \times M_{\text{leaf}} \times W_{\text{soil}} \times e^{-\frac{(T_{\text{eff}} - T_{\text{opt}})^2}{2 T_{\text{tol}}^2}} \times 0.02$$
- **Respiration**:
  $$\text{Cost} = \text{MetabolicRate} \times 0.005 \times (1 + (T_{\text{eff}} - 20) \times 0.03)$$
- **Biomass Allocation**:
  - Spring: Leaves $\times 2.0$, Stems $\times 1.3$
  - Summer: Fruit $\times 1.5$, Leaves $\times 1.2$
  - Autumn: Roots $\times 2.0$, Fruit/Seeds $\times 2.5$
  - Winter: Roots $\times 1.5$, Leaves $\times 0.2$
- **Damage & Dormancy**:
  - Frost and drought damage subtracted from accumulated biomass.
  - Winter triggers `PlantDormant`, dropping respiration near zero.

---

## 6. Scent & Spatial Hash
- **Spatial Hash**: Multi-entry cell mapping allows $O(1)$ lookup for nearby predators, prey, and resources without $O(N^2)$ neighbor scans.
- **Scent Advection**: Per-cell dynamic buffer `CellScent` holds food, water, mate, and threat scents, which decay each tick and advect downwind according to the climate wind field.

---

## 7. Stage 06 Reproduction Handoff
Stage 05 prepares the foundational contracts for Stage 06 (Ecology & Evolution):
1. `CellSeedBank` buffer accumulates seeds with wind/current dispersal offsets.
2. `Reproduction` need urgency tracks readiness to mate.
3. `SeekMate` utility action identifies compatible conspecific partners.
4. Stage 06 will consume these buffers to implement crossover, mutation, mate selection, and speciation.
