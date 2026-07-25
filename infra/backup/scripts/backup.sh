#!/usr/bin/env bash
# QuestionsHub backup: logical DB dump + files, straight into restic on OVH Object Storage.
#
# Runs as the unprivileged `qh` user via the questionshub-backup.timer USER unit.
# It needs no `docker` group and no sudo: it talks to qh's own rootless Docker socket, and every
# path it reads is either qh-owned or world-readable.

set -euo pipefail

DEPLOY_DIR="/srv/questions-hub/deploy"
BACKUP_DIR="/srv/questions-hub/backup"

# Non-login shell (systemd user unit) — the rootless Docker environment is not inherited.
export XDG_RUNTIME_DIR="/run/user/$(id -u)"
export DOCKER_HOST="unix://${XDG_RUNTIME_DIR}/docker.sock"

# App secrets (for POSTGRES_ROOT_PASSWORD) and backup secrets (S3 + restic).
set -a
# shellcheck disable=SC1091
source "${DEPLOY_DIR}/app.env"
# shellcheck disable=SC1091
source "${BACKUP_DIR}/backup.env"
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
RESTIC_HOST="questions-hub"
export RESTIC_CACHE_DIR="${BACKUP_DIR}/restic-cache"
TS="$(date -u +%F)"

ping_fail() {
  if [[ -n "${HC_URL:-}" ]]; then
    curl -fsS -m 10 --retry 3 "${HC_URL}/fail" >/dev/null 2>&1 || true
  fi
}
trap ping_fail ERR

ping_ok() {
  if [[ -n "${HC_URL:-}" ]]; then
    curl -fsS -m 10 --retry 3 "${HC_URL}" >/dev/null 2>&1 || true
  fi
}

compose() { docker compose --env-file "${DEPLOY_DIR}/app.env" "$@"; }
cd "${DEPLOY_DIR}"

# --- DB dump streamed to restic (no temp files, no disk headroom needed) ---
# `exec -T` (not `docker exec -i`): this is a non-interactive context, and going through compose
# means the container name is resolved from the project rather than hard-coded.
compose exec -T -e PGPASSWORD="${POSTGRES_ROOT_PASSWORD}" postgres \
    pg_dump -U postgres -d questionshub -Fc -Z9 \
  | restic -r "${RESTIC_REPO}" backup \
      --host "${RESTIC_HOST}" \
      --tag db \
      --stdin \
      --stdin-filename "db/questionshub_${TS}.dump"

# --- Files snapshot ---
# keys/ is the ASP.NET Data Protection key ring: without it every existing session and auth
# cookie is invalidated on restore, so it matters as much as the database.
restic -r "${RESTIC_REPO}" backup \
    --host "${RESTIC_HOST}" \
    --tag files \
    /srv/questions-hub/uploads \
    /srv/questions-hub/keys \
    /srv/questions-hub/deploy/app.env \
    /etc/nginx/conf.d/questions.com.ua.conf

# --- Retention ---
restic -r "${RESTIC_REPO}" forget --tag db --host "${RESTIC_HOST}" \
    --keep-daily 7 --keep-weekly 4 --keep-monthly 12 --prune

restic -r "${RESTIC_REPO}" forget --tag files --host "${RESTIC_HOST}" \
    --keep-daily 30 --prune

ping_ok
