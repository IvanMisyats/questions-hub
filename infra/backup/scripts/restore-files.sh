#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <SNAPSHOT_ID> <TARGET_DIR>"
  echo "Example: $0 12345678 /tmp/qh-restore"
  exit 1
fi

SNAPSHOT_ID="$1"
TARGET_DIR="$2"

set -a
source /home/github-actions/.config/questions-hub-backup/backup.env
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"

mkdir -p "${TARGET_DIR}"
restic -r "${RESTIC_REPO}" restore "${SNAPSHOT_ID}" --target "${TARGET_DIR}"

echo "Restored to: ${TARGET_DIR}"
echo "Uploads will be under: ${TARGET_DIR}/home/github-actions/questions-hub/uploads"
