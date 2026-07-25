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

- **Encryption mode: Full (Strict)** — both legs encrypted (visitor→Cloudflare, Cloudflare→origin)
- **Origin certificate: Cloudflare Origin CA**, 15-year, at
  `/etc/ssl/cloudflare/questions.com.ua.{pem,key}`. There is **no Certbot on the host** — no ACME,
  no renewal timer, and port 80 is closed at the firewall.
- **Authenticated Origin Pulls: ON.** nginx runs `ssl_verify_client on` against Cloudflare's
  origin-pull CA, so a request that skips Cloudflare — and therefore skips the WAF and the rate
  limits below — is rejected during the TLS handshake.
- Always Use HTTPS: ON
- Minimum TLS: 1.2

> Consequence worth remembering: `curl --resolve questions.com.ua:443:<origin-ip>` cannot work.
> To test the origin directly, point a temporary **proxied** Cloudflare hostname at it (e.g.
> `new.questions.com.ua`) and exercise the real path.

## Page rules

| Priority | Pattern | Action |
|----------|---------|--------|
| 1 | `questions.com.ua/_blazor/*` | Cache Level: Bypass |
| 2 | `questions.com.ua/media/*` | Cache Everything, Edge TTL: 1 month |

## Origin protection

### Authenticated Origin Pulls (primary control)

This is what actually keeps traffic from bypassing Cloudflare. An IP allowlist was considered and
rejected: it needs a refresh treadmill and fails closed on the site if the list goes stale, while
mTLS against a static CA proves the same thing cryptographically and needs no upkeep.

### Nginx — real IP restoration

`/etc/nginx/conf.d/00-shared.conf` (installed from the private ops repo) sets
`real_ip_header CF-Connecting-IP` and includes the generated trusted-range list at
`/etc/nginx/conf.d/includes/cloudflare-real-ip.conf`, so `$remote_addr` and the `X-Forwarded-For`
handed to ASP.NET reflect the real visitor rather than a Cloudflare edge.

### Updating Cloudflare IPs

Automatic: `/usr/local/sbin/cf-ips-refresh` runs monthly, regenerates the include from
<https://www.cloudflare.com/ips-v4> and `ips-v6`, validates with `nginx -t`, and rolls itself back
if the result doesn't parse.

Because access control is enforced by Authenticated Origin Pulls, a stale list degrades only
logging accuracy and per-IP rate limiting — it can never take the site down.

## Nginx rate limiting

`infra/nginx/questions.com.ua.conf` defines two rate limit zones using the real visitor IP (restored from `CF-Connecting-IP`):

| Zone | Rate | Burst | Applied to |
|------|------|-------|------------|
| `qh_api_zone` | 30 req/min per IP | 10 | `/api/v1/` (public API) |
| `qh_auth_zone` | 5 req/min per IP | 3 | `/api/Auth/` (login/register) |

Zone names are namespaced `qh_*` because a second application shares this nginx instance and
`limit_req_zone` lives in the shared http context.

These are first-line defenses before requests reach ASP.NET, which has its own per-API-key rate limiting.

## Do NOT enable

| Setting | Why |
|---------|-----|
| Rocket Loader | Breaks Blazor JS bootstrap |
| SSL Flexible mode | Creates redirect loops; the origin has a valid Origin CA cert |
| Turning off Authenticated Origin Pulls | nginx would still demand a client cert and every request would fail |
| Proxy on `mail` / non-HTTP records | Cloudflare only proxies HTTP/HTTPS |
