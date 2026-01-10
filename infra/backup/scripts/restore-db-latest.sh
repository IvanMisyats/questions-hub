#!/usr/bin/env bash
set -euo pipefail

set -a
source /home/github-actions/.config/questions-hub-backup/backup.env
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"

echo "Listing DB dump files in latest db snapshot..."
restic -r "${RESTIC_REPO}" ls latest --tag db

echo "Restoring latest DB dump into database 'questionshub'..."
restic -r "${RESTIC_REPO}" dump latest --tag db "db/questionshub_*.dump"   | docker exec -i questions-hub-db pg_restore -U postgres -d questionshub --clean --if-exists

echo "Done."
