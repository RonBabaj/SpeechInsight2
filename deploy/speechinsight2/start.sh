#!/bin/sh
# Starts the API on an internal port, then Caddy on $SITE_PORT (default 8080).
set -e

export SITE_PORT="${PORT:-8080}"
# Program.cs binds to PORT; keep the API internal so Caddy can own SITE_PORT.
export PORT=5000
export ASPNETCORE_URLS="http://127.0.0.1:5000"

cd /app/api && dotnet SpeechInsight.Api.dll &
sleep 2
exec caddy run --config /etc/Caddyfile --adapter caddyfile
