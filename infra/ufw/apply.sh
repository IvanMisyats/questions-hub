#!/usr/bin/env bash
# Apply the host firewall policy. Idempotent — safe to re-run.
#
#   sudo ./apply.sh
#
# WARNING: this resets ufw. Keep a second SSH session open while you run it, and make sure
# SSH_PORT below matches the port sshd is actually listening on.
#
# Context: this host runs ROOTLESS Docker only (the rootful daemon is masked at bootstrap).
# Rootless Docker writes no host iptables rules, so — unlike the old box — published container
# ports are ordinary userspace sockets and ufw genuinely filters them. The DOCKER-USER section at
# the end is belt-and-braces should a rootful daemon ever be re-enabled.

set -euo pipefail

SSH_PORT="${SSH_PORT:-55055}"

if [[ "${EUID}" -ne 0 ]]; then
    echo "Run as root: sudo $0" >&2
    exit 1
fi

# ── IPv6 must be enabled: this box has a public IPv6 address ─────────
if ! grep -qE '^IPV6=yes' /etc/default/ufw; then
    echo "Enabling IPV6=yes in /etc/default/ufw"
    sed -i 's/^IPV6=.*/IPV6=yes/' /etc/default/ufw
fi

ufw --force reset

ufw default deny incoming
ufw default allow outgoing
ufw default deny routed
ufw logging low

# ── Inbound ──────────────────────────────────────────────────────────
ufw allow "${SSH_PORT}/tcp" comment 'ssh'
ufw allow 443/tcp            comment 'nginx origin (Cloudflare-authenticated)'

# Port 80 is deliberately NOT opened. Cloudflare reaches the origin on 443 under Full (strict),
# and TLS is a Cloudflare Origin CA certificate, so no ACME HTTP-01 challenge ever runs here.
# If you ever move back to Let's Encrypt you will need: ufw allow 80/tcp

# ── Outbound ─────────────────────────────────────────────────────────
# Neither app sends mail directly (QuestionsHub uses the Mailjet HTTPS API). Blocking SMTP
# limits how useful this box is as a spam relay if it is ever compromised.
ufw deny out 25/tcp comment 'no outbound smtp'

ufw --force enable

# ── DOCKER-USER guard (no-op while only rootless Docker is in use) ───
# Rootful Docker inserts its rules ahead of ufw's, so a container published on 0.0.0.0 becomes
# internet-reachable regardless of ufw. If that chain exists, drop external traffic into it.
if command -v iptables >/dev/null 2>&1 && iptables -nL DOCKER-USER >/dev/null 2>&1; then
    echo "DOCKER-USER chain found (rootful Docker active?) — installing guard rules."
    PUBLIC_IF="$(ip route show default | awk '/default/ {print $5; exit}')"
    iptables -D DOCKER-USER -i "${PUBLIC_IF}" -m conntrack --ctstate ESTABLISHED,RELATED -j RETURN 2>/dev/null || true
    iptables -D DOCKER-USER -i "${PUBLIC_IF}" -j DROP 2>/dev/null || true
    iptables -I DOCKER-USER 1 -i "${PUBLIC_IF}" -j DROP
    iptables -I DOCKER-USER 1 -i "${PUBLIC_IF}" -m conntrack --ctstate ESTABLISHED,RELATED -j RETURN
    echo "NOTE: these iptables rules are not persistent across reboot. Either install"
    echo "      iptables-persistent, or (better) keep the rootful daemon masked."
else
    echo "No DOCKER-USER chain — rootful Docker is off, as intended. Skipping guard rules."
fi

echo
ufw status verbose
