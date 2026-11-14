#!/usr/bin/env bash
# Start the API (SQLite, seeded) and the Vite console for local development.
# Usage: ./scripts/dev.sh   — Ctrl+C stops both.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
api_port="${API_PORT:-8080}"
web_port="${WEB_PORT:-5173}"

cleanup() {
  echo
  echo "Stopping NexusBank..."
  [[ -n "${api_pid:-}" ]] && kill "$api_pid" 2>/dev/null || true
  [[ -n "${web_pid:-}" ]] && kill "$web_pid" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if [[ ! -d "$root/frontend/node_modules" ]]; then
  echo "Installing frontend dependencies..."
  (cd "$root/frontend" && npm install)
fi

echo "Starting API on :$api_port (development, SQLite)"
(
  cd "$root/backend"
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$api_port" \
    dotnet run --project NexusBank.Api --no-launch-profile
) &
api_pid=$!

echo "Starting console on :$web_port"
(
  cd "$root/frontend"
  VITE_API_TARGET="http://localhost:$api_port" npm run dev -- --port "$web_port"
) &
web_pid=$!

echo
echo "Console:  http://127.0.0.1:$web_port"
echo "API:      http://localhost:$api_port/swagger"
echo "Health:   http://localhost:$api_port/health"
echo "Sign in:  admin@nexusbank.dev / NexusBank!2026"

wait
