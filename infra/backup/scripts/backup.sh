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

run_backup() {
  # --- DB dump streamed to restic (no temp files) ---
  docker exec -i -e PGPASSWORD="${POSTGRES_ROOT_PASSWORD}" questions-hub-db \
    pg_dump -U postgres -d questionshub -Fc -Z9 \
    | restic -r "${RESTIC_REPO}" backup \
        --host "${RESTIC_HOST}" \
        --tag db \
        --stdin \
        --stdin-filename "db/questionshub_${TS}.dump"

  # --- Files snapshot ---
  # keys/ is only readable through the unit's CAP_DAC_READ_SEARCH: start this via systemctl,
  # not `sudo -u github-actions`.
  restic -r "${RESTIC_REPO}" backup \
    --host "${RESTIC_HOST}" \
    --tag files \
    /home/github-actions/questions-hub/uploads \
    /home/github-actions/questions-hub/keys \
    /etc/nginx/conf.d/questions.com.ua.conf \
    /home/github-actions/.env

  # --- Retention ---
  restic -r "${RESTIC_REPO}" forget --tag db --host "${RESTIC_HOST}" \
    --keep-daily 7 --keep-weekly 4 --keep-monthly 12 --prune

  restic -r "${RESTIC_REPO}" forget --tag files --host "${RESTIC_HOST}" \
    --keep-daily 30 --prune
}

# Capture the output so the Healthchecks ping can carry it: the DOWN email then shows the error
# rather than just "received a failure signal". bash drops errexit inside $(...), hence the
# inner `set -e`.
set +e
output="$(set -e; run_backup 2>&1)"
rc=$?
set -e
printf '%s\n' "${output}"  # still goes to the journal

if [[ -n "${HC_URL:-}" ]]; then
  # Ping /<exit code>: 0 is success, anything else is a failure.
  tail -n 50 <<<"${output}" \
    | curl -fsS -m 10 --retry 3 --data-binary @- "${HC_URL}/${rc}" >/dev/null 2>&1 || true
fi

exit "${rc}"
