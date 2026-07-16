# SpeechInsight2 deploy helpers (legacy Render-oriented layout).
#
# Production deployment uses the **repo-root** files:
#   - Dockerfile
#   - docker-compose.yml
#   - .github/workflows/deploy.yml
# on the VPS at /opt/apps/SpeechInsight2 behind Nginx Proxy Manager.
#
# This folder keeps optional helpers (Caddy-based image variant). Prefer the
# root Compose stack for VPS deploys.

## Production (recommended)

From `/opt/apps/SpeechInsight2` (repo root):

```bash
# Ensure .env exists (see repo-root .env.example)
docker compose build --pull
docker compose up -d
curl -fsS http://127.0.0.1:8080/api/health
```

## Alternate image (Caddy + API)

Build from repo root:

```bash
docker build -f deploy/speechinsight2/Dockerfile .
```

See the root README for GitHub Actions secrets, rollback, and troubleshooting.
