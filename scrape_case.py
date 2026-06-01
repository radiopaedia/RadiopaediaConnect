"""
Radiopaedia Case Scraper
Fetches a case page and extracts all viewer data, S3 URLs, and API endpoints
to understand how DICOM files are served/downloaded client-side.

Usage:
    python scrape_case.py
    python scrape_case.py --cookies "_session=abc123; user_id=456"
    python scrape_case.py --url "https://radiopaedia.org/cases/MY-CASE-SLUG"
"""

import re
import sys
import json
import argparse
import requests
from bs4 import BeautifulSoup
from pprint import pprint

# ── Config ───────────────────────────────────────────────────────────────────
DEFAULT_URL = "https://radiopaedia.org/cases/ct-trauma-cta-chest-pv-abdo-pelvis-t-l-spine"

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/124.0.0.0 Safari/537.36"
    ),
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "Accept-Language": "en-US,en;q=0.9",
    "Referer": "https://radiopaedia.org/",
}

# ── Helpers ───────────────────────────────────────────────────────────────────

def p(msg=""):
    """Print with ASCII-safe fallback for Windows console."""
    try:
        print(msg)
    except UnicodeEncodeError:
        print(msg.encode("ascii", "replace").decode("ascii"))

def sep(title=""):
    line = "-" * 70
    p(f"\n{line}")
    if title:
        p(f"  {title}")
        p(line)

def extract_json_blobs(text):
    """Pull every JSON-looking blob out of a JS string."""
    results = []
    for m in re.finditer(r'(?:var\s+\w+|window\.\w+|\w+)\s*=\s*(\{.*?\});', text, re.DOTALL):
        try:
            obj = json.loads(m.group(1))
            results.append(obj)
        except Exception:
            pass
    for m in re.finditer(r'=\s*(\[.*?\]);', text, re.DOTALL):
        try:
            obj = json.loads(m.group(1))
            results.append(obj)
        except Exception:
            pass
    return results

def find_urls_in_text(text, patterns):
    hits = []
    for pat in patterns:
        for m in re.finditer(pat, text):
            hits.append(m.group(0))
    return list(dict.fromkeys(hits))

def deep_search(obj, keywords, path=""):
    """Recursively walk a JSON object and return (path, value) for matching keys."""
    found = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            cur = f"{path}.{k}" if path else k
            if any(kw in str(k).lower() for kw in keywords):
                found.append((cur, v))
            found.extend(deep_search(v, keywords, cur))
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            found.extend(deep_search(item, keywords, f"{path}[{i}]"))
    return found

def parse_cookie_string(cookie_str):
    """Turn 'name=val; name2=val2' into a dict."""
    cookies = {}
    for part in cookie_str.split(";"):
        part = part.strip()
        if "=" in part:
            k, _, v = part.partition("=")
            cookies[k.strip()] = v.strip()
    return cookies

# ── Main ───────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Scrape a Radiopaedia case viewer for S3/DICOM URLs")
    parser.add_argument("--url", default=DEFAULT_URL, help="Case URL to scrape")
    parser.add_argument("--cookies", default="", help="Cookie string from browser (e.g. '_session=abc; user_id=1')")
    args = parser.parse_args()

    case_url = args.url
    slug = case_url.rstrip("/").split("/")[-1]

    session = requests.Session()
    session.headers.update(HEADERS)

    if args.cookies:
        session.cookies.update(parse_cookie_string(args.cookies))
        p(f"[auth] Using {len(session.cookies)} cookie(s)")

    p(f"\nScraping: {case_url}")

    resp = session.get(case_url, timeout=30)
    p(f"HTTP {resp.status_code}  ({len(resp.text):,} chars)  final URL: {resp.url}")

    if resp.status_code != 200:
        p(f"\n[!] Non-200 response. First 800 chars:\n{resp.text[:800]}")
        p("\nHint: If this case is a draft or requires login, grab your session")
        p("cookie from the browser DevTools (Application > Cookies > radiopaedia.org)")
        p("then run:  python scrape_case.py --cookies \"_session=XXXX; ...\"")
        sys.exit(1)

    soup = BeautifulSoup(resp.text, "html.parser")
    all_script_text = "\n".join(s.get_text() for s in soup.find_all("script"))

    # ── Section 1: S3 / CDN / presigned URLs ────────────────────────────────
    sep("[1] S3 / CDN / DICOM URLs in page source")
    url_patterns = [
        r'https?://[^\s\'"<>{}\[\]\\]+\.s3[^\s\'"<>{}\[\]\\]*',
        r'https?://s3[^\s\'"<>{}\[\]\\]+',
        r'https?://[^\s\'"<>{}\[\]\\]*cloudfront[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*\.dcm[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*dicom[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*/direct_s3[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*/image_preparation[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*/uploads/[^\s\'"<>{}\[\]\\]*',
        r'https?://[^\s\'"<>{}\[\]\\]*radiopaedia[^\s\'"<>{}\[\]\\]*(?:dcm|dicom)[^\s\'"<>{}\[\]\\]*',
    ]
    cdn_hits = find_urls_in_text(resp.text, url_patterns)
    if cdn_hits:
        for u in cdn_hits:
            p(f"  {u}")
    else:
        p("  (none found)")

    # ── Section 2: data-* attributes ────────────────────────────────────────
    sep("[2] data-* attributes on viewer/stack/image elements")
    interesting_data = [
        "stack", "image", "dicom", "series", "study", "viewer",
        "url", "src", "path", "file", "wado", "s3", "upload", "json"
    ]
    found_data = False
    for tag in soup.find_all(True):
        for attr, val in tag.attrs.items():
            if isinstance(val, str) and any(kw in attr.lower() for kw in interesting_data):
                p(f"  <{tag.name}> [{attr}] = {str(val)[:300]}")
                found_data = True
    if not found_data:
        p("  (none found)")

    # ── Section 3: JSON blobs in scripts ─────────────────────────────────────
    sep("[3] JSON blobs in inline scripts — keys: url/s3/series/upload/dicom")
    search_keys = [
        "url", "src", "path", "s3", "dicom", "series", "stack",
        "upload", "image", "file", "wado", "presign", "download",
        "signed", "bucket", "key", "host", "token", "credential",
        "image_format", "stack_upload", "uploaded_data"
    ]
    any_json = False
    for script in soup.find_all("script"):
        text = script.get_text()
        blobs = extract_json_blobs(text)
        for blob in blobs:
            hits = deep_search(blob, search_keys)
            if hits:
                any_json = True
                preview = text.replace("\n", " ").strip()[:80]
                p(f"\n  -- from script: {preview} --")
                for path, val in hits:
                    p(f"    [{path}] = {str(val)[:250]}")
    if not any_json:
        p("  (none found)")

    # ── Section 4: window.* global JS vars ───────────────────────────────────
    sep("[4] window.* assignments")
    window_hits = re.findall(r'window\.(\w+)\s*=\s*([^\n;]{0,400})', all_script_text)
    if window_hits:
        for name, val in window_hits:
            p(f"  window.{name} = {val[:200].strip()}")
    else:
        p("  (none found)")

    # ── Section 5: Look for Cornerstone / WADO / viewer config ───────────────
    sep("[5] Cornerstone / WADO / imageLoader config in scripts")
    viewer_patterns = [
        r'wadouri[^\s\'"<>{}\[\]\\]*',
        r'wadoRs[^\s\'"<>{}\[\]\\]*',
        r'cornerstoneWADOImageLoader[^\n]{0,200}',
        r'imageLoader[^\n]{0,200}',
        r'createImageId[^\n]{0,200}',
        r'cornerstoneTools[^\n]{0,200}',
        r'/wado[^\s\'"<>{}\[\]\\]*',
        r'dicomweb[^\s\'"<>{}\[\]\\]*',
        r'DICOMweb[^\s\'"<>{}\[\]\\]*',
    ]
    wado_hits = find_urls_in_text(all_script_text, viewer_patterns)
    if wado_hits:
        for h in wado_hits[:30]:
            p(f"  {h}")
    else:
        p("  (none found)")

    # ── Section 6: All unique external URLs ──────────────────────────────────
    sep("[6] All unique non-obvious external URLs in page")
    all_urls = re.findall(r'https?://[^\s\'"<>{}\[\]\\]{10,}', resp.text)
    unique_urls = list(dict.fromkeys(all_urls))
    skip = {"google", "twitter", "facebook", "youtube", "linkedin",
            "cloudflare", "jquery", "bootstrap", "fonts.g", "gstat",
            "w3.org", "schema.org", "openstreetmap", "opengraph"}
    filtered = [u for u in unique_urls if not any(s in u for s in skip)]
    for u in filtered:
        p(f"  {u}")

    # ── Section 7: Probe API endpoints ──────────────────────────────────────
    sep("[7] Probing Radiopaedia API for case data")
    endpoints = [
        f"https://radiopaedia.org/api/v1/cases/{slug}",
        f"https://radiopaedia.org/api/v1/cases/{slug}/studies",
        f"https://radiopaedia.org/cases/{slug}.json",
    ]
    for ep in endpoints:
        try:
            r = session.get(ep, headers={**HEADERS, "Accept": "application/json"}, timeout=15)
            p(f"\n  GET {ep}")
            p(f"  --> HTTP {r.status_code}  Content-Type: {r.headers.get('content-type','')}")
            if r.status_code == 200 and "json" in r.headers.get("content-type", ""):
                try:
                    data = r.json()
                    dumped = json.dumps(data, indent=2)
                    p(f"  --> JSON ({len(dumped)} chars):")
                    p(dumped[:4000])
                    if len(dumped) > 4000:
                        p("  ... [truncated]")
                except Exception as e:
                    p(f"  --> JSON parse error: {e}")
                    p(f"  --> Raw: {r.text[:500]}")
            elif r.status_code == 200:
                p(f"  --> Body (first 500): {r.text[:500]}")
        except Exception as e:
            p(f"  --> Error: {e}")

    sep("Done")

if __name__ == "__main__":
    main()
