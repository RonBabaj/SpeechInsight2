#!/bin/sh
set -e
export PORT="${PORT:-8080}"
export ASPNETCORE_URLS="http://localhost:5000"

cd /app/api && dotnet SpeechInsight.Api.dll &
sleep 2
exec caddy run --config /etc/Caddyfile --adapter caddyfile
