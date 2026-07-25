# VPS Deployment Guide

How QuestionsHub is deployed in production, and how to operate it.

> **Host bootstrap is not in this repo.** Installing Docker, creating users, firewall, TLS and DNS
> are host-level concerns shared with the other application on the same box, and they live in the
> private ops repo's `vps/bootstrap.md`. This guide starts from a bootstrapped host.

## Architecture

```
GitHub Actions                    VPS
──────────────                    ───
CI: build + test
        │
CD: build image
    push ghcr.io/…:latest
         and  …:sha-abc1234
        │
        │ ssh (forced command, no shell)
        └─────────────────────────► /usr/local/bin/qh-deploy   (root-owned)
                                      docker compose pull
                                      docker compose up -d
                                      wait for /health
                                              │
                        ┌─────────────────────┴──────────────────┐
                        │  rootless Docker daemon, user `qh`     │
                        │   questions-hub-web   127.0.0.1:8080   │
                        │   questions-hub-db    (no host port)   │
                        └────────────────────────────────────────┘
                                              ▲
                              host nginx ──────┘  (TLS: Cloudflare Origin CA)
```

Three properties are deliberate:

1. **The app runs as an unprivileged user with its own rootless Docker daemon.** `qh` has no sudo
   and is not in the `docker` group — that group is root-equivalent, and this box hosts a second,
   unrelated application whose data must stay unreachable.
2. **CI cannot run commands on the host.** The deploy key is pinned server-side to a forced
   command, so the only sentence it can utter is "redeploy".
3. **CI cannot change what runs.** The compose file lives root-owned at
   `/srv/questions-hub/deploy/`; it is *not* shipped by the pipeline.

## Layout on the host

| Path | Owner | Mode | Contents |
|---|---|---|---|
| `/srv/questions-hub/deploy/` | `root:qh` | 0750 | `docker-compose.yml`, `app.env`, `db/` |
| `/srv/questions-hub/uploads/` | `qh:qh` | 0755 | user uploads; `handouts/` served by nginx |
| `/srv/questions-hub/keys/` | `qh:qh` | 0700 | ASP.NET Data Protection key ring |
| `/srv/questions-hub/backup/` | `qh:qh` | 0700 | `backup.env`, restic cache |
| `/usr/local/bin/qh-deploy` | `root:root` | 0755 | the forced command |
| `/usr/local/bin/qh-backup` | `root:root` | 0755 | backup entry point |

Postgres data is a **named Docker volume** (`questions-hub_pgdata`), not a bind mount — under
rootless Docker the container's postgres UID maps into `qh`'s subuid range, which makes host-side
bind mounts awkward for no benefit. Backups are logical dumps, so nothing is lost.

Nothing lives under `/home/`: home directories are `0750` on Ubuntu, so nginx could not traverse
into one to serve `/media/`.

## Installing / updating the deployment files

Image updates are automatic. Everything below is a deliberate admin action, because these files
are root-owned on purpose.

```bash
git clone https://github.com/IvanMisyats/questions-hub /tmp/qh && cd /tmp/qh

# Compose file + DB init scripts
sudo install -o root -g qh   -m 0640 infra/docker-compose.yml /srv/questions-hub/deploy/
sudo install -o root -g root -m 0755 -d /srv/questions-hub/deploy/db/scripts \
                                       /srv/questions-hub/deploy/db/dictionaries
sudo install -o root -g root -m 0644 db/scripts/*.sql        /srv/questions-hub/deploy/db/scripts/
sudo install -o root -g root -m 0644 db/dictionaries/uk_UA.* /srv/questions-hub/deploy/db/dictionaries/

# Deploy + backup entry points
sudo install -o root -g root -m 0755 infra/deploy/qh-deploy.sh   /usr/local/bin/qh-deploy
sudo install -o root -g root -m 0755 infra/backup/scripts/backup.sh /usr/local/bin/qh-backup

# nginx vhost
sudo install -m 0644 infra/nginx/questions.com.ua.conf /etc/nginx/conf.d/
sudo nginx -t && sudo systemctl reload nginx
```

> `db/` is world-readable (0755/0644) on purpose: the Postgres container runs as its own UID,
> which maps into a subuid range rather than to `qh`, so it cannot read `root:qh 0640` files.
> These are public SQL scripts and dictionaries — there is nothing to protect. `app.env` and the
> compose file stay `0640`.

## Secrets

`/srv/questions-hub/deploy/app.env`, owned `root:qh` mode 0640 — readable by the app user,
writable only by root, so a stolen deploy key cannot rewrite the environment its own container
runs with. Template: `infra/app.env.example`. Real values are in 1Password.

```bash
sudo install -o root -g qh -m 0640 /dev/null /srv/questions-hub/deploy/app.env
sudo nano /srv/questions-hub/deploy/app.env
```

## GitHub secrets

Three, none of which grants a shell:

| Secret | Value |
|---|---|
| `DEPLOY_SSH_KEY` | private half of the forced-command key |
| `DEPLOY_HOST` | VPS address |
| `DEPLOY_KNOWN_HOSTS` | the host's SSH public key line, so CI verifies what it connects to |

Generate `DEPLOY_KNOWN_HOSTS` once, from a machine you trust, and eyeball it against the host:

```bash
ssh-keyscan -p 55055 <vps-host>
```

The container image is **public** on GHCR, so the VPS holds no registry credential at all. Do not
reintroduce a `packages:write` PAT on the host — the previous one could push to any package on the
account, including the other application's.

## Manual operations

All as the `qh` user (`sudo machinectl shell qh@`):

```bash
cd /srv/questions-hub/deploy
alias dc='docker compose --env-file app.env'

dc ps                 # status
dc logs -f web        # follow logs
dc restart web        # restart just the app
/usr/local/bin/qh-deploy   # pull + converge (same thing CI triggers)
```

## Rollback

Every CI build publishes `:latest` **and** `:sha-<short>`. To pin an older build:

```bash
sudo sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=sha-1a2b3c4/' /srv/questions-hub/deploy/app.env
sudo machinectl shell qh@ /usr/local/bin/qh-deploy
```

Set `IMAGE_TAG=latest` again once the fix ships, or the next deploy will appear to do nothing.

## First-time / disaster-recovery bring-up

Order matters — start the database, restore, *then* start the app. Starting `web` first lets its
EF migrations and admin seeding race the `pg_restore --clean`.

```bash
cd /srv/questions-hub/deploy
docker compose --env-file app.env up -d postgres db-setup   # schema, roles, extensions, FTS
./backup/restore-db-latest.sh                                # DB from restic
./backup/restore-files.sh latest /tmp/qh-restore             # uploads + keys, then move into place
docker compose --env-file app.env up -d                      # now the app
```

**Restore `keys/`.** It is the ASP.NET Data Protection key ring; without it every existing auth
cookie and session is invalidated.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `web` exits immediately after an app change | `read_only: true` — the app tried to write outside `/tmp`, `/app/uploads`, `/app/keys` |
| `permission denied` on the Docker socket | `DOCKER_HOST` unset in a non-login shell. Export `DOCKER_HOST=unix:///run/user/$(id -u)/docker.sock` |
| Containers gone after reboot | `loginctl enable-linger qh` missing, or the user `docker` unit not enabled |
| `db-setup` cannot read `/scripts` | `deploy/db/` is not world-readable — see the note above |
| 413 on a large import | `client_max_body_size` in the vhost is below the app's 50 MB limit |
| `curl --resolve` to the origin fails TLS | Working as intended — Authenticated Origin Pulls. Test via a proxied Cloudflare hostname |
| Deploy step succeeds but nothing changed | `IMAGE_TAG` is pinned to an old `sha-…` in `app.env` |

## See also

- [`DOCKER_PROFILES.md`](DOCKER_PROFILES.md) — local dev profiles (production is a separate file)
- [`../infra/backup/backups.md`](../infra/backup/backups.md) — backup + restore runbook
- [`../infra/nginx/README.md`](../infra/nginx/README.md) — vhost, TLS, Cloudflare
- [`../infra/ufw/README.md`](../infra/ufw/README.md) — firewall policy
