# UFW firewall

Policy:
- Default deny incoming / allow outgoing / deny routed
- Logging: low

| Port | State |
|---|---|
| 55055/tcp | allow — SSH |
| 443/tcp | allow — nginx origin (further filtered by Cloudflare Authenticated Origin Pulls) |
| 80/tcp | **closed** — Cloudflare reaches the origin on 443, and Origin CA certs need no ACME |
| out 25/tcp | deny — no outbound SMTP (limits abuse if the box is compromised) |

## Apply

```bash
sudo bash infra/ufw/apply.sh          # override the SSH port with SSH_PORT=22 if needed
```

Keep a second SSH session open: the script resets ufw before reapplying.

## Why ufw is trustworthy here now

On the old box it wasn't. Rootful Docker inserts its own iptables rules ahead of ufw's, so a
container published as `5432:5432` bound `0.0.0.0` and was reachable from the internet regardless
of what `ufw status` claimed — which is exactly what the old compose file did.

This host runs **rootless Docker only** (the rootful daemon is masked at bootstrap). Rootless
Docker writes no host iptables rules; published ports are ordinary userspace sockets that ufw
filters normally. `apply.sh` still installs `DOCKER-USER` drop rules if it finds that chain, as a
guard in case a rootful daemon is ever re-enabled.
