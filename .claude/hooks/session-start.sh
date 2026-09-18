#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# A fresh container has neither the .NET SDK nor the generated files that are
# deliberately gitignored, so without this the repository builds in CI but not
# locally. Everything here is idempotent: the container image is cached after
# the hook completes, so a warm start re-runs it cheaply.
set -euo pipefail

# Local machines are expected to have their own toolchain; only the remote
# containers start empty.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

log() { printf '[session-start] %s\n' "$1"; }

# --- .NET SDK -------------------------------------------------------------
# global.json pins 8.0 with rollForward: latestFeature, matching the net8.0
# target of EchelonFoundry.Aegis.Core.
if command -v dotnet >/dev/null 2>&1; then
  log "dotnet $(dotnet --version) already present"
else
  log "installing .NET SDK 8"
  export DEBIAN_FRONTEND=noninteractive
  # The image ships a stale package index whose .deb versions have already been
  # superseded on the mirror, so installing without updating first fails with
  # 404s on every dotnet package.
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0
  log "installed dotnet $(dotnet --version)"
fi

# --- npm ------------------------------------------------------------------
# @echelon-foundry/typescript-wasm-kernel is Limen, the browser/application
# boundary required by VIG-GOV-008.
if [ -f package.json ]; then
  log "installing npm dependencies"
  npm install --no-audit --no-fund
fi

# --- NuGet ----------------------------------------------------------------
# Warms the package cache so the first build in the session is not also the
# first restore.
if [ -f Vigila.sln ]; then
  log "restoring NuGet packages"
  dotnet restore Vigila.sln
fi

# --- Visual Engineering context -------------------------------------------
# .visual-engineering/ is gitignored by design and refreshed by its own tool,
# so a fresh clone does not have it -- yet AGENTS.md instructs agents to read
# those files before any UI work. Without this step that instruction points at
# files that do not exist.
if [ -f .echelon/visual-engineering.json ]; then
  log "refreshing Visual Engineering context"
  npx --yes @echelon-foundry/visual-engineering init >/dev/null 2>&1 \
    || log "warning: Visual Engineering context refresh failed (non-fatal)"
fi

# --- ROS ------------------------------------------------------------------
# The ./ros launcher downloads a platform binary from GitHub Releases on first
# use. Fetching it here keeps the first ROS command in a session fast, and
# surfaces a download problem at startup rather than mid-task.
if [ -x ./ros ]; then
  log "warming ROS CLI"
  ./ros --version >/dev/null 2>&1 || log "warning: ROS CLI warm-up failed (non-fatal)"
fi

log "ready"
