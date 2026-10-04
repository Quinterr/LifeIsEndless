# GitHub Actions pipeline

`ci.yml` is the Ecosphere CI pipeline. It is kept here (rather than in `.github/workflows/`)
because the automation account used for this repository cannot push files under
`.github/workflows/` without the GitHub App's `workflows` permission.

## Enabling it

Either:

1. **Grant the app the `workflows` permission** (repository Settings → GitHub Apps), then move
   the file back:
   ```bash
   mkdir -p .github/workflows && git mv Tools/github-workflows/ci.yml .github/workflows/ci.yml
   ```
2. Or **paste it in the browser**: create `.github/workflows/ci.yml` on GitHub and copy the
   contents of this file. Required secrets for the Unity jobs: `UNITY_LICENSE`, `UNITY_EMAIL`,
   `UNITY_PASSWORD`. The `core` job runs without any secret.

## What it runs

1. `core` — the engine-free net8 scenario suite (`Tools/EvolutionCli --all`) plus a second seed.
2. `editmode` — Unity EditMode tests (product core: localization, overlays, save names/headers,
   quality tiers + budgets, scrub planner, audio mix, feed aggregation).
3. `playmode` — boot/run, life cycle, stage-06 scenario suite, perf smoke, and the stage-07
   save/load/rewind payload-hash determinism + crash-guard injected-fault tests.
4. `build` — Linux/Windows/macOS IL2CPP players and the headless regression build as artifacts.

The same sequence runs locally via `Tools/ci_local.sh` (set `UNITY` to a Unity binary).
