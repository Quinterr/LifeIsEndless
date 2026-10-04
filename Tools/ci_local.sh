#!/usr/bin/env bash
# Ecosphere — local CI (same sequence as .github/workflows/ci.yml).
#
# Usage:  UNITY=/path/to/Unity Tools/ci_local.sh [--skip-unity] [--skip-build]
#
# Stage 07 gate order is deliberate: the engine-free scenario suite is seconds and catches
# simulation regressions before the expensive Unity steps run.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SKIP_UNITY=0
SKIP_BUILD=0
for arg in "$@"; do
  case "$arg" in
    --skip-unity) SKIP_UNITY=1 ;;
    --skip-build) SKIP_BUILD=1 ;;
  esac
done

export ECOSPHERE_GIT_HASH="${ECOSPHERE_GIT_HASH:-$(git rev-parse --short HEAD 2>/dev/null || echo nogit)}"
ARTIFACTS="$ROOT/artifacts"
mkdir -p "$ARTIFACTS"

echo "== 1/5 core scenarios (net8) =="
dotnet run --project Tools/EvolutionCli -c Release -- --all --out "$ARTIFACTS/evolution"

echo "== 2/5 determinism seed sweep =="
dotnet run --project Tools/EvolutionCli -c Release -- --all --seed 7 --out "$ARTIFACTS/evolution-seed7"

if [[ "$SKIP_UNITY" == "1" ]]; then
  echo "== 3/5..5/5 skipped (--skip-unity) =="
  exit 0
fi

UNITY="${UNITY:-}"
if [[ -z "$UNITY" || ! -x "$UNITY" ]]; then
  echo "UNITY is not set to an executable Unity binary; skipping Unity steps." >&2
  exit 0
fi

UNITY_ARGS=(-batchmode -quit -projectPath "$ROOT" -logFile "$ARTIFACTS/unity.log")

echo "== 3/5 EditMode tests =="
"$UNITY" "${UNITY_ARGS[@]}" -runTests -testPlatform EditMode \
  -testResults "$ARTIFACTS/editmode-results.xml" || { tail -80 "$ARTIFACTS/unity.log"; exit 1; }

echo "== 4/5 PlayMode tests (boot, scenarios, perf smoke, determinism, crash guard) =="
"$UNITY" "${UNITY_ARGS[@]}" -runTests -testPlatform PlayMode \
  -testResults "$ARTIFACTS/playmode-results.xml" || { tail -80 "$ARTIFACTS/unity.log"; exit 1; }

if [[ "$SKIP_BUILD" == "1" ]]; then
  echo "== 5/5 build skipped (--skip-build) =="
  exit 0
fi

echo "== 5/5 headless regression build =="
"$UNITY" "${UNITY_ARGS[@]}" -executeMethod Ecosphere.EditorTools.EcosphereBuildMenu.BuildHeadless

echo "CI OK — artifacts in $ARTIFACTS"
