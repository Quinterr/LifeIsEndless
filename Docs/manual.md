# Ecosphere — player manual (stage 07, v0.1.0-rc)

You are looking at a planet. Nothing here is scripted: every creature was born from a genome
someone evolved, every storm is the climate model moving heat and water around a sphere, and
every number the interface shows is read out of the running simulation.

This manual is written for a player who has never seen the game, and doubles as the acceptance
walkthrough: the three-minute script at the end touches every design pillar and every god tool.

---

## 1. First minute — finding a creature and understanding it

1. The game opens in **orbit**. Drag with the **right mouse button** to spin the planet, scroll
   to zoom. After a few seconds of stillness the camera drifts gently by itself.
2. **Click a creature.** The camera eases down to it and the **Creature** panel opens.
3. Read the top of the panel: it answers the only question the game considers mandatory —
   *what is it doing, and why?*
   - **Doing** — the current action (`Foraging`, `Fleeing`, `Basking`, …).
   - **Because** — the dominant reason (the highest-scoring need, with the utility value the
     behaviour system actually used to pick that action).
4. Below that: needs bars (energy, hydration, thermal comfort, rest, safety, social,
   reproduction, exploration), status chips (`Starving`, `Freezing`, …), the environment
   moving averages it has been living in, its lineage and generation, a readable genome
   (grouped, with above-average genes highlighted), and the phenotype summary the mesh is
   built from.

If you have 30 seconds, that is the whole game: watch, understand, care.

## 2. Controls

| Input | Action |
| --- | --- |
| Right mouse drag | Orbit the planet / look around |
| Left click | Select a cell or a creature |
| Scroll | Zoom (field of view widens as you pull back) |
| `W A S D`, `Q E` | Pan the focus / fly (photo mode) |
| `Tab` | Back to orbit and re-frame the planet |
| `Space` | Pause / resume |
| `[` `]` or `,` `.` | Time scale down / up (×0.5 … ×256) |
| Drag the date scrubber | Jump forward to a date (catch-up batches); drag left to rewind |
| `0` … `9`, `-` `=` | Overlay selection (0 = biomes), `-`/`=` cycle |
| `I B T F K G O` | Creature, Species, Evolution tree, Events, Stats, God tools, Saves |
| `Esc` | Close panel / open settings |
| `H` | Hide the interface |
| `M` | Mute |
| `P` | Photo mode |
| `F3` | Toggle the performance chip |
| `F5` / `F9` | Quick save / quick load (slot 1) |
| `Ctrl+Z` | Undo the last god action (rewind to the newest snapshot) |
| `F1` | Settings (language, quality, audio) |

## 3. Observation panels

- **Species** — cards with a population sparkline, biome range, diet, key trait means, diversity
  and a thriving/stable/declining/extinct verdict. Click a card to frame a living member.
- **Evolution tree** — the phylogeny DAG over the time axis: speciations and extinctions are
  colour-coded; click a node to jump to the species.
- **Events** — the world's story in one line each: weather (`Front · Tundra`), aggregated deaths
  (`213 deaths · starvation · Tundra`), evolution events (speciation, extinction, boom, die-off)
  and your own god actions. Filter by category; click `→` to fly to the place it happened.
- **Stats** — world KPIs, per-species trends, CSV export for spreadsheets.
- **Creature** — the inspector, with pinning (up to three creatures) and a follow camera.

## 4. Overlays

Twelve client-side views of simulation data, with a legend and a smooth cross-fade when you
switch: biomes, temperature, pressure, wind (with direction tinting), humidity, soil moisture,
snow, ocean currents, storminess (live weather events are marked red), insolation, fertility and
population density (summed from the per-cell species bins). Overlays are computed from the same
buffers the simulation writes; they never change it.

## 5. God tools

Open **God tools** (`G`) and pick a tool. Everything is logged in the Events feed and can be
undone with `Ctrl+Z` (rewind to the last snapshot).

- **Seed life** — place plants or animals in a region (up to 512 at once).
- **Climate nudge** — warm, cool or moisten a region for a few days. The climate model applies
  it; the tool never writes climate fields directly.
- **Summon weather** — attach a storm, blizzard, heat wave, cold wave, drought or fog to a region.
- **Meteorite (Catastrophe)** — impact heat spike, dust that dims the sun and cools the planet
  for days, and lethal blast damage in the radius.
- **Raise / Lower / Flood** — terrain edits: elevation changes, the biome classifier is re-run,
  and the planet meshes rebuild from the new geometry.
- **Wipe region** — kill everything in the radius (recorded with a `Cataclysm` cause of death).
- **Time jump** — fast-forward to a target date through the catch-up tick path.

Editing while paused is allowed: requests are applied on the next simulation tick, so the world
never mutates mid-frame.

## 6. Saves, rewind and sharing

- An **autosave ring** keeps the last 24 snapshots (one per game day by default). The Saves panel
  lists them; **Rewind** loads the newest snapshot at or before the moment you pick and replays
  the world forward to it.
- **Slots** (8) are manual saves with world name, date, species count and an orbit thumbnail.
- **Export** writes a single `.ecoworld` file you can copy to another machine; **Import** puts it
  in the first free slot. The header is plain JSON (seed, date, version, balance hash), so a
  world file can be inspected without decompressing the payload.
- Loading verifies a checksum: a truncated or hand-edited file is refused with a readable error
  instead of corrupting your session.

## 7. Audio

Everything you hear is generated at boot — wind, rain, ocean, storm rumbles, a day/night bed and
a dawn chorus — and mixed from the weather at the camera. Creature calls are synthesized from
phenotype traits, so big slow animals sound different from small fast ones. Volume sliders are in
Settings; `M` mutes.

## 8. Photo mode

`P` hands the camera to you: free flight (WASD, `Shift` fast), `G` for the rule-of-thirds grid,
sliders for field of view, exposure, saturation, depth of field and time of day, and a watermark
toggle. `Space` (or the Capture button) writes a PNG into `Pictures/Ecosphere`. The interface
hides itself; a semver + git-hash stamp is available if you want it in the shot.

## 9. Performance and quality

Quality presets (Low/Medium/High/Ultra) change planet subdivision, shadow distance, creature LOD
distance, overlay refresh rate, streamline count, cloud detail and the optional aurora. The
controller watches the frame budget and steps the tier down under load, and back up when
headroom returns; at the organism cap, distant creatures are aggregated instead of dropped.

The performance chip shows FPS / sim ms / rendered instances; the budget report (used in
`Docs/performance.md` capture runs) prints organisms, load time, memory and GC bytes per frame.

## 10. Builds

| Target | Backend | Notes |
| --- | --- | --- |
| Windows x64 | IL2CPP | Shipping configuration |
| macOS | IL2CPP | Universal binary, notarisation out of scope for the RC |
| Linux x64 | IL2CPP | Shipping configuration |
| Headless regression | Mono | Runs the stage-06 scenario suite in CI; Burst enabled, rendering off |

Build from the editor (`Ecosphere ▸ Build ▸ …`) or from the CLI:

```
Unity -quit -batchmode -projectPath . \
  -executeMethod Ecosphere.EditorTools.EcosphereBuildMenu.BuildLinux
```

Every build carries `0.1.0-rc+<git hash>` in the HUD corner and in crash reports.

## 11. CI

`Tools/github-workflows/ci.yml` runs (enable it per `Tools/github-workflows/README.md`):

1. the engine-free net8 scenario suite (`Tools/EvolutionCli --all`), plus a second seed;
2. Unity **EditMode** tests (product core: localization tables, overlay ramps, save names and
   headers, quality presets and budgets, the scrub planner, audio mix mapping, feed aggregation);
3. Unity **PlayMode** tests: boot and run, life cycle, the stage-06 scenarios, perf smoke, the
   save/load/rewind payload-hash determinism test and the crash-guard injected-fault proof;
4. desktop + headless builds, uploaded as artifacts.

`Tools/ci_local.sh` runs the same sequence locally (Unity path via `$UNITY`).

## 12. Three-minute scripted demo (acceptance walkthrough)

Record with screen capture; the stops below are the acceptance checklist.

**0:00 — orbit.** Hold still for the idle drift, then zoom in. *Pillar: observation as gameplay.*
**0:20 — time.** Press `]` a few times to ×32, watch a day pass, then pause. *Clock, seasons, weather.*
**0:35 — overlays.** Press `3` (wind), note the legend and the streamlines hint, press `1`
temperature, `8` storminess, then `0` back to biomes. *Twelve overlays with legend + blending.*
**0:55 — a creature.** Click one. Read the panel out loud: doing / because / needs / statuses.
Pin it, press **Follow**. *"Why is it doing that?" in under two minutes, unaided.*
**1:20 — species + tree + feed + stats.** Open each panel from the bottom bar, open the evolution
tree, click a node, then the Events panel and click `→` on a death cluster to fly there.
*Every pillar feature is reachable from the UI.*
**1:50 — god tools.** Press `G`, choose **Meteorite**, click a populated coast, apply. Watch the
dust dim the sky and the feed fill up. Press `Ctrl+Z` (or Saves ▸ Rewind) and watch the world
screen after the impact. *God tools are logged and undoable; rewind works after a catastrophe.*
**2:20 — photo mode.** Press `P`, fly close to the surface, enable the grid, scrub time of day to
dusk, capture. Open the saved PNG. *Clean export to the pictures folder.*
**2:40 — audio.** Press `M` twice (mute/unmute) then summon a storm and listen to the mix swell.
*Ambient audio audibly reacts to weather.*
**2:55 — build stamp + perf chip.** Show `0.1.0-rc+<hash>` and the FPS/sim-ms/instances chip,
then close on the orbit view.

## 13. Known gaps in this RC

Honest list, kept short and specific:

- The measured budget numbers in `Docs/performance.md` come from the reference machine runs; the
  sandbox that produced this build has no GPU/Unity, so the numbers there are targets plus the
  capture recipe, not measurements.
- Photo-mode depth of field is an approximation (near-plane/framing based) until the post stack
  lands; exposure/saturation sliders are wired to the same contract.
- The beauty pass ships an animated cloud shell, seasonal palette/light transitions and
  quality-gated aurora ribbons. **Wind sway on plants and snow sparkle** still need vertex-shader
  work on the organism/terrain materials and are not in this RC.
- Terrain LOD, an ocean shell and a selection ring are still on the stage-02 debt list; terrain
  chunks rebuild wholesale after a god terrain edit (a few ms, once per edit).
- Portrait rendering in the creature panel uses a colour swatch placeholder until the offscreen
  mesh renderer lands (the mesh portrait API, `OrganismMeshManager.EnsureMesh`, is already the
  intended hook).
- The CI determinism test proves the codec/checksum/ring half of save→load→replay. The full
  interactive "meteorite, rewind, replay" check is stop 5 of the scripted demo above; it needs a
  built player because it exercises the running clock.
