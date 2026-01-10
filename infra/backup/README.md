# Backup-as-code (restic → OVH Object Storage)

This folder contains the scripts and systemd units to back up QuestionsHub.

- DB: `pg_dump` inside Docker → streamed into restic
- Files: uploads + keys + nginx conf + .env → restic
- Storage: OVH Object Storage bucket `lucky-chandrasekhar` (DE)
- Retention:
  - DB: 7 daily / 4 weekly / 12 monthly
  - Files: 30 daily

## What you must create on the host (NOT in git)

Create secrets file:
- `/home/github-actions/.config/questions-hub-backup/backup.env`

Use `backup.env.example` as a template.

## Install
1. Install deps: `restic`, `curl`
2. Init repo (first time only): `restic init`
3. Install the systemd units from `systemd/`
4. Install `scripts/backup.sh` to `/home/github-actions/questions-hub/infra/backup/runtime/backup.sh`
5. Enable timers.

See `backups.md` for the full runbook.
