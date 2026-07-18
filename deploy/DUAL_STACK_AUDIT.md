# Dual-stack deployment audit (SpeechInsight2 VPS)

**Status:** LIVE UPSTREAM = `speechinsight-api` (NPM). **Resolution:** make Compose/GHA use that same `container_name` — keep NPM unchanged.

**Evidence sources:**
- GitHub Actions deploy logs (e.g. run [29638525502](https://github.com/RonBabaj/SpeechInsight2/actions/runs/29638525502))
- Nginx Proxy Manager: proxy host for `speechinsight.rongurfinkel.com`
- Public probe of `https://speechinsight.rongurfinkel.com`

## Confirmed NPM upstream (production)

| NPM field | Value |
|-----------|--------|
| Domain | `speechinsight.rongurfinkel.com` |
| Scheme | `http` |
| **Forward Hostname / IP** | **`speechinsight-api`** |
| Forward Port | `8080` |
| Cache Assets | On |

**Conclusion:** Production traffic goes to Docker DNS name **`speechinsight-api`**. Keep that NPM setting; align the Compose stack to it.

Public corroboration (2026-07-18, before cutover):
- `GET /api/health` → `{"status":"ok",...}` with **no** `gitSha`
- `index.html` `Last-Modified: Tue, 14 Jul 2026` — stale orphan build

## Snapshot before fix (two containers)

| Container | Image | Compose service | Host ports | Role |
|-----------|--------|-----------------|------------|------|
| `speechinsight2` | `speechinsight2:latest` | `app` | `0.0.0.0:8080->8080` | What GHA rebuilt (wrong name for NPM) |
| `speechinsight-api` | `speechinsight2-speechinsight` | `speechinsight` (orphan) | none | What NPM served |

Compose warned: *Found orphan containers (speechinsight-api)*.

---

## 1. Why two deployments existed

1. Repo compose (PR #9) used `container_name: speechinsight2` while NPM already pointed at `speechinsight-api`.
2. The live `speechinsight-api` was an orphan of project `speechinsight2` (service `speechinsight`, image `speechinsight2-speechinsight`).
3. `docker compose down` without `--remove-orphans` left the orphan forever; GHA health-checked host `:8080` (the new container) so deploys looked green.

---

## 2. Which deployment serves production traffic

**Confirmed live:** Docker DNS **`speechinsight-api:8080`** (NPM Forward Hostname).

---

## 3. Compose project ownership

Both were project **`speechinsight2`**. After the fix there is one container: **`speechinsight-api`**, service `app`, image `speechinsight2:latest`.

---

## 4. Did GHA deploy to the same target NPM serves?

**Previously: no.** GHA updated `speechinsight2`; NPM hit the orphan `speechinsight-api`.

**After this PR: yes.** Compose `container_name` is `speechinsight-api`; deploy recreates that name, re-attaches prior Docker networks for NPM DNS, and verifies `/api/health.gitSha`.

---

## 5. Should old artifacts be removed?

**Yes — the duplicate `speechinsight2` container and the old image `speechinsight2-speechinsight`.** The name `speechinsight-api` stays; it becomes the fresh Compose-managed container.

Do not manually `docker rm speechinsight-api` while it is still the only NPM target unless a replacement with the same name is coming up immediately (the deploy workflow handles recreate).

---

## 6. Migration (keep NPM on `speechinsight-api`)

**No NPM change required.**

1. Merge this PR and let GitHub Actions deploy (or run the workflow manually).
2. Deploy will:
   - Capture networks attached to the current `speechinsight-api` (so NPM Docker DNS keeps working)
   - `docker compose down --remove-orphans` (drops old `speechinsight2` + orphan)
   - Build `speechinsight2:latest` with `GIT_SHA`
   - Create **new** `speechinsight-api` from that image (`8080:8080`)
   - Re-attach saved / common NPM networks
   - Fail unless host `:8080` health returns matching `gitSha`
3. Verify:
   ```bash
   curl -fsS https://speechinsight.rongurfinkel.com/api/health   # expect gitSha
   curl -sI https://speechinsight.rongurfinkel.com/ | grep -i last-modified
   ```
4. Optionally turn NPM **Cache Assets** off briefly and hard-refresh the browser.

Expect a short blip during container recreate (same as any compose down/up deploy). NPM Forward Hostname stays **`speechinsight-api`**.
