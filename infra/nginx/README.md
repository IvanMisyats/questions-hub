# Nginx

The site vhost is stored in this repo as:
- `infra/nginx/questions.com.ua.conf`

On the server it must be installed to:
- `/etc/nginx/conf.d/questions.com.ua.conf`

## Install / update on host
```bash
sudo cp infra/nginx/questions.com.ua.conf /etc/nginx/conf.d/questions.com.ua.conf
sudo nginx -t
sudo systemctl reload nginx
```

## Features

### Direct Media Serving

Nginx serves media files directly from the VPS filesystem (`/home/github-actions/questions-hub/uploads/handouts/`), bypassing Docker and ASP.NET for optimal performance.

- **Path:** `/media/` → `/home/github-actions/questions-hub/uploads/handouts/`
- **Allowed extensions:** jpg, jpeg, png, gif, webp, svg, mp4, webm, ogg, mp3, wav, m4a
- **Caching:** Immutable, 1 year max-age (files are immutable - new upload = new filename)

See [docs/MEDIA_SETUP.md](../../docs/MEDIA_SETUP.md) for detailed documentation.

## Notes

HTTPS certificates are managed by Certbot (Let's Encrypt).
Do not store cert private keys in git.
