# Backups (OVH Object Storage + restic)

"Backup-as-code" for **QuestionsHub**: scripts, systemd units, and the runbook to reproduce the
setup on any host.

## What is backed up

### Database
Logical `pg_dump` executed inside the DB container and streamed straight into restic (no temp
file, no disk headroom needed). Stored as `db/questionshub_YYYY-MM-DD.dump`, tag `db`.

Retention: **7 daily · 4 weekly · 12 monthly**.

### Files
Tag `files`, from host paths:

- `/srv/questions-hub/uploads` — user uploads
- `/srv/questions-hub/keys` — **ASP.NET Data Protection key ring**
- `/srv/questions-hub/deploy/app.env` — app secrets (encrypted inside restic)
- `/etc/nginx/conf.d/questions.com.ua.conf` — the vhost

Retention: **30 daily**.

> `keys/` matters as much as the database. Restore it, or every existing auth cookie and session
> is invalidated the moment the app comes back up.

## Storage backend

| | |
|---|---|
| Backend | OVH Object Storage (S3-compatible) |
| Endpoint | `https://s3.de.io.cloud.ovh.net` (region `de`) |
| Bucket | `lucky-chandrasekhar` |
| restic repo | `s3:https://s3.de.io.cloud.ovh.net/lucky-chandrasekhar/restic` |
| host-tag | `questions-hub` |

**AnkiLearner shares the VPS but not the backups.** It has its own bucket, its own S3 access key
and its own `RESTIC_PASSWORD`. This is required rather than tidy: each app's file snapshot
contains that app's `app.env`, so a shared repository would hand one app's secrets to whoever
holds the other's credentials. The S3 key here must be scoped to this bucket only.

## Who runs it

The unprivileged `qh` user, via a **user** systemd timer.

It needs **no `docker` group** — that group is root-equivalent, and this box hosts a second
application whose data must stay unreachable — and no `appgroup`/GID-10000 membership. Both were
required on the old box; under rootless Docker the app's files are plainly `qh:qh` and `pg_dump`
runs through qh's own Docker socket at `/run/user/$(id -u)/docker.sock`.

## Files on the host (never in git)

| Path | Owner | Mode |
|---|---|---|
| `/srv/questions-hub/backup/backup.env` | `qh:qh` | 0600 |
| `/srv/questions-hub/backup/restic-cache/` | `qh:qh` | 0700 |
| `/usr/local/bin/qh-backup` | `root:root` | 0755 |
| `/srv/questions-hub/deploy/backup/restore-*.sh` | `root:qh` | 0750 |
| `~qh/.config/systemd/user/questionshub-*.{service,timer}` | `qh:qh` | 0644 |

---

## Quickstart on a new host

Assumes the host is already bootstrapped (rootless Docker for `qh`, `/srv` layout, linger enabled)
per the private ops repo's `vps/bootstrap.md`.

### 1) Install restic

```bash
sudo apt-get install -y restic
```

### 2) Create the credentials file

```bash
sudo install -o qh -g qh -m 0700 -d /srv/questions-hub/backup
sudo install -o qh -g qh -m 0600 /dev/null /srv/questions-hub/backup/backup.env
sudo nano /srv/questions-hub/backup/backup.env      # template: backup.env.example
```

### 3) Point at the existing repository — do NOT `restic init`

The restic repository is portable and **already exists**. Initialising over it, or using a
different `RESTIC_PASSWORD`, makes every stored snapshot undecryptable.

```bash
sudo machinectl shell qh@ /bin/bash -c '
  set -a; source /srv/questions-hub/backup/backup.env; set +a
  restic -r "s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic" snapshots | tail -20'
```

Seeing the existing snapshots proves the endpoint, key and password are all correct.

### 4) Install the scripts and timers

```bash
sudo install -o root -g root -m 0755 infra/backup/scripts/backup.sh /usr/local/bin/qh-backup
sudo install -o root -g qh   -m 0750 -d /srv/questions-hub/deploy/backup
sudo install -o root -g qh   -m 0750 infra/backup/scripts/restore-*.sh /srv/questions-hub/deploy/backup/

sudo -u qh install -d -m 0755 /home/qh/.config/systemd/user
sudo -u qh install -m 0644 infra/backup/systemd/* /home/qh/.config/systemd/user/

sudo machinectl shell qh@ /bin/bash -c '
  systemctl --user daemon-reload &&
  systemctl --user enable --now questionshub-backup.timer questionshub-restic-check.timer &&
  systemctl --user list-timers'
```

### 5) Smoke test

```bash
sudo machinectl shell qh@ /usr/local/bin/qh-backup
```

Then confirm a fresh snapshot landed:

```bash
sudo machinectl shell qh@ /bin/bash -c '
  set -a; source /srv/questions-hub/backup/backup.env; set +a
  restic -r "s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic" snapshots --host questions-hub | tail -5'
```

---

## Restore

### Database

```bash
sudo machinectl shell qh@
cd /srv/questions-hub/deploy
./backup/restore-db-latest.sh
```

**Order matters on a fresh host.** Bring up only the database first, restore, then start the app:

```bash
docker compose --env-file app.env up -d postgres db-setup   # schema, roles, extensions, FTS
./backup/restore-db-latest.sh
docker compose --env-file app.env up -d                     # now the app
```

Starting `web` before the restore lets its EF migrations and admin seeding race
`pg_restore --clean`, which can leave a half-overwritten schema.

### Files

```bash
./backup/restore-files.sh latest /tmp/qh-restore
rsync -a /tmp/qh-restore/srv/questions-hub/uploads/ /srv/questions-hub/uploads/
rsync -a /tmp/qh-restore/srv/questions-hub/keys/    /srv/questions-hub/keys/
chmod 700 /srv/questions-hub/keys
```

No `chown` to UID 10000 any more — under rootless Docker the container writes as `qh`.

---

## Verification and maintenance

- `questionshub-restic-check.timer` runs `restic check` weekly.
- `HC_URL` (Healthchecks.io) is pinged on success and on failure — set it, otherwise a silently
  broken backup looks identical to a working one.
- **Run a restore drill quarterly.** A backup that has never been restored is a hypothesis.
  Restore into a scratch database and confirm row counts, not just that the command exited 0.
- Consider `restic copy` to a non-OVH bucket: OVH snapshots plus OVH object storage means a single
  provider holds every copy.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `permission denied` on the Docker socket | `DOCKER_HOST` unset — the scripts export it, but ad-hoc shells need `export DOCKER_HOST=unix:///run/user/$(id -u)/docker.sock` |
| Timer never fires | `loginctl enable-linger qh` missing; user timers only run while the user has a session |
| `wrong password or no key found` | `RESTIC_PASSWORD` differs from the one the repo was created with. There is no recovery — check 1Password |
| `pg_dump: error: connection` | The stack is down, or the service is named something other than `postgres` in the compose project |
| Backup succeeds but files are missing | A path in `backup.sh` does not exist on this host — restic warns and continues |
