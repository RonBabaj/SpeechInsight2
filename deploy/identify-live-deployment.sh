#!/usr/bin/env bash
# Read-only inventory: identify which SpeechInsight container is live.
# Run on the VPS. Does NOT delete or restart anything.
#
#   bash deploy/identify-live-deployment.sh
#   bash deploy/identify-live-deployment.sh https://your.public.hostname

set -euo pipefail

PUBLIC_URL="${1:-}"

echo "========== Docker inventory (read-only) =========="
docker ps -a --filter name=speechinsight --format 'table {{.ID}}\t{{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
echo
echo "========== Compose labels per container =========="
for c in $(docker ps -a --filter name=speechinsight --format '{{.Names}}'); do
  echo "--- ${c} ---"
  docker inspect "${c}" --format \
    'name={{.Name}}
image={{.Config.Image}}
imageID={{.Image}}
created={{.Created}}
status={{.State.Status}}
started={{.State.StartedAt}}
project={{index .Config.Labels "com.docker.compose.project"}}
service={{index .Config.Labels "com.docker.compose.service"}}
working_dir={{index .Config.Labels "com.docker.compose.project.working_dir"}}
config_files={{index .Config.Labels "com.docker.compose.project.config_files"}}
networks={{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}
env_GIT_SHA={{range .Config.Env}}{{if (slice . 0 8 | eq "GIT_SHA=")}}{{println .}}{{end}}{{end}}'
  echo
done

echo "========== Host :8080 binding =========="
docker ps --filter publish=8080 --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}'
echo "(Only containers listed above receive traffic sent to the VPS host port 8080.)"
echo

echo "========== Local health probes =========="
if curl -fsS --max-time 3 http://127.0.0.1:8080/api/health; then
  echo
  echo "OK: host :8080 answered (this is what GitHub Actions health-checks)."
else
  echo
  echo "FAIL: host :8080 did not answer."
fi
echo

for c in $(docker ps --filter name=speechinsight --format '{{.Names}}'); do
  # Probe each container on the bridge network via docker exec curl to its own loopback.
  echo -n "exec health inside ${c}: "
  if docker exec "${c}" curl -fsS --max-time 3 http://127.0.0.1:8080/api/health 2>/dev/null; then
    echo
  else
    echo "(curl failed or curl missing in image)"
  fi
done
echo

if [ -n "${PUBLIC_URL}" ]; then
  echo "========== Public URL probe =========="
  echo "GET ${PUBLIC_URL%/}/api/health"
  if body="$(curl -fsS --max-time 10 "${PUBLIC_URL%/}/api/health")"; then
    echo "${body}"
    echo
    echo "Compare this JSON to the exec-health lines above."
    echo "Whichever container's payload matches the public response is what NPM is serving."
  else
    echo "Public health request failed."
  fi
fi

echo
echo "========== Interpretation cheat-sheet =========="
echo "1. Host port 8080 publisher  = target of NPM 'http://IP:8080' / '127.0.0.1:8080' configs."
echo "2. Container with no host Ports column = only reachable via Docker DNS name on a shared network."
echo "3. Authoritative production container = name 'speechinsight-api' (Compose service 'app', project 'speechinsight2')."
echo "4. Legacy duplicate = name 'speechinsight2' or image 'speechinsight2-speechinsight' without matching gitSha — remove after cutover."
echo "5. NPM Forward Hostname should be speechinsight-api:8080 (matches compose container_name)."
echo "6. Do NOT docker rm the live speechinsight-api until a replacement with the same name is up."
