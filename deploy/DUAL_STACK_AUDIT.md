# Dual-stack deployment audit (SpeechInsight2 VPS)

**Status:** LIVE UPSTREAM IDENTIFIED. **Do not delete** `speechinsight-api` until NPM is retargeted and public traffic is confirmed on `speechinsight2`.

**Evidence sources:**
- GitHub Actions deploy logs (e.g. run [29638525502](https://github.com/RonBabaj/SpeechInsight2/actions/runs/29638525502))
- Nginx Proxy Manager screenshot (2026-07-18): proxy host for `speechinsight.rongurfinkel.com`
- Public probe of `https://speechinsight.rongurfinkel.com`

## Confirmed NPM upstream (production)

| NPM field | Value |
|-----------|--------|
| Domain | `speechinsight.rongurfinkel.com` |
| Scheme | `http` |
| **Forward Hostname / IP** | **`speechinsight-api`** |
| Forward Port | `8080` |
| Cache Assets | On |

**Conclusion:** Production traffic goes to the **orphan** container `speechinsight-api`, **not** the GHA-deployed `speechinsight2`.

Public corroboration (2026-07-18):
- `GET /api/health` → `{"status":"ok",...}` with **no** `gitSha` (pre–PR #18 shape)
- `index.html` `Last-Modified: Tue, 14 Jul 2026` — matches orphan age, not Jul 18 deploys

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

**Confirmed live:** `speechinsight-api` (via NPM Forward Hostname).

| Path | Hits which container? |
|------|------------------------|
| NPM → `speechinsight-api:8080` (current) | **`speechinsight-api`** ← **production today** |
| Host / NPM `http://127.0.0.1:8080` | **`speechinsight2` only** (sole publisher of host port 8080) |
| Docker DNS `http://speechinsight2:8080` | **`speechinsight2`** |

GitHub Actions verifies only host `:8080` → green deploys update `speechinsight2` while users still hit the orphan.

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

**No.** Confirmed mismatch.

- GHA rebuilds/health-checks **`speechinsight2`** on host `:8080`.
- NPM serves **`speechinsight-api:8080`**.
- Users never see new builds until NPM is changed.

---

## 5. Should old artifacts be removed?

**Yes — after live traffic is confirmed on `speechinsight2`.**

Safe to remove once public health matches `speechinsight2`:

- Container: `speechinsight-api`
- Image: `speechinsight2-speechinsight` (and any dangling layers)

**Do not remove** while public `/api/health` still matches only the orphan (that would take production down if NPM uses that name).

---

## 6. Zero-downtime migration (do this next)

Both containers stay up until after cutover — **change NPM first, delete later**.

1. **In NPM → Edit Proxy Host → Details** for `speechinsight.rongurfinkel.com`:
   - Change **Forward Hostname / IP** from `speechinsight-api` → **`speechinsight2`**
     (or `127.0.0.1` if you prefer host networking; port stays **8080**)
   - Optionally turn **Cache Assets** **off** temporarily so browsers/NPM don’t keep the Jul 14 HTML
   - Save

2. **Verify cutover (no deletes yet):**
   ```bash
   curl -fsS https://speechinsight.rongurfinkel.com/api/health
   curl -sI https://speechinsight.rongurfinkel.com/ | grep -i last-modified
   ```
   Expect a fresh `Last-Modified` (not 14 Jul) and, after PR #18 is deployed, a `gitSha` matching `main`.

3. **Hard-refresh the site** (or private window) and confirm UI (e.g. Client build stamp from #17).

4. **Merge/run [PR #18](https://github.com/RonBabaj/SpeechInsight2/pull/18)** so the next deploy uses `--remove-orphans` and removes `speechinsight-api` safely.

5. **Only then** (if orphan still present and NPM already points at `speechinsight2`):
   ```bash
   docker rm -f speechinsight-api
   docker rmi speechinsight2-speechinsight
   ```

6. **Permanent NPM setting:** Forward Hostname = `speechinsight2` **or** `127.0.0.1`, port `8080`. Never `speechinsight-api`.
