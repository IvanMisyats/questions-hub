# Backups

Daily automated backups using **restic** to **OVH Object Storage** (S3-compatible).

## What is backed up

| Data | Method | Retention |
|------|--------|-----------|
| PostgreSQL database | `pg_dump` inside Docker container, streamed to restic (no temp files) | 7 daily, 4 weekly, 12 monthly |
| Uploads (`questions-hub/uploads`) | restic file snapshot | 30 daily |
| Data Protection keys (`questions-hub/keys`) | restic file snapshot | 30 daily |
| Nginx config (`/etc/nginx/conf.d/questions.com.ua.conf`) | restic file snapshot | 30 daily |
| App env (`.env`) | restic file snapshot (encrypted client-side by restic) | 30 daily |

## Schedule

- **Backup**: daily at **03:15 UTC** via systemd timer (`questionshub-backup.timer`)
- **Integrity check**: weekly on Sunday at **04:30 UTC** (`questionshub-restic-check.timer`)

## Storage

- **Provider**: OVH Object Storage (DE region, S3-compatible)
- **Bucket**: `lucky-chandrasekhar`
- **Restic repo**: `s3:https://s3.de.io.cloud.ovh.net/lucky-chandrasekhar/restic`
- All data is **encrypted client-side** by restic before upload.

## Monitoring

Optional [Healthchecks](https://healthchecks.io/) integration:
- On success: pings `HC_URL`
- On failure: pings `HC_URL/fail` (via `trap ... ERR`)
- Configure by setting `HC_URL` in `backup.env` on the host.

## Host details

| Item | Value |
|------|-------|
| Backup user | `github-actions` (UID 1001) |
| App directory | `/home/github-actions/questions-hub/` |
| Backup secrets | `/home/github-actions/.config/questions-hub-backup/backup.env` |
| Backup script (runtime) | `/home/github-actions/questions-hub/infra/backup/runtime/backup.sh` |
| systemd units | `/etc/systemd/system/questionshub-backup.{service,timer}` |
| restic version | 0.16.4 |

## IaC (Infrastructure as Code)

All scripts and configuration are version-controlled in [`infra/backup/`](../infra/backup/):

```
infra/backup/
├── backup.env.example          # Template for host secrets (never commit real values)
├── scripts/
│   ├── backup.sh               # Main backup script (runs via systemd)
│   ├── restore-db-latest.sh    # Restore latest DB dump
│   └── restore-files.sh        # Restore files from a snapshot
├── systemd/
│   ├── questionshub-backup.service
│   ├── questionshub-backup.timer
│   ├── questionshub-restic-check.service
│   └── questionshub-restic-check.timer
├── backups.md                  # Full runbook (setup, restore, troubleshooting)
└── README.md                   # Quick reference
```

## Quick operations

### List snapshots
```bash
sudo -u github-actions bash -lc '
set -a; source /home/github-actions/.config/questions-hub-backup/backup.env; set +a
restic -r "s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic" snapshots
'
```

### Run backup manually
```bash
sudo -u github-actions /home/github-actions/questions-hub/infra/backup/runtime/backup.sh
```

### Check logs
```bash
journalctl -u questionshub-backup.service -n 200 --no-pager
systemctl list-timers | grep questionshub
```

### Restore

See [`infra/backup/backups.md`](../infra/backup/backups.md) for full restore procedures, or use the scripts in `infra/backup/scripts/`.
