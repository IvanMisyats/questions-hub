"""Find rating.chgk.info tournaments that were played on Questions Hub packages.

For each package id: reads the package through the Agent API, scores every rating tournament
(name similarity, editors, dates), adds "siblings" of the strong candidates (same editor set,
start within 45 days: language versions, synchron/asynchron pairs), and verifies the question
count of each against the package's regular tours. Read-only on both sites.

Usage (from the repo root):
    set QH_AGENT_TOKEN=qh_pat_...        (or: --token-file <file with "mcp_token:qh_pat_..." or the bare token>)
    python -I -X utf8 tools/rating/find_tournaments.py 140 141 [--refresh] [--since 2014-06-01] [--top 8]

The tournament list (~10k entries since 2014) is cached for a day in %TEMP%/qh-rating-cache.
Background, matching rules and pitfalls: docs/RATING_SITE.md.
"""
import argparse, json, os, re, sys, tempfile, time, urllib.error, urllib.request
from datetime import date, datetime, timedelta, timezone

RATING_API = 'https://api.rating.chgk.info'
QH_API = 'https://questions.com.ua/api/v1/manage'
UA = 'questions-hub-rating-tools/1.0'  # Cloudflare rejects Python's default User-Agent


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """urllib forwards the Authorization header on redirects, even to another host: refuse them instead."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise urllib.error.HTTPError(req.full_url, code, f'refusing redirect to {newurl} for an authenticated request', headers, fp)


_AUTHENTICATED = urllib.request.build_opener(_NoRedirect)


def get_json(url, token=None, accept='application/json'):
    headers = {'User-Agent': UA, 'Accept': accept}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    opener = _AUTHENTICATED if token else urllib.request.build_opener()
    with opener.open(urllib.request.Request(url, headers=headers), timeout=60) as r:
        return json.load(r)


def read_token(path):
    if path:
        text = open(path, encoding='utf8').read()
        m = re.search(r'qh_pat_\w+', text)
        if m:
            return m.group(0)
    token = os.environ.get('QH_AGENT_TOKEN')
    if not token:
        sys.exit('Set QH_AGENT_TOKEN or pass --token-file.')
    return token.strip()


def load_tournaments(since, refresh):
    cache_dir = os.path.join(tempfile.gettempdir(), 'qh-rating-cache')
    os.makedirs(cache_dir, exist_ok=True)
    path = os.path.join(cache_dir, f'tournaments-{since}.json')
    if not refresh and os.path.exists(path) and time.time() - os.path.getmtime(path) < 86400:
        return json.load(open(path, encoding='utf8'))
    result, page = {}, 1
    while True:
        batch = get_json(f'{RATING_API}/tournaments?itemsPerPage=512&page={page}&dateStart%5Bafter%5D={since}')
        for t in batch:
            result[t['id']] = t
        if len(batch) < 512:
            break
        page += 1
    tournaments = list(result.values())
    json.dump(tournaments, open(path, 'w', encoding='utf8'), ensure_ascii=False)
    return tournaments


# Ukrainian and Russian spellings of the same name collapse to one form ("Ільїн" ~ "Ильин", "Мисяць" ~ "Місяць")
_FOLD = str.maketrans({'і': 'и', 'ї': 'и', 'й': 'и', 'є': 'е', 'ґ': 'г', 'ы': 'и', 'э': 'е', 'ё': 'е',
                       'ъ': None, 'ь': None, '’': None, "'": None, 'ʼ': None, '`': None})


def norm(s):
    return re.sub(r'[^\w]+', ' ', (s or '').lower().translate(_FOLD)).strip()


def tokens(s):
    return {t for t in norm(s).split() if len(t) > 1}


def kyiv_date(iso):
    """Rating API times are UTC; organizers set local midnights (Kyiv or Moscow), so convert before taking the date."""
    u = datetime.fromisoformat(iso).astimezone(timezone.utc).replace(tzinfo=None)

    def last_sunday(month):
        d = datetime(u.year, month + 1, 1) - timedelta(days=1)
        return d - timedelta(days=(d.weekday() + 1) % 7)

    summer = last_sunday(3).replace(hour=1) <= u < last_sunday(10).replace(hour=1)
    return (u + timedelta(hours=3 if summer else 2)).date().isoformat()


def package_summary(pkg):
    editors = {}
    for e in pkg['editors']:
        editors[e['id']] = e
    for t in pkg['tours']:
        for e in t['editors']:
            editors[e['id']] = e
        for b in t['blocks']:
            for e in b.get('editors', []):
                editors[e['id']] = e
    regular = [len(t['questions']) + sum(len(b['questions']) for b in t['blocks'])
               for t in pkg['tours'] if t['type'] == 'regular']
    return editors.values(), regular


def score(pkg, surnames, tournament):
    name = len(tokens(pkg['title']) & tokens(tournament['name'] + ' ' + (tournament.get('longName') or ''))) / max(1, len(tokens(pkg['title'])))
    eds = len(surnames & {norm(e['surname']) for e in tournament.get('editors', [])})
    when = 0
    if pkg['playedFrom']:
        delta = abs((date.fromisoformat(tournament['dateStart'][:10]) - date.fromisoformat(pkg['playedFrom'])).days)
        when = 1 if delta < 60 else 0.5 if delta < 200 else 0
    return name * 3 + eds * 1.5 + when, name, eds, when


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('packages', nargs='+', type=int)
    ap.add_argument('--token-file')
    ap.add_argument('--since', default='2014-06-01')
    ap.add_argument('--top', type=int, default=8)
    ap.add_argument('--refresh', action='store_true', help='re-download the tournament list')
    args = ap.parse_args()

    token = read_token(args.token_file)
    tournaments = load_tournaments(args.since, args.refresh)
    by_id = {t['id']: t for t in tournaments}

    for pid in args.packages:
        pkg = get_json(f'{QH_API}/packages/{pid}', token)
        editors, regular = package_summary(pkg)
        surnames = {norm(e['lastName']) for e in editors}
        print(f"\n=== {pid} {pkg['title']} | {pkg['gameType']} | dates {pkg['playedFrom']}..{pkg['playedTo']} "
              f"| regular tours {regular} = {sum(regular)} | hasResults={pkg['hasResults']}")
        print('    editors: ' + ', '.join(f"{e['firstName']} {e['lastName']}" for e in editors))

        scored = []
        for t in tournaments:
            s, name, eds, when = score(pkg, surnames, t)
            if name >= 0.5 or eds >= 2 or (eds >= 1 and when > 0):
                scored.append((s, t))
        scored.sort(key=lambda x: -x[0])
        shortlist = [t for _, t in scored[:args.top]]

        # Siblings of the strong candidates: identical editor set, start within 45 days
        seen = {t['id'] for t in shortlist}
        for t in list(shortlist[:3]):
            ids = frozenset(e['id'] for e in t.get('editors', []))
            if not ids:
                continue
            for o in tournaments:
                if o['id'] not in seen and frozenset(e['id'] for e in o.get('editors', [])) == ids \
                        and abs((date.fromisoformat(o['dateStart'][:10]) - date.fromisoformat(t['dateStart'][:10])).days) <= 45:
                    seen.add(o['id'])
                    shortlist.append(o)

        for t in shortlist:
            detail = get_json(f"{RATING_API}/tournaments/{t['id']}")
            qty = detail.get('questionQty') or {}
            per_tour = [qty[k] for k in sorted(qty, key=int)]
            check = 'OK ' if sum(per_tour) == sum(regular) else 'CNT'
            if per_tour != regular and check == 'OK ':
                check = 'OK~'  # same total, different tour split: stats still map, with a warning
            langs = '/'.join(l['id'] for l in detail.get('languages', []))
            print(f"  {check} {t['id']} {detail['name'].strip()} [{detail['type']['name']}] {langs} "
                  f"{kyiv_date(detail['dateStart'])}..{kyiv_date(detail['dateEnd'])} q={per_tour} "
                  f"teams={t.get('teamsCount')} eds={', '.join(e['surname'] for e in detail.get('editors', []))}")
            time.sleep(0.2)
    print('\nOK = total and per-tour counts match; OK~ = total matches; CNT = count mismatch (standings only).')


if __name__ == '__main__':
    main()
