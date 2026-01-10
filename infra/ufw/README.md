# UFW firewall

This server uses UFW with:
- Default deny incoming
- Default allow outgoing
- Default deny routed
- Logging: low

Allowed inbound ports:
- 80/tcp (HTTP)
- 443/tcp (HTTPS)
- 55055/tcp (SSH)

## Apply rules
Run on the host as an admin with sudo:
```bash
bash infra/ufw/apply.sh
```

