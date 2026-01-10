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

## Notes

HTTPS certificates are managed by Certbot (Let's Encrypt).
Do not store cert private keys in git.
