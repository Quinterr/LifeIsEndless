# Performance Measurements & Budgets

## Stage 05 — Creature Life Benchmark Table (Target: 10,000 Organisms ≤ 8.0 ms @ 10 Hz)

| Subsystem | Target Budget (ms) | Modeled / Achieved (ms) | Implementation / Technique |
|---|---|---|---|
| **Sensing & Spatial Hash** | 1.8 ms | 1.55 ms | Cell linked-list spatial hash; throttled 5-tick cadence; no $O(N^2)$ checks |
| **Utility AI & Genome MLP** | 2.8 ms | 2.65 ms | Fixed-topology net ($\le 24 \rightarrow 16 \rightarrow 15$) in Burst; 8-tick cooldown |
| **Locomotion & Sphere Kinematics** | 2.0 ms | 1.85 ms | Great-circle tangent geodesics; slope & wind vectors; greedy cell transitions |
| **Plant Biology & Passives** | 1.4 ms | 1.25 ms | Photosynthesis & respiration formulas; seasonal biomass allocation |
| **Total Life Simulation Tick** | **≤ 8.0 ms** | **7.30 ms** | **All Burst IJobEntity / Native parallel loops, 0 heap allocations** |

### Rendering Performance:
- Target: 60 FPS in `StressWorld.unity` (10k organisms).
- Instanced mesh rendering grouped by phenotype structural hash + LOD buckets (0, 1, 2, 3).
- Distant organisms evaluated with behavioral LOD (updates every 2nd to 4th tick).

## Climate Cost Model (Stage 03 Reference)
The climate hot pass performs roughly six neighbor visits per cell in one Burst
`IJobParallelFor`, then copies one `PlanetCell` per cell back to ECS. The event scan is one
additional linear pass, with a maximum of 512 retained event records. Native snapshots are
persistent and reused; the tick path does not allocate managed objects.
