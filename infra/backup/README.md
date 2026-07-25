# Backup-as-code (restic → OVH Object Storage)

Scripts and systemd units to back up QuestionsHub.

- **DB:** `pg_dump` inside the Postgres container, streamed straight into restic (no temp file)
- **Files:** `uploads/`, `keys/`, `app.env`, the nginx vhost
- **Storage:** OVH Object Storage bucket `lucky-chandrasekhar` (DE), host-tag `questions-hub`
- **Retention:** DB 7 daily / 4 weekly / 12 monthly · files 30 daily

## Isolation

AnkiLearner shares the VPS but **not** the backups: it has its own bucket, its own S3 access key
and its own `RESTIC_PASSWORD`. That separation is required, not cosmetic — each app's file
snapshot includes that app's `app.env`, so a shared repository would hand one app's secrets to
whoever holds the other's credentials.

The backup runs as the unprivileged `qh` user through a **user** systemd timer, talking to qh's
own rootless Docker socket. It needs **no `docker` group** (that group is root-equivalent) and no
`appgroup`/GID-10000 membership — under rootless Docker the files are plainly `qh:qh`.

## Files on the host (NOT in git)

| Path | Owner | Mode |
|---|---|---|
| `/srv/questions-hub/backup/backup.env` | `qh:qh` | 0600 |
| `/usr/local/bin/qh-backup` | `root:root` | 0755 |
| `~qh/.config/systemd/user/questionshub-*.{service,timer}` | `qh:qh` | 0644 |

Use `backup.env.example` as the template; real values are in 1Password.

## Install

```bash
sudo install -o root -g root -m 0755 infra/backup/scripts/backup.sh /usr/local/bin/qh-backup
sudo install -o root -g qh   -m 0750 -d /srv/questions-hub/deploy/backup
sudo install -o root -g qh   -m 0750 infra/backup/scripts/restore-*.sh /srv/questions-hub/deploy/backup/

sudo -u qh install -d -m 0755 /home/qh/.config/systemd/user
sudo -u qh install -m 0644 infra/backup/systemd/* /home/qh/.config/systemd/user/

sudo machinectl shell qh@ /bin/bash -c '
  systemctl --user daemon-reload &&
  systemctl --user enable --now questionshub-backup.timer questionshub-restic-check.timer'
```

> **Do not run `restic init`.** This repository already exists and must keep its existing
> `RESTIC_PASSWORD`; a new one makes every stored snapshot undecryptable.

See `backups.md` for the full runbook, including restores and the migration procedure.
