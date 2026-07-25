#!/usr/bin/env bash
# Restore an uploads/keys snapshot from restic to a staging directory.
# Run as the `qh` user.
#
# This deliberately restores to a staging path rather than writing over live data — inspect it,
# then move what you need into place.

set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <SNAPSHOT_ID|latest> <TARGET_DIR>"
  echo "Example: $0 latest /tmp/qh-restore"
  exit 1
fi

SNAPSHOT_ID="$1"
TARGET_DIR="$2"
BACKUP_DIR="/srv/questions-hub/backup"

set -a
# shellcheck disable=SC1091
source "${BACKUP_DIR}/backup.env"
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
export RESTIC_CACHE_DIR="${BACKUP_DIR}/restic-cache"

mkdir -p "${TARGET_DIR}"
restic -r "${RESTIC_REPO}" restore "${SNAPSHOT_ID}" --tag files --target "${TARGET_DIR}"

cat <<EOF

Restored to: ${TARGET_DIR}
  uploads:  ${TARGET_DIR}/srv/questions-hub/uploads
  keys:     ${TARGET_DIR}/srv/questions-hub/keys

To put them live (as qh — no chown to UID 10000 is needed any more, files are plain qh:qh
under rootless Docker):

  rsync -a  ${TARGET_DIR}/srv/questions-hub/uploads/  /srv/questions-hub/uploads/
  rsync -a  ${TARGET_DIR}/srv/questions-hub/keys/     /srv/questions-hub/keys/
  chmod 700 /srv/questions-hub/keys

WARNING: keys/ is the ASP.NET Data Protection key ring. Restore it, or every existing user
session and auth cookie is invalidated.
EOF
