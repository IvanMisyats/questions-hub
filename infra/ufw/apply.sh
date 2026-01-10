#!/usr/bin/env bash
set -euo pipefail

# WARNING:
# Before running this on a remote server, confirm your SSH port is allowed.
# If you use SSH on port 22, add: sudo ufw allow 22/tcp
# If you use SSH on port 55055, ensure it stays allowed (it does below).

sudo ufw --force reset

sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw default deny routed

sudo ufw logging low

# Web
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Custom port
sudo ufw allow 55055/tcp

sudo ufw --force enable
sudo ufw status verbose
