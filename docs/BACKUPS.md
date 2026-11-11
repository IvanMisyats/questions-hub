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

**Format-level copies.** Besides these server-side snapshots, an admin keeps a second, off-server copy of
every package as a `.qhub` file, downloaded daily by a scheduled script on a personal machine through the
Agent API export (`GET /api/v1/manage/packages/{id}/export`, a read-only token; unchanged packages answer
`304` to the stored `ETag`). It holds the content only — no accounts, results or statistics — and restores
a package by importing the file as a new draft. The script is not part of this repository.

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
- Pings `HC_URL/<exit code>` after every run: `/0` is success, anything else is a failure.
- The ping body is the last 50 lines of the run's output, so the DOWN email shows the error.
- Configure by setting `HC_URL` in `backup.env` on the host.

Healthchecks emails only when the status changes. A check that stays DOWN sends nothing more,
and in the monthly report the "All good!" columns are past months, not the current status.

## Host details

| Item | Value |
|------|-------|
| Backup user | `qh` — **no `docker` group, no sudo**; uses its own rootless Docker socket |
| App directory | `/srv/questions-hub/` |
| Backup secrets | `/srv/questions-hub/backup/backup.env` (`qh:qh` 0600) |
| Backup script | `/usr/local/bin/qh-backup` (`root:root` 0755) |
| systemd units | **user** units: `~qh/.config/systemd/user/questionshub-backup.{service,timer}` |
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
sudo machinectl shell qh@ /bin/bash -c '
set -a; source /srv/questions-hub/backup/backup.env; set +a
restic -r "s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic" snapshots
'
```

### Run backup manually
```bash
sudo machinectl shell qh@ /usr/local/bin/qh-backup
```

### Check logs
```bash
# User units, so journalctl needs --user-unit under that account:
sudo machinectl shell qh@ /bin/bash -c 'journalctl --user -u questionshub-backup.service -n 200 --no-pager'
sudo machinectl shell qh@ /bin/bash -c 'systemctl --user list-timers'
```

### Restore

See [`infra/backup/backups.md`](../infra/backup/backups.md) for full restore procedures, or use the scripts in `infra/backup/scripts/`.
