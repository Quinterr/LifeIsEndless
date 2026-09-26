# Climate model (stage 03)

## Status and ownership

`ClimateSystem` is the only writer of climate fields in `PlanetCell` after terrain setup;
`SunSystem` remains the only writer of `Insolation`. Terrain initializes the first values
before either system runs. Climate updates run after `SunSystem` in
`SimulationSystemGroup`. The simulation uses a Burst `IJobParallelFor` over the cell
buffer, reads an immutable previous-step snapshot, and writes a separate destination
snapshot. If the host advances several fixed ticks in one rendered frame, Climate replays each
climate-step tick (including that tick's stage-02 sun sample), so results do not depend on
render-frame batching. The main thread copies completed results into the ECS buffer and
performs each small event-feed pass. `ClimateConfig` can raise `ClimateTicksPerSimTick` above one; its
default is one, so every 10 Hz game tick is queryable without interpolation. When a larger
interval is selected, fields hold their last completed climate step between updates.

The job is deterministic: the world seed is mixed with the stable cell index and seasonal
phase for gust noise; it does not use Unity's global random state. Identical topology,
seed, time schedule, and climate settings produce identical climate values.

## Fields and units

| Field | Unit / range | Meaning |
|---|---|---|
| `Temperature` | °C-like, clamped -100..75 | Surface/near-surface air temperature. |
| `Pressure` | relative 0..1 | Smoothed pressure proxy; larger values are highs. |
| `Humidity`, `CloudCover` | fraction 0..1 | Column moisture and derived cloudiness. |
| `Wind` | tangent vector, m/s, max 32 | Local surface wind, tangent to the unit sphere. |
| `Precipitation` | mm per climate step | Condensed moisture; capped at 12 mm/step. |
| `SoilMoisture`, `SnowCover` | fraction 0..1 | Available soil water and fractional snow cover. |
| `OceanTemperature` | °C-like | Slowly responding surface ocean temperature, water cells only. |
| `OceanCurrent` | tangent vector, max 2.5 model units | Wind-driven + thermohaline surface flow, water only. |
| `Nutrient` | fraction 0..1 | Placeholder transported/renewed ocean nutrient value. |
| `Storminess` | hazard factor 0..1 | Smoothed pressure-gradient + moisture/cloud hazard score. |
| `*Trend` | change per climate step | Temperature, humidity, and soil-moisture direction for HUD/AI. |
| `TemperatureInsolationTerm` | °C-like contribution | Latest local solar/day-night forcing after snow albedo. |
| `TemperatureAdvectionTerm` | °C-like contribution | Latest upstream temperature contribution. |
| `EvaporationTerm` | humidity fraction per step | Latest surface evaporation source. |
| `OrographicLiftTerm` | fraction 0..1 | Windward slope uplift used to enhance rain. |

The static cell elevation is normalized terrain height. The model uses a simplified lapse
rate of 24 °C per unit positive elevation (terrain scale is not a physical kilometer).
Altitude samples use 6.5 °C/km for fliers.

## Update order and one-sentence rules

1. **Sunlight and temperature:** use stage-02 `SunMath`/`PlanetCell.Insolation` as the solar source; combine a fixed equator-to-pole baseline with daily and seasonal insolation forcing, subtract snow-albedo and elevation cooling, and apply a slower ocean response. Land therefore warms/cools faster than water.
2. **Air pressure:** map warm columns to lower pressure, cold columns to higher pressure, add a latitude-banded circulation bias, then blend 28% of the neighboring mean as one diffusion pass.
3. **Wind:** move down the local pressure gradient, add easterly trades (0–30°), westerlies (30–60°), and polar easterlies, apply a latitude-signed Coriolis turn, deterministic small gusts, then clamp to 32 m/s.
4. **Temperature advection:** blend a small upwind-weighted neighbor temperature difference into the local relaxation term.
5. **Moisture transport:** evaporate from warm ocean or wet land, then transfer humidity across each shared cell edge using antisymmetric signed flux based on the mean edge wind; absent sources/sinks, each edge adds equal and opposite amounts to its two endpoints.
6. **Clouds and rain:** derive cloud cover from humidity and low pressure; condense above a temperature-sensitive humidity threshold and increase precipitation on windward rising terrain.
7. **Snow and soil:** precipitation below 0 °C adds snow, positive temperatures melt it, and land soil water receives rain/melt and loses evaporation plus a small elevation-sensitive drainage term.
8. **Ocean:** drive tangent currents with wind and a weak temperature-difference conveyor, then slowly relax surface-water temperature toward local air and neighboring ocean values.
9. **Hazards and events:** smooth storminess from pressure-gradient/moisture/cloud terms, track persistent dry and temperature-anomaly ticks, and append at most one strongest eligible local event each climate step.

## ECS and query API

`IClimateSampler` is the read-only consumer contract. `ClimateSampler.Sample(cellIndex)`
returns a value-type `ClimateSample` with all live fields, derived wind strength, effective
temperature, causal debug terms, and submergence. `SampleAt(direction, altitude)` resolves
the nearest topology cell and applies the flight lapse rate; `altitude` is meters above the
surface. `WindAt`, `EffectiveTemperature`,
`IsSubmerged`, and `GetWeatherEvents` are convenience accessors. Buffer views are frame-local;
consumers must not cache them across structural changes. The climate overlays and selected-cell
weather station use this sampler, demonstrating that presentation does not own climate writes.

Creature modifier meanings: `EffectiveTemperature` is air temperature after wind chill and
humid-heat adjustment; `Wind` is the motion/scent/seed vector and magnitude in m/s; `Storminess`
is a 0..1 local hazard factor (not direct damage); `Precipitation` is current-step water input;
`SnowCover` and `SoilMoisture` are normalized surface fractions; `Pressure` is relative and its
local gradient, not its absolute number, predicts wind/storm changes. No creature code is added
in this stage.

`WeatherEvent` stores type, cell, radius, strength, and start/end ticks. `WeatherEventLog` is
bounded to the newest 512 `{tick,type,cell,strength}` records for later event-feed consumers.
Implemented detectors are storm, blizzard, fog, drought, heat/cold anomaly, and a strong
neighbor temperature boundary classified as a front. Storm/blizzard/front/fog markers move to
the downwind neighbor every 12 ticks; fronts are a local moving-boundary proxy, not a
persistent air-mass solver.

## Presentation

Keys **1–8** select temperature, pressure bands, wind direction/speed, humidity/rain, soil water,
snow, ocean-current speed, and storminess/event markers; **0** restores biome colors. Clicking
a cell opens the weather-station panel with live values, causal terms, local event and one-step
trend arrows. Overlay colors are written to the existing chunk mesh and are presentation-only.
The wind overlay encodes direction and speed in color; it does not yet draw animated arrows.
Cloud cover is visible in the humidity/rain layer; a separate volumetric cloud shell is not yet
implemented.

## Performance and validation

The expensive pass is O(cells × mean degree) (about six neighbors per cell) and uses a Burst job
with double-buffered `NativeArray<PlanetCell>` snapshots; the event scan is O(cells) and retains
at most 512 feed records. The main-thread copy is O(cells). There are no per-tick managed
allocations in `ClimateSystem`; persistent arrays are created only when the planet buffer is
first seen or resized. Climate update milliseconds at 20k cells have **not been measured** in
this workspace because no Unity Editor/runtime is installed. The ≤4 ms target is therefore not
claimed. The old `Docs/performance.md` also records stage-02 budgets as unmeasured.

EditMode tests cover pressure/wind and hemispheric Coriolis sign, antisymmetric edge humidity
flux, snow accumulation/melt, windward uplift, wind-driven ocean current, event thresholds, and
effective temperature. The seed-42, 60-day deterministic CSV regression is included but was not
executed here; the Unity-side regression and performance capture remain follow-ups before tagging
the stage complete. See `Docs/performance.md`.

## Known simplifications

This is a low-cost causal toy model, not a GCM: pressure diffusion is one neighbor blend; general
circulation is a zonal latitude template; Coriolis and radiative balance are heuristic; edge
fluxes assume equal cell areas; cloud microphysics and water reservoirs are simple; coastline
current deflection, salinity, nutrients, hail, lightning, and a rendered cloud shell are omitted.
No long climate history ring is stored; the current one-step trend fields are for near-term
behavior only.
