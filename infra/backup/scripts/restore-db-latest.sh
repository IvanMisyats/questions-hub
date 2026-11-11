#!/usr/bin/env bash
# Restore the most recent DB dump from restic into the running Postgres container.
# Run as the `qh` user.
#
# MIGRATION ORDER MATTERS: bring up only `postgres` and `db-setup` first, restore, and start
# `web` afterwards. If the app is already running, its EF migrations and admin seeding race the
# pg_restore --clean and you can end up with a half-overwritten schema.
#
#   docker compose --env-file app.env up -d postgres db-setup
#   ./restore-db-latest.sh
#   docker compose --env-file app.env up -d

set -euo pipefail

DEPLOY_DIR="/srv/questions-hub/deploy"
BACKUP_DIR="/srv/questions-hub/backup"

export XDG_RUNTIME_DIR="/run/user/$(id -u)"
export DOCKER_HOST="unix://${XDG_RUNTIME_DIR}/docker.sock"

set -a
# shellcheck disable=SC1091
source "${BACKUP_DIR}/backup.env"
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
export RESTIC_CACHE_DIR="${BACKUP_DIR}/restic-cache"

cd "${DEPLOY_DIR}"

echo "Listing DB dump files in the latest db snapshot..."
listing="$(restic -r "${RESTIC_REPO}" ls latest --tag db)"
printf '%s\n' "${listing}"

# `restic dump` takes one exact path — it does not expand globs — so resolve the dump's name
# (it carries the backup date) and pin the snapshot ID, so a backup finishing in between cannot
# swap the file under us.
snapshot="$(awk '/^snapshot /{print $2; exit}' <<<"${listing}")"
dump_path="$(grep -E '^/db/questionshub_.*\.dump$' <<<"${listing}" | tail -n 1 || true)"
[[ -n "${snapshot}" && -n "${dump_path}" ]] || { echo "No db/questionshub_*.dump in the latest db snapshot."; exit 1; }

read -r -p "Restore ${dump_path} (snapshot ${snapshot}) over database 'questionshub'? [y/N] " confirm
[[ "${confirm}" == "y" || "${confirm}" == "Y" ]] || { echo "Aborted."; exit 1; }

echo "Restoring..."
restic -r "${RESTIC_REPO}" dump "${snapshot}" "${dump_path}" \
  | docker compose --env-file "${DEPLOY_DIR}/app.env" exec -T postgres \
      pg_restore -U postgres -d questionshub --clean --if-exists

echo "Done. Now start the app:  docker compose --env-file app.env up -d"
