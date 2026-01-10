#!/usr/bin/env bash
set -euo pipefail

# Load env (app secrets + backup secrets)
set -a
source /home/github-actions/.env
source /home/github-actions/.config/questions-hub-backup/backup.env
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
RESTIC_HOST="questions-hub"
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

# --- DB dump streamed to restic (no temp files) ---
docker exec -i -e PGPASSWORD="${POSTGRES_ROOT_PASSWORD}" questions-hub-db   pg_dump -U postgres -d questionshub -Fc -Z9   | restic -r "${RESTIC_REPO}" backup       --host "${RESTIC_HOST}"       --tag db       --stdin       --stdin-filename "db/questionshub_${TS}.dump"

# --- Files snapshot ---
restic -r "${RESTIC_REPO}" backup   --host "${RESTIC_HOST}"   --tag files   /home/github-actions/questions-hub/uploads   /home/github-actions/questions-hub/keys   /etc/nginx/conf.d/questions.com.ua.conf   /home/github-actions/.env

# --- Retention ---
restic -r "${RESTIC_REPO}" forget --tag db --host "${RESTIC_HOST}"   --keep-daily 7 --keep-weekly 4 --keep-monthly 12 --prune

restic -r "${RESTIC_REPO}" forget --tag files --host "${RESTIC_HOST}"   --keep-daily 30 --prune

ping_ok
