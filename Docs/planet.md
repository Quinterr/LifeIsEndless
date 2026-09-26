# Planet implementation (stage 02, partial)

Primal icosphere vertex cells: `10*4^n+2` cells, `20*4^n` triangles. Level 5 = 10,242 cells; level 6 = 40,962 (there is no subdivision with exactly 20k primal cells). Indices are assigned in fixed initial-vertex and face order; midpoint edges are deduplicated by sorted endpoint pair. Seed affects terrain, **not** topology. The persistent blob owns unit directions, faces, CSR neighbor offsets + sorted indices, and approximate barycentric face areas. `PlanetBootstrap` owns/disposes the blob; do not retain its reference after scene teardown.

`PlanetCell` is one ECS dynamic buffer element per cell. Terrain generation writes elevation, lat/lon, land, biome, and initial climate placeholders. `SunSystem` writes insolation each tick. Climate owns temperature, pressure, humidity, wind, cloud cover, precipitation, soil moisture, snow, ocean temperature/current, nutrient placeholder, and storminess after initial terrain setup; stage 05 Life owns biomass and detritus. Do not write these dynamic slots from terrain after generation. `ProxyBiomeClassifier` is the replaceable classifier seam.

Sun coordinates: Y is north, X is longitude zero. Noon is day fraction 0.5; vernal equinox at year phase .125, northern summer solstice at .375, winter solstice at .875. Declination amplitude 23.44 degrees. Each tick the light vector and per-cell normal dot sunlight are updated. `SunMath.Insolation` supplies the pure sampling calculation; the current buffer value is the cell's ground-level sample.

Orbit camera uses a radius of 1000 world units, double precision for orbit angles/distance, float transforms near the origin. Right-drag rotates, wheel zooms, WASD moves focus near the surface, left-click resolves a nearest vertex cell via ray/sphere intersection. No world-space floating origin is required at this radius.

## Climate integration (stage 03)

The climate step is in `Assets/Ecosphere/Planet/Runtime/ClimateSimulation.cs`; it consumes the `Insolation` field written by `SunSystem` and uses topology CSR neighbors. `ClimateSystem` is the sole post-generation writer for dynamic climate fields. Climate rules, fields, sample API and validation limitations are in [`climate.md`](climate.md).

## Known gaps

This remains a partial planet implementation: current rendering has a single mesh resolution (no surface-detail LOD/crossfade), no separate ocean or volumetric cloud shell, and no selection ring. Terrain is generated synchronously, not in jobs. No measured 20k-cell FPS, terrain generation time, or climate-step budget is available because Unity Editor is not installed in this environment. Do not treat the unmeasured performance targets as achieved.
