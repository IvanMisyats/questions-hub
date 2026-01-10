# Backups (OVH Object Storage + restic)

This repository includes “backup-as-code” for **QuestionsHub**: scripts + systemd units + documentation to reproduce the backup setup on any host.

## What is backed up

### Database
- Logical Postgres dump (`pg_dump`) executed inside the DB container and streamed directly into restic.
- Stored as `db/questionshub_YYYY-MM-DD.dump` in the restic repository.

Retention:
- **7 daily**, **4 weekly**, **12 monthly** snapshots (DB-tagged).

### Files
Backed up from host paths (no Docker access required for files):
- `questions-hub/uploads` (user uploads)
- `questions-hub/keys` (ASP.NET Data Protection keys)
- `/etc/nginx/conf.d/questions.com.ua.conf` (nginx vhost)
- `/home/github-actions/.env` (app env — encrypted in restic repo)

Retention:
- **30 daily** snapshots (rewind ~1 month).

## Storage backend

- OVH Object Storage (S3-compatible)
- Bucket: `lucky-chandrasekhar`
- Endpoint: `https://s3.de.io.cloud.ovh.net`
- Restic repository path: `s3:https://s3.de.io.cloud.ovh.net/lucky-chandrasekhar/restic`

## Monitoring (optional)

Healthchecks can be used for alerts if the job fails or doesn’t run:
- Set `HC_URL="https://hc-ping.com/<uuid>"` in `backup.env`
- Script pings success URL on completion and `${HC_URL}/fail` on errors.
- Healthchecks are available at https://healthchecks.io/

---

# Runtime files on the host (NOT in git)

These are created on each host:

- Backup secrets:
  - `/home/github-actions/.config/questions-hub-backup/backup.env`
- Executable backup script path used by systemd:
  - `/home/github-actions/questions-hub/infra/backup/runtime/backup.sh`
  - (Can be a symlink to the version in repo if you prefer.)
- systemd units:
  - `/etc/systemd/system/questionshub-backup.service`
  - `/etc/systemd/system/questionshub-backup.timer`
  - `/etc/systemd/system/questionshub-restic-check.service`
  - `/etc/systemd/system/questionshub-restic-check.timer`

---

# Quickstart on a new host

## 1) Install dependencies
Ubuntu/Debian:
```bash
sudo apt-get update
sudo apt-get install -y restic curl
```

## 2) Create secrets file (backup.env) on the host
Create folder + file:
```bash
sudo mkdir -p /home/github-actions/.config/questions-hub-backup
sudo chown -R github-actions:github-actions /home/github-actions/.config
sudo chmod 700 /home/github-actions/.config /home/github-actions/.config/questions-hub-backup

sudo nano /home/github-actions/.config/questions-hub-backup/backup.env
sudo chown github-actions:github-actions /home/github-actions/.config/questions-hub-backup/backup.env
sudo chmod 600 /home/github-actions/.config/questions-hub-backup/backup.env
```

Content (fill values):
```bash
export AWS_ACCESS_KEY_ID="..."
export AWS_SECRET_ACCESS_KEY="..."
export AWS_DEFAULT_REGION="de"

export OVH_S3_ENDPOINT="https://s3.de.io.cloud.ovh.net"
export OVH_S3_BUCKET="lucky-chandrasekhar"

export RESTIC_PASSWORD="a-long-random-password"

# optional
export HC_URL="https://hc-ping.com/<uuid>"
```

Verify:
```bash
sudo -u github-actions bash -lc 'source /home/github-actions/.config/questions-hub-backup/backup.env; env | grep OVH_S3_'
```

## 3) Initialize restic repository (first time only)
```bash
sudo -u github-actions bash -lc '
set -e
source /home/github-actions/.config/questions-hub-backup/backup.env
RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
restic -r "${RESTIC_REPO}" init
'
```

If the repo already exists (migrating), skip `init` and just run:
```bash
sudo -u github-actions bash -lc '
source /home/github-actions/.config/questions-hub-backup/backup.env
RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
restic -r "${RESTIC_REPO}" snapshots
'
```

## 4) Ensure github-actions can run Docker commands
DB backup calls `docker exec ... pg_dump ...`

Test:
```bash
sudo -u github-actions docker ps
```

If it fails (permission denied), add docker group:
```bash
sudo usermod -aG docker github-actions
```

Re-login or restart services, then re-test.

> Note: docker group is effectively root-equivalent. This is common but be aware.

## 5) Install script and systemd units
Copy from repo:
```bash
sudo install -m 0755 -o github-actions -g github-actions infra/backup/scripts/backup.sh   /home/github-actions/questions-hub/infra/backup/runtime/backup.sh

sudo install -m 0644 infra/backup/systemd/questionshub-backup.service /etc/systemd/system/questionshub-backup.service
sudo install -m 0644 infra/backup/systemd/questionshub-backup.timer   /etc/systemd/system/questionshub-backup.timer
sudo install -m 0644 infra/backup/systemd/questionshub-restic-check.service /etc/systemd/system/questionshub-restic-check.service
sudo install -m 0644 infra/backup/systemd/questionshub-restic-check.timer   /etc/systemd/system/questionshub-restic-check.timer

sudo systemctl daemon-reload
sudo systemctl enable --now questionshub-backup.timer
sudo systemctl enable --now questionshub-restic-check.timer
```

Run a manual smoke test:
```bash
sudo -u github-actions /home/github-actions/questions-hub/infra/backup/runtime/backup.sh
```

---

# Restore

## Restore latest DB
```bash
sudo -u github-actions bash -lc '
set -a
source /home/github-actions/.config/questions-hub-backup/backup.env
set +a
RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"

restic -r "${RESTIC_REPO}" ls latest --tag db

restic -r "${RESTIC_REPO}" dump latest --tag db "db/questionshub_*.dump"   | docker exec -i questions-hub-db pg_restore -U postgres -d questionshub --clean --if-exists
'
```

## Restore uploads/keys “rewind”
```bash
sudo -u github-actions bash -lc '
source /home/github-actions/.config/questions-hub-backup/backup.env
RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"

restic -r "${RESTIC_REPO}" snapshots --tag files
mkdir -p /tmp/qh-restore
restic -r "${RESTIC_REPO}" restore <SNAPSHOT_ID> --target /tmp/qh-restore
'
```

---

# Troubleshooting

## “Permission denied” reading backup.env
Fix ownership/permissions:
```bash
sudo chown github-actions:github-actions /home/github-actions/.config
sudo chmod 700 /home/github-actions/.config
sudo chown -R github-actions:github-actions /home/github-actions/.config/questions-hub-backup
sudo chmod 700 /home/github-actions/.config/questions-hub-backup
sudo chmod 600 /home/github-actions/.config/questions-hub-backup/backup.env
```

## Docker permission denied
```bash
sudo usermod -aG docker github-actions
```

## Logs
```bash
systemctl list-timers | grep questionshub
journalctl -u questionshub-backup.service -n 200 --no-pager
```

---

**Never** place `backup.env` inside the repo.
