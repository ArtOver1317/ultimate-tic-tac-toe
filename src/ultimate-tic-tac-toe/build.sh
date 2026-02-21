#!/usr/bin/env bash
set -euo pipefail

UNITY_PATH="${UNITY_PATH:-}"
BUNDLE_VERSION="${BUNDLE_VERSION:-}"
BUILD_TARGET="${BUILD_TARGET:-All}"
BUILD_PATH="${BUILD_PATH:-Builds}"
SKIP_TESTS="${SKIP_TESTS:-0}"

if [[ -z "${UNITY_PATH}" ]]; then
  echo "[build.sh] ERROR: UNITY_PATH is required"
  exit 1
fi

if [[ -z "${BUNDLE_VERSION}" ]]; then
  echo "[build.sh] ERROR: BUNDLE_VERSION is required"
  exit 1
fi

if [[ ! -x "${UNITY_PATH}" ]]; then
  echo "[build.sh] ERROR: UNITY_PATH is not executable: ${UNITY_PATH}"
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "[build.sh] ERROR: python3 is required"
  exit 1
fi

if [[ ! -f "ProjectSettings/ProjectSettings.asset" ]]; then
  echo "[build.sh] ERROR: ProjectSettings/ProjectSettings.asset not found"
  exit 1
fi

if [[ "${SKIP_TESTS}" == "1" ]]; then
  current_allow="${ALLOW_SKIP_TESTS:-}"
  if [[ -n "${current_allow}" && "${current_allow,,}" != "true" ]]; then
    echo "[build.sh] ERROR: SKIP_TESTS=1 requires ALLOW_SKIP_TESTS=true"
    exit 1
  fi

  export ALLOW_SKIP_TESTS=true
fi

if [[ -n "${UNITY_LICENSE:-}" ]]; then
  license_file="/tmp/UnityLicenseFile.ulf"
  printf '%s' "${UNITY_LICENSE}" > "${license_file}"
  "${UNITY_PATH}" -batchmode -nographics -quit -manualLicenseFile "${license_file}" || true
fi

original_bundle_version="$(python3 - <<'PY'
import re
from pathlib import Path

path = Path('ProjectSettings/ProjectSettings.asset')
text = path.read_text(encoding='utf-8')
match = re.search(r'^\s*bundleVersion:\s*(.*)$', text, flags=re.MULTILINE)
if not match:
    raise SystemExit(2)
print(match.group(1).strip())
PY
)"

restore_bundle_version() {
  python3 - <<'PY'
import os
import re
from pathlib import Path

path = Path('ProjectSettings/ProjectSettings.asset')
text = path.read_text(encoding='utf-8')
original = os.environ['ORIGINAL_BUNDLE_VERSION']
new_text, count = re.subn(r'^(\s*bundleVersion:\s*).*$','\\g<1>'+original,text,flags=re.MULTILINE)
if count != 1:
    raise SystemExit(2)
path.write_text(new_text, encoding='utf-8')
PY
}

capture_tracked_state() {
  {
    git diff --name-only
    git diff --cached --name-only
  } | sed '/^$/d' | sort -u
}

assert_repo_not_worse_after_restore() {
  if [[ "${GIT_TRACKED_BASELINE_CAPTURED:-0}" -ne 1 ]]; then
    return 0
  fi

  if ! command -v git >/dev/null 2>&1; then
    return 0
  fi

  if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    return 0
  fi

  local current_tracked_state
  current_tracked_state="$(capture_tracked_state)"

  if [[ "${current_tracked_state}" != "${GIT_TRACKED_BASELINE}" ]]; then
    echo "[build.sh] ERROR: tracked repository state changed compared to baseline" >&2
    echo "[build.sh] Baseline tracked changes:" >&2
    if [[ -n "${GIT_TRACKED_BASELINE}" ]]; then
      printf '%s\n' "${GIT_TRACKED_BASELINE}" >&2
    else
      echo "<no tracked changes>" >&2
    fi
    echo "[build.sh] Current tracked changes:" >&2
    if [[ -n "${current_tracked_state}" ]]; then
      printf '%s\n' "${current_tracked_state}" >&2
    else
      echo "<no tracked changes>" >&2
    fi
    return 1
  fi

  return 0
}

on_exit() {
  local exit_code=$?

  if ! restore_bundle_version; then
    echo "[build.sh] ERROR: failed to restore bundleVersion" >&2
    exit 1
  fi

  if ! assert_repo_not_worse_after_restore; then
    if [[ $exit_code -eq 0 ]]; then
      exit 1
    fi

    echo "[build.sh] WARN: repository state changed, but build already failed with exit code $exit_code" >&2
  fi

  exit $exit_code
}

export ORIGINAL_BUNDLE_VERSION="${original_bundle_version}"

GIT_TRACKED_BASELINE=""
GIT_TRACKED_BASELINE_CAPTURED=0
if command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  GIT_TRACKED_BASELINE="$(capture_tracked_state)"
  GIT_TRACKED_BASELINE_CAPTURED=1
fi

trap on_exit EXIT

python3 - <<'PY'
import os
import re
from pathlib import Path

path = Path('ProjectSettings/ProjectSettings.asset')
text = path.read_text(encoding='utf-8')
version = os.environ['BUNDLE_VERSION']
new_text, count = re.subn(r'^(\s*bundleVersion:\s*).*$', '\\g<1>'+version, text, flags=re.MULTILINE)
if count != 1:
    raise SystemExit(2)
path.write_text(new_text, encoding='utf-8')
PY

execute_method="BuildScript.BuildAll"
case "${BUILD_TARGET}" in
  All) execute_method="BuildScript.BuildAll" ;;
  Desktop) execute_method="BuildScript.BuildDesktop" ;;
  WebGL) execute_method="BuildScript.BuildWebGL" ;;
  AddressablesOnly) execute_method="BuildScript.BuildAddressablesOnly" ;;
  *)
    echo "[build.sh] ERROR: unsupported BUILD_TARGET='${BUILD_TARGET}'. Expected All|Desktop|WebGL|AddressablesOnly"
    exit 1
    ;;
esac

skip_tests_arg=()
if [[ "${SKIP_TESTS}" == "1" ]]; then
  skip_tests_arg+=("-skipTests")
fi

"${UNITY_PATH}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$(pwd)" \
  -executeMethod "${execute_method}" \
  -buildPath "${BUILD_PATH}" \
  "${skip_tests_arg[@]}"
