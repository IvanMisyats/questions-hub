# Nginx

This repo holds only QuestionsHub's **vhost**:

- `infra/nginx/questions.com.ua.conf` → install to `/etc/nginx/conf.d/questions.com.ua.conf`

Host-level nginx config is shared with the other site on the box and therefore lives in the
private infra repo (`vps/nginx/`), installed to:

| File | Purpose |
|---|---|
| `/etc/nginx/conf.d/00-shared.conf` | `map $connection_upgrade`, Cloudflare real-IP, `log_format cf`, `server_tokens off` |
| `/etc/nginx/conf.d/00-default-server.conf` | catch-all returning **444** for direct-IP hits and unknown `Host` headers |
| `/etc/nginx/conf.d/includes/cf-origin-tls.conf` | Origin CA posture + Authenticated Origin Pulls |
| `/etc/nginx/conf.d/includes/security-headers.conf` | HSTS, nosniff, frame-ancestors, Referrer-Policy |
| `/etc/nginx/conf.d/includes/cloudflare-real-ip.conf` | generated CF range list (monthly timer) |

## Install / update on host

```bash
sudo install -m 0644 infra/nginx/questions.com.ua.conf /etc/nginx/conf.d/
sudo nginx -t
sudo systemctl reload nginx
```

## TLS

**No Certbot.** The certificate is a **Cloudflare Origin CA** cert (15-year) at
`/etc/ssl/cloudflare/questions.com.ua.{pem,key}`, and `ssl_verify_client on` makes nginx demand a
client certificate signed by Cloudflare's origin-pull CA — so requests that skip Cloudflare (and
therefore skip its WAF and rate limits) are rejected at the TLS handshake.

Zone settings that must match: SSL/TLS mode **Full (strict)**, **Authenticated Origin Pulls On**,
**Always Use HTTPS On**.

> Consequence: `curl --resolve questions.com.ua:443:<origin-ip>` will fail by design. To test the
> origin, point a temporary **proxied** Cloudflare hostname at it and go through the real path.

Port 80 is closed at the firewall — Cloudflare reaches the origin on 443 and Origin CA certs need
no ACME challenge.

## Features

### Direct media serving

nginx serves media straight from disk, bypassing Docker and ASP.NET.

- **Path:** `/media/` → `/srv/questions-hub/uploads/handouts/`
- **Allowed extensions:** jpg, jpeg, png, gif, webp, svg, mp4, webm, ogg, mp3, wav, m4a
- **Caching:** immutable, 1-year max-age (a new upload always gets a new filename)

The path is under `/srv`, not a home directory, on purpose: Ubuntu creates home directories `0750`,
so `www-data` cannot traverse into one and the old `/home/...` alias would have 403'd.

See [docs/MEDIA_SETUP.md](../../docs/MEDIA_SETUP.md).

### Upload size

`client_max_body_size 64m` is deliberately looser than the app's own 50 MB package-import limit
(`PackageImportOptions.MaxFileSizeBytes`), so oversized uploads produce the app's Ukrainian error
message instead of a bare nginx 413. Real-world packages are ~40 MB.
