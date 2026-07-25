#!/usr/bin/env bash
# QuestionsHub deploy: pull the current image and reconcile the running stack.
#
# Installed on the VPS as /usr/local/bin/qh-deploy, owned root:root 0755 — the `qh` user runs it
# but cannot edit it, so a stolen CI deploy key cannot turn "redeploy" into "run anything".
#
# Two callers, same script:
#   1. CI, instantly, through a forced-command SSH key:
#        ~qh/.ssh/authorized_keys:
#        restrict,command="/usr/local/bin/qh-deploy" ssh-ed25519 AAAA... qh-ci-deploy
#      $SSH_ORIGINAL_COMMAND is ignored on purpose — whatever the client asks for, this runs.
#   2. qh-deploy.timer (user unit), every 5 minutes, as a fallback so a missed CI trigger or a
#      manually pushed image still converges.
#
# Rollback: set IMAGE_TAG=sha-<short> in app.env (as root), then trigger a deploy.

set -euo pipefail

DEPLOY_DIR="/srv/questions-hub/deploy"
ENV_FILE="${DEPLOY_DIR}/app.env"
HEALTH_URL="http://127.0.0.1:8080/health"
HEALTH_TIMEOUT=120          # seconds to wait for the app to come up
LOCK_FILE="/tmp/qh-deploy.lock"

# This script runs from a non-login shell (SSH forced command, systemd user unit), so the
# rootless Docker environment is not inherited from a profile — set it explicitly.
export XDG_RUNTIME_DIR="/run/user/$(id -u)"
export DOCKER_HOST="unix://${XDG_RUNTIME_DIR}/docker.sock"

log() { printf '[qh-deploy %s] %s\n' "$(date -u +'%H:%M:%SZ')" "$*"; }
fail() { log "ERROR: $*"; exit 1; }

# Serialise: the 5-minute timer and a CI trigger can land at the same moment.
exec 9>"${LOCK_FILE}"
if ! flock -w 300 9; then
    fail "another deploy has held the lock for 5 minutes; giving up"
fi

cd "${DEPLOY_DIR}" || fail "missing ${DEPLOY_DIR}"
[[ -r "${ENV_FILE}" ]] || fail "cannot read ${ENV_FILE}"

compose() { docker compose --env-file "${ENV_FILE}" "$@"; }

IMAGE="$(compose config --images 2>/dev/null | grep 'questions-hub' | head -1)"
[[ -n "${IMAGE}" ]] || fail "could not resolve the web image from docker-compose.yml"
log "target image: ${IMAGE}"

before="$(docker image inspect --format '{{.Id}}' "${IMAGE}" 2>/dev/null || echo none)"

log "pulling..."
compose pull --quiet || fail "docker compose pull failed"

after="$(docker image inspect --format '{{.Id}}' "${IMAGE}" 2>/dev/null || echo none)"
[[ "${after}" != "none" ]] || fail "image ${IMAGE} is not present after pull"

running="$(docker ps --quiet --filter 'name=questions-hub-web')"

if [[ "${before}" == "${after}" && -n "${running}" ]]; then
    log "image unchanged and stack is up — nothing to do."
    exit 0
fi

if [[ "${before}" == "${after}" ]]; then
    log "image unchanged, but the stack is not running — starting it."
else
    log "new image ${before:0:19} -> ${after:0:19}"
fi

# `up -d` is declarative: it recreates only what actually changed. No `down` first — that would
# turn every deploy into avoidable downtime.
compose up -d --remove-orphans || fail "docker compose up failed"

log "waiting for health (max ${HEALTH_TIMEOUT}s)..."
deadline=$(( SECONDS + HEALTH_TIMEOUT ))
until curl -fsS --max-time 5 "${HEALTH_URL}" >/dev/null 2>&1; do
    if (( SECONDS >= deadline )); then
        log "health check never passed. Recent logs:"
        compose logs --tail 50 web || true
        fail "deploy failed health check — the previous image is NOT automatically restored; \
roll back by setting IMAGE_TAG in app.env and re-running"
    fi
    sleep 3
done
log "healthy."

# Keep the previous image around for a manual rollback; prune only dangling layers.
docker image prune -f >/dev/null 2>&1 || true

log "deploy complete."
