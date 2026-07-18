# Dual-stack deployment audit (SpeechInsight2 VPS)

**Status:** read-only analysis. **Do not delete** `speechinsight-api` until public traffic is confirmed on `speechinsight2`.

**Evidence source:** GitHub Actions deploy logs (e.g. run [29638525502](https://github.com/RonBabaj/SpeechInsight2/actions/runs/29638525502), 2026-07-18 after merge of #17) and committed Compose/workflow history.

## Snapshot from latest successful deploy (main)

| Container | Image | Compose service | Host ports | Age at that deploy |
|-----------|--------|-----------------|------------|--------------------|
| **`speechinsight2`** | `speechinsight2:latest` | **`app`** | **`0.0.0.0:8080->8080`** | recreated every deploy |
| **`speechinsight-api`** | `speechinsight2-speechinsight` | **`speechinsight`** | none (`8080/tcp` only) | **Up ~3 days** (never recreated by GHA) |

Compose also logged:

> Found orphan containers (**speechinsight-api**) for this project.

So both containers share Compose **project** `speechinsight2`; the second is an **orphan** (service no longer in the compose file).

---

## 1. Why two deployments exist

1. **Authoritative stack (repo, since PR #9):** project `speechinsight2`, service `app`, `container_name: speechinsight2`, explicit `image: speechinsight2:latest`, publish `8080:8080`. GitHub Actions deploys this from `/opt/apps/SpeechInsight2`.

2. **Legacy stack (VPS-only, not in git history):** service name `speechinsight`, container `speechinsight-api`, image `speechinsight2-speechinsight`. That image name is Compose’s default `{project}-{service}` when no `image:` is set — i.e. project `speechinsight2` + service `speechinsight`. No committed compose file ever defined that service (root compose has always used `app` / `speechinsight2`).

3. **Why it survived:** on `main`, deploy still runs `docker compose down` **without** `--remove-orphans`, then `up -d`. Compose leaves orphans running and only warns. First successful GHA (#11, ~2026-07-17 06:00) already saw the orphan “Up 46 hours” → created ~**2026-07-15**, before the new pipeline worked.

---

## 2. Which deployment serves production traffic

| Path | Hits which container? |
|------|------------------------|
| Host / NPM `http://127.0.0.1:8080` or `http://VPS_IP:8080` | **`speechinsight2` only** (sole publisher of host port 8080) |
| Docker DNS `http://speechinsight-api:8080` on a shared NPM network | **`speechinsight-api`** (stale orphan) |
| Docker DNS `http://speechinsight2:8080` | **`speechinsight2`** |

**GitHub Actions always verifies the host-:8080 path** (`curl http://127.0.0.1:8080/api/health`). That proves the **new** container is healthy; it does **not** prove NPM points there.

**To confirm live traffic without deleting anything**, on the VPS:

```bash
cd /opt/apps/SpeechInsight2
bash deploy/identify-live-deployment.sh https://YOUR_PUBLIC_HOST
```

Compare public `/api/health` to each container’s in-container health. Matching container = what NPM serves.

Until that public probe is run, treat production as **ambiguous**: host-:8080 is definitely `speechinsight2`; NPM may still target the orphan by name.

---

## 3. Which Docker Compose project owns each container

Both are labeled under the **same** Compose project: **`speechinsight2`** (working dir `/opt/apps/SpeechInsight2`).

| Container | Project | Service | Role |
|-----------|---------|---------|------|
| `speechinsight2` | `speechinsight2` | `app` | Current compose file; GHA-owned |
| `speechinsight-api` | `speechinsight2` | `speechinsight` | Orphan; leftover from a prior/manual service definition |

There is **not** a second Compose project name in the Actions inventory — only one project with an orphaned service.

(The optional file `deploy/speechinsight2/docker-compose.yml` uses project name `speechinsight2-deploy-alt` and is **not** what GHA runs.)

---

## 4. Does GitHub Actions deploy to the same target NPM serves?

**Not guaranteed.**

- GHA rebuilds and health-checks **`speechinsight2`** via **host `:8080`**.
- GHA **does not** rebuild or recreate **`speechinsight-api`**.
- If NPM forwards to host `:8080` (or DNS name `speechinsight2`) → **yes, same deployment**.
- If NPM still points at Docker name **`speechinsight-api`** → **no**: CI is green on the new stack while users hit the 3-day-old orphan.

That mismatch is the likely cause of “Actions green, production looks stale.”

---

## 5. Should old artifacts be removed?

**Yes — after live traffic is confirmed on `speechinsight2`.**

Safe to remove once public health matches `speechinsight2`:

- Container: `speechinsight-api`
- Image: `speechinsight2-speechinsight` (and any dangling layers)

**Do not remove** while public `/api/health` still matches only the orphan (that would take production down if NPM uses that name).

---

## 6. Zero-downtime migration to a single authoritative Compose stack

1. **Identify (no deletes)**  
   `bash deploy/identify-live-deployment.sh https://PUBLIC_HOST`

2. **Point NPM at the GHA target (if not already)**  
   Upstream: `http://127.0.0.1:8080` **or** Docker `speechinsight2:8080` on NPM’s network.  
   Save → hit public `/api/health` → must match `speechinsight2` (and, after PR #18, `gitSha`).

3. **Keep orphan running briefly** while you verify UI (Client build stamp / health). Zero downtime: both containers stay up; only the proxy target changes.

4. **Merge and run PR #18** (deploy with `--remove-orphans`, force-recreate, fail if any extra `speechinsight*` container remains, require `health.gitSha == github.sha`).  
   That removes the orphan automatically on the next deploy.

5. **If removing manually before #18** (only after step 2–3):  
   `docker rm -f speechinsight-api`  
   `docker rmi speechinsight2-speechinsight`  
   Re-check public health.

6. **Authoritative forever:** only repo-root `docker-compose.yml` at `/opt/apps/SpeechInsight2`; NPM only to `127.0.0.1:8080` / `speechinsight2`.
