# Cloudflare

Site is proxied through **Cloudflare Free** tier for DDoS protection, bot mitigation, and edge caching.

## Critical Blazor Server settings

These must stay as-is or the app will break:

| Setting | Value | Why |
|---------|-------|-----|
| Rocket Loader | **OFF** | Rewrites JS loading, breaks Blazor boot |
| WebSockets | **ON** | Required for SignalR (Blazor Server) |
| Page Rule: `_blazor/*` | **Cache Bypass** (priority 1) | SignalR must never be cached |

## SSL/TLS

- **Encryption mode: Full (Strict)** — both legs encrypted (visitor-to-Cloudflare, Cloudflare-to-origin)
- **Let's Encrypt** runs on the VPS and provides the origin certificate — keep it active
- Always Use HTTPS: ON
- Minimum TLS: 1.2

## Page rules

| Priority | Pattern | Action |
|----------|---------|--------|
| 1 | `questions.com.ua/_blazor/*` | Cache Level: Bypass |
| 2 | `questions.com.ua/media/*` | Cache Everything, Edge TTL: 1 month |

## Origin protection

### Nginx — real IP restoration

`infra/nginx/questions.com.ua.conf` includes `set_real_ip_from` directives for all Cloudflare IP ranges and `real_ip_header CF-Connecting-IP`. This ensures `$remote_addr` in nginx (and `X-Forwarded-For` passed to ASP.NET) reflects the actual visitor IP, not a Cloudflare edge IP.

### Updating Cloudflare IPs

Cloudflare publishes their IP ranges at https://www.cloudflare.com/ips/. If they change, update `set_real_ip_from` directives in `infra/nginx/questions.com.ua.conf`.

## Nginx rate limiting

`infra/nginx/questions.com.ua.conf` defines two rate limit zones using the real visitor IP (restored from `CF-Connecting-IP`):

| Zone | Rate | Burst | Applied to |
|------|------|-------|------------|
| `api_zone` | 30 req/min per IP | 10 | `/api/v1/` (public API) |
| `auth_zone` | 5 req/min per IP | 3 | `/api/Auth/` (login/register) |

These are first-line defenses before requests reach ASP.NET, which has its own per-API-key rate limiting.

## Do NOT enable

| Setting | Why |
|---------|-----|
| Rocket Loader | Breaks Blazor JS bootstrap |
| SSL Flexible mode | Creates redirect loops; origin has a valid cert |
| Proxy on `mail` / non-HTTP records | Cloudflare only proxies HTTP/HTTPS |
