# SpeechInsight2 – Deploy to Render

This folder contains the Docker and CI/CD setup for deploying Api + Client as one service.

## Local test

From repo root:

```bash
docker build -f deploy/speechinsight2/Dockerfile .
docker run -p 8080:8080 <image-id>
```

Or: `docker compose -f deploy/speechinsight2/docker-compose.yml up --build`

Then open http://localhost:8080 (UI) and http://localhost:8080/api/... (API).

## Render setup

1. Create a **Web Service**; connect this GitHub repo.
2. Build: Docker; Dockerfile path `deploy/speechinsight2/Dockerfile`.
3. Add env vars in the dashboard (e.g. `ASPNETCORE_ENVIRONMENT=Production`, any API keys).
4. Deploy. Render will set `PORT`; the container listens on it.

## GitHub Actions (optional)

Copy `.github/workflows/deploy.yml` from this folder to the repo root as `.github/workflows/deploy.yml`. Add secrets: `RENDER_API_KEY`, `RENDER_SERVICE_ID`. Pushes to `main` will then build and trigger a Render deploy.

## Safety

- Caddy limits request body to 50 MB for `/api/*`. Also set `Kestrel.Limits.MaxRequestBodySize` in the Api if you want the API to reject oversized requests.
