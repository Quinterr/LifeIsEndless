# Contributing to Ecosphere (LifeIsEndless)

Development is **stage-based**: each stage is one reviewable unit of work defined by its
prompt, ending with a tagged commit. Read `Docs/project-brief.md` (frozen vision) and
`Docs/architecture.md` (binding rules) before touching code.

## Stage workflow

1. Create/checkout the working branch for the stage (Arena sessions work on their own
   `arena/...` branch; humans may use `stage/NN-name`).
2. Implement only the stage's scope. If you discover work belonging to another stage,
   note it in the stage report instead of doing it.
3. Before merging a stage, ALL of these must hold:
   - Project compiles with **zero console errors and zero warnings** (asmdef warnings
     are treated as errors — fix, don't suppress).
   - All EditMode + PlayMode tests pass (`Window → General → Test Runner`, or
     `Unity -runTests`).
   - Performance budgets respected (see `PerformanceSmokeTests` and the brief).
   - `README.md` updated (stage status, new controls/flows).
   - Squash-friendly history; commit + tag: `stage-NN-name` (e.g. `stage-01-foundation`).

## Commit rules

- Conventional-ish prefixes: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `perf:`, `refactor:`.
- Imperative subject, ≤ 72 chars; body explains *why*, not *what*.
- Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, IDE files (see `.gitignore`).
- Never modify `Docs/project-brief.md` without a design review.

## Hard coding rules (enforced by tests where possible)

- **Fixed sim ticks only** — gameplay systems read `GameTime`, never `Time.deltaTime`.
- **SimRandom only** — `UnityEngine.Random` / `System.Random` are banned in simulation
  code. `BannedRandomApiTests` scans `Core.Simulation` sources and fails on violations;
  mentions in comments are allowed and are the convention for documenting the ban.
- **No per-tick allocations** — struct log entries, pooled/native containers, ECB reuse.
- **SoA data** — components and dynamic buffers, no per-entity managed objects.
- `Core.Simulation` must stay engine-free (`noEngineReferences: true`); if a Unity API
  is unavoidable, isolate it behind an interface in `Core.ECS`.
- Prefer `ISystem` + `SystemAPI` + Burst-compilable code in all Core systems.
- New systems join the schedule via `[UpdateInGroup]/[UpdateBefore]` attributes into the
  named groups from `Docs/architecture.md` — never reorder existing groups ad hoc.
